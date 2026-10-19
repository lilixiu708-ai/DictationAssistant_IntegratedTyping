namespace DictationAssistant.App.Helpers;

/// <summary>
/// A generic in-memory LRU cache with async loading and per-key concurrent deduplication.
/// Thread-safe: multiple threads may call <see cref="GetAsync"/> simultaneously.
/// The loader is never cancelled — callers control their own cancellation via the ct passed to GetAsync.
/// </summary>
public sealed class CachedDataLoader<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Func<TKey, Task<TValue>> _loader;
    private readonly LinkedList<CacheEntry> _cacheOrder = new();
    private readonly Dictionary<TKey, LinkedListNode<CacheEntry>> _cacheMap = new();
    private readonly Dictionary<TKey, Task<TValue>> _pendingLoads = new();
    private readonly object _lock = new();

    public CachedDataLoader(Func<TKey, Task<TValue>> loader, int capacity = 4)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _loader = loader;
        _capacity = capacity;
    }

    public async Task<TValue> GetAsync(TKey key, CancellationToken ct)
    {
        Task<TValue> task;

        lock (_lock)
        {
            if (TryGetFromCache(key, out var cached))
            {
                return cached!;
            }

            if (!_pendingLoads.TryGetValue(key, out task!))
            {
                task = LoadAndCacheAsync(key);
                _pendingLoads[key] = task;
            }
        }

        return await task.WaitAsync(ct).ConfigureAwait(false);
    }

    private async Task<TValue> LoadAndCacheAsync(TKey key)
    {
        try
        {
            var result = await _loader(key).ConfigureAwait(false);

            lock (_lock)
            {
                PutInCache(key, result);
            }

            return result;
        }
        finally
        {
            lock (_lock)
            {
                _pendingLoads.Remove(key);
            }
        }
    }

    private bool TryGetFromCache(TKey key, out TValue? value)
    {
        if (_cacheMap.TryGetValue(key, out var node))
        {
            _cacheOrder.Remove(node);
            _cacheOrder.AddFirst(node);
            value = node.Value.Value;
            return true;
        }

        value = default;
        return false;
    }

    private void PutInCache(TKey key, TValue value)
    {
        if (_cacheMap.TryGetValue(key, out var existingNode))
        {
            _cacheOrder.Remove(existingNode);
            _cacheOrder.AddFirst(existingNode);
            return;
        }

        if (_cacheOrder.Count >= _capacity)
        {
            var lastNode = _cacheOrder.Last;
            if (lastNode is not null)
            {
                _cacheOrder.RemoveLast();
                _cacheMap.Remove(lastNode.Value.Key);
            }
        }

        var newNode = new LinkedListNode<CacheEntry>(new CacheEntry { Key = key, Value = value });
        _cacheOrder.AddFirst(newNode);
        _cacheMap[key] = newNode;
    }

    private readonly struct CacheEntry
    {
        public required TKey Key { get; init; }
        public required TValue Value { get; init; }
    }
}
