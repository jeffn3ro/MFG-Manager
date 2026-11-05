using MfgManager.Core.Mod;
using MfgManager.Core.Models;
using MfgManager.Core.Settings;

namespace MfgManager.Tests.Core;

public class WineCaseTests
{
    [Fact]
    public void A_game_file_named_with_other_capitals_still_counts_as_taken()
    {
        // Linux would allow both Version.dll and version.dll, but Wine would see them as one file. The game's file must win.
        using var temp = new TempFolder();
        string exe = temp.File("game/Game.exe");
        temp.File("game/Version.DLL", "the game's own file");
        var game = new Game { Name = "G", Launcher = Launcher.Steam, Folder = Path.GetDirectoryName(exe)!, Exe = exe };
        var installer = new ModInstaller(new AppSettings(), () => { }, temp.Folder("removed"), FakeMod.ReadVersion);

        Assert.DoesNotContain("version.dll", ModInstaller.GetFreeNames(game));
        Assert.Throws<InvalidOperationException>(() => installer.Install(game, FakeMod.Create(temp, "1.7.0"), "version.dll"));
        Assert.Equal("the game's own file", File.ReadAllText(Path.Combine(game.ExeFolder!, "Version.DLL")));
    }
}
