using MfgManager.App.Services;

namespace MfgManager.Tests.ViewModels;

/// <summary>Answers every question with <see cref="Answer"/> and remembers what was asked.</summary>
public sealed class FakeDialogs : IDialogs
{
    public bool Answer { get; set; } = true;
    // What "Open file" returns (null = Cancel).
    public string? PickedFile { get; set; }
    public List<string> Messages { get; } = [];
    public string? Copied { get; private set; }

    public Task<bool> ConfirmAsync(string message, bool warning = false) { Messages.Add(message); return Task.FromResult(Answer); }
    public Task ShowAsync(string message, bool error = false) { Messages.Add(message); return Task.CompletedTask; }
    public Task<string?> PickFileAsync(string title, string typeName, string pattern, string? startFolder = null) => Task.FromResult(PickedFile);
    public Task CopyTextAsync(string text) { Copied = text; return Task.CompletedTask; }
}
