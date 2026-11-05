namespace MfgManager.App.ViewModels;

/// <summary>One line in a sidebar list, e.g. "Steam" with how many games it has.</summary>
public sealed class FilterOption(string name) : ObservableObject
{
    public string Name { get; } = name;

    public int Count
    {
        get;
        set => SetProperty(ref field, value);
    }
}
