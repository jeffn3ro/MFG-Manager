using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace MfgManager.Core.Collections;

/// <summary>
/// A live view of a list: filtered, sorted and grouped, without changing the list itself.
/// It does the job of WPF's ICollectionView, which Avalonia doesn't have, and it knows nothing about
/// the screen or about games, so it works for any kind of item and can be tested on its own.
///
/// <code>
/// var view = new GroupedView&lt;Game&gt;(games)
/// {
///     Filter = game => game.HasFrameGen,
///     Sort = Comparer&lt;Game&gt;.Create((a, b) => string.Compare(a.Name, b.Name)),
///     GroupBy = game => [game.Launcher.ToString()],
/// };
/// </code>
/// Show <see cref="Groups"/> on screen. The view updates when items are added or removed, or when you change
/// its settings. If something inside an item changes, call <see cref="Refresh"/>.
/// </summary>
public sealed class GroupedView<T> : INotifyPropertyChanged, IDisposable where T : class
{
    private readonly ObservableCollection<T> _source;
    private readonly List<T> _items = [];
    private Func<T, bool>? _filter;
    private IComparer<T>? _sort;
    private Func<T, IEnumerable<string>>? _groupBy;
    private IComparer<string> _groupOrder = StringComparer.OrdinalIgnoreCase;
    private int _deferDepth;
    private bool _refreshPending;

    public GroupedView(ObservableCollection<T> source)
    {
        _source = source;
        _source.CollectionChanged += OnSourceChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Which items to show. Null shows everything.</summary>
    public Func<T, bool>? Filter
    {
        get => _filter;
        set { _filter = value; Refresh(); }
    }

    /// <summary>The order of items, inside each group. Null keeps the list's own order.</summary>
    public IComparer<T>? Sort
    {
        get => _sort;
        set { _sort = value; Refresh(); }
    }

    /// <summary>
    /// The group name(s) of an item. An item with several names appears in each of those groups.
    /// Null means no grouping: everything goes into one group without a name.
    /// </summary>
    public Func<T, IEnumerable<string>>? GroupBy
    {
        get => _groupBy;
        set { _groupBy = value; Refresh(); }
    }

    /// <summary>The order of the groups. Alphabetical unless you set your own.</summary>
    public IComparer<string> GroupOrder
    {
        get => _groupOrder;
        set { _groupOrder = value; Refresh(); }
    }

    /// <summary>The groups to show. Groups stay the same objects between updates, so the screen only redraws what changed.</summary>
    public ObservableCollection<ViewGroup<T>> Groups { get; } = [];

    /// <summary>Everything that passes the filter, sorted, each item once.</summary>
    public IReadOnlyList<T> Items => _items;

    public int Count => _items.Count;
    public bool IsEmpty => _items.Count == 0;

    /// <summary>Works out the view again. Use it after something inside an item changed.</summary>
    public void Refresh()
    {
        if (_deferDepth > 0)
        {
            _refreshPending = true;
            return;
        }
        _refreshPending = false;

        int wasCount = _items.Count;
        _items.Clear();
        _items.AddRange(_filter is null ? _source : _source.Where(_filter));
        if (_sort is not null) StableSort(_items, _sort);

        SyncGroups();

        if (wasCount != _items.Count)
        {
            OnPropertyChanged(nameof(Count));
            OnPropertyChanged(nameof(IsEmpty));
        }
        OnPropertyChanged(nameof(Items));
    }

    /// <summary>
    /// Pauses updates until the returned object is disposed, then updates once.
    /// Use it around many changes: <c>using (view.DeferRefresh()) { ... }</c>
    /// </summary>
    public IDisposable DeferRefresh()
    {
        _deferDepth++;
        return new DeferScope(this);
    }

    public void Dispose() => _source.CollectionChanged -= OnSourceChanged;

    private void SyncGroups()
    {
        var wanted = new Dictionary<string, List<T>>();
        if (_groupBy is null)
        {
            if (_items.Count > 0) wanted[ViewGroup<T>.NoName] = _items;
        }
        else
        {
            foreach (T item in _items)
                foreach (string name in _groupBy(item).Distinct())
                {
                    if (!wanted.TryGetValue(name, out List<T>? members)) wanted[name] = members = [];
                    members.Add(item);
                }
        }

        var existing = Groups.ToDictionary(group => group.Name);
        var ordered = wanted.Keys.Order(_groupOrder)
            .Select(name => existing.GetValueOrDefault(name) ?? new ViewGroup<T>(name))
            .ToList();
        ListSync.Sync(Groups, ordered);

        foreach (ViewGroup<T> group in Groups) group.SetItems(wanted[group.Name]);
    }

    private static void StableSort(List<T> items, IComparer<T> comparer)
    {
        // List.Sort isn't stable; keeping equal items in their original order stops cards from jumping around.
        var sorted = items.OrderBy(item => item, comparer).ToList();
        items.Clear();
        items.AddRange(sorted);
    }

    private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();

    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private sealed class DeferScope(GroupedView<T> view) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            view._deferDepth--;
            if (view._deferDepth == 0 && view._refreshPending) view.Refresh();
        }
    }
}
