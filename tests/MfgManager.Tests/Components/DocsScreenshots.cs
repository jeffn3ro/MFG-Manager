using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MfgManager.App.Components.Shell;
using MfgManager.App.ViewModels;
using MfgManager.Core.Mod;
using MfgManager.Core.Models;
using MfgManager.Core.Settings;
using MfgManager.Core.Store;
using MfgManager.Tests.ViewModels;

namespace MfgManager.Tests.Components;

/// <summary>
/// Draws the pictures in docs/ with made-up games and made-up covers, so they show no real username, game library or
/// copyrighted art. Only runs when you ask for it: set MFG_DOCS to the docs folder.
/// The paths on screen need a short, neutral folder, so it creates C:\Demo Games and deletes it again.
/// </summary>
public class DocsScreenshots
{
    public static TheoryData<string> Modes => Stage.Modes;

    private const string Root = """C:\Demo Games""";

    private static readonly (string Name, Launcher Launcher, string From, string To, string Drm, string? AntiCheat, string? ModVersion)[] Games =
    [
        ("Starfall Drift", Launcher.Steam, "#1B2A6B", "#E0559B", "Steamworks", null, "1.7.0"),
        ("Neon Harbor Nights", Launcher.Steam, "#0B3C49", "#2FD6B5", "Steamworks", null, null),
        ("Ironwake Protocol", Launcher.Steam, "#2B3036", "#F2A33A", "Steamworks", "Easy Anti-Cheat", null),
        ("Hollow Circuit", Launcher.Gog, "#20123A", "#8E5BE8", "GOG Galaxy", null, "1.6.0"),
        ("Ember Vale Saga", Launcher.Epic, "#4A1410", "#F06A2C", "Epic Online Services", null, "1.7.0"),
        ("Gridlock 2099", Launcher.Ubisoft, "#0F2E1F", "#A3E635", "Ubisoft Connect", null, null),
    ];

    private static string MakeCover(string name, string from, string to)
    {
        string path = Path.Combine(Root, $"{name}.png");
        using var bitmap = new RenderTargetBitmap(new PixelSize(340, 510));
        using (DrawingContext context = bitmap.CreateDrawingContext())
        {
            var background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse(from), 0), new GradientStop(Color.Parse(to), 1) },
            };
            context.FillRectangle(background, new Rect(0, 0, 340, 510));

            var title = new FormattedText(name.ToUpperInvariant(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("fonts:Inter#Inter"), weight: FontWeight.Bold), 36, Brushes.White)
            { MaxTextWidth = 296 };
            context.DrawText(title, new Point(22, 510 - 30 - title.Height));
        }
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
        return path;
    }

    [Theory, MemberData(nameof(Modes))]
    public Task Draw_the_pictures_for_the_docs(string mode) => TestApp.Run(() =>
    {
        string? docs = Environment.GetEnvironmentVariable("MFG_DOCS");
        Assert.SkipWhen(docs is null, "Set MFG_DOCS to the docs folder to draw the pictures");
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The neutral paths on screen are Windows paths");

        using var s = new AppSetup();
        Directory.CreateDirectory(Root);
        try
        {
            s.Vm.Mod.Mod = new ModPackage(Path.Combine(Root, "Mods", "MFGUnlocker-v1.7.0", ModPackage.FileName), new Version(1, 7, 0));

            foreach ((string Name, Launcher Launcher, string From, string To, string Drm, string? AntiCheat, string? ModVersion) demo in Games)
            {
                string folder = Directory.CreateDirectory(Path.Combine(Root, demo.Name)).FullName;
                string exe = Path.Combine(folder, $"{demo.Name}.exe");
                File.WriteAllText(exe, "demo");

                var game = new Game { Name = demo.Name, Launcher = demo.Launcher, Folder = folder, Exe = exe, HasFrameGen = true, AntiCheat = demo.AntiCheat };
                game.Drm.Add(demo.Drm);
                if (demo.ModVersion is { } version)
                {
                    File.WriteAllText(Path.Combine(folder, "version.dll"), $"MOD {version}");
                    s.Settings.Installs.Add(new InstallRecord(folder, "version.dll", version, DateTime.Now));
                }

                var card = new GameViewModel(game, select => s.Library.SelectedGame = select) { CoverPath = MakeCover(demo.Name, demo.From, demo.To) };
                if (demo.Name == "Starfall Drift") card.StoreDrm = new StoreDrm("Denuvo Anti-tamper", ["Denuvo"]);
                s.Library.Games.Add(card);
            }

            s.Library.RefreshStatuses();
            s.Library.RefreshFilterOptions();
            s.Library.GroupBy = LibraryViewModel.NoGrouping;
            s.Library.SelectedGame = s.Library.Games.First();
            s.Vm.Status.Text = $"Found {Games.Length} games, {Games.Length} with DLSS Frame Generation.";

            Window window = Stage.Show(new MainWindow(), mode, 1280, 820, s.Vm);
            Stage.Settle();
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
                frame!.Save(Path.Combine(docs!, $"screenshot-{mode.ToLowerInvariant()}.png"), PngBitmapEncoderOptions.Default);
            window.Close();
        }
        finally
        {
            Directory.Delete(Root, recursive: true);
        }
    });
}
