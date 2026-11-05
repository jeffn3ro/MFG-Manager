using MfgManager.Core;

namespace MfgManager.Tests.Core;

public class PathUtilTests
{
    [Fact]
    public void IsInside_is_not_fooled_by_dots_or_lookalike_names()
    {
        using var temp = new TempFolder();
        string mods = Path.Combine(temp.Path, "Mods");

        Assert.True(PathUtil.IsInside(Path.Combine(mods, "a", "mod.dll"), mods));
        Assert.False(PathUtil.IsInside(Path.Combine(mods, "..", "evil.dll"), mods));
        Assert.False(PathUtil.IsInside(Path.Combine(temp.Path, "Mods-evil", "mod.dll"), mods));
        Assert.False(PathUtil.IsInside(mods, mods));
    }

    [Fact]
    public void Normalize_keeps_the_root_and_drops_other_trailing_slashes()
    {
        string root = Path.GetPathRoot(Path.GetTempPath())!;

        Assert.Equal(root, PathUtil.Normalize(root));
        Assert.Equal(Path.Combine(root, "games"), PathUtil.Normalize(Path.Combine(root, "games") + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void FileNamesIn_ignores_upper_and_lower_case()
    {
        using var temp = new TempFolder();
        temp.File("Version.DLL");

        Assert.Contains("version.dll", PathUtil.FileNamesIn(temp.Path));
        Assert.Empty(PathUtil.FileNamesIn(Path.Combine(temp.Path, "missing")));
    }
}
