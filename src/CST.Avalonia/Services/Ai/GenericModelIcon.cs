using System;
using System.IO;
using Avalonia.Platform;
using Serilog;

namespace CST.Avalonia.Services.Ai
{
    /// <summary>
    /// The mark shown for anything without a logo of its own. (#740)
    ///
    /// <para>models.dev serves this same sparkle for every provider it has no mark for — Ollama, LM Studio
    /// and any id that cannot exist all return it, byte for byte. #738 recognised it by hash and treated it
    /// as "no logo", on the reasoning that a generic mark says less than a coloured initial. The maintainer
    /// wants the opposite, and it is the better call: every row then carries an icon instead of a mixture of
    /// icons and letter tiles, and a local runner is not a lesser thing for having no brand.</para>
    ///
    /// <para><b>Bundled rather than fetched</b>, for three reasons that all point the same way: a custom
    /// endpoint has no provider id to fetch with; local runners are precisely the case that must work with no
    /// network; and the file is constant, so a request per provider buys nothing. Carried under the models.dev
    /// MIT notice that #738 already ships.</para>
    /// </summary>
    internal static class GenericModelIcon
    {
        private static readonly object Gate = new();
        private static string? _path;

        /// <summary>
        /// A path to the bundled mark on disk, or null if it cannot be written.
        ///
        /// <para>Extracted to a file because the renderer reads paths, not Avalonia resource URIs. Re-extracted
        /// if the file goes missing, so clearing a temp directory heals rather than breaks.</para>
        /// </summary>
        internal static string? Path()
        {
            lock (Gate)
            {
                if (_path is not null && File.Exists(_path)) return _path;

                try
                {
                    var target = System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(), "cst-reader-generic-model.svg");

                    using (var source = AssetLoader.Open(
                        new Uri("avares://CST.Avalonia/Assets/Ai/generic-model.svg")))
                    using (var file = File.Create(target))
                    {
                        source.CopyTo(file);
                    }

                    _path = target;
                    return _path;
                }
                catch (Exception ex)
                {
                    // The monogram is still there. An icon that cannot be written is not worth a failure.
                    Log.Debug(ex, "Could not extract the generic model icon (#740)");
                    return null;
                }
            }
        }
    }
}
