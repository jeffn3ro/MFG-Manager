using MfgManager.Core.Mod;
using MfgManager.Core.Models;
using MfgManager.Core.Settings;

namespace MfgManager.Tests.Core;

public sealed class ModInstallerTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly AppSettings _settings = new();
    private readonly ModInstaller _installer;
    private readonly Game _game;
    private readonly string _gameFolder;
    private int _saves;

    public ModInstallerTests()
    {
        _installer = new ModInstaller(_settings, () => _saves++, Path.Combine(_temp.Path, "removed"), FakeMod.ReadVersion);
        var exe = _temp.File("games/Cool Game/Cool.exe", "game");
        _gameFolder = Path.GetDirectoryName(exe)!;
        _game = new Game { Name = "Cool Game", Launcher = Launcher.Steam, Folder = _gameFolder, Exe = exe };
    }

    public void Dispose() => _temp.Dispose();

    private string InGame(string name) => Path.Combine(_gameFolder, name);

    [Fact]
    public void Auto_installs_as_version_dll_and_remembers_it()
    {
        var mod = FakeMod.Create(_temp, "1.7.0");

        var name = _installer.Install(_game, mod);

        Assert.Equal("version.dll", name);
        Assert.Equal("MOD 1.7.0", File.ReadAllText(InGame("version.dll")));
        Assert.Equal(new ModStatus(ModState.Installed, "version.dll", new Version(1, 7, 0)), _installer.GetStatus(_game, mod));
        Assert.Single(_settings.Installs);
        Assert.Equal(1, _saves);
    }

    [Fact]
    public void Auto_never_overwrites_a_dll_that_belongs_to_the_game()
    {
        File.WriteAllText(InGame("version.dll"), "the game's own file");

        var name = _installer.Install(_game, FakeMod.Create(_temp, "1.7.0"));

        Assert.Equal("winmm.dll", name);
        Assert.Equal("the game's own file", File.ReadAllText(InGame("version.dll")));
        Assert.DoesNotContain("version.dll", _installer.GetFreeNames(_game));
    }

    [Fact]
    public void Picking_a_name_that_is_taken_is_refused()
    {
        File.WriteAllText(InGame("dxgi.dll"), "ReShade");

        var error = Assert.Throws<InvalidOperationException>(() => _installer.Install(_game, FakeMod.Create(_temp, "1.7.0"), "dxgi.dll"));

        Assert.Contains("already exists", error.Message);
        Assert.Equal("ReShade", File.ReadAllText(InGame("dxgi.dll")));
    }

    [Fact]
    public void Picking_an_unsupported_name_is_refused() =>
        Assert.Throws<ArgumentException>(() => _installer.Install(_game, FakeMod.Create(_temp, "1.7.0"), "kernel32.dll"));

    [Fact]
    public void Newer_mod_shows_outdated_and_update_keeps_the_same_name()
    {
        _installer.Install(_game, FakeMod.Create(_temp, "1.6.0"), "dxgi.dll");
        var newer = FakeMod.Create(_temp, "1.7.0");

        Assert.Equal(ModState.Outdated, _installer.GetStatus(_game, newer).State);

        var name = _installer.Install(_game, newer);

        Assert.Equal("dxgi.dll", name);
        Assert.Equal(ModState.Installed, _installer.GetStatus(_game, newer).State);
        Assert.False(File.Exists(InGame("version.dll")));   // still only one copy
    }

    [Fact]
    public void Finds_a_copy_installed_by_hand()
    {
        File.WriteAllText(InGame("winmm.dll"), "MOD 1.7.0");

        var status = _installer.GetStatus(_game, FakeMod.Create(_temp, "1.7.0"));

        Assert.Equal(new ModStatus(ModState.Installed, "winmm.dll", new Version(1, 7, 0)), status);
    }

    [Fact]
    public void Remove_deletes_the_mod_and_keeps_its_settings_by_default()
    {
        var mod = FakeMod.Create(_temp, "1.7.0");
        _installer.Install(_game, mod);
        File.WriteAllText(InGame("RTXMFG-Universal.json"), "{}");

        Assert.True(_installer.Remove(_game));

        Assert.False(File.Exists(InGame("version.dll")));
        Assert.True(File.Exists(InGame("RTXMFG-Universal.json")));
        Assert.Equal(ModState.NotInstalled, _installer.GetStatus(_game, mod).State);
        Assert.Empty(_settings.Installs);
    }

    [Fact]
    public void Remove_with_mod_data_moves_settings_and_logs_out_of_the_game()
    {
        _installer.Install(_game, FakeMod.Create(_temp, "1.7.0"));
        File.WriteAllText(InGame("RTXMFG-Universal.json"), "{}");
        File.WriteAllText(InGame("RTXMFG-Universal.log"), "log");

        _installer.Remove(_game, removeModData: true);

        Assert.Empty(ModInstaller.FindModDataFiles(_gameFolder));
        var moved = Directory.GetFiles(Path.Combine(_temp.Path, "removed"), "*", SearchOption.AllDirectories);
        Assert.Equal(2, moved.Length);
        Assert.Equal(["Cool.exe"], Directory.GetFiles(_gameFolder).Select(Path.GetFileName));
    }

    [Fact]
    public void A_file_that_was_replaced_is_never_deleted()
    {
        _installer.Install(_game, FakeMod.Create(_temp, "1.7.0"));
        File.WriteAllText(InGame("version.dll"), "game update put its own file here");

        Assert.Equal(ModState.Replaced, _installer.GetStatus(_game, null).State);
        Assert.Throws<InvalidOperationException>(() => _installer.Install(_game, FakeMod.Create(_temp, "1.7.0")));
        Assert.False(_installer.Remove(_game));

        Assert.Equal("game update put its own file here", File.ReadAllText(InGame("version.dll")));
        Assert.Empty(_settings.Installs);
    }

    [Fact]
    public void Deleted_by_the_game_shows_missing_and_reinstall_uses_the_same_name()
    {
        var mod = FakeMod.Create(_temp, "1.7.0");
        _installer.Install(_game, mod, "winhttp.dll");
        File.Delete(InGame("winhttp.dll"));

        Assert.Equal(ModState.Missing, _installer.GetStatus(_game, mod).State);
        Assert.Equal("winhttp.dll", _installer.Install(_game, mod));
    }

    [Fact]
    public void Game_without_exe_is_not_installable()
    {
        var game = new Game { Name = "No exe", Launcher = Launcher.Steam, Folder = _temp.Path };

        Assert.Equal(ModState.NotInstalled, _installer.GetStatus(game, null).State);
        Assert.Throws<InvalidOperationException>(() => _installer.Install(game, FakeMod.Create(_temp, "1.7.0")));
    }

    // settings.json can be changed by any program, and this app writes and deletes files as admin.

    [Fact]
    public void A_changed_settings_file_cant_make_install_write_outside_the_game_folder()
    {
        var outside = _temp.Folder("outside");
        _settings.Installs.Add(new InstallRecord(_gameFolder, @"..\..\outside\planted.dll", "1.7.0", DateTime.Now));

        Assert.Equal(ModState.NotInstalled, _installer.GetStatus(_game, null).State);
        var name = _installer.Install(_game, FakeMod.Create(_temp, "1.7.0"));

        Assert.Equal("version.dll", name);
        Assert.True(File.Exists(InGame("version.dll")));
        Assert.Empty(Directory.GetFiles(outside));
        Assert.Equal("version.dll", Assert.Single(_settings.Installs).DllName);
    }

    [Fact]
    public void A_changed_settings_file_cant_make_remove_delete_outside_the_game_folder()
    {
        var planted = _temp.File("outside/planted.dll", "MOD 1.7.0");
        _settings.Installs.Add(new InstallRecord(_gameFolder, @"..\..\outside\planted.dll", "1.7.0", DateTime.Now));

        Assert.False(_installer.Remove(_game));

        Assert.True(File.Exists(planted));
    }

    [Fact]
    public void A_saved_name_that_differs_only_in_upper_and_lower_case_is_still_ours()
    {
        File.WriteAllText(InGame("Version.dll"), "MOD 1.7.0");
        _settings.Installs.Add(new InstallRecord(_gameFolder, "Version.dll", "1.7.0", DateTime.Now));

        Assert.Equal(ModState.Installed, _installer.GetStatus(_game, null).State);
    }

    [Fact]
    public void The_mod_is_never_put_in_the_Windows_folder()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows has this folder");
        var system32 = Environment.SystemDirectory;
        var game = new Game { Name = "Fake", Launcher = Launcher.Steam, Folder = system32, Exe = Path.Combine(system32, "notepad.exe") };

        var error = Assert.Throws<InvalidOperationException>(() => _installer.Install(game, FakeMod.Create(_temp, "1.7.0")));

        Assert.Contains("belongs to Windows", error.Message);
        Assert.Empty(_settings.Installs);
    }
}

public class ModPackageTests
{
    [Theory]
    [InlineData("v1.7.0", "1.7.0")]
    [InlineData("1.7.0.1145", "1.7.0")]
    [InlineData("v1.5.0-hotfix.1", "1.5.0")]
    [InlineData("v2", "2.0.0")]
    [InlineData("nonsense", "0.0.0")]
    [InlineData(null, "0.0.0")]
    [InlineData("99999999999.1.1", "0.0.0")]   // too big for a number: it comes from a DLL or GitHub, so it must not crash
    [InlineData("1.99999999999", "0.0.0")]
    public void Parses_version_text(string? text, string expected) =>
        Assert.Equal(Version.Parse(expected), ModPackage.ParseVersion(text));

    [Theory]
    [InlineData("nonsense")]
    [InlineData("99999999999.1.1")]
    [InlineData("v2")]
    public void Every_parsed_version_can_be_shown_on_screen(string text) =>
        Assert.Equal(3, ModPackage.ParseVersion(text).ToString(3).Split('.').Length);   // the window shows three numbers, and a shorter Version would throw

    [Fact]
    public void Picks_the_newest_copy_in_downloads()
    {
        using var temp = new TempFolder();
        FakeMod.Create(temp, "1.6.1");
        var newest = FakeMod.Create(temp, "1.7.0");
        temp.File("downloads/other/MFGUnlocker.dll", "not the mod");

        var found = ModPackage.FindNewestIn(Path.Combine(temp.Path, "downloads"), FakeMod.ReadVersion);

        Assert.Equal(newest, found);
    }

    [Fact]
    public void Copies_the_mod_from_downloads_into_the_mods_folder()
    {
        using var temp = new TempFolder();
        var downloaded = FakeMod.Create(temp, "1.7.0");   // lives in temp\downloads
        var mods = Path.Combine(temp.Path, "App", "Mods");

        var (inMods, newer) = ModPackage.FindCopies(mods, Path.Combine(temp.Path, "downloads"), FakeMod.ReadVersion);
        Assert.Null(inMods);
        var mod = ModPackage.Import(newer!, mods);

        Assert.Equal(Path.Combine(mods, "MFGUnlocker-v1.7.0", "MFGUnlocker.dll"), mod.DllPath);
        Assert.Equal("MOD 1.7.0", File.ReadAllText(mod.DllPath));
        Assert.True(File.Exists(downloaded.DllPath));   // the original is left alone
    }

    [Fact]
    public void A_dll_in_downloads_is_only_reported_never_copied_by_itself()
    {
        using var temp = new TempFolder();
        var mods = Path.Combine(temp.Path, "Mods");
        temp.File("Mods/MFGUnlocker-v1.7.0/MFGUnlocker.dll", "MOD 1.7.0");
        FakeMod.Create(temp, "9.9.0");   // e.g. planted by another program

        var (inMods, newer) = ModPackage.FindCopies(mods, Path.Combine(temp.Path, "downloads"), FakeMod.ReadVersion);

        Assert.Equal(new Version(9, 9, 0), newer?.Version);
        Assert.Equal(new Version(1, 7, 0), inMods?.Version);
        Assert.False(Directory.Exists(Path.Combine(mods, "MFGUnlocker-v9.9.0")));
    }

    [Fact]
    public void Mods_folder_wins_unless_downloads_has_a_newer_version()
    {
        using var temp = new TempFolder();
        var mods = Path.Combine(temp.Path, "Mods");
        var downloads = Path.Combine(temp.Path, "downloads");
        temp.File("Mods/MFGUnlocker-v1.8.0/MFGUnlocker.dll", "MOD 1.8.0");
        FakeMod.Create(temp, "1.7.0");

        var (inMods, older) = ModPackage.FindCopies(mods, downloads, FakeMod.ReadVersion);
        Assert.Equal(new Version(1, 8, 0), inMods?.Version);
        Assert.Null(older);

        FakeMod.Create(temp, "1.9.0");
        var (_, newer) = ModPackage.FindCopies(mods, downloads, FakeMod.ReadVersion);

        Assert.Equal(new Version(1, 9, 0), newer?.Version);
    }

    [Fact]
    public void Importing_a_dll_already_in_mods_does_not_copy_it()
    {
        using var temp = new TempFolder();
        var path = temp.File("Mods/MFGUnlocker-v1.7.0/MFGUnlocker.dll", "MOD 1.7.0");
        var mod = new ModPackage(path, new Version(1, 7, 0));

        Assert.Same(mod, ModPackage.Import(mod, Path.Combine(temp.Path, "Mods")));
    }
}

public class SettingsStoreTests
{
    [Fact]
    public void Saves_and_loads_everything()
    {
        using var temp = new TempFolder();
        var store = new SettingsStore(Path.Combine(temp.Path, "settings.json"));
        var settings = new AppSettings { ModDllPath = @"C:\mod.dll" };
        settings.ManualGames.Add(@"D:\Game\game.exe");
        settings.ExeOverrides[@"D:\Game"] = @"D:\Game\bin\game.exe";
        settings.Installs.Add(new InstallRecord(@"D:\Game\bin", "version.dll", "1.7.0", new DateTime(2026, 11, 1)));

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(@"C:\mod.dll", loaded.ModDllPath);
        Assert.Equal(settings.ManualGames, loaded.ManualGames);
        Assert.Equal(@"D:\Game\bin\game.exe", loaded.ExeOverrides[@"d:\game"]);   // folder lookups ignore case
        Assert.Equal(settings.Installs, loaded.Installs);
    }

    [Fact]
    public void Broken_or_missing_file_gives_empty_settings()
    {
        using var temp = new TempFolder();
        var path = temp.File("settings.json", "{ broken");

        Assert.Empty(new SettingsStore(path).Load().Installs);
        Assert.Empty(new SettingsStore(Path.Combine(temp.Path, "missing.json")).Load().Installs);
    }

    [Theory]
    [InlineData("""{"ExeOverrides":null,"Installs":null,"ManualGames":null}""")]
    [InlineData("""{"Installs":[null,{"Folder":null,"DllName":"version.dll"},{"Folder":"","DllName":""},{"Folder":"D:\\a\u0000b","DllName":"version.dll"}]}""")]
    [InlineData("""{"ManualGames":[null,"","D:\\a\u0000b.exe"],"ExeOverrides":{"D:\\game":null}}""")]
    [InlineData("""{"Theme":99,"ModDllPath":"D:\\a\u0000b.dll"}""")]
    public void Valid_json_with_nonsense_in_it_gives_clean_settings(string json)
    {
        using var temp = new TempFolder();

        var loaded = new SettingsStore(temp.File("settings.json", json)).Load();

        Assert.Empty(loaded.Installs);
        Assert.Empty(loaded.ManualGames);
        Assert.Empty(loaded.ExeOverrides);
        Assert.Null(loaded.ModDllPath);
        Assert.Equal(ThemeChoice.System, loaded.Theme);
    }

    [Fact]
    public void Cleaning_keeps_the_good_entries()
    {
        using var temp = new TempFolder();
        var json = """{"Installs":[null,{"Folder":"D:\\game","DllName":"version.dll","ModVersion":"1.7.0"}],"ManualGames":["","D:\\game\\a.exe"],"ExeOverrides":{"D:\\game":"D:\\game\\a.exe","D:\\other":null},"Theme":2}""";

        var loaded = new SettingsStore(temp.File("settings.json", json)).Load();

        Assert.Equal(@"D:\game", Assert.Single(loaded.Installs).Folder);
        Assert.Equal(@"D:\game\a.exe", Assert.Single(loaded.ManualGames));
        Assert.Equal(@"D:\game\a.exe", Assert.Single(loaded.ExeOverrides).Value);
        Assert.Equal(ThemeChoice.Dark, loaded.Theme);
    }

    [Fact]
    public void Two_folder_names_that_differ_only_in_case_dont_stop_the_app()
    {
        using var temp = new TempFolder();
        var json = """{"ExeOverrides":{"D:\\Game":"D:\\Game\\a.exe","d:\\game":"d:\\game\\b.exe"}}""";

        var loaded = new SettingsStore(temp.File("settings.json", json)).Load();

        Assert.NotEmpty(loaded.ExeOverrides);
    }
}
