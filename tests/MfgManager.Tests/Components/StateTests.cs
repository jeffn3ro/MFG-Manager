using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using MfgManager.App.Components.Details;
using MfgManager.App.Components.Library;
using MfgManager.App.Components.Shell;
using MfgManager.App.Components.Sidebar;
using MfgManager.App.Components.Toolbar;
using MfgManager.App.Converters;
using MfgManager.App.ViewModels;
using MfgManager.Tests.ViewModels;

namespace MfgManager.Tests.Components;

/// <summary>
/// Colours change when you point at, press, tick or open something, and a theme can get those wrong while
/// the resting look is fine. These tests really move the mouse and check the text in each of those states.
/// </summary>
public class StateTests
{
    public static TheoryData<string> Modes => Stage.Modes;

    /// <summary>The middle of a control, if that's what the mouse would really land on there.</summary>
    private static Point? ReachablePoint(TopLevel window, Control control)
    {
        var middle = new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
        if (control.TranslatePoint(middle, window) is not { } point) return null;
        return window.InputHitTest(point) is Visual hit && hit.GetSelfAndVisualAncestors().Contains(control) ? point : null;
    }

    private static string Describe(Control control) => control is ContentControl { Content: string text } ? text : control.GetType().Name;

    private static void AssertReadableWhilePointingAndPressing(Window window, Control control, Point away, string mode)
    {
        if (ReachablePoint(window, control) is not { } point) return;

        window.MouseMove(point);
        Stage.Settle();
        Assert.True(control.IsPointerOver, $"{Describe(control)} didn't notice the mouse");
        ReadabilityCheck.Assert_all_text_is_readable(window, $"pointing at {Describe(control)} in {mode} mode");

        // Letting go somewhere else doesn't count as a click, so nothing really runs.
        window.MouseDown(point, MouseButton.Left);
        Stage.Settle();
        ReadabilityCheck.Assert_all_text_is_readable(window, $"pressing {Describe(control)} in {mode} mode");
        window.MouseMove(away);
        window.MouseUp(away, MouseButton.Left);
        Stage.Settle();
    }

    private static void PointAt(TopLevel window, Control control)
    {
        Point? point = ReachablePoint(window, control);
        Assert.NotNull(point);
        window.MouseMove(point.Value);
        Stage.Settle();
    }

    [Theory, MemberData(nameof(Modes))]
    public Task Every_button_stays_readable_when_pointed_at_and_pressed(string mode) => TestApp.Run(() =>
    {
        using AppSetup s = ComponentTests.Library();
        s.Library.OnlyFrameGen = false;
        Window window = Stage.Show(new MainWindow(), mode, 1280, 820, s.Vm);
        var statusBar = new Point(640, 810);

        var buttons = window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.IsEffectivelyEnabled).ToList();
        Assert.True(buttons.Count > 8, $"only found {buttons.Count} buttons");
        foreach (Button button in buttons) AssertReadableWhilePointingAndPressing(window, button, statusBar, mode);

        // Pressing and letting go elsewhere didn't click anything.
        Assert.Equal("Alpha Strike", s.Library.SelectedGame?.Name);
        PointAt(window, buttons.Single(b => (b.Content as string) == "Install / Update"));
        Stage.Snapshot(window, "Point-accent", mode);
        window.Close();
    });

    [Theory, MemberData(nameof(Modes))]
    public Task Sidebar_items_stay_readable_when_pointed_at_and_pressed(string mode) => TestApp.Run(() =>
    {
        using AppSetup s = ComponentTests.Library();
        Window window = Stage.Show(new Sidebar { DataContext = s.Vm }, mode, 248, 700);
        var bottom = new Point(120, 480);

        var items = window.GetVisualDescendants().OfType<ListBoxItem>().ToList();
        Assert.True(items.Count > 6);
        foreach (ListBoxItem item in items) AssertReadableWhilePointingAndPressing(window, item, bottom, mode);

        PointAt(window, items[2]);
        Stage.Snapshot(window, "Point-sidebar", mode);
        window.Close();
    });

    [Theory, MemberData(nameof(Modes))]
    public Task Cover_wall_shows_every_label_and_the_selected_card(string mode) => TestApp.Run(() =>
    {
        using AppSetup s = ComponentTests.Library();
        s.Library.OnlyFrameGen = false;
        s.Library.GroupBy = LibraryViewModel.NoGrouping;
        Window window = Stage.Show(new CoverWall { DataContext = s.Library }, mode, 1000, 720);

        var cards = window.GetVisualDescendants().OfType<GameCard>().ToList();
        Assert.Equal(6, cards.Count);
        ReadabilityCheck.Assert_all_text_is_readable(window, $"flat wall in {mode} mode");

        // "Delta Force..." has a two-line name and shares the first row. Its cover must still line up with the others.
        var firstRow = cards.Take(5).Select(c => c.GetVisualDescendants().OfType<Image>().Single().TranslatePoint(default, window)!.Value.Y).ToList();
        Assert.Single(firstRow.Distinct());

        GameCard other = cards.First(c => ((GameViewModel)c.DataContext!).Name == "Beta Run");
        PointAt(window, other.GetVisualDescendants().OfType<Button>().Single());
        ReadabilityCheck.Assert_all_text_is_readable(window, $"pointing at a card in {mode} mode");
        Stage.Snapshot(window, "CoverWall-flat", mode);
        window.Close();
    });

    [Theory, MemberData(nameof(Modes))]
    public Task Open_dropdown_is_readable(string mode) => TestApp.Run(() =>
    {
        using AppSetup s = ComponentTests.Library();
        Window window = Stage.Show(new LibraryToolbar { DataContext = s.Vm }, mode, 660, 300);
        ComboBox groupBy = window.GetVisualDescendants().OfType<ComboBox>().Single();

        groupBy.IsDropDownOpen = true;
        Stage.Settle();

        Popup popup = groupBy.GetVisualDescendants().OfType<Popup>().Single();
        TopLevel list = TopLevel.GetTopLevel(popup.Child)!;
        var items = list.GetVisualDescendants().OfType<ComboBoxItem>().ToList();
        Assert.Equal(3, items.Count);
        ReadabilityCheck.Assert_all_text_is_readable(list, $"open dropdown in {mode} mode");
        Stage.Snapshot(list, "Dropdown", mode);

        foreach (ComboBoxItem item in items)
        {
            PointAt(list, item);
            ReadabilityCheck.Assert_all_text_is_readable(list, $"pointing at {item.Content} in {mode} mode");
        }
        Stage.Snapshot(list, "Dropdown-point", mode);

        groupBy.IsDropDownOpen = false;
        window.Close();
    });

    [Theory, MemberData(nameof(Modes))]
    public Task Theme_dropdown_is_readable_and_lists_all_three(string mode) => TestApp.Run(() =>
    {
        using AppSetup s = ComponentTests.Library();
        Window window = Stage.Show(new ThemePicker { DataContext = s.Vm }, mode, 248, 300);
        ComboBox picker = window.GetVisualDescendants().OfType<ComboBox>().Single();

        picker.IsDropDownOpen = true;
        Stage.Settle();

        Popup popup = picker.GetVisualDescendants().OfType<Popup>().Single();
        TopLevel list = TopLevel.GetTopLevel(popup.Child)!;
        Assert.Equal(3, list.GetVisualDescendants().OfType<ComboBoxItem>().Count());
        ReadabilityCheck.Assert_all_text_is_readable(list, $"theme dropdown in {mode} mode");

        picker.IsDropDownOpen = false;
        window.Close();
    });

    [Theory, MemberData(nameof(Modes))]
    public Task Ticked_box_is_easy_to_see(string mode) => TestApp.Run(() =>
    {
        using AppSetup s = ComponentTests.Library();
        Window window = Stage.Show(new LibraryToolbar { DataContext = s.Vm }, mode, 660, 120);
        CheckBox box = window.GetVisualDescendants().OfType<CheckBox>().Single();
        box.IsChecked = true;
        Stage.Settle();

        void AssertTickStandsOut(string state)
        {
            Avalonia.Controls.Shapes.Path tick = box.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(p => p.Name == "CheckGlyph");
            Border square = box.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "NormalRectangle");
            double ratio = Contrast.Ratio(((ISolidColorBrush)tick.Fill!).Color, ((ISolidColorBrush)square.Background!).Color);
            Assert.True(ratio >= Contrast.LargeText, $"the tick {state} is only {ratio:0.00}:1 in {mode} mode");
        }

        AssertTickStandsOut("at rest");
        ReadabilityCheck.Assert_all_text_is_readable(window, $"ticked box in {mode} mode");
        Stage.Snapshot(window, "Toolbar-ticked", mode);

        PointAt(window, box);
        AssertTickStandsOut("when pointed at");
        ReadabilityCheck.Assert_all_text_is_readable(window, $"pointing at the ticked box in {mode} mode");
        window.Close();
    });

    [Theory, MemberData(nameof(Modes))]
    public Task Search_box_is_readable_when_typing_and_selecting(string mode) => TestApp.Run(() =>
    {
        using AppSetup s = ComponentTests.Library();
        Window window = Stage.Show(new LibraryToolbar { DataContext = s.Vm }, mode, 660, 120);
        TextBox search = window.GetVisualDescendants().OfType<TextBox>().Single(box => box.PlaceholderText == "Search games");

        PointAt(window, search);
        ReadabilityCheck.Assert_all_text_is_readable(window, $"pointing at search in {mode} mode");

        search.Text = "Alpha Strike";
        search.Focus();
        Stage.Settle();
        ReadabilityCheck.Assert_all_text_is_readable(window, $"typing in search in {mode} mode");

        search.SelectAll();
        Stage.Settle();
        Color background = ((ISolidColorBrush)search.SelectionBrush!).Color;
        Color foreground = ((ISolidColorBrush)(search.SelectionForegroundBrush ?? search.Foreground)!).Color;
        double ratio = Contrast.Ratio(foreground, background);
        Assert.True(ratio >= Contrast.NormalText, $"selected text {foreground} on {background} is only {ratio:0.00}:1 in {mode} mode");
        Stage.Snapshot(window, "Toolbar-selected-text", mode);
        window.Close();
    });

    [Theory, MemberData(nameof(Modes))]
    public Task Tooltip_is_readable(string mode) => TestApp.Run(() =>
    {
        // A tooltip is fully see-through until it opens, so show this one as an open one looks.
        Window window = Stage.Show(new ToolTip { Content = """E:\Games\Alpha Strike""", Opacity = 1 }, mode, 300, 80);
        ReadabilityCheck.Assert_all_text_is_readable(window, $"tooltip in {mode} mode");
        Stage.Snapshot(window, "Tooltip", mode);
        window.Close();
    });

    [Theory, MemberData(nameof(Modes))]
    public Task The_smallest_allowed_window_still_fits_everything(string mode) => TestApp.Run(() =>
    {
        using AppSetup s = ComponentTests.Library();
        s.Library.OnlyFrameGen = false;
        var main = new MainWindow();
        Window window = Stage.Show(main, mode, main.MinWidth, main.MinHeight, s.Vm);

        double detailsStart = window.GetVisualDescendants().OfType<DetailsPanel>().Single().TranslatePoint(default, window)!.Value.X;
        LibraryToolbar toolbar = window.GetVisualDescendants().OfType<LibraryToolbar>().Single();
        foreach (Control control in toolbar.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && c is Button or ComboBox or CheckBox))
        {
            double right = control.TranslatePoint(new Point(control.Bounds.Width, 0), window)!.Value.X;
            Assert.True(right <= detailsStart, $"{Describe(control)} is cut off: it ends at {right}, the details panel starts at {detailsStart}");
        }
        TextBox search = toolbar.GetVisualDescendants().OfType<TextBox>().Single(box => box.PlaceholderText == "Search games");
        Assert.True(search.Bounds.Width >= 150, $"the search box is squashed to {search.Bounds.Width}");

        // What doesn't fit has to be reachable by scrolling.
        Button openFolder = window.GetVisualDescendants().OfType<Button>().Single(b => (b.Content as string) == "Open folder");
        openFolder.BringIntoView();
        Stage.Settle();
        Assert.NotNull(ReachablePoint(window, openFolder));

        ReadabilityCheck.Assert_all_text_is_readable(window, $"small window in {mode} mode");
        Stage.Snapshot(window, "MainWindow-small", mode);
        window.Close();
    });

    [Fact]
    public void Long_paths_can_wrap_between_folders_but_look_the_same()
    {
        string shown = (string)WrapPathConverter.Instance.Convert("""E:\GOG\Cyberpunk 2077\bin\x64\Cyberpunk2077.exe""", typeof(string), null, System.Globalization.CultureInfo.InvariantCulture)!;
        Assert.Equal("""E:\GOG\Cyberpunk 2077\bin\x64\Cyberpunk2077.exe""", shown.Replace("\u200B", ""));
        Assert.Equal(5, shown.Count(c => c == '\u200B'));

        string linux = (string)WrapPathConverter.Instance.Convert("/home/me/game.exe", typeof(string), null, System.Globalization.CultureInfo.InvariantCulture)!;
        Assert.Equal(3, linux.Count(c => c == '\u200B'));

        Assert.Equal("No game .exe found", WrapPathConverter.Instance.Convert("No game .exe found", typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Null(WrapPathConverter.Instance.Convert(null, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Counting_games_reads_like_english()
    {
        Assert.Equal("1 game", LibraryViewModel.CountOfGames(1));
        Assert.Equal("3 games", LibraryViewModel.CountOfGames(3));
    }
}
