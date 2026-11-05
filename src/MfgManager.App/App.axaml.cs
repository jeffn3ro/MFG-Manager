using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MfgManager.App.Components.Shell;
using MfgManager.App.Services;
using MfgManager.App.ViewModels;
using MfgManager.Core.Mod;
using MfgManager.Core.Platform;
using MfgManager.Core.Scanning;
using MfgManager.Core.Settings;
using MfgManager.Core.Store;

namespace MfgManager.App;

/// <summary>The app's starting point (like main.ts in Angular): builds the services and opens the window.</summary>
public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var dialogs = new Dialogs(window);
            var viewModel = CreateMainViewModel(dialogs);
            window.DataContext = viewModel;
            desktop.MainWindow = window;
            window.Opened += async (_, _) => await viewModel.InitializeAsync();

            // Last safety net: an unexpected error shows a message instead of closing the app.
            Dispatcher.UIThread.UnhandledException += async (_, e) =>
            {
                e.Handled = true;
                await dialogs.ShowAsync(e.Exception.Message, error: true);
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Creates every service and hands them to the ViewModel (Angular would do this with dependency injection).</summary>
    public static MainViewModel CreateMainViewModel(IDialogs dialogs)
    {
        var dataFolder = AppPaths.DataFolder;
        var settingsStore = new SettingsStore(Path.Combine(dataFolder, "settings.json"));
        var settings = settingsStore.Load();
        void SaveSettings() => settingsStore.Save(settings);

        var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize = 64 * 1024 * 1024,   // store answers and cover art are far smaller
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MFG-Manager/2.0");   // GitHub requires a name

        AppPaths.EnsureFolder(dataFolder);
        var modsFolder = AppPaths.ModsFolder;

        return new MainViewModel(
            settings,
            SaveSettings,
            new GameScanner(settings),
            new ModInstaller(settings, SaveSettings, Path.Combine(dataFolder, "Removed mod files")),
            new SteamStore(http, Path.Combine(dataFolder, "cache")),
            new UpdateChecker(http),
            dialogs,
            new ThemeService(),
            modsFolder,
            KnownFolders.Downloads);
    }
}
