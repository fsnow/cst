using System.Collections.Generic;

namespace CST.Avalonia.Search
{
    /// <summary>
    /// A small thread-safe cache bounded to a fixed number of entries. When full, the
    /// oldest-inserted entry is evicted (FIFO). Re-setting an existing key updates its value
    /// without changing eviction order. Used for the search-result cache, which would otherwise
    /// grow without bound for the life of the session.
    /// </summary>
    public sealed class BoundedCache<TKey, TValue> where TKey : notnull
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, TValue> _map = new();
        private readonly Queue<TKey> _order = new();
        private readonly object _lock = new();

        public BoundedCache(int capacity)
        {
            if (capacity < 1) capacity = 1;
            _capacity = capacity;
        }

        public bool TryGet(TKey key, out TValue? value)
        {
            lock (_lock)
            {
                return _map.TryGetValue(key, out value);
            }
        }

        public void Set(TKey key, TValue value)
        {
            lock (_lock)
            {
                if (!_map.ContainsKey(key))
                {
                    if (_order.Count >= _capacity)
                    {
                        var oldest = _order.Dequeue();
                        _map.Remove(oldest);
                    }
                    _order.Enqueue(key);
                }
                _map[key] = value;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _map.Clear();
                _order.Clear();
            }
        }

        public int Count
        {
            get { lock (_lock) { return _map.Count; } }
        }
    }
}
