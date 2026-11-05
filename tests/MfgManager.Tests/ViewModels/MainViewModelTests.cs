namespace MfgManager.Tests.ViewModels;

public class MainViewModelTests
{
    [Fact]
    public void The_theme_choice_is_saved()
    {
        using var s = new AppSetup();
        int before = s.Saves;

        s.Vm.Theme = MfgManager.Core.Settings.ThemeChoice.Dark;

        Assert.Equal(MfgManager.Core.Settings.ThemeChoice.Dark, s.Settings.Theme);
        Assert.Equal(before + 1, s.Saves);
    }

    [Fact]
    public async Task Start_up_finds_the_mod_scans_and_checks_GitHub_in_that_order()
    {
        using var s = new AppSetup();
        s.WriteRealLookingMod("mods/MFGUnlocker-v2.0.0", "2.0.0");
        s.ScannerGames.Add(s.MakeGame("Alpha", MfgManager.Core.Models.Launcher.Steam));
        s.Web.Add("https://api.github.com/", """{"tag_name":"v2.0.0","html_url":"https://github.com/x","assets":[]}""");

        await s.Vm.InitializeAsync();

        Assert.Equal(new Version(2, 0, 0), s.Vm.Mod.Mod?.Version);
        Assert.Equal(new Version(2, 0, 0), s.Library.Mod?.Version);
        Assert.Single(s.Library.Games);
        // GitHub has the same version you have.
        Assert.False(s.Vm.Mod.ShowUpdateBanner);
    }
}
