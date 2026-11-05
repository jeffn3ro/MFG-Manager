using Avalonia.Controls;
using MfgManager.App.Services;

namespace MfgManager.App.Components.Shell;

/// <summary>
/// Almost no code here: everything the window does lives in MainViewModel,
/// and the components connect to it with {Binding ...}.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        WindowFrame.FollowTheme(this);
    }
}
