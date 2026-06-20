using CST.Avalonia.Search;
using Xunit;

namespace CST.Avalonia.Tests.Search
{
    public class BoundedCacheTests
    {
        [Fact]
        public void SetAndGet_ReturnsStoredValue()
        {
            var cache = new BoundedCache<string, int>(3);
            cache.Set("a", 1);
            Assert.True(cache.TryGet("a", out var v));
            Assert.Equal(1, v);
        }

        [Fact]
        public void TryGet_MissingKey_ReturnsFalse()
        {
            var cache = new BoundedCache<string, int>(3);
            Assert.False(cache.TryGet("nope", out _));
        }

        [Fact]
        public void ExceedingCapacity_EvictsOldestInserted()
        {
            var cache = new BoundedCache<string, int>(2);
            cache.Set("a", 1);
            cache.Set("b", 2);
            cache.Set("c", 3); // evicts "a" (oldest)

            Assert.Equal(2, cache.Count);
            Assert.False(cache.TryGet("a", out _));
            Assert.True(cache.TryGet("b", out _));
            Assert.True(cache.TryGet("c", out _));
        }

        [Fact]
        public void ResettingExistingKey_UpdatesValue_WithoutEvicting()
        {
            var cache = new BoundedCache<string, int>(2);
            cache.Set("a", 1);
            cache.Set("b", 2);
            cache.Set("a", 99); // update, not a new entry

            Assert.Equal(2, cache.Count);
            Assert.True(cache.TryGet("a", out var a));
            Assert.Equal(99, a);
            Assert.True(cache.TryGet("b", out _)); // "b" not evicted
        }

        [Fact]
        public void Clear_RemovesAllEntries()
        {
            var cache = new BoundedCache<string, int>(3);
            cache.Set("a", 1);
            cache.Set("b", 2);
            cache.Clear();

            Assert.Equal(0, cache.Count);
            Assert.False(cache.TryGet("a", out _));
        }

        [Fact]
        public void EvictionOrder_IsFifo_AcrossManyInserts()
        {
            var cache = new BoundedCache<int, int>(3);
            for (int i = 0; i < 10; i++)
                cache.Set(i, i);

            // Only the last 3 inserted keys (7,8,9) remain.
            Assert.Equal(3, cache.Count);
            for (int i = 0; i < 7; i++)
                Assert.False(cache.TryGet(i, out _));
            for (int i = 7; i < 10; i++)
                Assert.True(cache.TryGet(i, out _));
        }
    }
}
