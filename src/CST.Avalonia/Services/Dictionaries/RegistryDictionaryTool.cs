using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CST.Tools;

namespace CST.Avalonia.Services.Dictionaries
{
    /// <summary>
    /// The <see cref="IDictionaryTool"/> exposed over <c>/v1/dictionary</c> + MCP, backed by the
    /// <see cref="DictionarySourceRegistry"/>. It advertises and routes to EVERY available source — flat-file,
    /// DPD, downloaded lexicons — with no special cases; it never consults any UI preference (an agent sees all
    /// installed sources regardless of the human's picker). Replaces the former CompositeDictionaryTool. (#466)
    /// </summary>
    public sealed class RegistryDictionaryTool : IDictionaryTool
    {
        private readonly DictionarySourceRegistry _registry;

        public RegistryDictionaryTool(DictionarySourceRegistry registry) => _registry = registry;

        public IReadOnlyList<DictionaryLanguageInfo> Languages =>
            _registry.Available.Select(s => new DictionaryLanguageInfo(s.Id, s.Attribution)).ToList();

        public Task<IReadOnlyList<DictionaryEntry>> LookupAsync(DictionaryRequest request, CancellationToken ct = default)
        {
            var source = _registry.ById(request.Language);
            return source is null
                ? Task.FromResult<IReadOnlyList<DictionaryEntry>>(Array.Empty<DictionaryEntry>())
                : source.LookupAsync(request, ct);
        }
    }
}
