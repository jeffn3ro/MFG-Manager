using MfgManager.Core.Models;
using MfgManager.Core.Scanning;
using MfgManager.Core.Scanning.Launchers;

namespace MfgManager.Tests.Core;

public class VdfTests
{
    [Fact]
    public void Reads_values_and_unescapes_paths()
    {
        var text = """
            "libraryfolders"
            {
                "0" { "path"   "C:\\Program Files (x86)\\Steam" }
                "1" { "path"   "E:\\SteamLibrary" }
            }
            """;

        Assert.Equal([@"C:\Program Files (x86)\Steam", @"E:\SteamLibrary"], Vdf.GetValues(text, "path"));
    }

    [Fact]
    public void Missing_key_gives_null() => Assert.Null(Vdf.GetValue("\"name\" \"x\"", "installdir"));
}

public class SteamLibraryTests
{
    [Fact]
    public void Finds_games_in_every_library_folder()
    {
        using var temp = new TempFolder();
        var steam = temp.Folder("Steam");
        var second = temp.Folder("SteamLibrary");
        temp.File("Steam/steamapps/libraryfolders.vdf", $"\"path\" \"{second.Replace(@"\", @"\\")}\"");
        temp.File("Steam/steamapps/appmanifest_10.acf", "\"appid\" \"10\"\n\"name\" \"Game One\"\n\"installdir\" \"GameOne\"");
        temp.Folder("Steam/steamapps/common/GameOne");
        temp.File("SteamLibrary/steamapps/appmanifest_20.acf", "\"appid\" \"20\"\n\"name\" \"Game Two\"\n\"installdir\" \"GameTwo\"");
        temp.Folder("SteamLibrary/steamapps/common/GameTwo");
        // a manifest whose folder was deleted must be skipped
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
        var steam = temp.Folder("Steam");
        temp.File("Steam/steamapps/appmanifest_228980.acf", "\"appid\" \"228980\"\n\"name\" \"Steamworks Common Redistributables\"\n\"installdir\" \"Steamworks Shared\"");
        temp.Folder("Steam/steamapps/common/Steamworks Shared");
        temp.File("Steam/steamapps/appmanifest_250820.acf", "\"appid\" \"250820\"\n\"name\" \"SteamVR\"\n\"installdir\" \"SteamVR\"");
        temp.Folder("Steam/steamapps/common/SteamVR");
        temp.File("Steam/steamapps/appmanifest_10.acf", "\"appid\" \"10\"\n\"name\" \"Game One\"\n\"installdir\" \"GameOne\"");
        temp.Folder("Steam/steamapps/common/GameOne");

        var game = Assert.Single(SteamLibrary.FindGames(steam));

        Assert.Equal("Game One", game.Name);
    }
}

public class OtherLauncherTests
{
    [Fact]
    public void Epic_reads_item_files_and_skips_broken_ones()
    {
        using var temp = new TempFolder();
        var gameFolder = temp.Folder("Epic Games/Cool Game");
        temp.File("manifests/a.item", $$"""{"DisplayName":"Cool Game","InstallLocation":"{{gameFolder.Replace(@"\", @"\\")}}","LaunchExecutable":"Cool.exe"}""");
        temp.File("manifests/b.item", "{ this is not json");

        var game = Assert.Single(EpicLibrary.FindGames(Path.Combine(temp.Path, "manifests")));

        Assert.Equal("Cool Game", game.Name);
        Assert.Equal(Path.Combine(gameFolder, "Cool.exe"), game.ExeHint);
    }

    [Fact]
    public void Gog_skips_dlc_that_shares_the_games_folder()
    {
        using var temp = new TempFolder();
        var folder = temp.Folder("GOG/Cyberpunk 2077");
        var prelauncher = Path.Combine(folder, "REDprelauncher.exe");
        GogEntry[] entries =
        [
            new("Cyberpunk 2077: Phantom Liberty", folder, "", "1423049311"),
            new("Cyberpunk 2077", folder, prelauncher, ""),
            new("Cyberpunk 2077 REDmod", folder, "", "1423049311"),
            new("Uninstalled Game", Path.Combine(temp.Path, "gone"), null, null),
        ];

        var game = Assert.Single(GogLibrary.FindGames(entries));

        Assert.Equal("Cyberpunk 2077", game.Name);
        Assert.Equal(Launcher.Gog, game.Launcher);
        Assert.Equal(prelauncher, game.ExeHint);
    }

    [Fact]
    public void Ubisoft_names_games_after_their_folder()
    {
        using var temp = new TempFolder();
        var folder = temp.Folder("Uplay Games/Assassin's Creed Mirage");

        var game = Assert.Single(UbisoftLibrary.FindGames([folder + "/", Path.Combine(temp.Path, "not installed")]));

        Assert.Equal("Assassin's Creed Mirage", game.Name);
        Assert.Equal(Launcher.Ubisoft, game.Launcher);
    }

    [Fact]
    public void Xbox_skips_the_GameSave_folder()
    {
        using var temp = new TempFolder();
        temp.Folder("XboxGames/Starfield");
        temp.Folder("XboxGames/GameSave");

        var game = Assert.Single(XboxLibrary.FindGames([temp.Path]));

        Assert.Equal("Starfield", game.Name);
    }
}

public class UninstallLibraryTests
{
    // These look exactly like the "Apps & features" entries on the PC this was built on.
    private static readonly UninstallEntry DiabloIV = new("Diablo IV", "Blizzard Entertainment", @"E:\Battle.net\Diablo IV",
        "\"C:\\ProgramData\\Battle.net\\Agent\\Blizzard Uninstaller.exe\" --lang=enUS --uid=fenris --displayname=\"Diablo IV\"");
    private static readonly UninstallEntry BattleNetItself = new("Battle.net", "Blizzard Entertainment", @"C:\Program Files (x86)\Battle.net",
        "\"C:\\ProgramData\\Battle.net\\Agent\\Blizzard Uninstaller.exe\" --lang=enGB --uid=battle.net --displayname=\"Battle.net\"");
    private static readonly UninstallEntry DeadSpace = new("Dead Space™", "Electronic Arts", @"E:\EA\Dead Space (2023)\",
        "\"C:\\Program Files\\Common Files\\EAInstaller\\Dead Space (2023)\\Cleanup.exe\" uninstall_game -autologging");
    private static readonly UninstallEntry EaAppItself = new("EA app", "Electronic Arts", null,
        "\"C:\\Program Files\\Electronic Arts\\EA Desktop\\EA Desktop\\EAUninstall.exe\" /uninstall");
    private static readonly UninstallEntry Gta5 = new("Grand Theft Auto V Enhanced", "Rockstar Games", @"E:\Rockstar Games\Grand Theft Auto V Enhanced",
        "\"E:\\Rockstar Games\\Grand Theft Auto V Enhanced\\uninstall.exe\" -enableFullMode -uninstall=gta5_gen9");
    private static readonly UninstallEntry RockstarLauncherItself = new("Rockstar Games Launcher", "Rockstar Games", @"C:\Program Files\Rockstar Games\Launcher",
        "\"C:\\Program Files\\Rockstar Games\\Launcher\\uninstall.exe\" -enableFullMode -uninstall=launcher");
    private static readonly UninstallEntry SomeApp = new("7-Zip", "Igor Pavlov", @"C:\Program Files\7-Zip", "C:\\Program Files\\7-Zip\\Uninstall.exe");

    [Fact]
    public void Recognises_each_launchers_games_but_not_the_launchers_themselves()
    {
        Assert.Equal(Launcher.BattleNet, UninstallLibrary.LauncherOf(DiabloIV));
        Assert.Equal(Launcher.Ea, UninstallLibrary.LauncherOf(DeadSpace));
        Assert.Equal(Launcher.Rockstar, UninstallLibrary.LauncherOf(Gta5));

        Assert.Null(UninstallLibrary.LauncherOf(BattleNetItself));
        Assert.Null(UninstallLibrary.LauncherOf(EaAppItself));
        Assert.Null(UninstallLibrary.LauncherOf(RockstarLauncherItself));
        Assert.Null(UninstallLibrary.LauncherOf(SomeApp));
    }

    [Fact]
    public void Makes_games_from_entries_whose_folder_exists()
    {
        using var temp = new TempFolder();
        var folder = temp.Folder("EA/Dead Space (2023)");
        var installed = DeadSpace with { InstallLocation = folder + @"\" };
        var uninstalledLater = Gta5 with { InstallLocation = Path.Combine(temp.Path, "gone") };

        var game = Assert.Single(UninstallLibrary.FindGames([installed, uninstalledLater, SomeApp with { InstallLocation = temp.Path }]));

        Assert.Equal("Dead Space", game.Name);   // ™ removed
        Assert.Equal(Launcher.Ea, game.Launcher);
        Assert.Equal(folder, game.Folder);
    }

    [Theory]
    [InlineData("Dead Space™", "Dead Space")]
    [InlineData("Command & Conquer(tm) 3 Kanes Wrath(tm)", "Command & Conquer 3 Kanes Wrath")]
    [InlineData("Diablo IV", "Diablo IV")]
    public void Cleans_trademark_signs_from_names(string raw, string clean) => Assert.Equal(clean, UninstallLibrary.CleanName(raw));
}

public class GameInspectorTests
{
    private static Game NewGame(string folder, Launcher launcher = Launcher.Steam) =>
        new() { Name = "Test", Launcher = launcher, Folder = folder };

    [Fact]
    public void Unreal_shipping_exe_wins_over_the_small_launcher_exe()
    {
        using var temp = new TempFolder();
        temp.File("Game.exe", new string('x', 5000));   // bigger, but only the launcher stub
        var shipping = temp.File("Game/Binaries/Win64/Game-Win64-Shipping.exe", "x");
        var game = NewGame(temp.Path);

        GameInspector.Inspect(game);

        Assert.Equal(shipping, game.Exe);
        Assert.Equal(Path.GetDirectoryName(shipping), game.ExeFolder);
    }

    [Fact]
    public void Helper_exes_are_never_picked()
    {
        using var temp = new TempFolder();
        temp.File("UnityCrashHandler64.exe", new string('x', 9000));
        temp.File("unins000.exe", new string('x', 9000));
        var real = temp.File("MyGame.exe", "x");
        var game = NewGame(temp.Path);

        GameInspector.Inspect(game);

        Assert.Equal(real, game.Exe);
    }

    [Fact]
    public void Cyberpunk_picks_the_game_not_redmods_tools_or_gogs_prelauncher()
    {
        using var temp = new TempFolder();
        temp.File("tools/redmod/bin/scc.exe", new string('x', 9000));   // shader compiler, bigger than the game
        var prelauncher = temp.File("REDprelauncher.exe", new string('x', 9000));
        var real = temp.File("bin/x64/Cyberpunk2077.exe", new string('x', 5000));
        var game = new Game { Name = "Cyberpunk 2077", Launcher = Launcher.Gog, Folder = temp.Path, ExeHint = prelauncher };   // GOG's registry hint

        GameInspector.Inspect(game);

        Assert.Equal(real, game.Exe);
    }

    [Fact]
    public void Launcher_hint_still_wins_when_it_is_the_game()
    {
        using var temp = new TempFolder();
        temp.File("Big.exe", new string('x', 9000));
        var hinted = temp.File("bin/Game.exe", "x");
        var game = new Game { Name = "Test", Launcher = Launcher.Gog, Folder = temp.Path, ExeHint = hinted };

        GameInspector.Inspect(game);

        Assert.Equal(hinted, game.Exe);
    }

    [Fact]
    public void Exe_picked_by_the_user_wins()
    {
        using var temp = new TempFolder();
        temp.File("Big.exe", new string('x', 9000));
        var mine = temp.File("bin/Small.exe", "x");
        var game = NewGame(temp.Path);

        GameInspector.Inspect(game, exeOverride: mine);

        Assert.Equal(mine, game.Exe);
    }

    [Theory]
    [InlineData("elsewhere/Other.exe")]
    [InlineData("game2/Other.exe")]   // only starts with the game folder's name
    public void A_saved_exe_outside_the_game_folder_is_ignored(string savedExe)
    {
        // settings.json can be changed by any program, and the mod goes next to this .exe.
        using var temp = new TempFolder();
        var real = temp.File("game/Game.exe", "x");
        var saved = temp.File(savedExe, "x");
        var game = NewGame(Path.GetDirectoryName(real)!);

        GameInspector.Inspect(game, exeOverride: saved);

        Assert.Equal(real, game.Exe);
    }

    [Fact]
    public void Detects_frame_gen_anti_cheat_and_drm()
    {
        using var temp = new TempFolder();
        temp.File("Game.exe");
        temp.File("NVStreamline/production/nvngx_dlssg.dll");
        temp.Folder("EasyAntiCheat");
        temp.File("steam_api64.dll");
        temp.File("EOSSDK-Win64-Shipping.dll");
        var game = NewGame(temp.Path);

        GameInspector.Inspect(game);

        Assert.True(game.HasFrameGen);
        Assert.Equal("Easy Anti-Cheat", game.AntiCheat);
        Assert.Equal(["Epic Online Services", "Steamworks"], game.Drm.Order());
    }

    [Theory]
    [InlineData(Launcher.BattleNet, "Battle.net")]
    [InlineData(Launcher.Ea, "EA app")]
    [InlineData(Launcher.Rockstar, "Rockstar Launcher")]
    public void Launcher_games_get_their_launcher_as_drm(Launcher launcher, string drm)
    {
        using var temp = new TempFolder();
        temp.File("Game.exe");
        var game = NewGame(temp.Path, launcher);

        GameInspector.Inspect(game);

        Assert.Equal([drm], game.Drm);
    }

    [Fact]
    public void Battle_net_games_always_count_as_having_anti_cheat()
    {
        using var temp = new TempFolder();
        temp.File("Diablo IV.exe");
        var game = NewGame(temp.Path, Launcher.BattleNet);

        GameInspector.Inspect(game);

        Assert.Equal("Blizzard anti-cheat (built in)", game.AntiCheat);
    }

    [Theory]
    [InlineData("nvngx_dlssg.dll")]
    [InlineData("sl.dlss_g.dll")]
    [InlineData("NVStreamline/production/SL.DLSS_G.DLL")]
    public void Either_frame_gen_file_counts_wherever_it_is(string file)
    {
        using var temp = new TempFolder();
        temp.File("Game.exe");
        temp.File(file);
        var game = new Game { Name = "Game", Launcher = Launcher.Steam, Folder = temp.Path };

        GameInspector.Inspect(game);

        Assert.True(game.HasFrameGen);
    }

    [Fact]
    public void Streamline_without_its_frame_gen_parts_is_not_frame_gen()
    {
        // The NVIDIA files Dead Space (2023) really ships: DLSS upscaling and Reflex, but no Frame Generation.
        using var temp = new TempFolder();
        temp.File("Dead Space.exe");
        foreach (var file in new[] { "nvngx_dlss.dll", "sl.common.dll", "sl.dlss.dll", "sl.interposer.dll", "sl.nis.dll", "sl.nrd.dll", "sl.reflex.dll" })
            temp.File(file);
        var game = new Game { Name = "Dead Space", Launcher = Launcher.Ea, Folder = temp.Path };

        GameInspector.Inspect(game);

        Assert.False(game.HasFrameGen);
    }

    [Fact]
    public void A_renamed_backup_of_the_frame_gen_file_doesnt_count()
    {
        // DLSS tools leave these behind.
        using var temp = new TempFolder();
        temp.File("Game.exe");
        temp.File("nvngx_dlssg.dll.dlsss");
        var game = new Game { Name = "Game", Launcher = Launcher.Steam, Folder = temp.Path };

        GameInspector.Inspect(game);

        Assert.False(game.HasFrameGen);
    }

    [Fact]
    public void Plain_game_has_no_frame_gen_or_anti_cheat_but_ubisoft_games_get_ubisoft_connect()
    {
        using var temp = new TempFolder();
        temp.File("Game.exe");
        var game = NewGame(temp.Path, Launcher.Ubisoft);

        GameInspector.Inspect(game);

        Assert.False(game.HasFrameGen);
        Assert.Null(game.AntiCheat);
        Assert.Equal(["Ubisoft Connect"], game.Drm);
    }
}
