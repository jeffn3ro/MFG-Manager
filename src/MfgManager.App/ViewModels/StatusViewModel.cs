namespace MfgManager.App.ViewModels;

/// <summary>The status line at the bottom of the window: what's happening, and whether the app is busy.</summary>
public sealed class StatusViewModel : ObservableObject
{
    public string Text
    {
        get;
        set => SetProperty(ref field, value);
    } = "Starting...";

    public bool IsBusy
    {
        get;
        set => SetProperty(ref field, value);
    }
}
