using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Constants;
using Serilog;

namespace CST.Avalonia.Services.Ai
{
    public interface IAiProviderLogos
    {
        /// <summary>
        /// The path to a cached SVG for this provider, or null when it has none and the caller should fall
        /// back to the monogram. Never throws.
        /// </summary>
        Task<string?> GetLogoPathAsync(string providerId, CancellationToken ct = default);
    }

    /// <summary>
    /// Provider logos from models.dev, cached on disk, with the monogram as the fallback. (#738)
    ///
    /// <para><b>Why this exists.</b> Logos were deferred to coloured monogram tiles for want of an asset
    /// source, and shipping third-party marks raised a licensing question. models.dev answers both:
    /// per-provider SVGs at <c>/logos/{id}.svg</c>, keyed by the same id we already use, MIT.</para>
    ///
    /// <para><b>A missing logo returns 200, not 404.</b> models.dev serves a default placeholder for any
    /// unknown id — measured byte-identical across ids (1,421 bytes, one hash) — so status cannot tell a real
    /// logo from a stand-in. The placeholder is recognised by hash and reported as "no logo", because a
    /// generic grey mark says less than a coloured initial does. Ollama returns the placeholder, which is the
    /// case that matters: local runners are not catalogue providers and never will be.</para>
    ///
    /// <para><b>The placeholder is never cached.</b> Caching it would leave a provider that gains a logo
    /// next month showing nothing until somebody cleared the directory.</para>
    /// </summary>
    public sealed class AiProviderLogos : IAiProviderLogos
    {
        internal const string DefaultSource = "https://models.dev/logos";

        internal const string NoticeFile = "NOTICE.txt";

        /// <summary>
        /// Written beside the cached SVGs. MIT asks that the notice accompany copies, and these files are
        /// copies — sitting on the reader's disk, outside the app bundle, where nothing else would say where
        /// they came from or under what terms.
        ///
        /// <para>Each mark also remains its owner's; showing a company's logo to label that company's own
        /// service is the ordinary use, and no other use is made of them here.</para>
        /// </summary>
        internal const string NoticeText = """
            Provider logos in this directory are from models.dev (https://models.dev),
            fetched on demand and cached here. They are re-fetched if deleted.

            MIT License

            Copyright (c) 2025 models.dev

            Permission is hereby granted, free of charge, to any person obtaining a copy
            of this software and associated documentation files (the "Software"), to deal
            in the Software without restriction, including without limitation the rights
            to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
            copies of the Software, and to permit persons to whom the Software is
            furnished to do so, subject to the following conditions:

            The above copyright notice and this permission notice shall be included in all
            copies or substantial portions of the Software.

            THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
            IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
            FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
            AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
            LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
            OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
            SOFTWARE.

            Each provider's mark remains the property of its owner.
            """;

        /// <summary>
        /// SHA-256 of the placeholder models.dev returns for an unknown provider, measured 2026-08-19
        /// (1,421 bytes; identical for <c>ollama</c> and for ids that cannot exist).
        ///
        /// <para>If upstream changes it, the effect is a placeholder cached and shown as though it were a
        /// logo — mildly wrong, not broken — and the hash is re-derivable by requesting any id that cannot
        /// exist. Recorded rather than probed at runtime so no request is spent discovering it.</para>
        /// </summary>
        internal const string PlaceholderSha256 =
            "13bb0b37c627f6e5961487cd0159abc18dd87ff318668357c7c9990b42e7f32f";

        private readonly HttpClient _http;
        private readonly string _cacheDirectory;
        private readonly string _source;
        private readonly string _placeholderHash;

        /// <summary>Ids already looked up this session, so a provider with no logo is asked for once rather
        /// than on every rebind.</summary>
        private readonly ConcurrentDictionary<string, string?> _resolved = new(StringComparer.OrdinalIgnoreCase);

        /// <param name="placeholderSha256">The hash treated as "this provider has no logo". Overridable only
        /// so tests can exercise the detection against bytes they control — no caller should pass it.</param>
        public AiProviderLogos(
            HttpClient? http = null,
            string? cacheDirectory = null,
            string? source = null,
            string? placeholderSha256 = null)
        {
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

            // Under cache/, not beside application-state.json: these are purely derived and re-fetchable, so
            // deleting the directory should cost a refetch and nothing else.
            _cacheDirectory = cacheDirectory
                ?? Path.Combine(AppConstants.DataDirectory, "cache", "provider-logos");
            _source = source ?? DefaultSource;
            _placeholderHash = placeholderSha256 ?? PlaceholderSha256;
        }

        public async Task<string?> GetLogoPathAsync(string providerId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(providerId)) return null;

            // A connection id is whatever its owner typed, and it reaches here as both a URL segment and a
            // file name. models.dev's own ids are slugs (#737 rejects anything else), so anything that is not
            // one cannot have a logo anyway - and refusing it here is what stops "../../.." from naming a file
            // outside the cache.
            if (!SlugPattern.IsMatch(providerId)) return null;

            if (_resolved.TryGetValue(providerId, out var known)) return known;

            try
            {
                var path = Path.Combine(_cacheDirectory, providerId + ".svg");
                if (File.Exists(path))
                {
                    _resolved[providerId] = path;
                    return path;
                }

                var bytes = await _http.GetByteArrayAsync($"{_source}/{providerId}.svg", ct)
                    .ConfigureAwait(false);

                // A definite answer, unlike the catch below: this provider has no mark, so remember it and
                // stop asking.
                if (bytes.Length == 0 || IsPlaceholder(bytes, _placeholderHash))
                {
                    _resolved[providerId] = null;
                    return null;
                }

                Directory.CreateDirectory(_cacheDirectory);
                WriteNoticeOnce();

                // Temp-then-rename: a process killed mid-write must not leave a truncated SVG that every
                // later start reads as a valid cached logo.
                var tmp = path + ".tmp";
                await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(false);
                File.Move(tmp, path, overwrite: true);

                _resolved[providerId] = path;
                return path;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // A logo is decoration. Offline, a 500, an unwritable directory - all mean "use the
                // monogram", which is what the UI already does for a custom endpoint with no provider id.
                //
                // Deliberately NOT remembered. A failure says nothing about whether this provider has a logo,
                // only about this moment; recording it would mean a reader who opened Settings before their
                // wifi came up sees monograms for the rest of the session, with no way to ask again. The
                // retry cost is bounded by the rows themselves, which ask once each.
                Log.Debug(ex, "No logo for {Provider} right now; falling back to the monogram (#738)", providerId);
                return null;
            }
        }

        private static readonly Regex SlugPattern =
            new("^[a-z0-9][a-z0-9_-]*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Puts the licence beside the first cached logo. Rewritten if deleted, never overwritten
        /// when already present, and a failure to write it must not cost the reader a logo.</summary>
        private void WriteNoticeOnce()
        {
            try
            {
                var notice = Path.Combine(_cacheDirectory, NoticeFile);
                if (!File.Exists(notice)) File.WriteAllText(notice, NoticeText);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not write the logo licence notice (#738)");
            }
        }

        /// <summary>
        /// Whether these bytes are the shared placeholder rather than a provider's own mark.
        ///
        /// <para>Compared by content hash rather than by byte count: two real logos could coincidentally
        /// share a length, and the placeholder's own size is not a promise.</para>
        /// </summary>
        internal static bool IsPlaceholder(byte[] bytes, string? expectedSha256 = null)
        {
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            return string.Equals(hash, expectedSha256 ?? PlaceholderSha256, StringComparison.Ordinal);
        }
    }
}
