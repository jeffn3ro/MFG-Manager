using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MfgManager.Core.Collections;

/// <summary>One group in a <see cref="GroupedView{T}"/>: a name and its items.</summary>
public sealed class ViewGroup<T> : INotifyPropertyChanged where T : class
{
    /// <summary>The name of the single group you get when there's no grouping.</summary>
    public const string NoName = "";

    internal ViewGroup(string name) => Name = name;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    /// <summary>False for the single group you get without grouping, so the screen can leave out its heading.</summary>
    public bool HasName => Name.Length > 0;

    public ObservableCollection<T> Items { get; } = [];
    public int Count => Items.Count;

    internal void SetItems(IReadOnlyList<T> items)
    {
        int before = Items.Count;
        ListSync.Sync(Items, items);
        if (before != Items.Count) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
    }
}
