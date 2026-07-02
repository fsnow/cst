using System.IO;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Util;

namespace CST
{
    public class DevaXmlAnalyzer : Analyzer
    {
        private readonly LuceneVersion matchVersion;

        public DevaXmlAnalyzer(LuceneVersion matchVersion)
        {
            this.matchVersion = matchVersion;
        }

        protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
        {
            // No shared state: each (per-thread) TokenStreamComponents gets its own tokenizer, and the
            // tokenizer buffers the document in its Reset() from its own reader. (SRCH-14)
            return new TokenStreamComponents(new DevaXmlTokenizer(matchVersion, reader));
        }
    }
}
