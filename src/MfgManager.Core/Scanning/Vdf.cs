using System.Text.RegularExpressions;

namespace MfgManager.Core.Scanning;

/// <summary>
/// Reads values out of Steam's ".vdf"/".acf" text files, which look like:
/// <code>"name"   "Monster Hunter Wilds"</code>
/// We only need simple "key" "value" pairs, so a regex is enough.
/// </summary>
public static class Vdf
{
    public static IEnumerable<string> GetValues(string text, string key)
    {
        string pattern = "\"" + Regex.Escape(key) + "\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"";
        foreach (Match match in Regex.Matches(text, pattern, RegexOptions.IgnoreCase))
        {
            // Steam escapes backslashes in paths: "C:\\Games" means C:\Games.
            yield return match.Groups[1].Value.Replace("""\\""", """\""");
        }
    }

    public static string? GetValue(string text, string key) => GetValues(text, key).FirstOrDefault();
}
