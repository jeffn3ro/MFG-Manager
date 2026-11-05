using MfgManager.Core.Platform;

namespace MfgManager.Tests.Core;

public class GameProcessesTests
{
    [Theory]
    // Windows.
    [InlineData("Game", "Game.exe", true)]
    [InlineData("game", "Game.exe", true)]
    // Proton.
    [InlineData("Game.exe", "Game.exe", true)]
    // Linux keeps only 15 letters.
    [InlineData("Cyberpunk2077.e", "Cyberpunk2077.exe", true)]
    [InlineData("Cyberpunk2077", "Cyberpunk2077.exe", true)]
    // Shorter than 15 letters, so it's a different program.
    [InlineData("Cyberpunk", "Cyberpunk2077.exe", false)]
    [InlineData("Other", "Game.exe", false)]
    [InlineData("", "Game.exe", false)]
    [InlineData(null, "Game.exe", false)]
    public void Matches_a_process_to_a_game(string? process, string exe, bool expected) =>
        Assert.Equal(expected, GameProcesses.Matches(process, exe));
}
