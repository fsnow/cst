using System.IO;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Util;

namespace CST
{
    public class IpeAnalyzer : Analyzer
    {
        private readonly LuceneVersion matchVersion;

        public IpeAnalyzer(LuceneVersion matchVersion)
        {
            this.matchVersion = matchVersion;
        }

        protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
        {
            StandardTokenizer tokenizer = new StandardTokenizer(matchVersion, reader);
            return new TokenStreamComponents(tokenizer);
        }

        protected override TextReader InitReader(string fieldName, TextReader reader)
        {
            return base.InitReader(fieldName, reader);
        }
    }
}
