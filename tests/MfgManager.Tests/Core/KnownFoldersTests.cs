using MfgManager.Core.Platform;

namespace MfgManager.Tests.Core;

public class KnownFoldersTests
{
    [Theory]
    // Translated folder names work.
    [InlineData("XDG_DOWNLOAD_DIR=\"$HOME/Nedlastinger\"", "/home/me/Nedlastinger")]
    [InlineData("# comment\nXDG_DESKTOP_DIR=\"$HOME/Desktop\"\nXDG_DOWNLOAD_DIR=\"$HOME/Downloads\"\n", "/home/me/Downloads")]
    [InlineData("XDG_DESKTOP_DIR=\"$HOME/Desktop\"", null)]
    [InlineData("", null)]
    public void Reads_the_Linux_downloads_folder(string text, string? expected) =>
        Assert.Equal(expected?.Replace('/', Path.DirectorySeparatorChar), KnownFolders.ParseUserDirs(text, "/home/me".Replace('/', Path.DirectorySeparatorChar))?.Replace('/', Path.DirectorySeparatorChar));
}
