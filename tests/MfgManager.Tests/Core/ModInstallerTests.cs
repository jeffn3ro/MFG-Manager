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
        string exe = _temp.File("games/Cool Game/Cool.exe", "game");
        _gameFolder = Path.GetDirectoryName(exe)!;
        _game = new Game { Name = "Cool Game", Launcher = Launcher.Steam, Folder = _gameFolder, Exe = exe };
    }

    public void Dispose() => _temp.Dispose();

    private string InGame(string name) => Path.Combine(_gameFolder, name);

    [Fact]
    public void Auto_installs_as_version_dll_and_remembers_it()
    {
        ModPackage mod = FakeMod.Create(_temp, "1.7.0");

        string name = _installer.Install(_game, mod);

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

        string name = _installer.Install(_game, FakeMod.Create(_temp, "1.7.0"));

        Assert.Equal("winmm.dll", name);
        Assert.Equal("the game's own file", File.ReadAllText(InGame("version.dll")));
        Assert.DoesNotContain("version.dll", ModInstaller.GetFreeNames(_game));
    }

    [Fact]
    public void Picking_a_name_that_is_taken_is_refused()
    {
        File.WriteAllText(InGame("dxgi.dll"), "ReShade");

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => _installer.Install(_game, FakeMod.Create(_temp, "1.7.0"), "dxgi.dll"));

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
        ModPackage newer = FakeMod.Create(_temp, "1.7.0");

        Assert.Equal(ModState.Outdated, _installer.GetStatus(_game, newer).State);

        string name = _installer.Install(_game, newer);

        Assert.Equal("dxgi.dll", name);
        Assert.Equal(ModState.Installed, _installer.GetStatus(_game, newer).State);
        // Still only one copy.
        Assert.False(File.Exists(InGame("version.dll")));
    }

    [Fact]
    public void Finds_a_copy_installed_by_hand()
    {
        File.WriteAllText(InGame("winmm.dll"), "MOD 1.7.0");

        ModStatus status = _installer.GetStatus(_game, FakeMod.Create(_temp, "1.7.0"));

        Assert.Equal(new ModStatus(ModState.Installed, "winmm.dll", new Version(1, 7, 0)), status);
    }

    [Fact]
    public void Remove_deletes_the_mod_and_keeps_its_settings_by_default()
    {
        ModPackage mod = FakeMod.Create(_temp, "1.7.0");
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
        string[] moved = Directory.GetFiles(Path.Combine(_temp.Path, "removed"), "*", SearchOption.AllDirectories);
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
        ModPackage mod = FakeMod.Create(_temp, "1.7.0");
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
        string outside = _temp.Folder("outside");
        _settings.Installs.Add(new InstallRecord(_gameFolder, """..\..\outside\planted.dll""", "1.7.0", DateTime.Now));

        Assert.Equal(ModState.NotInstalled, _installer.GetStatus(_game, null).State);
        string name = _installer.Install(_game, FakeMod.Create(_temp, "1.7.0"));

        Assert.Equal("version.dll", name);
        Assert.True(File.Exists(InGame("version.dll")));
        Assert.Empty(Directory.GetFiles(outside));
        Assert.Equal("version.dll", Assert.Single(_settings.Installs).DllName);
    }

    [Fact]
    public void A_changed_settings_file_cant_make_remove_delete_outside_the_game_folder()
    {
        string planted = _temp.File("outside/planted.dll", "MOD 1.7.0");
        _settings.Installs.Add(new InstallRecord(_gameFolder, """..\..\outside\planted.dll""", "1.7.0", DateTime.Now));

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
        string system32 = Environment.SystemDirectory;
        var game = new Game { Name = "Fake", Launcher = Launcher.Steam, Folder = system32, Exe = Path.Combine(system32, "notepad.exe") };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => _installer.Install(game, FakeMod.Create(_temp, "1.7.0")));

        Assert.Contains("belongs to Windows", error.Message);
        Assert.Empty(_settings.Installs);
    }
}
