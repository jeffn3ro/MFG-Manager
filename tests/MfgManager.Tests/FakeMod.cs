using MfgManager.Core.Mod;

namespace MfgManager.Tests;

/// <summary>
/// The tests don't use the real MFGUnlocker.dll. Instead a "fake mod" is a text file containing
/// "MOD 1.7.0", and this reader treats such files as the mod with that version.
/// </summary>
public static class FakeMod
{
    public static Version? ReadVersion(string path)
    {
        string text = System.IO.File.ReadAllText(path);
        return text.StartsWith("MOD ", StringComparison.Ordinal) ? ModPackage.ParseVersion(text[4..]) : null;
    }

    public static ModPackage Create(TempFolder temp, string version) =>
        new(temp.File($"downloads/MFGUnlocker-v{version}/MFGUnlocker.dll", $"MOD {version}"), new Version(version));
}
