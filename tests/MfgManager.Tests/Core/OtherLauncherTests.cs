using MfgManager.Core.Models;
using MfgManager.Core.Scanning.Launchers;

namespace MfgManager.Tests.Core;

public class OtherLauncherTests
{
    [Fact]
    public void Epic_reads_item_files_and_skips_broken_ones()
    {
        using var temp = new TempFolder();
        string gameFolder = temp.Folder("Epic Games/Cool Game");
        temp.File("manifests/a.item", $$"""{"DisplayName":"Cool Game","InstallLocation":"{{gameFolder.Replace("""\""", """\\""")}}","LaunchExecutable":"Cool.exe"}""");
        temp.File("manifests/b.item", "{ this is not json");

        Game game = Assert.Single(EpicLibrary.FindGames(Path.Combine(temp.Path, "manifests")));

        Assert.Equal("Cool Game", game.Name);
        Assert.Equal(Path.Combine(gameFolder, "Cool.exe"), game.ExeHint);
    }

    [Fact]
    public void Gog_skips_dlc_that_shares_the_games_folder()
    {
        using var temp = new TempFolder();
        string folder = temp.Folder("GOG/Cyberpunk 2077");
        string prelauncher = Path.Combine(folder, "REDprelauncher.exe");
        GogEntry[] entries =
        [
            new("Cyberpunk 2077: Phantom Liberty", folder, "", "1423049311"),
            new("Cyberpunk 2077", folder, prelauncher, ""),
            new("Cyberpunk 2077 REDmod", folder, "", "1423049311"),
            new("Uninstalled Game", Path.Combine(temp.Path, "gone"), null, null),
        ];

        Game game = Assert.Single(GogLibrary.FindGames(entries));

        Assert.Equal("Cyberpunk 2077", game.Name);
        Assert.Equal(Launcher.Gog, game.Launcher);
        Assert.Equal(prelauncher, game.ExeHint);
    }

    [Fact]
    public void Ubisoft_names_games_after_their_folder()
    {
        using var temp = new TempFolder();
        string folder = temp.Folder("Uplay Games/Assassin's Creed Mirage");

        Game game = Assert.Single(UbisoftLibrary.FindGames([folder + "/", Path.Combine(temp.Path, "not installed")]));

        Assert.Equal("Assassin's Creed Mirage", game.Name);
        Assert.Equal(Launcher.Ubisoft, game.Launcher);
    }

    [Fact]
    public void Xbox_skips_the_GameSave_folder()
    {
        using var temp = new TempFolder();
        temp.Folder("XboxGames/Starfield");
        temp.Folder("XboxGames/GameSave");

        Game game = Assert.Single(XboxLibrary.FindGames([temp.Path]));

        Assert.Equal("Starfield", game.Name);
    }
}
