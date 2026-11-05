using MfgManager.Core.Models;
using MfgManager.Core.Scanning.Launchers;

namespace MfgManager.Tests.Core;

public class SteamLibraryTests
{
    [Fact]
    public void Finds_games_in_every_library_folder()
    {
        using var temp = new TempFolder();
        string steam = temp.Folder("Steam");
        string second = temp.Folder("SteamLibrary");
        temp.File("Steam/steamapps/libraryfolders.vdf", $"\"path\" \"{second.Replace("""\""", """\\""")}\"");
        temp.File("Steam/steamapps/appmanifest_10.acf", "\"appid\" \"10\"\n\"name\" \"Game One\"\n\"installdir\" \"GameOne\"");
        temp.Folder("Steam/steamapps/common/GameOne");
        temp.File("SteamLibrary/steamapps/appmanifest_20.acf", "\"appid\" \"20\"\n\"name\" \"Game Two\"\n\"installdir\" \"GameTwo\"");
        temp.Folder("SteamLibrary/steamapps/common/GameTwo");
        // A manifest whose folder was deleted must be skipped.
        temp.File("SteamLibrary/steamapps/appmanifest_30.acf", "\"appid\" \"30\"\n\"name\" \"Gone\"\n\"installdir\" \"Gone\"");

        var games = SteamLibrary.FindGames(steam).OrderBy(g => g.Name).ToList();

        Assert.Equal(["Game One", "Game Two"], games.Select(g => g.Name));
        Assert.Equal([10, 20], games.Select(g => g.SteamAppId));
        Assert.All(games, g => Assert.Equal(Launcher.Steam, g.Launcher));
    }

    [Fact]
    public void Skips_steam_tools_that_are_not_games()
    {
        using var temp = new TempFolder();
        string steam = temp.Folder("Steam");
        temp.File("Steam/steamapps/appmanifest_228980.acf", "\"appid\" \"228980\"\n\"name\" \"Steamworks Common Redistributables\"\n\"installdir\" \"Steamworks Shared\"");
        temp.Folder("Steam/steamapps/common/Steamworks Shared");
        temp.File("Steam/steamapps/appmanifest_250820.acf", "\"appid\" \"250820\"\n\"name\" \"SteamVR\"\n\"installdir\" \"SteamVR\"");
        temp.Folder("Steam/steamapps/common/SteamVR");
        temp.File("Steam/steamapps/appmanifest_10.acf", "\"appid\" \"10\"\n\"name\" \"Game One\"\n\"installdir\" \"GameOne\"");
        temp.Folder("Steam/steamapps/common/GameOne");

        Game game = Assert.Single(SteamLibrary.FindGames(steam));

        Assert.Equal("Game One", game.Name);
    }
}
