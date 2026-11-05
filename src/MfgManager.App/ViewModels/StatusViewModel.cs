namespace MfgManager.App.ViewModels;

/// <summary>The status line at the bottom of the window: what's happening, and whether the app is busy.</summary>
public sealed class StatusViewModel : ObservableObject
{
    private string _text = "Starting...";
    private bool _isBusy;

    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }
}
