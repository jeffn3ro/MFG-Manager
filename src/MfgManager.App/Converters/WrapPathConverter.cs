using System.Globalization;
using Avalonia.Data.Converters;

namespace MfgManager.App.Converters;

/// <summary>
/// Lets a long path wrap between folders. Without this the text only has one place to break, right after "C:",
/// and the rest gets cut in the middle of a folder name. The added characters are invisible.
/// </summary>
public sealed class WrapPathConverter : IValueConverter
{
    public static readonly WrapPathConverter Instance = new();

    private const string ZeroWidthSpace = "​";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string path ? path.Replace("\\", "\\" + ZeroWidthSpace).Replace("/", "/" + ZeroWidthSpace) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
