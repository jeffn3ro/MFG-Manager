using System.Collections.ObjectModel;
using MfgManager.Core.Collections;

namespace MfgManager.Tests.Core;

public class ListSyncTests
{
    [Theory]
    // Same list: no changes at all.
    [InlineData(new[] { "a", "b", "c" }, new[] { "a", "b", "c" }, 0)]
    [InlineData(new string[0], new[] { "a", "b" }, 2)]
    [InlineData(new[] { "a", "b" }, new string[0], 2)]
    // One move, not a rebuild.
    [InlineData(new[] { "a", "b", "c" }, new[] { "c", "a", "b" }, 1)]
    [InlineData(new[] { "a", "b", "c" }, new[] { "a", "x", "c" }, 2)]
    public void Makes_the_list_match_with_few_changes(string[] start, string[] wanted, int expectedChanges)
    {
        var list = new ObservableCollection<string>(start);
        int changes = 0;
        list.CollectionChanged += (_, _) => changes++;

        ListSync.Sync(list, wanted);

        Assert.Equal(wanted, list);
        Assert.Equal(expectedChanges, changes);
    }
}
