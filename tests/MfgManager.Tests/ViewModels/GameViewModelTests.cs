using MfgManager.App.ViewModels;
using MfgManager.Core.Models;

namespace MfgManager.Tests.ViewModels;

public class GameViewModelTests
{
    [Theory]
    [InlineData("Assassin's Creed Mirage", "AC")]
    [InlineData("Stray", "S")]
    [InlineData("007 Legends", "0L")]
    public void Initials_are_shown_while_there_is_no_cover(string name, string expected)
    {
        var game = new GameViewModel(new Game { Name = name, Launcher = Launcher.Steam, Folder = "x" });

        Assert.Equal(expected, game.Initials);
        Assert.True(game.HasNoCover);
    }

    [Theory]
    [InlineData(Launcher.Gog, "GOG")]
    [InlineData(Launcher.BattleNet, "Battle.net")]
    [InlineData(Launcher.Ea, "EA app")]
    [InlineData(Launcher.Manual, "Added by you")]
    [InlineData(Launcher.Steam, "Steam")]
    public void Launcher_names_read_well(Launcher launcher, string expected) =>
        Assert.Equal(expected, new GameViewModel(new Game { Name = "x", Launcher = launcher, Folder = "x" }).Launcher);

    [Theory]
    [InlineData("version.dll", """WINEDLLOVERRIDES="version=n,b" %command%""")]
    [InlineData("winmm.dll", """WINEDLLOVERRIDES="winmm=n,b" %command%""")]
    [InlineData(null, null)]
    public void Linux_launch_option_names_the_dll_without_its_extension(string? dll, string? expected) =>
        Assert.Equal(expected, GameViewModel.LaunchOptionFor(dll));
}
