using MfgManager.Core.Scanning;

namespace MfgManager.Tests.Core;

public class VdfTests
{
    [Fact]
    public void Reads_values_and_unescapes_paths()
    {
        string text = """
            "libraryfolders"
            {
                "0" { "path"   "C:\\Program Files (x86)\\Steam" }
                "1" { "path"   "E:\\SteamLibrary" }
            }
            """;

        Assert.Equal(["""C:\Program Files (x86)\Steam""", """E:\SteamLibrary"""], Vdf.GetValues(text, "path"));
    }

    [Fact]
    public void Missing_key_gives_null() => Assert.Null(Vdf.GetValue("\"name\" \"x\"", "installdir"));
}
