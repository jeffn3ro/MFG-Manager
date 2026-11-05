using System.Text.RegularExpressions;
using System.Xml.Linq;
using MfgManager.Tests.ViewModels;

namespace MfgManager.Tests.Core;

/// <summary>
/// The Linux build files can't run on this computer, so these catch the mistakes that would only show up after moving:
/// Windows line endings, a forgotten version number, a broken menu entry, or a script that asks for root.
/// </summary>
public class PackagingTests
{
    private static readonly string Root = FindRoot();
    private static readonly string[] CodeFolders = ["src", "tests", "installer"];

    private static string FindRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Directory.Build.props"))) folder = folder.Parent;
        return folder?.FullName ?? throw new DirectoryNotFoundException("Can't find the project's root folder");
    }

    private static string Linux(string file) => Path.Combine(Root, "packaging", "linux", file);

    private static string AppVersion =>
        XDocument.Load(Path.Combine(Root, "Directory.Build.props")).Descendants("Version").Single().Value;

    public static readonly TheoryData<string> LinuxFiles = new(
        "tools/build-linux.sh", "packaging/linux/install.sh", "packaging/linux/uninstall.sh",
        "packaging/linux/mfg-manager.desktop", "packaging/linux/PKGBUILD", "packaging/linux/deb-control");

    [Theory, MemberData(nameof(LinuxFiles))]
    public void Linux_files_have_linux_line_endings(string file)
    {
        string text = File.ReadAllText(Path.Combine(Root, file));
        Assert.DoesNotContain("\r", text);
        Assert.EndsWith("\n", text);
    }

    [Theory]
    [InlineData("tools/build-linux.sh")]
    [InlineData("packaging/linux/install.sh")]
    [InlineData("packaging/linux/uninstall.sh")]
    public void Scripts_stop_at_the_first_error_and_never_use_root_or_download_scripts(string file)
    {
        string text = File.ReadAllText(Path.Combine(Root, file));
        Assert.StartsWith("#!/usr/bin/env bash\n", text);
        Assert.Contains("set -euo pipefail", text);

        // "sudo" may only appear in the advice the script prints, never as something it runs itself.
        string withoutPrintedText = Regex.Replace(text, """<<'(\w+)'.*?^\1$""", "", RegexOptions.Singleline | RegexOptions.Multiline);
        var lines = withoutPrintedText.Split('\n').Where(line => !line.TrimStart().StartsWith('#')).ToList();
        Assert.DoesNotContain(lines, line => line.TrimStart().StartsWith("sudo ", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("curl ") || line.Contains("wget "));
    }

    [Fact]
    public void Scripts_that_install_refuse_to_run_as_root()
    {
        Assert.Contains("\"$(id -u)\" -eq 0", File.ReadAllText(Linux("install.sh")));
        Assert.Contains("\"$(id -u)\" -eq 0", File.ReadAllText(Path.Combine(Root, "tools", "build-linux.sh")));
    }

    [Fact]
    public void Arch_package_has_the_same_version_as_the_app()
    {
        string line = File.ReadAllLines(Linux("PKGBUILD")).Single(l => l.StartsWith("pkgver=", StringComparison.Ordinal));
        Assert.Equal(AppVersion, line["pkgver=".Length..]);
    }

    [Fact]
    public void The_window_shows_the_same_version_as_the_app()
    {
        using var s = new AppSetup();
        Assert.Equal($"Version {AppVersion}", s.Vm.VersionText);
    }

    [Fact]
    public void Debian_package_takes_its_version_from_the_build()
    {
        string[] lines = File.ReadAllLines(Linux("deb-control"));
        Assert.Contains("Version: @VERSION@", lines);
        Assert.Contains("Package: mfg-manager", lines);
        Assert.Contains("Architecture: amd64", lines);
        Assert.Contains(lines, l => l.StartsWith("Depends: ", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("Description: ", StringComparison.Ordinal));
    }

    [Fact]
    public void Menu_entry_has_what_a_desktop_needs_and_matches_the_app()
    {
        var entries = File.ReadAllLines(Linux("mfg-manager.desktop"))
            .Where(l => l.Contains('=')).ToDictionary(l => l[..l.IndexOf('=')], l => l[(l.IndexOf('=') + 1)..]);

        Assert.Equal("[Desktop Entry]", File.ReadLines(Linux("mfg-manager.desktop")).First());
        Assert.Equal("Application", entries["Type"]);
        Assert.Equal("MFG Manager", entries["Name"]);
        Assert.Equal("mfg-manager", entries["Exec"]);
        Assert.Equal("mfg-manager", entries["Icon"]);
        Assert.Equal("false", entries["Terminal"]);
        Assert.EndsWith(";", entries["Categories"]);
        // Lets the taskbar match the window to this entry.
        Assert.Equal("MfgManager", entries["StartupWMClass"]);
    }

    [Fact]
    public void Licence_notices_come_with_the_program_and_name_everything_built_in()
    {
        string folder = Path.Combine(Root, "src", "MfgManager.App", "Licenses");
        string notices = File.ReadAllText(Path.Combine(folder, "THIRD-PARTY-NOTICES.txt"));
        foreach (string name in new[] { "Avalonia", "SkiaSharp", "HarfBuzzSharp", "Tmds.DBus.Protocol", "MicroCom.Runtime", "Inter font", "ANGLE", ".NET" })
            Assert.Contains(name, notices);
        Assert.Contains("Permission is hereby granted", notices);
        Assert.Contains("Redistribution and use in source and binary forms", notices);
        Assert.True(new FileInfo(Path.Combine(folder, "SkiaSharp-THIRD-PARTY-NOTICES.txt")).Length > 10_000);

        Assert.Contains("""Licenses\*.txt""", File.ReadAllText(Path.Combine(Root, "src", "MfgManager.App", "MfgManager.App.csproj")));
    }

    [Fact]
    public void Windows_installer_checks_for_the_right_runtime_and_keeps_debug_files_out()
    {
        string package = File.ReadAllText(Path.Combine(Root, "installer", "Package.wxs"));
        // Avalonia doesn't need the Desktop Runtime that the old WPF app did.
        Assert.Contains("RuntimeType=\"core\"", package);
        Assert.DoesNotContain("RuntimeType=\"desktop\"", package);

        string script = File.ReadAllText(Path.Combine(Root, "tools", "build-installer.ps1"));
        Assert.Contains("-r win-x64", script);
        Assert.Contains("-Filter *.pdb", script);
        Assert.Contains("AVALONIA_TELEMETRY_OPTOUT", script);
    }

    [Fact]
    public void The_app_asks_for_admin_rights_and_the_installer_refuses_a_program_that_doesnt()
    {
        string manifest = File.ReadAllText(Path.Combine(Root, "src", "MfgManager.App", "app.manifest"));
        Assert.Contains("level=\"requireAdministrator\"", manifest);

        // What the build really produced, not only what the project asks for.
        string built = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "MfgManager.dll")));
        Assert.Contains("level=\"requireAdministrator\"", built);

        string script = File.ReadAllText(Path.Combine(Root, "tools", "build-installer.ps1"));
        Assert.Contains("requireAdministrator", script);
        Assert.Contains("throw", script);
    }

    [Fact]
    public void The_licence_is_MIT_and_comes_with_every_build()
    {
        string licence = File.ReadAllText(Path.Combine(Root, "LICENSE"));
        Assert.StartsWith("MIT License", licence);
        Assert.Contains("Permission is hereby granted", licence);

        Assert.Contains("""..\..\LICENSE""", File.ReadAllText(Path.Combine(Root, "src", "MfgManager.App", "MfgManager.App.csproj")));
        string pkgbuild = File.ReadAllText(Linux("PKGBUILD"));
        Assert.Contains("license=('MIT')", pkgbuild);
        Assert.Contains("usr/share/licenses", pkgbuild);
        Assert.Contains("usr/share/doc/mfg-manager/copyright", File.ReadAllText(Path.Combine(Root, "tools", "build-linux.sh")));
    }

    [Fact]
    public void The_readme_says_what_a_new_user_has_to_know()
    {
        string readme = File.ReadAllText(Path.Combine(Root, "README.md"));
        foreach (string warning in new[] { "Unofficial", "isn't affiliated", "get you banned", "isn't included", "admin rights", "isn't code-signed", "Linux is experimental", "MIT License" })
            Assert.Contains(warning, readme);

        Assert.True(File.Exists(Path.Combine(Root, "docs", "screenshot-dark.png")));
        Assert.True(File.Exists(Path.Combine(Root, "docs", "screenshot-light.png")));
    }

    [Theory]
    [InlineData("bin/")]
    [InlineData("obj/")]
    [InlineData(".idea/")]
    [InlineData("*.user")]
    [InlineData("TestResults/")]
    [InlineData("artifacts/")]
    public void Git_leaves_out_build_results_and_personal_IDE_files(string pattern) =>
        Assert.Contains(pattern, File.ReadAllLines(Path.Combine(Root, ".gitignore")).ToList());

    [Fact]
    public void No_file_contains_a_path_to_someones_user_folder()
    {
        var kinds = new HashSet<string> { ".cs", ".axaml", ".csproj", ".props", ".sln", ".md", ".ps1", ".sh", ".json", ".wxs", ".wxl", ".desktop", "" };
        var skipped = new HashSet<string> { "bin", "obj", "artifacts", ".idea", ".vs", ".git", "TestResults", "Licenses" };
        var personal = new Regex("""[A-Za-z]:[\\/]+Users[\\/]+[A-Za-z]""");

        IEnumerable<string> found = Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Where(file => kinds.Contains(Path.GetExtension(file)) && !Path.GetRelativePath(Root, file).Split(Path.DirectorySeparatorChar).Any(skipped.Contains))
            .SelectMany(file => File.ReadLines(file).Where(line => personal.IsMatch(line)).Select(line => $"{Path.GetFileName(file)}: {line.Trim()}"));

        Assert.Empty(found);
    }

    [Fact]
    public void Release_builds_ignore_the_environment_variables_that_could_load_other_code()
    {
        string project = File.ReadAllText(Path.Combine(Root, "src", "MfgManager.App", "MfgManager.App.csproj"));
        Assert.Contains("<StartupHookSupport>false</StartupHookSupport>", project);
        Assert.Contains(">Global</AppHostDotNetSearch>", project);

        string script = File.ReadAllText(Path.Combine(Root, "tools", "build-installer.ps1"));
        Assert.Contains("""System\.StartupHookProvider\.IsSupported""", script);
    }

    [Theory]
    [InlineData("src/MfgManager.App/Services/WindowFrame.cs")]
    [InlineData("installer/CustomActions/FolderPicker.cs")]
    public void Windows_calls_only_load_their_dlls_from_the_system_folder(string file)
    {
        string text = File.ReadAllText(Path.Combine(Root, file));

        int calls = Regex.Count(text, """\[(DllImport|LibraryImport)\(""");
        Assert.True(calls > 0);
        Assert.Equal(calls, Regex.Count(text, """\[DefaultDllImportSearchPaths\(DllImportSearchPath\.System32\)\]"""));
    }

    [Fact]
    public void Every_class_has_its_own_file()
    {
        // A type nested inside a class is indented, so only top-level types start at the beginning of a line.
        var topLevelType = new Regex("""^(?:(?:public|internal|sealed|static|abstract|partial|readonly|file)\s+)*(?:class|record|struct|interface|enum|delegate)\s""", RegexOptions.Multiline);
        var counts = CodeFolders
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(Root, folder), "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .ToDictionary(file => file, file => topLevelType.Count(File.ReadAllText(file)));

        Assert.True(counts.Values.Sum() > 50, "the check found too few types, so it's probably not looking in the right place");
        Assert.Empty(counts.Where(file => file.Value > 1).Select(file => file.Key));
    }

    [Fact]
    public void A_no_admin_test_build_cant_leave_its_result_behind_for_a_normal_build()
    {
        // A normal build right after one of these once reused it, and an installer shipped without admin rights.
        string project = File.ReadAllText(Path.Combine(Root, "src", "MfgManager.App", "MfgManager.App.csproj"));
        Assert.Contains("NO_ADMIN_MANIFEST", project);
    }

    [Fact]
    public void Linux_build_uses_no_secrets_and_keeps_telemetry_off()
    {
        string script = File.ReadAllText(Path.Combine(Root, "tools", "build-linux.sh"));
        Assert.Contains("AVALONIA_TELEMETRY_OPTOUT=1", script);
        Assert.Contains("DOTNET_CLI_TELEMETRY_OPTOUT=1", script);
        Assert.DoesNotContain("TOKEN", script);
        Assert.DoesNotContain("PASSWORD", script);
        Assert.DoesNotContain(".env", script);
    }
}
