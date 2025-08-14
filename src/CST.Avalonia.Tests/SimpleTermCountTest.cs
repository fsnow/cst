using System;
using System.IO;
using System.Text;
using Xunit;

namespace CST.Avalonia.Tests
{
    public class SimpleTermCountTest
    {
        [Fact]
        public void TestManualTermCounting_s0101m()
        {
            // Skip if real book file doesn't exist
            string realBookPath = "/Users/fsnow/github/fsnow/cst/tipitaka-latn/s0101m.mul.xml";
            if (!File.Exists(realBookPath))
            {
                // Skip test if file doesn't exist - this is expected in CI
                return;
            }

            // Read the content and do simple string matching
            string content = File.ReadAllText(realBookPath, Encoding.UTF8);
            
            int count1 = CountOccurrences(content, "bhikkhusaṅghena");
            int count2 = CountOccurrences(content, "bhikkhusaṅghañca");
            
            // These are the counts verified manually in VS Code
            Assert.Equal(20, count1); // bhikkhusaṅghena appears 20 times
            Assert.Equal(11, count2); // bhikkhusaṅghañca appears 11 times
            Assert.Equal(31, count1 + count2); // Total should be 31, not 155
        }

        private int CountOccurrences(string text, string pattern)
        {
            int count = 0;
            int index = 0;
            
            while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += pattern.Length;
            }
            
            return count;
        }
    }
}