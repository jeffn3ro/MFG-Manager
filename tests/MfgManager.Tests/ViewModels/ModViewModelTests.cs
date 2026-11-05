using MfgManager.Core.Mod;

namespace MfgManager.Tests.ViewModels;

public class ModViewModelTests
{
    [Fact]
    public async Task GitHub_is_checked_once_at_start_and_never_again()
    {
        using var s = new AppSetup();
        s.Web.Add("https://api.github.com/", """{"tag_name":"v1.8.0","html_url":"https://github.com/x","assets":[]}""");
        int GitHubRequests() => s.Web.Requests.Count(url => url.Contains("github.com"));

        await s.Vm.InitializeAsync();

        Assert.Equal(1, GitHubRequests());
        // v1.8.0 is newer than nothing.
        Assert.True(s.Vm.Mod.ShowUpdateBanner);

        // Rescanning reuses the start-up answer instead of asking GitHub again.
        await s.Library.RescanCommand.ExecuteAsync();

        Assert.Equal(1, GitHubRequests());
    }

    [Theory]
    // No assets or page address.
    [InlineData("""{"tag_name":"v1.9.0"}""")]
    // An address that isn't text.
    [InlineData("""{"tag_name":"v1.9.0","html_url":5,"assets":[]}""")]
    // A download without a name.
    [InlineData("""{"tag_name":"v1.9.0","html_url":"https://github.com/x","assets":[{"id":1}]}""")]
    public async Task A_strange_answer_from_GitHub_only_adds_a_note(string answer)
    {
        using var s = new AppSetup();
        s.Web.Add("https://api.github.com/", answer);

        await s.Vm.Mod.CheckGitHubOnceAsync();

        Assert.Contains("Couldn't check GitHub", s.Vm.Status.Text);
        Assert.False(s.Vm.Mod.ShowUpdateBanner);
    }

    [Fact]
    public async Task A_release_number_too_big_to_be_real_is_not_offered_as_an_update()
    {
        using var s = new AppSetup();
        s.Web.Add("https://api.github.com/", """{"tag_name":"v99999999999","html_url":"https://github.com/x","assets":[]}""");
        s.Vm.Mod.Mod = null;

        await s.Vm.Mod.CheckGitHubOnceAsync();

        Assert.False(s.Vm.Mod.ShowUpdateBanner);
    }

    [Fact]
    public async Task A_fake_dll_with_a_huge_version_in_downloads_doesnt_stop_the_start()
    {
        using var s = new AppSetup();
        s.ScannerGames.Add(s.MakeGame("Alpha", MfgManager.Core.Models.Launcher.Steam));
        s.WriteRealLookingMod("downloads/fake", "99999999999.0.0");

        await s.Vm.InitializeAsync();

        Assert.Equal(1, s.Scans);
        Assert.Single(s.Library.Games);
    }

    [Fact]
    public async Task If_copying_from_downloads_fails_the_games_still_get_scanned()
    {
        using var s = new AppSetup();
        s.ScannerGames.Add(s.MakeGame("Alpha", MfgManager.Core.Models.Launcher.Steam));
        s.WriteRealLookingMod("downloads/MFGUnlocker-v9.0.0", "9.0.0");
        s.Temp.File("mods/MFGUnlocker-v9.0.0", "a file where the folder has to go");

        await s.Vm.InitializeAsync();

        // The question, then why it failed.
        Assert.Equal(2, s.Dialogs.Messages.Count);
        Assert.Equal(1, s.Scans);
        Assert.Single(s.Library.Games);
    }

    [Fact]
    public async Task No_internet_only_adds_a_note_to_the_status_line()
    {
        // The fake web answers 404.
        using var s = new AppSetup();

        await s.Vm.Mod.CheckGitHubOnceAsync();

        Assert.Contains("Couldn't check GitHub", s.Vm.Status.Text);
        Assert.False(s.Vm.Mod.ShowUpdateBanner);
    }

    [Fact]
    public async Task A_newer_dll_in_downloads_asks_first_and_is_only_used_after_yes()
    {
        using var s = new AppSetup();
        s.WriteRealLookingMod("downloads/MFGUnlocker-v1.9.0", "1.9.0");
        s.Vm.Mod.Mod = null;

        s.Dialogs.Answer = false;
        await s.Vm.Mod.FindAsync();
        Assert.Contains("Only use it if you downloaded it yourself", s.Dialogs.Messages.Single());
        Assert.Null(s.Vm.Mod.Mod);
        Assert.Empty(Directory.EnumerateFiles(s.ModsFolder, "*", SearchOption.AllDirectories));

        s.Dialogs.Answer = true;
        await s.Vm.Mod.FindAsync();
        Assert.Equal(new Version(1, 9, 0), s.Vm.Mod.Mod?.Version);
        Assert.StartsWith(s.ModsFolder, s.Vm.Mod.Mod!.DllPath);
    }

    [Fact]
    public async Task A_dll_path_in_settings_is_only_trusted_inside_the_Mods_folder()
    {
        // settings.json can be changed by any program, so it must not be able to point the app at any DLL.
        using var s = new AppSetup();
        s.Settings.ModDllPath = s.WriteRealLookingMod("elsewhere", "9.9.9");
        s.Vm.Mod.Mod = null;

        await s.Vm.Mod.FindAsync();
        Assert.Null(s.Vm.Mod.Mod);

        s.Settings.ModDllPath = s.WriteRealLookingMod("mods/picked", "1.8.0");
        await s.Vm.Mod.FindAsync();
        Assert.Equal(new Version(1, 8, 0), s.Vm.Mod.Mod?.Version);
    }

    [Fact]
    public async Task Choose_dll_rejects_other_files_and_copies_the_mod_into_Mods()
    {
        using var s = new AppSetup();

        s.Dialogs.PickedFile = s.Temp.File("downloads/notes.dll", "not the mod");
        await s.Vm.Mod.ChooseCommand.ExecuteAsync();
        Assert.Equal("That file isn't MFG Unlocker.", s.Dialogs.Messages.Single());

        s.Dialogs.PickedFile = s.WriteRealLookingMod("downloads/mine", "1.9.5");
        await s.Vm.Mod.ChooseCommand.ExecuteAsync();

        Assert.Equal(new Version(1, 9, 5), s.Vm.Mod.Mod?.Version);
        Assert.True(MfgManager.Core.PathUtil.IsInside(s.Vm.Mod.Mod!.DllPath, s.ModsFolder));
        Assert.Equal(s.Vm.Mod.Mod.DllPath, s.Settings.ModDllPath);
    }

    [Fact]
    public async Task A_failed_download_shows_why_and_keeps_your_current_mod()
    {
        using var s = new AppSetup();
        s.Web.Add("https://api.github.com/", """
            {"tag_name":"v1.8.0","html_url":"https://github.com/x","assets":[
              {"name":"MFGUnlocker-v1.8.0.zip","browser_download_url":"https://example.test/m.zip","digest":"sha256:abc"}]}
            """);
        await s.Vm.Mod.CheckGitHubOnceAsync();
        ModPackage? before = s.Vm.Mod.Mod;

        // The fake web has no such file.
        await s.Vm.Mod.DownloadCommand.ExecuteAsync();

        Assert.StartsWith("Download failed", s.Dialogs.Messages.Single());
        Assert.Same(before, s.Vm.Mod.Mod);
        Assert.False(s.Vm.Status.IsBusy);
    }

    [Fact]
    public void Texts_say_what_is_going_on()
    {
        using var s = new AppSetup();

        Assert.Equal("MFG Unlocker v1.7.0", s.Vm.Mod.VersionText);
        Assert.False(s.Vm.Mod.Missing);

        s.Vm.Mod.Mod = null;

        Assert.Equal("MFGUnlocker.dll not found", s.Vm.Mod.VersionText);
        Assert.True(s.Vm.Mod.Missing);
        Assert.Equal("", s.Vm.Mod.PathText);
    }
}
