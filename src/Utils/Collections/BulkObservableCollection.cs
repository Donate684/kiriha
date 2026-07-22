using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Kiriha.Utils.Collections;

/// <summary>
/// ObservableCollection variant that supports bulk insertion with a single
/// CollectionChanged(Reset) notification. The default ObservableCollection raises
/// one event per item, which is fine for a few entries but causes hundreds of
/// milliseconds of UI-thread work when populating large lists (e.g. 2000+ anime
/// from the local cache on startup).
///
/// Drop-in replacement: still IS an ObservableCollection&lt;T&gt;, all existing
/// consumers (bindings, foreach, indexer, +=/-= on CollectionChanged) keep working.
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public BulkObservableCollection() { }

    public BulkObservableCollection(IEnumerable<T> items) : base(items) { }

    /// <summary>
    /// Append all items, then raise a single Reset notification. Use this on the
    /// UI thread when you need to seed/replace large amounts of data without
    /// stalling animations.
    /// </summary>
    public void AddRange(IEnumerable<T> items)
    {
        if (items == null) return;
        CheckReentrancy();

        bool any = false;
        foreach (var item in items)
        {
            Items.Add(item);
            any = true;
        }
        if (!any) return;

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Clear and replace contents in a single Reset notification.
    /// Equivalent to Clear() + AddRange(items) but emits only one event.
    /// </summary>
    public void Reset(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        if (items != null)
        {
            foreach (var item in items) Items.Add(item);
        }
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
