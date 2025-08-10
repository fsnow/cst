using System.IO;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Util;

namespace CST
{
    public class DevaXmlAnalyzer : Analyzer
    {
        private readonly LuceneVersion matchVersion;
        private DevaXmlTokenizer tokenizer;

        public DevaXmlAnalyzer(LuceneVersion matchVersion)
        {
            this.matchVersion = matchVersion;
        }

        protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
        {
            tokenizer = new DevaXmlTokenizer(matchVersion, reader);
            //WhitespaceTokenizer tokenizer = new WhitespaceTokenizer(matchVersion, reader);
            //DummyTokenizer tokenizer = new DummyTokenizer(matchVersion, reader);
            return new TokenStreamComponents(tokenizer);
        }

        protected override TextReader InitReader(string fieldName, TextReader reader)
        {
            TextReader reader2 = base.InitReader(fieldName, reader);
            if (tokenizer != null)
            {
                tokenizer.InitPerBook(reader2);
            }
            return reader2;
        }
    }
}
