using System.Globalization;
using Avalonia.Data.Converters;
using MfgManager.App.Services;

namespace MfgManager.App.Converters;

/// <summary>A file path -> a picture, for cover art. <see cref="CoverImages"/> loads it small and remembers it.</summary>
public sealed class CoverImageConverter : IValueConverter
{
    public static readonly CoverImageConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string path ? CoverImages.Get(path) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
