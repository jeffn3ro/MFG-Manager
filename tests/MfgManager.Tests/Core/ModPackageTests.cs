using MfgManager.Core.Mod;

namespace MfgManager.Tests.Core;

public class ModPackageTests
{
    [Theory]
    [InlineData("v1.7.0", "1.7.0")]
    [InlineData("1.7.0.1145", "1.7.0")]
    [InlineData("v1.5.0-hotfix.1", "1.5.0")]
    [InlineData("v2", "2.0.0")]
    [InlineData("nonsense", "0.0.0")]
    [InlineData(null, "0.0.0")]
    // Too big for a number: it comes from a DLL or GitHub, so it must not crash.
    [InlineData("99999999999.1.1", "0.0.0")]
    [InlineData("1.99999999999", "0.0.0")]
    public void Parses_version_text(string? text, string expected) =>
        Assert.Equal(Version.Parse(expected), ModPackage.ParseVersion(text));

    [Theory]
    [InlineData("nonsense")]
    [InlineData("99999999999.1.1")]
    [InlineData("v2")]
    public void Every_parsed_version_can_be_shown_on_screen(string text) =>
        // The window shows three numbers, and a shorter Version would throw.
        Assert.Equal(3, ModPackage.ParseVersion(text).ToString(3).Split('.').Length);

    [Fact]
    public void Picks_the_newest_copy_in_downloads()
    {
        using var temp = new TempFolder();
        FakeMod.Create(temp, "1.6.1");
        ModPackage newest = FakeMod.Create(temp, "1.7.0");
        temp.File("downloads/other/MFGUnlocker.dll", "not the mod");

        var found = ModPackage.FindNewestIn(Path.Combine(temp.Path, "downloads"), FakeMod.ReadVersion);

        Assert.Equal(newest, found);
    }

    [Fact]
    public void Copies_the_mod_from_downloads_into_the_mods_folder()
    {
        using var temp = new TempFolder();
        // Lives in temp\downloads.
        ModPackage downloaded = FakeMod.Create(temp, "1.7.0");
        string mods = Path.Combine(temp.Path, "App", "Mods");

        (ModPackage? inMods, ModPackage? newer) = ModPackage.FindCopies(mods, Path.Combine(temp.Path, "downloads"), FakeMod.ReadVersion);
        Assert.Null(inMods);
        var mod = ModPackage.Import(newer!, mods);

        Assert.Equal(Path.Combine(mods, "MFGUnlocker-v1.7.0", "MFGUnlocker.dll"), mod.DllPath);
        Assert.Equal("MOD 1.7.0", File.ReadAllText(mod.DllPath));
        // The original is left alone.
        Assert.True(File.Exists(downloaded.DllPath));
    }

    [Fact]
    public void A_dll_in_downloads_is_only_reported_never_copied_by_itself()
    {
        using var temp = new TempFolder();
        string mods = Path.Combine(temp.Path, "Mods");
        temp.File("Mods/MFGUnlocker-v1.7.0/MFGUnlocker.dll", "MOD 1.7.0");
        // e.g. planted by another program.
        FakeMod.Create(temp, "9.9.0");

        (ModPackage? inMods, ModPackage? newer) = ModPackage.FindCopies(mods, Path.Combine(temp.Path, "downloads"), FakeMod.ReadVersion);

        Assert.Equal(new Version(9, 9, 0), newer?.Version);
        Assert.Equal(new Version(1, 7, 0), inMods?.Version);
        Assert.False(Directory.Exists(Path.Combine(mods, "MFGUnlocker-v9.9.0")));
    }

    [Fact]
    public void Mods_folder_wins_unless_downloads_has_a_newer_version()
    {
        using var temp = new TempFolder();
        string mods = Path.Combine(temp.Path, "Mods");
        string downloads = Path.Combine(temp.Path, "downloads");
        temp.File("Mods/MFGUnlocker-v1.8.0/MFGUnlocker.dll", "MOD 1.8.0");
        FakeMod.Create(temp, "1.7.0");

        (ModPackage? inMods, ModPackage? older) = ModPackage.FindCopies(mods, downloads, FakeMod.ReadVersion);
        Assert.Equal(new Version(1, 8, 0), inMods?.Version);
        Assert.Null(older);

        FakeMod.Create(temp, "1.9.0");
        (_, ModPackage? newer) = ModPackage.FindCopies(mods, downloads, FakeMod.ReadVersion);

        Assert.Equal(new Version(1, 9, 0), newer?.Version);
    }

    [Fact]
    public void Importing_a_dll_already_in_mods_does_not_copy_it()
    {
        using var temp = new TempFolder();
        string path = temp.File("Mods/MFGUnlocker-v1.7.0/MFGUnlocker.dll", "MOD 1.7.0");
        var mod = new ModPackage(path, new Version(1, 7, 0));

        Assert.Same(mod, ModPackage.Import(mod, Path.Combine(temp.Path, "Mods")));
    }
}
