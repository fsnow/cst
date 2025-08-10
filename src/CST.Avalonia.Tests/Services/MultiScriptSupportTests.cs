using System.IO;
using CST;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Util;
using Xunit;

namespace CST.Avalonia.Tests.Services
{
    public class MultiScriptSupportTests
    {
        private const LuceneVersion LUCENE_VERSION = LuceneVersion.LUCENE_48;

        [Fact]
        public void DevaXmlAnalyzer_CanBeInstantiated()
        {
            // Act
            var analyzer = new DevaXmlAnalyzer(LUCENE_VERSION);

            // Assert
            Assert.NotNull(analyzer);
        }

        [Fact]
        public void IpeAnalyzer_CanBeInstantiated()
        {
            // Act
            var analyzer = new IpeAnalyzer(LUCENE_VERSION);

            // Assert
            Assert.NotNull(analyzer);
        }

        [Fact]
        public void DevaXmlAnalyzer_CanAnalyzeSimpleText()
        {
            // Arrange
            var analyzer = new DevaXmlAnalyzer(LUCENE_VERSION);
            var testText = "बुद्ध धम्म संघ";
            
            // Act & Assert
            using (var tokenStream = analyzer.GetTokenStream("content", new StringReader(testText)))
            {
                Assert.NotNull(tokenStream);
                // The tokenizer should be created successfully
            }
        }

        [Fact]
        public void IpeAnalyzer_CanAnalyzeSimpleText()
        {
            // Arrange
            var analyzer = new IpeAnalyzer(LUCENE_VERSION);
            var testText = "buddha dhamma sa.ngha";
            
            // Act & Assert
            using (var tokenStream = analyzer.GetTokenStream("content", new StringReader(testText)))
            {
                Assert.NotNull(tokenStream);
                // Should successfully create token stream
            }
        }

        [Fact]
        public void DevaXmlTokenizer_CanBeInstantiated()
        {
            // Arrange
            var testInput = new StringReader("धम्म");

            // Act
            var tokenizer = new DevaXmlTokenizer(LUCENE_VERSION, testInput);

            // Assert
            Assert.NotNull(tokenizer);
        }

        [Fact]
        public void DevaXmlTokenizer_WithAttributeFactory_CanBeInstantiated()
        {
            // Arrange
            var testInput = new StringReader("धम्म");
            var factory = AttributeSource.AttributeFactory.DEFAULT_ATTRIBUTE_FACTORY;

            // Act
            var tokenizer = new DevaXmlTokenizer(LUCENE_VERSION, factory, testInput);

            // Assert
            Assert.NotNull(tokenizer);
        }

        [Fact]
        public void DevaXmlAnalyzer_HandlesEmptyInput()
        {
            // Arrange
            var analyzer = new DevaXmlAnalyzer(LUCENE_VERSION);
            var emptyText = "";
            
            // Act & Assert
            using (var tokenStream = analyzer.GetTokenStream("content", new StringReader(emptyText)))
            {
                Assert.NotNull(tokenStream);
            }
        }

        [Fact]
        public void IpeAnalyzer_HandlesEmptyInput()
        {
            // Arrange
            var analyzer = new IpeAnalyzer(LUCENE_VERSION);
            var emptyText = "";
            
            // Act & Assert
            using (var tokenStream = analyzer.GetTokenStream("content", new StringReader(emptyText)))
            {
                Assert.NotNull(tokenStream);
            }
        }

        [Fact]
        public void DevaXmlAnalyzer_HandlesXmlTags()
        {
            // Arrange
            var analyzer = new DevaXmlAnalyzer(LUCENE_VERSION);
            var xmlText = "<div>बुद्ध</div>";
            
            // Act & Assert
            using (var tokenStream = analyzer.GetTokenStream("content", new StringReader(xmlText)))
            {
                Assert.NotNull(tokenStream);
                // Should handle XML content without throwing
            }
        }

        [Fact]
        public void IpeAnalyzer_HandlesLatinScript()
        {
            // Arrange
            var analyzer = new IpeAnalyzer(LUCENE_VERSION);
            var latinText = "buddha dhamma sangha";
            
            // Act & Assert
            using (var tokenStream = analyzer.GetTokenStream("content", new StringReader(latinText)))
            {
                Assert.NotNull(tokenStream);
            }
        }

        [Fact]
        public void DevaXmlAnalyzer_HandlesUnicodeText()
        {
            // Arrange
            var analyzer = new DevaXmlAnalyzer(LUCENE_VERSION);
            var unicodeText = "धर्म क्षेत्र ज्ञान"; // Various complex Devanagari characters
            
            // Act & Assert
            using (var tokenStream = analyzer.GetTokenStream("content", new StringReader(unicodeText)))
            {
                Assert.NotNull(tokenStream);
            }
        }

        [Fact]
        public void MultipleAnalyzers_CanCoexist()
        {
            // Arrange
            var devaAnalyzer = new DevaXmlAnalyzer(LUCENE_VERSION);
            var ipeAnalyzer = new IpeAnalyzer(LUCENE_VERSION);
            
            // Act & Assert
            using (var devaStream = devaAnalyzer.GetTokenStream("content", new StringReader("धम्म")))
            using (var ipeStream = ipeAnalyzer.GetTokenStream("content", new StringReader("dhamma")))
            {
                Assert.NotNull(devaStream);
                Assert.NotNull(ipeStream);
                // Both analyzers should work independently
            }
        }

        [Fact]
        public void DevaXmlAnalyzer_DisposesCorrectly()
        {
            // Arrange & Act
            var analyzer = new DevaXmlAnalyzer(LUCENE_VERSION);
            
            // Assert - should dispose without throwing
            analyzer.Dispose();
        }

        [Fact]
        public void IpeAnalyzer_DisposesCorrectly()
        {
            // Arrange & Act
            var analyzer = new IpeAnalyzer(LUCENE_VERSION);
            
            // Assert - should dispose without throwing
            analyzer.Dispose();
        }

        [Fact]
        public void DevaXmlTokenizer_DisposesCorrectly()
        {
            // Arrange & Act
            var tokenizer = new DevaXmlTokenizer(LUCENE_VERSION, new StringReader("test"));
            
            // Assert - should dispose without throwing
            tokenizer.Dispose();
        }
    }
}