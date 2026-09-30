// SPDX-License-Identifier: GPL-3.0-only
using Microsoft.UI.Dispatching;
using PcmHacking.UnoUI.Utilities;

namespace PcmHacking.UnoUI.Services;

/// <summary>
/// The prompts <see cref="ReadManager"/> and <see cref="WriteManager"/> hand back to the UI. Callers
/// are on an operation's worker thread, so every prompt marshals to the UI thread.
/// </summary>
public interface IPromptService
{
    /// <summary>Tell the user something and wait for them to acknowledge it.</summary>
    Task Alert(string message, string title);

    /// <summary>Ask a yes/no question. Returns false if the dialog cannot be shown.</summary>
    Task<bool> AskYesNo(string message, string title);

    /// <summary>
    /// Ask which PCM is connected, for when the operating system query gave no answer. Returns
    /// <see cref="PcmType.Undefined"/> if the user cancels, which aborts the operation.
    /// </summary>
    Task<PcmType> AskPcmType();
}

public class PromptService : IPromptService
{
    private readonly DispatcherQueue dispatcherQueue;
    private readonly LoggerAdapter logger;

    public PromptService(DispatcherQueue dispatcherQueue, LoggerAdapter logger)
    {
        this.dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Alert(string message, string title)
    {
        // Mirror into the log; a dismissed dialog leaves no trace to ask for help with.
        this.logger.AddUserMessage(title + ": " + message);

        await this.ShowOnUiThread(() =>
        {
            ContentDialog dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = XamlRootService.GetXamlRoot(),
            };
            return dialog.ShowAsync().AsTask();
        });
    }

    public async Task<bool> AskYesNo(string message, string title)
    {
        this.logger.AddUserMessage(title + ": " + message);

        ContentDialogResult result = await this.ShowOnUiThread(() =>
        {
            ContentDialog dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                PrimaryButtonText = "Yes",
                CloseButtonText = "No",

                // Default to the safe answer, so dismissing the dialog does not consent to a write.
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRootService.GetXamlRoot(),
            };
            return dialog.ShowAsync().AsTask();
        }, ContentDialogResult.None);

        bool answer = result == ContentDialogResult.Primary;
        this.logger.AddUserMessage(answer ? "User chose to proceed." : "User chose not to proceed.");
        return answer;
    }

    public async Task<PcmType> AskPcmType()
    {
        // Types come from the shared catalog, so this list matches the other UIs' pickers.
        List<PcmType> types = OperationOptions.SelectablePcmTypes().ToList();

        (ContentDialogResult result, ComboBox? list) = await this.ShowOnUiThread(() =>
        {
            ComboBox picker = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = types.Select(type => type.ToString()).ToList(),
                SelectedIndex = 0,
            };

            StackPanel content = new StackPanel { Spacing = 8 };
            content.Children.Add(new TextBlock
            {
                Text = "The PCM did not report its operating system. Choose the PCM type to use.",
                TextWrapping = TextWrapping.WrapWholeWords,
            });
            content.Children.Add(picker);

            ContentDialog dialog = new ContentDialog
            {
                Title = "PCM Type",
                Content = content,
                PrimaryButtonText = "OK",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRootService.GetXamlRoot(),
            };

            return ShowAndReturn(dialog, picker);
        }, (ContentDialogResult.None, (ComboBox?)null));

        if (result != ContentDialogResult.Primary || list == null || list.SelectedIndex < 0)
        {
            return PcmType.Undefined;
        }

        return types[list.SelectedIndex];
    }

    private static async Task<(ContentDialogResult, ComboBox?)> ShowAndReturn(ContentDialog dialog, ComboBox picker)
    {
        ContentDialogResult result = await dialog.ShowAsync();
        return (result, picker);
    }

    /// <summary>
    /// Run a dialog on the UI thread and wait for it. A failure to show one returns the fallback
    /// rather than taking the operation down.
    /// </summary>
    private async Task<T> ShowOnUiThread<T>(Func<Task<T>> show, T fallback)
    {
        TaskCompletionSource<T> completion = new TaskCompletionSource<T>();

        if (!this.dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                completion.TrySetResult(await show());
            }
            catch (Exception exception)
            {
                this.logger.AddDebugMessage("Unable to show dialog: " + exception.ToString());
                completion.TrySetResult(fallback);
            }
        }))
        {
            this.logger.AddDebugMessage("Unable to reach the UI thread to show a dialog.");
            return fallback;
        }

        return await completion.Task;
    }

    private Task ShowOnUiThread(Func<Task> show) =>
        this.ShowOnUiThread<bool>(async () => { await show(); return true; }, false);
}
