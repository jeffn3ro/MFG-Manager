using MfgManager.Core.Models;
using MfgManager.Core.Scanning.Launchers;

namespace MfgManager.Tests.Core;

public class UninstallLibraryTests
{
    // These look exactly like the "Apps & features" entries on the PC this was built on.
    private static readonly UninstallEntry DiabloIV = new("Diablo IV", "Blizzard Entertainment", """E:\Battle.net\Diablo IV""",
        "\"C:\\ProgramData\\Battle.net\\Agent\\Blizzard Uninstaller.exe\" --lang=enUS --uid=fenris --displayname=\"Diablo IV\"");
    private static readonly UninstallEntry BattleNetItself = new("Battle.net", "Blizzard Entertainment", """C:\Program Files (x86)\Battle.net""",
        "\"C:\\ProgramData\\Battle.net\\Agent\\Blizzard Uninstaller.exe\" --lang=enGB --uid=battle.net --displayname=\"Battle.net\"");
    private static readonly UninstallEntry DeadSpace = new("Dead Space™", "Electronic Arts", """E:\EA\Dead Space (2023)\""",
        "\"C:\\Program Files\\Common Files\\EAInstaller\\Dead Space (2023)\\Cleanup.exe\" uninstall_game -autologging");
    private static readonly UninstallEntry EaAppItself = new("EA app", "Electronic Arts", null,
        "\"C:\\Program Files\\Electronic Arts\\EA Desktop\\EA Desktop\\EAUninstall.exe\" /uninstall");
    private static readonly UninstallEntry Gta5 = new("Grand Theft Auto V Enhanced", "Rockstar Games", """E:\Rockstar Games\Grand Theft Auto V Enhanced""",
        "\"E:\\Rockstar Games\\Grand Theft Auto V Enhanced\\uninstall.exe\" -enableFullMode -uninstall=gta5_gen9");
    private static readonly UninstallEntry RockstarLauncherItself = new("Rockstar Games Launcher", "Rockstar Games", """C:\Program Files\Rockstar Games\Launcher""",
        "\"C:\\Program Files\\Rockstar Games\\Launcher\\uninstall.exe\" -enableFullMode -uninstall=launcher");
    private static readonly UninstallEntry SomeApp = new("7-Zip", "Igor Pavlov", """C:\Program Files\7-Zip""", """C:\Program Files\7-Zip\Uninstall.exe""");

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
        string folder = temp.Folder("EA/Dead Space (2023)");
        UninstallEntry installed = DeadSpace with { InstallLocation = folder + """\""" };
        UninstallEntry uninstalledLater = Gta5 with { InstallLocation = Path.Combine(temp.Path, "gone") };

        Game game = Assert.Single(UninstallLibrary.FindGames([installed, uninstalledLater, SomeApp with { InstallLocation = temp.Path }]));

        // ™ removed.
        Assert.Equal("Dead Space", game.Name);
        Assert.Equal(Launcher.Ea, game.Launcher);
        Assert.Equal(folder, game.Folder);
    }

    [Theory]
    [InlineData("Dead Space™", "Dead Space")]
    [InlineData("Command & Conquer(tm) 3 Kanes Wrath(tm)", "Command & Conquer 3 Kanes Wrath")]
    [InlineData("Diablo IV", "Diablo IV")]
    public void Cleans_trademark_signs_from_names(string raw, string clean) => Assert.Equal(clean, UninstallLibrary.CleanName(raw));
}
