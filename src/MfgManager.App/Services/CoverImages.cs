using System.Collections.Concurrent;
using Avalonia.Media.Imaging;

namespace MfgManager.App.Services;

/// <summary>
/// Cover art, decoded small and remembered. The library decodes each cover in the background before it shows it,
/// so the window finds it here ready to draw instead of pausing to decode it.
/// </summary>
public static class CoverImages
{
    // Cards are 170 px wide; twice that stays sharp on high-resolution screens.
    private const int LoadedWidth = 340;
    // Real covers are far smaller; anything this big isn't worth decoding.
    private const long MaxFileBytes = 32 * 1024 * 1024;
    private const int MaxRemembered = 256;

    private static readonly ConcurrentDictionary<string, Bitmap?> Loaded = new();

    /// <summary>The picture in a file, decoded now if it isn't remembered yet. Null if the file can't be shown.</summary>
    public static Bitmap? Get(string path)
    {
        if (Loaded.TryGetValue(path, out Bitmap? known)) return known;
        if (Loaded.Count >= MaxRemembered) Loaded.Clear();
        return Loaded.GetOrAdd(path, Load);
    }

    /// <summary>Decodes the picture off the window's thread, so showing it afterwards costs nothing.</summary>
    public static Task PreloadAsync(string path) => Task.Run(() => Get(path));

    private static Bitmap? Load(string path)
    {
        try
        {
            if (new FileInfo(path).Length > MaxFileBytes) return null;
            // Not kept open, so the cache folder can be cleaned while the app runs.
            using FileStream stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, LoadedWidth);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NullReferenceException)
        {
            // Missing or broken image file: the card shows its placeholder instead.
            return null;
        }
    }
}
