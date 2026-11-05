using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace MfgManager.App.Converters;

/// <summary>A file path -> a picture, for cover art. Loads it small, doesn't keep the file open, and remembers it for next time.</summary>
public sealed class CoverImageConverter : IValueConverter
{
    public static readonly CoverImageConverter Instance = new();

    private const int LoadedWidth = 340;                     // cards are 170 px wide; twice that stays sharp on high-resolution screens
    private const long MaxFileBytes = 32 * 1024 * 1024;      // real covers are far smaller; anything this big isn't worth decoding
    private const int MaxRemembered = 256;

    private readonly Dictionary<string, Bitmap?> _loaded = [];

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path) return null;
        if (_loaded.TryGetValue(path, out var known)) return known;

        if (_loaded.Count >= MaxRemembered) _loaded.Clear();
        return _loaded[path] = Load(path);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();

    private static Bitmap? Load(string path)
    {
        try
        {
            if (new FileInfo(path).Length > MaxFileBytes) return null;
            using var stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, LoadedWidth);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NullReferenceException)
        {
            return null;   // missing or broken image file: the card shows its placeholder instead
        }
    }
}
