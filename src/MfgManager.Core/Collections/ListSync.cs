using System.Collections.ObjectModel;

namespace MfgManager.Core.Collections;

public static class ListSync
{
    /// <summary>
    /// Makes a list on screen match the wanted list using as few changes as possible, instead of clearing and refilling it.
    /// That way what you selected stays selected, the scroll position stays, and pictures aren't reloaded.
    /// </summary>
    public static void Sync<T>(ObservableCollection<T> list, IReadOnlyList<T> wanted)
    {
        var keep = new HashSet<T>(wanted);
        for (var i = list.Count - 1; i >= 0; i--)
            if (!keep.Contains(list[i])) list.RemoveAt(i);

        for (var i = 0; i < wanted.Count; i++)
        {
            if (i < list.Count && EqualityComparer<T>.Default.Equals(list[i], wanted[i])) continue;

            var at = list.IndexOf(wanted[i]);
            if (at == -1) list.Insert(i, wanted[i]);
            else list.Move(at, i);
        }
    }
}
