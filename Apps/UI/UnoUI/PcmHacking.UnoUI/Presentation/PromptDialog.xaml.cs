using Microsoft.UI.Xaml.Input;
using PcmHacking.UnoUI.Services;
using System.Runtime.CompilerServices;
using Uno.Extensions.Reactive.Commands;

namespace PcmHacking.UnoUI.Presentation;

public enum PrimaryButton
{
    Left,
    Middle,
    Close
}

public enum DialogResult
{
    None,
    ActionLeft,
    ActionMiddle,
    Close
}

public partial class PromptDialog : ContentDialog
{
    public DialogResult Result { get; private set; }
    public string? ComboBoxSelection { get; set; }
    public string BodyText { get; set; }
    public bool ShowComboBox { get; set; }
    public List<string> ComboBoxItems { get; set; }

    public PromptDialog(string title, string message, PrimaryButton primarySelection = PrimaryButton.Close, string closeText = "Confirm", string? leftButton = null, List<string>? comboBoxSource = null, string? midButton = null)
    {
        this.InitializeComponent();
        ShowComboBox = false;
        DataContext = this;
        XamlRoot = XamlRootService.GetXamlRoot();
        Title = title;
        BodyText = message;
        CloseButtonText = closeText;
        CloseButtonClick += PromptDialog_CloseButtonClick;
        IsPrimaryButtonEnabled = false;
        IsSecondaryButtonEnabled = false;
        if (leftButton != null)
        {
            PrimaryButtonText = leftButton;
            PrimaryButtonClick += PromptDialog_PrimaryButtonClick;
            IsPrimaryButtonEnabled = true;
        }
        if (midButton != null)
        {
            SecondaryButtonText = midButton;
            SecondaryButtonClick += PromptDialog_SecondaryButtonClick;
            IsSecondaryButtonEnabled = true;
        }
        if (comboBoxSource != null)
        {
            ItemComboBox.ItemsSource = comboBoxSource;
            ShowComboBox = true;
        }
        SetPrimaryButton(primarySelection);
    }

    private void PromptDialog_CloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Result = DialogResult.Close;
    }

    private void PromptDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Result = DialogResult.ActionMiddle;
    }

    private void PromptDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Result = DialogResult.ActionLeft;
    }

    public void SetPrimaryButton(PrimaryButton button)
    {
        switch (button)
        {
            case PrimaryButton.Left:
                DefaultButton = ContentDialogButton.Primary;
                break;
            case PrimaryButton.Middle:
                DefaultButton = ContentDialogButton.Secondary;
                break;
            case PrimaryButton.Close:
                DefaultButton = ContentDialogButton.Close;
                break;
        }
    }
}

public sealed partial class AlertPrompt : PromptDialog
{
    public AlertPrompt(string title, string message, PrimaryButton primarySelection = PrimaryButton.Close, string closeText = "Confirm", string? leftButton = null, List<string>? comboBoxSource = null, string? midButton = null)
        : base(title, message, primarySelection, closeText, leftButton, comboBoxSource, midButton)
    {
    }
}

public sealed partial class BinaryPrompt : PromptDialog
{
    public BinaryPrompt(string title, string message, string acceptText = "Yes", string cancelText = "No", PrimaryButton primarySelection = PrimaryButton.Close)
        : base(title, message, primarySelection, cancelText, acceptText)
    {
    }
}

public sealed partial class MultipleChoicePrompt : PromptDialog
{
    public MultipleChoicePrompt(string title, string message, string ChoiceA, string choiceB, string cancelText, PrimaryButton primarySelection = PrimaryButton.Close)
        : base(title, message, primarySelection, cancelText, ChoiceA, null, choiceB)
    {
    }
}

public sealed partial class TwoButtonComboPrompt : PromptDialog
{
    public TwoButtonComboPrompt(string title, string message, string acceptText, string cancelText, List<string> itemsSource, PrimaryButton primarySelection = PrimaryButton.Close)
        : base(title, message, primarySelection, cancelText, acceptText, itemsSource)
    {
    }
}