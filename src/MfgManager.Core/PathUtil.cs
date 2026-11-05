namespace MfgManager.Core;

public static class PathUtil
{
    /// <summary>Compares paths the way this system does: Windows ignores upper and lower case, Linux doesn't.</summary>
    public static StringComparer Comparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>"c:/games/x/" becomes "c:\games\x", so the same folder always compares equal. A drive or root folder keeps its slash.</summary>
    public static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public static bool SameFolder(string? a, string? b) =>
        a is not null && b is not null && string.Equals(Normalize(a), Normalize(b), Comparison);

    /// <summary>True when <paramref name="path"/> is somewhere inside <paramref name="folder"/> ("..\" tricks don't count).</summary>
    public static bool IsInside(string path, string folder)
    {
        var root = Normalize(folder);
        if (!Path.EndsInDirectorySeparator(root)) root += Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, Comparison);
    }

    /// <summary>
    /// The names of the files in a folder, ignoring upper and lower case. Games on Linux run through Wine, which
    /// treats "Version.dll" and "version.dll" as the same file even though Linux doesn't, so we must too.
    /// </summary>
    public static HashSet<string> FileNamesIn(string folder) =>
        Directory.Exists(folder)
            ? new HashSet<string>(Directory.EnumerateFiles(folder).Select(file => Path.GetFileName(file)!), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
