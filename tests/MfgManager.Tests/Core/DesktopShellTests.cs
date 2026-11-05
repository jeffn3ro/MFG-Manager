using System.Diagnostics;
using MfgManager.Core.Platform;

namespace MfgManager.Tests.Core;

public class DesktopShellTests
{
    private const string Url = "https://github.com/x/y";

    [Fact]
    public void Windows_opens_web_pages_through_Explorer_so_the_browser_does_not_run_as_admin()
    {
        ProcessStartInfo command = DesktopShell.WebPageCommand(Url, isWindows: true);

        Assert.Equal("explorer.exe", command.FileName);
        Assert.Equal(Url, Assert.Single(command.ArgumentList));
        Assert.False(command.UseShellExecute);
    }

    [Fact]
    public void Linux_uses_xdg_open_and_never_a_shell()
    {
        ProcessStartInfo command = DesktopShell.WebPageCommand("https://x/;rm -rf ~", isWindows: false);

        Assert.Equal("xdg-open", command.FileName);
        // One argument, never run as a command.
        Assert.Equal("https://x/;rm -rf ~", Assert.Single(command.ArgumentList));
        Assert.False(command.UseShellExecute);
    }

    [Fact]
    public void Reveal_selects_the_file_on_Windows_and_opens_its_folder_on_Linux()
    {
        Assert.Equal("/select,\"C:\\Games\\a.exe\"", DesktopShell.RevealCommand("""C:\Games\a.exe""", isWindows: true).Arguments);

        string folder = Path.Combine("games", "x");
        Assert.Equal(folder, Assert.Single(DesktopShell.RevealCommand(Path.Combine(folder, "a.exe"), isWindows: false).ArgumentList));
    }
}
