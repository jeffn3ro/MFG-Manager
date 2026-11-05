using System.Text.RegularExpressions;

namespace MfgManager.Core.Mod;

/// <summary>A copy of MFGUnlocker.dll on disk (usually in your Downloads folder) and its version.</summary>
public sealed record ModPackage(string DllPath, Version Version)
{
    /// <summary>The "Product name" written inside the mod's DLL. That's how we recognise it.</summary>
    public const string ProductName = "MFG Unlocker";
    public const string FileName = "MFGUnlocker.dll";

    /// <summary>Reads the version out of a DLL, or null when the DLL isn't MFG Unlocker.</summary>
    public static Version? ReadVersion(string dllPath)
    {
        var info = PeVersionInfo.Read(dllPath);
        return info?.ProductName == ProductName ? ParseVersion(info.ProductVersion) : null;
    }

    /// <summary>The newest MFGUnlocker.dll in a folder (looks two folders deep, like an extracted zip).</summary>
    public static ModPackage? FindNewestIn(string folder, Func<string, Version?>? readVersion = null)
    {
        if (!Directory.Exists(folder)) return null;
        readVersion ??= ReadVersion;

        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true };
        return Directory.EnumerateFiles(folder, FileName, options)
            .Select(path => readVersion(path) is { } version ? new ModPackage(path, version) : null)
            .OfType<ModPackage>()
            .MaxBy(package => package.Version);
    }

    /// <summary>
    /// The mod in the app's own Mods folder, plus a newer copy waiting in Downloads if there is one.
    /// The app must ask before using that newer copy with <see cref="Import"/>: any program can put files in Downloads and
    /// fake the "MFG Unlocker" name inside a DLL, and this app copies the DLL into game folders. So it never takes one silently.
    /// </summary>
    public static (ModPackage? InMods, ModPackage? NewerDownload) FindCopies(string modsFolder, string downloadsFolder, Func<string, Version?>? readVersion = null)
    {
        var inMods = FindNewestIn(modsFolder, readVersion);
        var inDownloads = FindNewestIn(downloadsFolder, readVersion);
        return (inMods, inDownloads is not null && (inMods is null || inDownloads.Version > inMods.Version) ? inDownloads : null);
    }

    /// <summary>Copies a mod DLL to Mods\MFGUnlocker-v[version]\MFGUnlocker.dll (unless it's already in the Mods folder).</summary>
    public static ModPackage Import(ModPackage source, string modsFolder)
    {
        if (PathUtil.IsInside(source.DllPath, modsFolder)) return source;

        var target = Path.Combine(modsFolder, $"MFGUnlocker-v{source.Version.ToString(3)}", FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source.DllPath, target, overwrite: true);
        return source with { DllPath = target };
    }

    /// <summary>
    /// "v1.7.0", "1.7.0.1145" or "v1.5.0-hotfix.1" -> a Version. Unreadable text, or a number too big to be real, -> 0.0.0.
    /// This text comes from DLLs and from GitHub, so a strange value must not stop the app.
    /// </summary>
    public static Version ParseVersion(string? text)
    {
        var unreadable = new Version(0, 0, 0);
        var match = Regex.Match(text ?? "", @"(\d+)(?:\.(\d+))?(?:\.(\d+))?");
        if (!match.Success) return unreadable;

        var parts = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            var group = match.Groups[i + 1];
            if (group.Success && !int.TryParse(group.Value, out parts[i])) return unreadable;
        }
        return new Version(parts[0], parts[1], parts[2]);
    }
}
