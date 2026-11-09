using MfgManager.Core.Models;
using MfgManager.Core.Scanning;

namespace MfgManager.Tests.Core;

public class GameInspectorTests
{
    private static Game NewGame(string folder, Launcher launcher = Launcher.Steam) =>
        new() { Name = "Test", Launcher = launcher, Folder = folder };

    [Fact]
    public void Unreal_shipping_exe_wins_over_the_small_launcher_exe()
    {
        using var temp = new TempFolder();
        // Bigger, but only the launcher stub.
        temp.File("Game.exe", new string('x', 5000));
        string shipping = temp.File("Game/Binaries/Win64/Game-Win64-Shipping.exe", "x");
        Game game = NewGame(temp.Path);

        GameInspector.Inspect(game);

        Assert.Equal(shipping, game.Exe);
        Assert.Equal(Path.GetDirectoryName(shipping), game.ExeFolder);
    }

    [Fact]
    public void Drm_is_swapped_in_as_a_new_list_never_changed_in_place()
    {
        // Change .exe checks a game in the background while the window may be reading its DRM.
        using var temp = new TempFolder();
        temp.File("Game.exe", "x");
        temp.File("steam_api64.dll");
        Game game = NewGame(temp.Path);
        List<string> before = game.Drm;

        GameInspector.Inspect(game);

        Assert.Empty(before);
        Assert.NotSame(before, game.Drm);
        Assert.Equal(["Steamworks"], game.Drm);
    }

    [Fact]
    public void Helper_exes_are_never_picked()
    {
        using var temp = new TempFolder();
        temp.File("UnityCrashHandler64.exe", new string('x', 9000));
        temp.File("unins000.exe", new string('x', 9000));
        string real = temp.File("MyGame.exe", "x");
        Game game = NewGame(temp.Path);

        GameInspector.Inspect(game);

        Assert.Equal(real, game.Exe);
    }

    [Fact]
    public void Cyberpunk_picks_the_game_not_redmods_tools_or_gogs_prelauncher()
    {
        using var temp = new TempFolder();
        // Shader compiler, bigger than the game.
        temp.File("tools/redmod/bin/scc.exe", new string('x', 9000));
        string prelauncher = temp.File("REDprelauncher.exe", new string('x', 9000));
        string real = temp.File("bin/x64/Cyberpunk2077.exe", new string('x', 5000));
        // GOG's registry hint.
        var game = new Game { Name = "Cyberpunk 2077", Launcher = Launcher.Gog, Folder = temp.Path, ExeHint = prelauncher };

        GameInspector.Inspect(game);

        Assert.Equal(real, game.Exe);
    }

    [Fact]
    public void Launcher_hint_still_wins_when_it_is_the_game()
    {
        using var temp = new TempFolder();
        temp.File("Big.exe", new string('x', 9000));
        string hinted = temp.File("bin/Game.exe", "x");
        var game = new Game { Name = "Test", Launcher = Launcher.Gog, Folder = temp.Path, ExeHint = hinted };

        GameInspector.Inspect(game);

        Assert.Equal(hinted, game.Exe);
    }

    [Fact]
    public void Exe_picked_by_the_user_wins()
    {
        using var temp = new TempFolder();
        temp.File("Big.exe", new string('x', 9000));
        string mine = temp.File("bin/Small.exe", "x");
        Game game = NewGame(temp.Path);

        GameInspector.Inspect(game, exeOverride: mine);

        Assert.Equal(mine, game.Exe);
    }

    [Theory]
    [InlineData("elsewhere/Other.exe")]
    // Only starts with the game folder's name.
    [InlineData("game2/Other.exe")]
    public void A_saved_exe_outside_the_game_folder_is_ignored(string savedExe)
    {
        // settings.json can be changed by any program, and the mod goes next to this .exe.
        using var temp = new TempFolder();
        string real = temp.File("game/Game.exe", "x");
        string saved = temp.File(savedExe, "x");
        Game game = NewGame(Path.GetDirectoryName(real)!);

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
        Game game = NewGame(temp.Path);

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
        Game game = NewGame(temp.Path, launcher);

        GameInspector.Inspect(game);

        Assert.Equal([drm], game.Drm);
    }

    [Fact]
    public void Battle_net_games_always_count_as_having_anti_cheat()
    {
        using var temp = new TempFolder();
        temp.File("Diablo IV.exe");
        Game game = NewGame(temp.Path, Launcher.BattleNet);

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
        foreach (string file in new[] { "nvngx_dlss.dll", "sl.common.dll", "sl.dlss.dll", "sl.interposer.dll", "sl.nis.dll", "sl.nrd.dll", "sl.reflex.dll" })
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
        Game game = NewGame(temp.Path, Launcher.Ubisoft);

        GameInspector.Inspect(game);

        Assert.False(game.HasFrameGen);
        Assert.Null(game.AntiCheat);
        Assert.Equal(["Ubisoft Connect"], game.Drm);
    }
}
