using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;

namespace MfgManager.Tests.Components;

/// <summary>Puts one component in a window nobody sees, draws it for real, and can save a picture of it.</summary>
public static class Stage
{
    public static readonly TheoryData<string> Modes = new("Dark", "Light");

    /// <summary>Set the MFG_SHOTS environment variable to a folder to get a picture of every component in both modes.</summary>
    private static string? ShotsFolder => Environment.GetEnvironmentVariable("MFG_SHOTS");

    public static ThemeVariant Mode(string name) => name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

    public static Window Show(Control component, string modeName, double width = 800, double height = 600, object? dataContext = null)
    {
        Application.Current!.RequestedThemeVariant = Mode(modeName);
        var window = component as Window ?? new Window { Content = component };
        window.Width = width;
        window.Height = height;
        if (dataContext is not null) window.DataContext = dataContext;
        window.Show();
        Settle();
        return window;
    }

    /// <summary>Lets the window finish layout, bindings and anything waiting to run.</summary>
    public static void Settle()
    {
        for (var i = 0; i < 4; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    /// <summary>Saves a picture if MFG_SHOTS is set. The drawing itself always runs, so a crash while drawing fails the test.</summary>
    public static void Snapshot(TopLevel window, string name, string modeName)
    {
        Settle();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (ShotsFolder is not { } folder) return;
        Directory.CreateDirectory(folder);
        frame.Save(Path.Combine(folder, $"{name}-{modeName}.png"), PngBitmapEncoderOptions.Default);
    }
}
