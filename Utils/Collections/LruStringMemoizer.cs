using Kiriha.Utils;
using Kiriha.Utils.Parsing;
using Kiriha.Utils.Collections;
using Kiriha.Utils.Async;
using Kiriha.Utils.Graphs;
using Kiriha.Utils.UI;
using System;
using System.Collections.Generic;

namespace Kiriha.Utils.Collections;

/// <summary>
/// Tiny thread-safe LRU memoizer keyed by string, capped by entry count.
/// Designed for memoizing pure functions whose result is small (e.g. a parsed
/// element list or a normalized title string). The factory runs OUTSIDE the
/// lock so concurrent callers with different keys don't serialize on each
/// other; in the rare case where two callers race on the same fresh key, both
/// compute and the second result is dropped.
/// </summary>
internal sealed class LruStringMemoizer<TVal> where TVal : class
{
    private readonly int _capacity;
    private readonly Dictionary<string, LinkedListNode<Entry>> _map;
    private readonly LinkedList<Entry> _order = new();
    private readonly object _gate = new();

    public LruStringMemoizer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _map = new Dictionary<string, LinkedListNode<Entry>>(capacity);
    }

    public TVal GetOrAdd(string key, Func<string, TVal> factory)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                return node.Value.Value;
            }
        }

        // Compute outside the lock — the factory may be expensive (Anitomy
        // parse takes a few ms). Holding the lock here would serialize all
        // first-touch callers across unrelated keys.
        var value = factory(key);

        lock (_gate)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                _order.AddFirst(existing);
                return existing.Value.Value;
            }

            var node = new LinkedListNode<Entry>(new Entry(key, value));
            _order.AddFirst(node);
            _map[key] = node;

            if (_map.Count > _capacity && _order.Last is { } tail)
            {
                _map.Remove(tail.Value.Key);
                _order.RemoveLast();
            }
            return value;
        }
    }

    private readonly record struct Entry(string Key, TVal Value);
}
