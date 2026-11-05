using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Styling;

namespace MfgManager.App.Services;

/// <summary>
/// Makes a window's title bar follow light and dark mode. Avalonia only does this on Windows 11,
/// which left a white bar on top of a dark window on Windows 10. On Linux the desktop draws the
/// title bar and follows your desktop's theme, so there's nothing to do there.
/// </summary>
public static partial class WindowFrame
{
    private const int UseDarkMode = 20;
    private const int UseDarkModeOnOlderWindows10 = 19;
    private const uint ActivationChangedMessage = 0x0086;

    // Loaded from Windows' own folder only, so a file with the same name somewhere else can't be picked up instead.
    [LibraryImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint SendMessageW(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetForegroundWindow();

    public static void FollowTheme(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;
        // The window exists but isn't on screen yet, so it never shows the wrong colour.
        Apply(window);
        window.Opened += (_, _) => Apply(window);
        window.ActualThemeVariantChanged += (_, _) => Apply(window);
    }

    private static void Apply(Window window)
    {
        // No real window, as in the tests.
        if (window.TryGetPlatformHandle() is not { HandleDescriptor: "HWND" } handle) return;

        int dark = window.ActualThemeVariant == ThemeVariant.Dark ? 1 : 0;
        if (DwmSetWindowAttribute(handle.Handle, UseDarkMode, ref dark, sizeof(int)) != 0)
            _ = DwmSetWindowAttribute(handle.Handle, UseDarkModeOnOlderWindows10, ref dark, sizeof(int));

        // Windows 10 only redraws the bar when the window gains or loses focus, so pretend that just happened.
        int isActive = GetForegroundWindow() == handle.Handle ? 1 : 0;
        SendMessageW(handle.Handle, ActivationChangedMessage, 1 - isActive, 0);
        SendMessageW(handle.Handle, ActivationChangedMessage, isActive, 0);
    }
}
