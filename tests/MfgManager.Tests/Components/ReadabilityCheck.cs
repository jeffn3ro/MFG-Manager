using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace MfgManager.Tests.Components;

/// <summary>
/// Goes through everything a window is really showing, finds every piece of text, works out what's behind it,
/// and reports any text that's hard to read. It looks at what's on screen rather than at the colour list,
/// so it also catches a wrong colour on a single label or a control whose own theme doesn't fit.
/// </summary>
public static class ReadabilityCheck
{
    public static void Assert_all_text_is_readable(TopLevel window, string context)
    {
        List<string> problems = FindProblems(window);
        Assert.True(problems.Count == 0, $"{context}: hard to read:\n" + string.Join("\n", problems));
    }

    /// <summary>How many pieces of text were checked. Tests use this to make sure the check really looked at something.</summary>
    public static int CountTexts(TopLevel window) => window.GetVisualDescendants().OfType<Control>().Count(c => c.IsEffectivelyVisible && TextOf(c) is { Length: > 0 });

    public static List<string> FindProblems(TopLevel window)
    {
        var problems = new List<string>();
        foreach (Control control in window.GetVisualDescendants().OfType<Control>())
        {
            if (!control.IsEffectivelyVisible || TextOf(control) is not { Length: > 0 } text) continue;

            bool large = FontSizeOf(control) >= 24 || (FontSizeOf(control) >= 18.66 && (int)FontWeightOf(control) >= 700);
            double minimum = large ? Contrast.LargeText : Contrast.NormalText;

            foreach (Color foreground in ForegroundsOf(control))
                foreach (Color background in BackgroundsBehind(control))
                {
                    Color shown = Contrast.Blend(foreground, background);
                    double ratio = Contrast.Ratio(shown, background);
                    if (ratio < minimum)
                        problems.Add($"  \"{Short(text)}\" in {control.GetType().Name}: {shown} on {background} is only {ratio:0.00}:1 (needs {minimum})");
                }
        }
        return [.. problems.Distinct()];
    }

    private static string? TextOf(Control control) => control switch
    {
        TextBlock { Inlines.Count: > 0 } block => string.Concat(block.Inlines.OfType<Run>().Select(run => run.Text)),
        TextBlock block => block.Text,
        TextPresenter presenter => presenter.Text,
        _ => null,
    };

    private static double FontSizeOf(Control control) => control is TextBlock block ? block.FontSize : control is TextPresenter presenter ? presenter.FontSize : 14;
    private static FontWeight FontWeightOf(Control control) => control is TextBlock block ? block.FontWeight : FontWeight.Normal;

    /// <summary>The text's colour, plus the colour of any part of it that has its own.</summary>
    private static IEnumerable<Color> ForegroundsOf(Control control)
    {
        IBrush? main = control switch
        {
            TextBlock block => block.Foreground,
            TextPresenter presenter => presenter.Foreground,
            _ => null,
        };
        foreach (Color color in ColorsOf(main, OpacityOf(control))) yield return color;

        if (control is TextBlock { Inlines: { } inlines })
            foreach (Run run in inlines.OfType<Run>())
                if (run.IsSet(TextElement.ForegroundProperty))
                    foreach (Color color in ColorsOf(run.Foreground, OpacityOf(control))) yield return color;
    }

    private static double OpacityOf(Visual visual)
    {
        double opacity = 1.0;
        for (Visual? v = visual; v is not null; v = v.GetVisualParent()) opacity *= v.Opacity;
        return opacity;
    }

    /// <summary>Everything behind the text, top to bottom, flattened into the colours you end up seeing.</summary>
    private static List<Color> BackgroundsBehind(Control control)
    {
        var layers = new List<IBrush>();
        for (Visual? v = control.GetVisualParent(); v is not null; v = v.GetVisualParent())
        {
            IBrush? brush = v switch
            {
                Border border => border.Background,
                Panel panel => panel.Background,
                ContentPresenter presenter => presenter.Background,
                // Text boxes and dropdowns draw their background beside the text, not behind it, so ask the control itself.
                TemplatedControl templated => templated.Background,
                _ => null,
            };
            if (brush is null) continue;
            layers.Add(brush);
            if (ColorsOf(brush, 1).All(color => color.A == 255)) break;
        }

        // Paint from the bottom up, starting from black if somehow nothing is solid.
        List<Color> result = [Colors.Black];
        for (int i = layers.Count - 1; i >= 0; i--)
        {
            var colors = ColorsOf(layers[i], 1).ToList();
            result = [.. result.SelectMany(below => colors.Select(color => Contrast.Blend(color, below))).Distinct()];
        }
        return result;
    }

    /// <summary>A plain colour, or both ends of a gradient: the text has to be readable at every point of it.</summary>
    private static IEnumerable<Color> ColorsOf(IBrush? brush, double opacity)
    {
        double total = opacity * (brush?.Opacity ?? 1);
        switch (brush)
        {
            case ISolidColorBrush solid:
                yield return Faded(solid.Color, total);
                break;
            case IGradientBrush gradient:
                foreach (IGradientStop stop in gradient.GradientStops) yield return Faded(stop.Color, total);
                break;
        }
    }

    private static Color Faded(Color color, double opacity) => Color.FromArgb((byte)Math.Round(color.A * opacity), color.R, color.G, color.B);

    private static string Short(string text) => text.Length <= 40 ? text : text[..40] + "...";
}
