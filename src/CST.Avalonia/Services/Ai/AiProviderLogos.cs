using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
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
        /// back to the monogram.
        ///
        /// <para>No failure surfaces as an exception — offline, a 500, an unwritable directory all return
        /// null. The one exception that does escape is cancellation of <paramref name="ct"/>, which is the
        /// caller's own doing rather than a fault to report.</para>
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

        /// <summary>
        /// Ids already settled this session, so a provider with no mark is asked for once rather than on
        /// every rebind.
        ///
        /// <para>Per row, not globally serialised: two rows carrying the same id can both be in flight before
        /// either records an answer, which costs a duplicate request and nothing else. Not worth a lock —
        /// the outcome is identical and the writers no longer collide.</para>
        /// </summary>
        private readonly ConcurrentDictionary<string, string?> _resolved = new(StringComparer.Ordinal);

        /// <param name="placeholderSha256">The hash treated as "this provider has no logo". Overridable only
        /// so tests can exercise the detection against bytes they control — no caller should pass it.</param>
        public AiProviderLogos(
            HttpClient? http = null,
            string? cacheDirectory = null,
            string? source = null,
            string? placeholderSha256 = null)
        {
            // 30s to match ModelsDevCatalog rather than pick a second number for the same host. Nothing waits
            // on this - a row draws its monogram immediately and swaps only if a logo arrives.
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

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

            // A remembered absence is final; a remembered path is only good while the file is still there,
            // since the cache directory is documented as safe to delete and a row would otherwise bind to a
            // file that has gone.
            if (_resolved.TryGetValue(providerId, out var known)
                && (known is null || File.Exists(known)))
            {
                return known;
            }

            try
            {
                var path = Path.Combine(_cacheDirectory, providerId + ".svg");
                if (File.Exists(path))
                {
                    // Re-checked rather than trusted. A file already on disk may have been written by an
                    // earlier release whose placeholder hash had drifted, or by a captive portal before the
                    // sniff below existed; re-reading is what lets a later release heal a poisoned cache
                    // instead of serving the bad copy forever. These are ~1 KB files.
                    var cached = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
                    if (LooksLikeSvg(cached) && !IsPlaceholder(cached, _placeholderHash))
                    {
                        _resolved[providerId] = path;
                        return path;
                    }

                    File.Delete(path);
                }

                var bytes = await _http.GetByteArrayAsync($"{_source}/{providerId}.svg", ct)
                    .ConfigureAwait(false);

                // A definite answer, unlike the two below: models.dev says this provider has no mark, so
                // remember it and stop asking.
                if (IsPlaceholder(bytes, _placeholderHash))
                {
                    _resolved[providerId] = null;
                    return null;
                }

                // Not an answer at all, and NOT written to disk.
                //
                // GetByteArrayAsync throws on a non-2xx, so an error page with an error status can never get
                // here - but a captive portal answers every request with 200 and a login page. Without this,
                // one hotel wifi writes HTML into anthropic.svg and every later session serves it from disk
                // without a request. That would make a transient network condition into a permanent wrong
                // answer, which is precisely the failure this class refuses even to remember for a session.
                //
                // An empty body is treated the same way: at least as plausibly a proxy artefact as a
                // deliberate "no mark", and models.dev states the latter with the placeholder.
                if (!LooksLikeSvg(bytes))
                {
                    Log.Debug(
                        "Ignoring a {Bytes}-byte non-SVG response for {Provider}'s logo (#738)",
                        bytes.Length, providerId);
                    return null;
                }

                Directory.CreateDirectory(_cacheDirectory);
                WriteNoticeOnce();

                // Temp-then-rename: a process killed mid-write must not leave a truncated SVG that every
                // later start reads as a valid cached logo.
                //
                // The temp name is unique PER CALL, not per process: a preset row and the connection added
                // from it carry the same id and can load at the same moment, on two threads of one process.
                // With any shared name the loser's Move finds its own file already gone, fails, and leaves
                // that row on the monogram for the session - measured at 7 of 8 concurrent callers.
                var tmp = $"{path}.{Guid.NewGuid():N}.tmp";
                try
                {
                    await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(false);
                    File.Move(tmp, path, overwrite: true);
                }
                catch
                {
                    // Litter outlives the process otherwise, and nothing sweeps the directory.
                    try { File.Delete(tmp); } catch { /* nothing further to try */ }
                    throw;
                }

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

        /// <summary>
        /// The same rule as <c>AiPresetSource</c> and <c>AiConnectionService</c> — case-SENSITIVE, because
        /// those are, and a resolver that accepted more than the id validators do would be the odd one out
        /// for no reason. Case-insensitivity also let "OpenAI" record an absence that "openai" then read,
        /// hiding a real provider's logo for the session.
        ///
        /// <para><c>\z</c> rather than <c>$</c>: <c>$</c> matches before a trailing newline, so "openai\n"
        /// passed as a slug and named a file with a newline in it.</para>
        /// </summary>
        private static readonly Regex SlugPattern =
            new(@"^[a-z0-9][a-z0-9_-]*\z", RegexOptions.Compiled);

        /// <summary>
        /// Whether these bytes open like an SVG document.
        ///
        /// <para>A prefix sniff rather than a parse: the question is only "did a network answer this with
        /// something that is not the file we asked for", and every real answer starts with an XML declaration,
        /// a doctype, a comment, or the root element.</para>
        /// </summary>
        internal static bool LooksLikeSvg(byte[] bytes)
        {
            if (bytes.Length == 0) return false;

            // Enough for a declaration and any leading whitespace; a BOM is skipped by the decoder.
            var head = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 200)).TrimStart('\uFEFF');
            var i = 0;
            while (i < head.Length && char.IsWhiteSpace(head[i])) i++;
            head = head[i..];

            return head.StartsWith("<svg", StringComparison.OrdinalIgnoreCase)
                || head.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
                || head.StartsWith("<!--", StringComparison.Ordinal)
                || head.StartsWith("<!DOCTYPE svg", StringComparison.OrdinalIgnoreCase);
        }

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
