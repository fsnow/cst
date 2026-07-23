namespace CST.Lexicon
{
    /// <summary>
    /// The canonical lexicon SQLite schema — one place so the builder and reader can't drift. A lexicon is two
    /// tables: <c>meta</c> (source id + attribution + version stamps, key/value) and <c>entry</c>, which stores
    /// just the published <c>headword</c> and its HTML <c>body</c>. The IPE lookup key and homonym number are
    /// NOT stored — <see cref="LexiconReader"/> derives them at load from the headword, so a producer (a
    /// build-time converter, or the in-app import) needs no knowledge of IPE and there is no build-vs-read key
    /// to keep in lockstep: one derivation path serves both stored headwords and typed queries. Bumping
    /// <see cref="SchemaVersion"/> means an older reader should refuse a newer file (the delivery layer gates on it).
    /// </summary>
    public static class LexiconSchema
    {
        public const int SchemaVersion = 1;

        // meta keys (mirror the LexiconMeta record + the dpd-cst-subset meta vocabulary).
        public const string MetaSchemaVersion = "schema_version";
        public const string MetaSourceId = "source_id";
        public const string MetaDisplayName = "display_name";
        public const string MetaDefinitionLanguage = "definition_language";
        public const string MetaKind = "kind";
        public const string MetaTitle = "title";
        public const string MetaAuthor = "author";
        public const string MetaReviser = "reviser";
        public const string MetaYear = "year";
        public const string MetaPublisher = "publisher";
        public const string MetaLicense = "license";
        public const string MetaUrl = "url";
        public const string MetaSourceVersion = "source_version";
        public const string MetaConverterVersion = "converter_version";

        public const string CreateSql = @"
CREATE TABLE meta(key TEXT PRIMARY KEY, value TEXT);
CREATE TABLE entry(
    id         INTEGER PRIMARY KEY,
    headword   TEXT NOT NULL,
    body_html  TEXT NOT NULL
);";
    }
}
