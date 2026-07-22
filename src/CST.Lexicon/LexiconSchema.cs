namespace CST.Lexicon
{
    /// <summary>
    /// The canonical lexicon SQLite schema — one place so the builder and reader can't drift. A lexicon is two
    /// tables: <c>meta</c> (source id + attribution + version stamps, key/value) and <c>entry</c>
    /// (IPE-keyed, homonym-aware, HTML-definition rows). Bumping <see cref="SchemaVersion"/> means an older
    /// reader should refuse a newer file (the delivery layer gates on it).
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
    ipe_key    TEXT NOT NULL,
    headword   TEXT NOT NULL,
    homonym    INTEGER NOT NULL DEFAULT 0,
    body_html  TEXT NOT NULL
);
CREATE INDEX ix_entry_key ON entry(ipe_key);";
    }
}
