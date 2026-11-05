using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace MfgManager.App.Components.Sidebar;

public partial class FilterList : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<FilterList, string?>(nameof(Title));

    public static readonly StyledProperty<IEnumerable?> OptionsProperty =
        AvaloniaProperty.Register<FilterList, IEnumerable?>(nameof(Options));

    public static readonly StyledProperty<object?> SelectedProperty =
        AvaloniaProperty.Register<FilterList, object?>(nameof(Selected), defaultBindingMode: BindingMode.TwoWay);

    public FilterList() => InitializeComponent();

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public IEnumerable? Options
    {
        get => GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    public object? Selected
    {
        get => GetValue(SelectedProperty);
        set => SetValue(SelectedProperty, value);
    }
}
