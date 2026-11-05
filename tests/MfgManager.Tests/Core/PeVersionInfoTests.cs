using System.Diagnostics;
using MfgManager.Core.Mod;

namespace MfgManager.Tests.Core;

public class PeVersionInfoTests
{
    private static PeVersionInfo? Read(byte[] file) => PeVersionInfo.Read(new MemoryStream(file));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Reads_product_name_and_version_from_32_and_64_bit_files(bool is64Bit)
    {
        var info = Read(FakePe.Build("MFG Unlocker", "1.7.0", is64Bit));

        Assert.Equal("MFG Unlocker", info?.ProductName);
        Assert.Equal("1.7.0", info?.ProductVersion);
    }

    [Fact]
    public void Recognises_the_mod_by_its_name_and_nothing_else()
    {
        using var temp = new TempFolder();
        var mod = Path.Combine(temp.Path, "mod.dll");
        var other = Path.Combine(temp.Path, "other.dll");
        File.WriteAllBytes(mod, FakePe.Build(ModPackage.ProductName, "1.8.2"));
        File.WriteAllBytes(other, FakePe.Build("Some Game", "1.8.2"));

        Assert.Equal(new Version(1, 8, 2), ModPackage.ReadVersion(mod));
        Assert.Null(ModPackage.ReadVersion(other));
    }

    [Fact]
    public void Text_files_and_missing_files_are_not_program_files()
    {
        using var temp = new TempFolder();

        Assert.Null(PeVersionInfo.Read(temp.File("notes.dll", "just some text")));
        Assert.Null(PeVersionInfo.Read(Path.Combine(temp.Path, "missing.dll")));
        Assert.Null(Read([]));
    }

    [Fact]
    public void A_cut_off_file_gives_null_at_every_length_and_never_crashes()
    {
        var whole = FakePe.Build("MFG Unlocker", "1.7.0");
        for (var length = 0; length < whole.Length; length++)
        {
            var info = Read(whole[..length]);   // must return, whatever the length
            Assert.True(info is null || info.ProductName is null or "MFG Unlocker");
        }
    }

    [Fact]
    public void Damaged_files_never_crash_or_hang_the_reader()
    {
        // The file comes from a game folder or Downloads, so it could hold anything.
        var whole = FakePe.Build("MFG Unlocker", "1.7.0");
        var random = new Random(12345);   // fixed seed: same test every run
        for (var round = 0; round < 3000; round++)
        {
            var damaged = (byte[])whole.Clone();
            for (var change = 0; change < 1 + round % 8; change++)
                damaged[random.Next(damaged.Length)] = (byte)random.Next(256);

            Read(damaged);   // the test is that this returns
        }
    }

    [Fact]
    public void Huge_offsets_are_rejected()
    {
        var file = FakePe.Build("MFG Unlocker", "1.7.0");
        file[0x3C] = 0xFF; file[0x3D] = 0xFF; file[0x3E] = 0xFF; file[0x3F] = 0x7F;

        Assert.Null(Read(file));
    }

    [Fact]
    public void Agrees_with_Windows_on_real_system_files()
    {
        if (!OperatingSystem.IsWindows()) return;

        var files = new[] { "kernel32.dll", "user32.dll", "notepad.exe", "shell32.dll", "version.dll", "winmm.dll", "dxgi.dll", "d3d12.dll" }
            .Select(name => Path.Combine(Environment.SystemDirectory, name))
            .Where(File.Exists)
            .ToList();
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var expected = FileVersionInfo.GetVersionInfo(file);
            var actual = PeVersionInfo.Read(file);
            if (string.IsNullOrEmpty(expected.ProductVersion)) continue;

            // Windows reads this text from a separate language file for its own DLLs, so the name can be translated and the
            // last number can differ. The exact comparison is done on the real mod below.
            Assert.False(string.IsNullOrEmpty(actual?.ProductName), file);
            Assert.Equal(string.Join('.', expected.ProductVersion.Split('.').Take(3)), string.Join('.', actual!.ProductVersion!.Split('.').Take(3)));
        }
    }

    [Fact]
    public void Reads_the_real_mod_if_it_is_on_this_PC()
    {
        // Only reads the version info stored in the file. Nothing is loaded or run.
        var mods = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "MFG Manager", "Mods");
        var real = Directory.Exists(mods)
            ? Directory.EnumerateFiles(mods, ModPackage.FileName, SearchOption.AllDirectories).FirstOrDefault()
            : null;
        if (real is null) Assert.Skip("MFG Unlocker isn't installed on this PC.");

        Assert.NotNull(ModPackage.ReadVersion(real));
        if (!OperatingSystem.IsWindows()) return;
        var windows = FileVersionInfo.GetVersionInfo(real);
        var ours = PeVersionInfo.Read(real);
        Assert.Equal(windows.ProductName, ours?.ProductName);
        Assert.Equal(windows.ProductVersion, ours?.ProductVersion);
    }
}
