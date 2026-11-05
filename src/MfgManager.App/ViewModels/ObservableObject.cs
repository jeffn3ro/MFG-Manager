using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MfgManager.App.ViewModels;

/// <summary>
/// Base class for ViewModels. If you know Angular: a ViewModel is like a component class, the .axaml file is its
/// template, and {Binding Name} in the markup is like {{ name }} or [value]="name".
/// Calling SetProperty tells the window "this value changed, redraw it".
/// Angular's change detection does this for you automatically; here it takes this event.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
