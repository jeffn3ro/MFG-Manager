using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace MfgManager.App.ViewModels;

// Two tiny building blocks for MVVM ("Model - View - ViewModel").
// If you know Angular: a ViewModel is like a component class, the .axaml file is its template,
// and {Binding Name} in the markup is like {{ name }} / [value]="name".

/// <summary>
/// Base class for ViewModels. Calling SetProperty tells the window "this value changed, redraw it"
/// (Angular's change detection does this for you automatically; here it takes this event).
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

/// <summary>
/// A button action. In markup: Command="{Binding InstallCommand}" (like (click)="install()" in Angular).
/// While an async action runs, the button is disabled so it can't be clicked twice.
/// </summary>
public sealed class RelayCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool _running;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(() => { execute(); return Task.CompletedTask; }, canExecute) { }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter = null) => !_running && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter) => await ExecuteAsync();

    /// <summary>Runs the action and finishes when it's done. Tests use this; the window uses <see cref="Execute"/>.</summary>
    public async Task ExecuteAsync()
    {
        _running = true;
        NotifyCanExecuteChanged();
        try { await execute(); }
        finally
        {
            _running = false;
            NotifyCanExecuteChanged();
        }
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Re-checks whether the button is enabled whenever anything on these objects changes, so a button
    /// can't get stuck enabled or disabled. WPF did this by itself; Avalonia leaves it to us.
    /// </summary>
    public RelayCommand WatchEnabledOn(params INotifyPropertyChanged[] sources)
    {
        foreach (var source in sources) source.PropertyChanged += (_, _) => NotifyCanExecuteChanged();
        return this;
    }
}
