namespace MfgManager.App.ViewModels;

/// <summary>One line in a sidebar list, e.g. "Steam" with how many games it has.</summary>
public sealed class FilterOption(string name) : ObservableObject
{
    private int _count;

    public string Name { get; } = name;

    public int Count
    {
        get => _count;
        set => SetProperty(ref _count, value);
    }
}
