namespace CST.Lucene
{
    public class IndexingProgress
    {
        public int CurrentBook { get; set; }
        public int TotalBooks { get; set; }
        public string CurrentFileName { get; set; } = string.Empty;
        public string StatusMessage { get; set; } = string.Empty;
        public bool IsComplete { get; set; }
        public double ProgressPercentage => TotalBooks > 0 ? (double)CurrentBook / TotalBooks * 100 : 0;
    }
}