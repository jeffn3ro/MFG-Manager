using Microsoft.Win32;

namespace MfgManager.Core.Scanning.Launchers;

internal static class WindowsRegistry
{
    /// <summary>Reads one value from every sub key of an HKLM key. Nothing on other systems.</summary>
    public static IEnumerable<string> ReadSubKeyValues(string keyPath, string valueName)
    {
        if (!OperatingSystem.IsWindows()) return [];

        using RegistryKey? root = Registry.LocalMachine.OpenSubKey(keyPath);
        if (root is null) return [];

        var values = new List<string>();
        foreach (string name in root.GetSubKeyNames())
        {
            using RegistryKey? key = root.OpenSubKey(name);
            if (key?.GetValue(valueName) is string value && value.Length > 0) values.Add(value);
        }
        return values;
    }
}
