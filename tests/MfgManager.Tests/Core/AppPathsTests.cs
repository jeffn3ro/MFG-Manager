using MfgManager.Core.Platform;

namespace MfgManager.Tests.Core;

public class AppPathsTests
{
    [Fact]
    public void Mods_folder_is_next_to_the_app_on_Windows_and_in_your_data_folder_on_Linux()
    {
        Assert.Equal(Path.Combine("app", "Mods"), AppPaths.PickModsFolder(isWindows: true, "app", "data"));
        Assert.Equal(Path.Combine("data", "Mods"), AppPaths.PickModsFolder(isWindows: false, "app", "data"));
    }

    [Fact]
    public void EnsureFolder_creates_nested_folders()
    {
        using var temp = new TempFolder();
        string folder = Path.Combine(temp.Path, "a", "b");

        AppPaths.EnsureFolder(folder);

        Assert.True(Directory.Exists(folder));
    }
}
