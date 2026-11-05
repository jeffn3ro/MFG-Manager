using Avalonia.Media;

namespace MfgManager.Tests.Components;

/// <summary>How easy text is to read on a background, using the WCAG formula: 1 means invisible, 21 is black on white.</summary>
public static class Contrast
{
    /// <summary>The usual minimum for normal text (WCAG AA).</summary>
    public const double NormalText = 4.5;

    /// <summary>The minimum for large text (18.66 px bold, or 24 px).</summary>
    public const double LargeText = 3.0;

    public static double Ratio(Color a, Color b)
    {
        double lighter = Math.Max(Luminance(a), Luminance(b));
        double darker = Math.Min(Luminance(a), Luminance(b));
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>The colour you see when a see-through colour is painted over a solid one.</summary>
    public static Color Blend(Color top, Color bottom)
    {
        double alpha = top.A / 255.0;
        byte Mix(byte t, byte b) => (byte)Math.Round(t * alpha + b * (1 - alpha));
        return Color.FromRgb(Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
    }

    private static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            double c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }
}
