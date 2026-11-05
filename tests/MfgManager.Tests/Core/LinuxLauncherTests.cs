using MfgManager.Core;
using MfgManager.Core.Models;
using MfgManager.Core.Scanning.Launchers;

namespace MfgManager.Tests.Core;

public class LinuxLauncherTests
{
    private static string Acf(int id, string name, string dir) => $"\"appid\" \"{id}\"\n\"name\" \"{name}\"\n\"installdir\" \"{dir}\"";

    [Fact]
    public void Finds_Steam_in_the_usual_Linux_places_then_Flatpak()
    {
        using var temp = new TempFolder();
        Assert.Null(SteamLibrary.FindOnLinux(temp.Path));

        temp.Folder(".var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps");
        Assert.Contains("com.valvesoftware.Steam", SteamLibrary.FindOnLinux(temp.Path));

        temp.Folder(".local/share/Steam/steamapps");
        Assert.DoesNotContain("com.valvesoftware.Steam", SteamLibrary.FindOnLinux(temp.Path));
    }

    [Fact]
    public void Proton_and_the_Steam_runtimes_are_not_games()
    {
        using var temp = new TempFolder();
        string steam = temp.Folder("Steam");
        void App(int id, string name, string dir)
        {
            temp.File($"Steam/steamapps/appmanifest_{id}.acf", Acf(id, name, dir));
            temp.Folder($"Steam/steamapps/common/{dir}");
        }
        App(1493710, "Proton Experimental", "Proton - Experimental");
        App(2348590, "Proton 8.0", "Proton 8.0");
        App(1628350, "Steam Linux Runtime 3.0 (sniper)", "SteamLinuxRuntime_sniper");
        App(1091500, "Cyberpunk 2077", "Cyberpunk 2077");

        Game game = Assert.Single(SteamLibrary.FindGames(steam));

        Assert.Equal("Cyberpunk 2077", game.Name);
    }

    [Fact]
    public void Heroic_finds_Epic_and_GOG_games_and_skips_DLC_and_uninstalled_games()
    {
        using var temp = new TempFolder();
        string epic = temp.Folder("Games/Heroic/Alan Wake 2");
        string gog = temp.Folder("Games/Heroic/Cyberpunk 2077");
        string dlc = temp.Folder("Games/Heroic/Some DLC");
        static string Json(string path) => path.Replace("""\""", """\\""");

        temp.File("heroic/legendaryConfig/legendary/installed.json", $$"""
            {
              "Alan": { "title": "Alan Wake 2", "install_path": "{{Json(epic)}}", "executable": "bin\\AW2.exe" },
              "Gone": { "title": "Gone", "install_path": "{{Json(Path.Combine(temp.Path, "gone"))}}" }
            }
            """);
        temp.File("heroic/gog_store/installed.json", $$"""
            { "installed": [
              { "install_path": "{{Json(gog)}}", "is_dlc": false },
              { "install_path": "{{Json(dlc)}}", "is_dlc": true }
            ] }
            """);

        var games = HeroicLibrary.FindGames([Path.Combine(temp.Path, "heroic")]).OrderBy(g => g.Name).ToList();

        Assert.Equal(["Alan Wake 2", "Cyberpunk 2077"], games.Select(g => g.Name));
        Assert.Equal(Launcher.Epic, games[0].Launcher);
        Assert.Equal(Path.Combine(epic, "bin", "AW2.exe"), PathUtil.Normalize(games[0].ExeHint!));
        Assert.Equal(Launcher.Gog, games[1].Launcher);
    }

    [Theory]
    [InlineData("not json {")]
    [InlineData("[1, 2, 3]")]
    [InlineData("""{ "a": 5 }""")]
    [InlineData("""{ "installed": 5 }""")]
    [InlineData("""{ "installed": [1, "x"] }""")]
    public void Heroic_files_with_unexpected_content_are_ignored(string content)
    {
        using var temp = new TempFolder();
        temp.File("heroic/legendaryConfig/legendary/installed.json", content);
        temp.File("heroic/gog_store/installed.json", content);

        Assert.Empty(HeroicLibrary.FindGames([Path.Combine(temp.Path, "heroic")]));
    }

    [Fact]
    public void Missing_Heroic_folder_finds_nothing()
    {
        using var temp = new TempFolder();

        Assert.Empty(HeroicLibrary.FindGames([Path.Combine(temp.Path, "nope")]));
    }
}
