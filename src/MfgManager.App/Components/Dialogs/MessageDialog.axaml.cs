using Avalonia.Controls;
using Avalonia.Interactivity;
using MfgManager.App.Services;

namespace MfgManager.App.Components.Dialogs;

public enum MessageKind { Question, Warning, Error, Information }

/// <summary>Shows a message with Yes and No, or just OK. Closing the window counts as No.</summary>
public partial class MessageDialog : Window
{
    public MessageDialog()
    {
        InitializeComponent();
        WindowFrame.FollowTheme(this);
    }

    public MessageDialog(string message, MessageKind kind, bool isQuestion) : this()
    {
        MessageText.Text = message;
        YesButton.Content = isQuestion ? "Yes" : "OK";
        NoButton.IsVisible = isQuestion;

        // A question defaults to the safe answer, so pressing Enter by accident doesn't install or delete anything.
        YesButton.IsDefault = !isQuestion;
        NoButton.IsDefault = isQuestion;

        Marker.Classes.Add(kind switch
        {
            MessageKind.Warning => "warning",
            MessageKind.Error => "danger",
            _ => "info",
        });
    }

    private void OnYes(object? sender, RoutedEventArgs e) => Close(true);

    private void OnNo(object? sender, RoutedEventArgs e) => Close(false);
}
