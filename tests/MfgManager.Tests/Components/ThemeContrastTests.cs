using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace MfgManager.Tests.Components;

/// <summary>Checks every text colour against every surface it's used on, in both modes.</summary>
public class ThemeContrastTests
{
    public static readonly TheoryData<string> Modes = new("Dark", "Light");

    private static readonly string[] Surfaces = ["WindowBrush", "PanelBrush", "RaisedBrush", "HoverBrush", "SelectedBrush"];
    private static readonly string[] TextsOnSurfaces = ["TextBrush", "TextSecondaryBrush", "TextMutedBrush", "AccentTextBrush"];

    /// <summary>Colours that are always used together: a text colour and what's behind it.</summary>
    private static readonly (string Text, string Background)[] Pairs =
    [
        ("NeutralTextBrush", "NeutralBackBrush"),
        ("SuccessTextBrush", "SuccessBackBrush"),
        ("InfoTextBrush", "InfoBackBrush"),
        ("WarningTextBrush", "WarningBackBrush"),
        ("DangerTextBrush", "DangerBackBrush"),
        ("OnAccentDisabledBrush", "AccentDisabledBrush"),
        ("ButtonForeground", "ButtonBackground"),
        ("ButtonForegroundPointerOver", "ButtonBackgroundPointerOver"),
        ("ButtonForegroundPressed", "ButtonBackgroundPressed"),
        ("ButtonForegroundDisabled", "ButtonBackgroundDisabled"),
        ("TextControlForeground", "TextControlBackground"),
        ("TextControlForegroundPointerOver", "TextControlBackgroundPointerOver"),
        ("TextControlForegroundFocused", "TextControlBackgroundFocused"),
        ("TextControlPlaceholderForeground", "TextControlBackground"),
        ("TextControlPlaceholderForegroundPointerOver", "TextControlBackgroundPointerOver"),
        ("TextControlPlaceholderForegroundFocused", "TextControlBackgroundFocused"),
        ("ComboBoxForeground", "ComboBoxBackground"),
        ("ComboBoxForeground", "ComboBoxBackgroundPointerOver"),
        ("ComboBoxForeground", "ComboBoxBackgroundPressed"),
        ("ComboBoxForeground", "ComboBoxDropDownBackground"),
        ("ComboBoxForegroundDisabled", "ComboBoxBackgroundDisabled"),
        ("ComboBoxItemForeground", "ComboBoxDropDownBackground"),
        ("ComboBoxItemForegroundPointerOver", "ComboBoxItemBackgroundPointerOver"),
        ("ComboBoxItemForegroundPressed", "ComboBoxItemBackgroundPressed"),
        ("ComboBoxItemForegroundSelected", "ComboBoxItemBackgroundSelected"),
        ("ComboBoxItemForegroundSelectedPointerOver", "ComboBoxItemBackgroundSelectedPointerOver"),
        ("ComboBoxItemForegroundSelectedPressed", "ComboBoxItemBackgroundSelectedPressed"),
        ("CheckBoxCheckGlyphForegroundChecked", "CheckBoxCheckBackgroundFillChecked"),
        ("CheckBoxCheckGlyphForegroundCheckedPointerOver", "CheckBoxCheckBackgroundFillCheckedPointerOver"),
        ("CheckBoxCheckGlyphForegroundCheckedPressed", "CheckBoxCheckBackgroundFillCheckedPressed"),
        ("OnTextSelectionBrush", "TextSelectionBrush"),
        ("ToolTipForeground", "ToolTipBackground"),
    ];

    private static object Lookup(string key, ThemeVariant mode)
    {
        Assert.True(Application.Current!.Resources.TryGetResource(key, mode, out var value), $"{key} is missing in {mode}");
        return value!;
    }

    private static Color ColorOf(string key, ThemeVariant mode) => Assert.IsType<SolidColorBrush>(Lookup(key, mode)).Color;

    private static ThemeVariant Mode(string name) => name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

    [Theory]
    [MemberData(nameof(Modes))]
    public Task Every_text_colour_is_readable_on_every_surface(string modeName) => TestApp.Run(() =>
    {
        var mode = Mode(modeName);
        foreach (var surface in Surfaces)
        foreach (var text in TextsOnSurfaces)
        {
            var ratio = Contrast.Ratio(ColorOf(text, mode), ColorOf(surface, mode));
            Assert.True(ratio >= Contrast.NormalText, $"{modeName}: {text} on {surface} is only {ratio:0.00}:1");
        }
    });

    [Theory]
    [MemberData(nameof(Modes))]
    public Task Every_colour_pair_is_readable(string modeName) => TestApp.Run(() =>
    {
        var mode = Mode(modeName);
        foreach (var (text, background) in Pairs)
        {
            var ratio = Contrast.Ratio(ColorOf(text, mode), ColorOf(background, mode));
            Assert.True(ratio >= Contrast.NormalText, $"{modeName}: {text} on {background} is only {ratio:0.00}:1");
        }
    });

    [Fact]
    public Task Text_on_the_gold_to_lime_buttons_is_readable_at_every_point_of_the_gradient() => TestApp.Run(() =>
    {
        var onAccent = ((SolidColorBrush)Lookup("OnAccentBrush", ThemeVariant.Dark)).Color;

        foreach (var key in new[] { "AccentBrush", "AccentHoverBrush", "AccentPressedBrush" })
        {
            var gradient = (LinearGradientBrush)Lookup(key, ThemeVariant.Dark);
            foreach (var stop in gradient.GradientStops)
            {
                var ratio = Contrast.Ratio(onAccent, stop.Color);
                Assert.True(ratio >= Contrast.NormalText, $"text on {key} at {stop.Offset} is only {ratio:0.00}:1");
            }
        }
    });

    [Fact]
    public Task Dark_and_light_mode_define_exactly_the_same_colours() => TestApp.Run(() =>
    {
        var colors = Application.Current!.Resources.MergedDictionaries.OfType<ResourceDictionary>().Single();
        var dark = (ResourceDictionary)colors.ThemeDictionaries[ThemeVariant.Dark];
        var light = (ResourceDictionary)colors.ThemeDictionaries[ThemeVariant.Light];

        Assert.Equal(dark.Keys.Select(k => k.ToString()).Order(), light.Keys.Select(k => k.ToString()).Order());
    });
}
