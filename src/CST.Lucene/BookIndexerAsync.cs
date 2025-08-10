using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CST.Lucene
{
    public class BookIndexerAsync
    {
        private readonly BookIndexer _bookIndexer;

        public BookIndexerAsync()
        {
            _bookIndexer = new BookIndexer();
        }

        public string XmlDirectory
        {
            get => _bookIndexer.XmlDirectory;
            set => _bookIndexer.XmlDirectory = value;
        }

        public string IndexDirectory
        {
            get => _bookIndexer.IndexDirectory;
            set => _bookIndexer.IndexDirectory = value;
        }

        public Task IndexAllAsync(IProgress<IndexingProgress> progress, List<int> changedFiles)
        {
            return Task.Run(() =>
            {
                Console.WriteLine($"BookIndexerAsync.IndexAllAsync() starting with {changedFiles.Count} changed files");
                Console.WriteLine($"XmlDirectory: {XmlDirectory}");
                Console.WriteLine($"IndexDirectory: {IndexDirectory}");
                
                _bookIndexer.IndexAll(message =>
                {
                    Console.WriteLine($"BookIndexer progress: {message}");
                    var indexingProgress = ParseProgressMessage(message);
                    progress?.Report(indexingProgress);
                }, changedFiles);
                
                Console.WriteLine("BookIndexerAsync.IndexAllAsync() completed");
            });
        }

        private IndexingProgress ParseProgressMessage(string message)
        {
            var progress = new IndexingProgress
            {
                StatusMessage = message,
                IsComplete = false
            };

            // Parse messages like "Building search index. (Book 1 of 217)"
            if (message.Contains("Book ") && message.Contains(" of "))
            {
                try
                {
                    var startIdx = message.IndexOf("Book ") + 5;
                    var ofIdx = message.IndexOf(" of ", startIdx);
                    var endIdx = message.IndexOf(")", ofIdx);

                    if (startIdx > 4 && ofIdx > startIdx && endIdx > ofIdx)
                    {
                        var currentStr = message.Substring(startIdx, ofIdx - startIdx);
                        var totalStr = message.Substring(ofIdx + 4, endIdx - (ofIdx + 4));

                        if (int.TryParse(currentStr, out int current) && int.TryParse(totalStr, out int total))
                        {
                            progress.CurrentBook = current;
                            progress.TotalBooks = total;
                        }
                    }
                }
                catch
                {
                    // If parsing fails, just use the message as-is
                }
            }
            else if (message.Contains("Optimizing search index"))
            {
                progress.StatusMessage = "Optimizing index...";
            }
            else if (message.Contains("Checking search index"))
            {
                progress.StatusMessage = "Verifying index...";
            }

            return progress;
        }
    }
}