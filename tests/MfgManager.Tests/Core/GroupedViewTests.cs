using System.Collections.ObjectModel;
using MfgManager.Core.Collections;

namespace MfgManager.Tests.Core;

public class GroupedViewTests
{
    private sealed record Item(string Name, string Kind, params string[] Tags);

    private static readonly IComparer<Item> ByName = Comparer<Item>.Create((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

    private static ObservableCollection<Item> Fruit() =>
    [
        new("Cherry", "Red", "Sweet"),
        new("Apple", "Red", "Sweet", "Sour"),
        new("Lime", "Green", "Sour"),
    ];

    private static string[] Names(IEnumerable<Item> items) => items.Select(i => i.Name).ToArray();

    [Fact]
    public void Without_settings_shows_everything_in_one_unnamed_group()
    {
        var view = new GroupedView<Item>(Fruit());
        view.Refresh();

        var group = Assert.Single(view.Groups);
        Assert.False(group.HasName);
        Assert.Equal(["Cherry", "Apple", "Lime"], Names(group.Items));
        Assert.Equal(3, view.Count);
        Assert.False(view.IsEmpty);
    }

    [Fact]
    public void Filter_sort_and_group_work_together()
    {
        var view = new GroupedView<Item>(Fruit()) { Sort = ByName, GroupBy = item => [item.Kind] };

        Assert.Equal(["Green", "Red"], view.Groups.Select(g => g.Name));
        Assert.Equal(["Apple", "Cherry"], Names(view.Groups[1].Items));

        view.Filter = item => item.Name != "Lime";

        Assert.Equal("Red", Assert.Single(view.Groups).Name);
        Assert.Equal(["Apple", "Cherry"], Names(view.Items));
    }

    [Fact]
    public void An_item_with_several_group_names_appears_in_each_group_but_once_in_Items()
    {
        var view = new GroupedView<Item>(Fruit()) { Sort = ByName, GroupBy = item => item.Tags };

        Assert.Equal(["Sour", "Sweet"], view.Groups.Select(g => g.Name));
        Assert.Equal(["Apple", "Lime"], Names(view.Groups[0].Items));
        Assert.Equal(["Apple", "Cherry"], Names(view.Groups[1].Items));
        Assert.Equal(3, view.Count);
    }

    [Fact]
    public void Follows_the_source_list()
    {
        var fruit = Fruit();
        var view = new GroupedView<Item>(fruit) { Sort = ByName };

        fruit.Add(new Item("Banana", "Yellow"));
        Assert.Equal(["Apple", "Banana", "Cherry", "Lime"], Names(view.Items));

        fruit.RemoveAt(0);
        Assert.Equal(["Apple", "Banana", "Lime"], Names(view.Items));
    }

    [Fact]
    public void Everything_filtered_out_leaves_no_groups()
    {
        var view = new GroupedView<Item>(Fruit()) { GroupBy = item => [item.Kind] };

        view.Filter = _ => false;

        Assert.Empty(view.Groups);
        Assert.True(view.IsEmpty);
    }

    [Fact]
    public void Removes_a_group_when_its_last_item_goes()
    {
        var fruit = Fruit();
        var view = new GroupedView<Item>(fruit) { GroupBy = item => [item.Kind] };

        fruit.Remove(fruit.Single(f => f.Name == "Lime"));

        Assert.Equal("Red", Assert.Single(view.Groups).Name);
    }

    [Fact]
    public void Groups_stay_the_same_objects_so_the_screen_only_redraws_what_changed()
    {
        var view = new GroupedView<Item>(Fruit()) { GroupBy = item => [item.Kind] };
        var red = view.Groups.Single(g => g.Name == "Red");
        var resets = 0;
        red.Items.CollectionChanged += (_, _) => resets++;

        view.Refresh();

        Assert.Same(red, view.Groups.Single(g => g.Name == "Red"));
        Assert.Equal(0, resets);   // nothing changed, so nothing is redrawn
    }

    [Fact]
    public void Custom_group_order_is_used()
    {
        var view = new GroupedView<Item>(Fruit())
        {
            GroupBy = item => [item.Kind],
            GroupOrder = Comparer<string>.Create((a, b) => string.Compare(b, a, StringComparison.Ordinal)),
        };

        Assert.Equal(["Red", "Green"], view.Groups.Select(g => g.Name));
    }

    [Fact]
    public void Equal_items_keep_their_original_order()
    {
        var view = new GroupedView<Item>(Fruit()) { Sort = Comparer<Item>.Create((_, _) => 0) };

        Assert.Equal(["Cherry", "Apple", "Lime"], Names(view.Items));
    }

    [Fact]
    public void DeferRefresh_updates_once_at_the_end()
    {
        var fruit = Fruit();
        var view = new GroupedView<Item>(fruit);
        view.Refresh();
        var updates = 0;
        view.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(view.Items)) updates++; };

        using (view.DeferRefresh())
        {
            fruit.Clear();
            fruit.Add(new Item("One", "X"));
            fruit.Add(new Item("Two", "X"));
            Assert.Equal(0, updates);
        }

        Assert.Equal(1, updates);
        Assert.Equal(2, view.Count);
    }

    [Fact]
    public void Tells_the_screen_when_the_count_and_group_count_change()
    {
        var view = new GroupedView<Item>(Fruit()) { GroupBy = item => [item.Kind] };
        var changed = new List<string?>();
        view.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var groupChanged = new List<string?>();
        view.Groups.Single(g => g.Name == "Red").PropertyChanged += (_, e) => groupChanged.Add(e.PropertyName);

        view.Filter = item => item.Name != "Apple";

        Assert.Contains(nameof(view.Count), changed);
        Assert.Contains(nameof(view.IsEmpty), changed);
        Assert.Contains("Count", groupChanged);
    }

    [Fact]
    public void Stops_listening_to_the_source_after_Dispose()
    {
        var fruit = Fruit();
        var view = new GroupedView<Item>(fruit);
        view.Refresh();

        view.Dispose();
        fruit.Clear();

        Assert.Equal(3, view.Count);
    }
}

public class ListSyncTests
{
    [Theory]
    [InlineData(new[] { "a", "b", "c" }, new[] { "a", "b", "c" }, 0)]   // same list: no changes at all
    [InlineData(new string[0], new[] { "a", "b" }, 2)]
    [InlineData(new[] { "a", "b" }, new string[0], 2)]
    [InlineData(new[] { "a", "b", "c" }, new[] { "c", "a", "b" }, 1)]   // one move, not a rebuild
    [InlineData(new[] { "a", "b", "c" }, new[] { "a", "x", "c" }, 2)]
    public void Makes_the_list_match_with_few_changes(string[] start, string[] wanted, int expectedChanges)
    {
        var list = new ObservableCollection<string>(start);
        var changes = 0;
        list.CollectionChanged += (_, _) => changes++;

        ListSync.Sync(list, wanted);

        Assert.Equal(wanted, list);
        Assert.Equal(expectedChanges, changes);
    }
}
