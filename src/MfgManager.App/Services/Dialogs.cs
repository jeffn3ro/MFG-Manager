using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using MfgManager.App.Components.Dialogs;

namespace MfgManager.App.Services;

/// <summary>The real pop-ups and file pickers, shown on top of the main window.</summary>
public sealed class Dialogs(Window owner) : IDialogs
{
    public async Task<bool> ConfirmAsync(string message, bool warning = false)
    {
        var dialog = new MessageDialog(message, warning ? MessageKind.Warning : MessageKind.Question, isQuestion: true);
        return await dialog.ShowDialog<bool>(owner);
    }

    public async Task ShowAsync(string message, bool error = false)
    {
        var dialog = new MessageDialog(message, error ? MessageKind.Error : MessageKind.Information, isQuestion: false);
        await dialog.ShowDialog<bool>(owner);
    }

    public async Task<string?> PickFileAsync(string title, string typeName, string pattern, string? startFolder = null)
    {
        var storage = owner.StorageProvider;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(typeName) { Patterns = [pattern] }],
            SuggestedStartLocation = startFolder is not null && Directory.Exists(startFolder)
                ? await storage.TryGetFolderFromPathAsync(startFolder)
                : null,
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task CopyTextAsync(string text)
    {
        if (owner.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    }
}
