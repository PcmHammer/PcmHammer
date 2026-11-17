using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
// On the android target Android.App.Application and Android.Widget.Button are also in scope; pin these
// names to the Avalonia types so this desktop dialog still compiles for the android head.
using Application = Avalonia.Application;
using Button = Avalonia.Controls.Button;

namespace PCMHammerAvalonia.Views;

public enum MessageBoxButton
{
    OK,
    OKCancel,
    YesNo,
    YesNoCancel
}

public enum MessageBoxResult
{
    None,
    OK,
    Cancel,
    Yes,
    No
}

public partial class MessageBoxWindow : Window
{
    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    public MessageBoxWindow()
    {
        InitializeComponent();
    }

    public MessageBoxWindow(string text, string title, MessageBoxButton buttons) : this()
    {
        Title = title;
        MessageText.Text = text;
        AddButtons(buttons);
    }

    private void AddButtons(MessageBoxButton buttons)
    {
        switch (buttons)
        {
            case MessageBoxButton.OK:
                AddButton("OK", MessageBoxResult.OK, isDefault: true);
                break;
            case MessageBoxButton.OKCancel:
                AddButton("OK", MessageBoxResult.OK, isDefault: true);
                AddButton("Cancel", MessageBoxResult.Cancel, isCancel: true);
                break;
            case MessageBoxButton.YesNo:
                AddButton("Yes", MessageBoxResult.Yes, isDefault: true);
                AddButton("No", MessageBoxResult.No, isCancel: true);
                break;
            case MessageBoxButton.YesNoCancel:
                AddButton("Yes", MessageBoxResult.Yes, isDefault: true);
                AddButton("No", MessageBoxResult.No);
                AddButton("Cancel", MessageBoxResult.Cancel, isCancel: true);
                break;
        }
    }

    private void AddButton(string caption, MessageBoxResult result, bool isDefault = false, bool isCancel = false)
    {
        var btn = new Button
        {
            Content = caption,
            MinWidth = 75,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            IsDefault = isDefault,
            IsCancel = isCancel
        };

        btn.Click += (_, _) =>
        {
            Result = result;
            Close(result);
        };

        ButtonPanel.Children.Add(btn);
    }
}

public static class MessageBox
{
    public static async Task<MessageBoxResult> ShowAsync(
        string messageBoxText,
        string caption = "",
        MessageBoxButton button = MessageBoxButton.OK,
        Window? owner = null)
    {
        var msgBox = new MessageBoxWindow(messageBoxText, caption, button);

        // Resolve active window owner if not supplied
        owner ??= GetActiveWindow();

        if (owner != null)
        {
            return await msgBox.ShowDialog<MessageBoxResult>(owner);
        }

        // Fallback if no window context is available
        msgBox.Show();
        return MessageBoxResult.None;
    }

    private static Window? GetActiveWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.MainWindow;
        }
        return null;
    }
}