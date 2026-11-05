using Avalonia;
using Avalonia.Styling;
using MfgManager.Core.Settings;

namespace MfgManager.App.Services;

/// <summary>Switches the whole app between light, dark and following the system.</summary>
public sealed class ThemeService
{
    public void Apply(ThemeChoice choice)
    {
        if (Application.Current is not { } app) return;
        app.RequestedThemeVariant = choice switch
        {
            ThemeChoice.Light => ThemeVariant.Light,
            ThemeChoice.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
