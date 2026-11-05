using Avalonia;

namespace MfgManager.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Windows needs admin rights to change game folders under Program Files. Linux doesn't: games are in your
        // own folders. Running as root there would only give a downloaded DLL and its folders more power than needed.
        if (!OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess)
        {
            Console.Error.WriteLine("MFG Manager doesn't need root. Please run it as your normal user.");
            return 1;
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Also used by Avalonia's design tools, so it has to stay public and static.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
