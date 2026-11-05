using MfgManager.Core;
using MfgManager.Core.Mod;
using MfgManager.Core.Models;
using MfgManager.Core.Platform;
using MfgManager.Core.Scanning.Launchers;
using MfgManager.Core.Settings;

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
        var folder = Path.Combine(temp.Path, "a", "b");

        AppPaths.EnsureFolder(folder);

        Assert.True(Directory.Exists(folder));
    }
}

public class KnownFoldersTests
{
    [Theory]
    [InlineData("XDG_DOWNLOAD_DIR=\"$HOME/Nedlastinger\"", "/home/me/Nedlastinger")]   // translated folder names work
    [InlineData("# comment\nXDG_DESKTOP_DIR=\"$HOME/Desktop\"\nXDG_DOWNLOAD_DIR=\"$HOME/Downloads\"\n", "/home/me/Downloads")]
    [InlineData("XDG_DESKTOP_DIR=\"$HOME/Desktop\"", null)]
    [InlineData("", null)]
    public void Reads_the_Linux_downloads_folder(string text, string? expected) =>
        Assert.Equal(expected?.Replace('/', Path.DirectorySeparatorChar), KnownFolders.ParseUserDirs(text, "/home/me".Replace('/', Path.DirectorySeparatorChar))?.Replace('/', Path.DirectorySeparatorChar));
}

public class DesktopShellTests
{
    private const string Url = "https://github.com/x/y";

    [Fact]
    public void Windows_opens_web_pages_through_Explorer_so_the_browser_does_not_run_as_admin()
    {
        var command = DesktopShell.WebPageCommand(Url, isWindows: true);

        Assert.Equal("explorer.exe", command.FileName);
        Assert.Equal(Url, Assert.Single(command.ArgumentList));
        Assert.False(command.UseShellExecute);
    }

    [Fact]
    public void Linux_uses_xdg_open_and_never_a_shell()
    {
        var command = DesktopShell.WebPageCommand("https://x/;rm -rf ~", isWindows: false);

        Assert.Equal("xdg-open", command.FileName);
        Assert.Equal("https://x/;rm -rf ~", Assert.Single(command.ArgumentList));   // one argument, never run as a command
        Assert.False(command.UseShellExecute);
    }

    [Fact]
    public void Reveal_selects_the_file_on_Windows_and_opens_its_folder_on_Linux()
    {
        Assert.Equal("/select,\"C:\\Games\\a.exe\"", DesktopShell.RevealCommand(@"C:\Games\a.exe", isWindows: true).Arguments);

        var folder = Path.Combine("games", "x");
        Assert.Equal(folder, Assert.Single(DesktopShell.RevealCommand(Path.Combine(folder, "a.exe"), isWindows: false).ArgumentList));
    }
}

public class GameProcessesTests
{
    [Theory]
    [InlineData("Game", "Game.exe", true)]                      // Windows
    [InlineData("game", "Game.exe", true)]
    [InlineData("Game.exe", "Game.exe", true)]                  // Proton
    [InlineData("Cyberpunk2077.e", "Cyberpunk2077.exe", true)]  // Linux keeps only 15 letters
    [InlineData("Cyberpunk2077", "Cyberpunk2077.exe", true)]
    [InlineData("Cyberpunk", "Cyberpunk2077.exe", false)]       // shorter than 15 letters, so it's a different program
    [InlineData("Other", "Game.exe", false)]
    [InlineData("", "Game.exe", false)]
    [InlineData(null, "Game.exe", false)]
    public void Matches_a_process_to_a_game(string? process, string exe, bool expected) =>
        Assert.Equal(expected, GameProcesses.Matches(process, exe));
}

public class PathUtilTests
{
    [Fact]
    public void IsInside_is_not_fooled_by_dots_or_lookalike_names()
    {
        using var temp = new TempFolder();
        var mods = Path.Combine(temp.Path, "Mods");

        Assert.True(PathUtil.IsInside(Path.Combine(mods, "a", "mod.dll"), mods));
        Assert.False(PathUtil.IsInside(Path.Combine(mods, "..", "evil.dll"), mods));
        Assert.False(PathUtil.IsInside(Path.Combine(temp.Path, "Mods-evil", "mod.dll"), mods));
        Assert.False(PathUtil.IsInside(mods, mods));
    }

    [Fact]
    public void Normalize_keeps_the_root_and_drops_other_trailing_slashes()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;

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

public class WineCaseTests
{
    [Fact]
    public void A_game_file_named_with_other_capitals_still_counts_as_taken()
    {
        // Linux would allow both Version.dll and version.dll, but Wine would see them as one file. The game's file must win.
        using var temp = new TempFolder();
        var exe = temp.File("game/Game.exe");
        temp.File("game/Version.DLL", "the game's own file");
        var game = new Game { Name = "G", Launcher = Launcher.Steam, Folder = Path.GetDirectoryName(exe)!, Exe = exe };
        var installer = new ModInstaller(new AppSettings(), () => { }, temp.Folder("removed"), FakeMod.ReadVersion);

        Assert.DoesNotContain("version.dll", installer.GetFreeNames(game));
        Assert.Throws<InvalidOperationException>(() => installer.Install(game, FakeMod.Create(temp, "1.7.0"), "version.dll"));
        Assert.Equal("the game's own file", File.ReadAllText(Path.Combine(game.ExeFolder!, "Version.DLL")));
    }
}

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
        var steam = temp.Folder("Steam");
        void App(int id, string name, string dir)
        {
            temp.File($"Steam/steamapps/appmanifest_{id}.acf", Acf(id, name, dir));
            temp.Folder($"Steam/steamapps/common/{dir}");
        }
        App(1493710, "Proton Experimental", "Proton - Experimental");
        App(2348590, "Proton 8.0", "Proton 8.0");
        App(1628350, "Steam Linux Runtime 3.0 (sniper)", "SteamLinuxRuntime_sniper");
        App(1091500, "Cyberpunk 2077", "Cyberpunk 2077");

        var game = Assert.Single(SteamLibrary.FindGames(steam));

        Assert.Equal("Cyberpunk 2077", game.Name);
    }

    [Fact]
    public void Heroic_finds_Epic_and_GOG_games_and_skips_DLC_and_uninstalled_games()
    {
        using var temp = new TempFolder();
        var epic = temp.Folder("Games/Heroic/Alan Wake 2");
        var gog = temp.Folder("Games/Heroic/Cyberpunk 2077");
        var dlc = temp.Folder("Games/Heroic/Some DLC");
        static string Json(string path) => path.Replace(@"\", @"\\");

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
    [InlineData("{ \"a\": 5 }")]
    [InlineData("{ \"installed\": 5 }")]
    [InlineData("{ \"installed\": [1, \"x\"] }")]
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

public class SettingsThemeTests
{
    [Fact]
    public void Theme_is_saved_and_old_settings_files_still_load()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "settings.json");
        var store = new SettingsStore(path);

        store.Save(new AppSettings { Theme = ThemeChoice.Light });
        Assert.Equal(ThemeChoice.Light, store.Load().Theme);

        File.WriteAllText(path, "{ \"ModDllPath\": null, \"ManualGames\": [], \"ExeOverrides\": {}, \"Installs\": [] }");   // written by 1.0.x
        Assert.Equal(ThemeChoice.System, store.Load().Theme);
    }
}
