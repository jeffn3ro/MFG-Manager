namespace MfgManager.App.Services;

/// <summary>
/// Pop-up questions, file pickers and the clipboard. The ViewModels only talk to this interface,
/// so they don't depend on real windows, and tests can swap in a fake.
/// </summary>
public interface IDialogs
{
    Task<bool> ConfirmAsync(string message, bool warning = false);
    Task ShowAsync(string message, bool error = false);

    /// <param name="typeName">Shown in the picker, e.g. "Programs".</param>
    /// <param name="pattern">Which files can be picked, e.g. "*.exe".</param>
    Task<string?> PickFileAsync(string title, string typeName, string pattern, string? startFolder = null);

    Task CopyTextAsync(string text);
}
