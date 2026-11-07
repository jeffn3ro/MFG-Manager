using System.ComponentModel;
using System.Windows.Input;

namespace MfgManager.App.ViewModels;

/// <summary>
/// A button action, bound in markup with Command="{Binding InstallCommand}".
/// While an async action runs, the button is disabled so it can't be clicked twice.
/// </summary>
public sealed class RelayCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool _running;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(
            () =>
            {
                execute();
                return Task.CompletedTask;
            },
            canExecute)
    {
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter = null) => !_running && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter) => await ExecuteAsync();

    /// <summary>Runs the action and finishes when it's done. Tests use this; the window uses <see cref="Execute"/>.</summary>
    public async Task ExecuteAsync()
    {
        _running = true;
        NotifyCanExecuteChanged();
        try
        {
            await execute();
        }
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
        foreach (INotifyPropertyChanged source in sources) source.PropertyChanged += (_, _) => NotifyCanExecuteChanged();
        return this;
    }
}
