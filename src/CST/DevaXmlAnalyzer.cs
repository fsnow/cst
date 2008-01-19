using System;
using System.IO;
using System.Text;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;

namespace CST
{
    public class DevaXmlAnalyzer : Lucene.Net.Analysis.Analyzer
    {
        public DevaXmlAnalyzer()
        {
        }

  
        /// <summary>
        /// Creates a TokenStream which tokenizes all the text in the provided Reader.
        /// </summary>
        /// <param name="fieldName"></param>
        /// <param name="reader"></param>
        /// <returns>A TokenStream built from the DevaXmlTokenizer</returns>
        public override TokenStream TokenStream(String fieldName, TextReader reader)
        {

            if (fieldName == null) throw new Exception("fieldName must not be null");
            if (reader == null) throw new Exception("reader must not be null");

            TokenStream result = new DevaXmlTokenizer(reader);
            return result;
        }
    }
}
