// SPDX-License-Identifier: GPL-3.0-only
using System.Windows;
using System.Windows.Controls;

namespace PCMHammer.Helpers;

/// <summary>
/// Drives a TextBox from a <see cref="LogTextBuffer"/>: appends each delta instead of re-setting
/// Text, and keeps the newest line in view. Auto-scroll is a property of the control rather than of
/// the text, so it belongs here rather than in a view model.
/// </summary>
public static class LogTextBoxBehavior
{
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.RegisterAttached(
            "Source",
            typeof(LogTextBuffer),
            typeof(LogTextBoxBehavior),
            new PropertyMetadata(null, OnSourceChanged));

    // Holds the event subscription so it can be undone if the source is swapped.
    private static readonly DependencyProperty SubscriptionProperty =
        DependencyProperty.RegisterAttached(
            "Subscription",
            typeof(Subscription),
            typeof(LogTextBoxBehavior));

    public static LogTextBuffer? GetSource(DependencyObject element) =>
        (LogTextBuffer?)element.GetValue(SourceProperty);

    public static void SetSource(DependencyObject element, LogTextBuffer? value) =>
        element.SetValue(SourceProperty, value);

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox textBox)
        {
            return;
        }

        (textBox.GetValue(SubscriptionProperty) as Subscription)?.Detach();

        textBox.SetValue(
            SubscriptionProperty,
            e.NewValue is LogTextBuffer buffer ? new Subscription(textBox, buffer) : null);
    }

    private sealed class Subscription
    {
        private readonly TextBox _textBox;
        private readonly LogTextBuffer _buffer;

        public Subscription(TextBox textBox, LogTextBuffer buffer)
        {
            _textBox = textBox;
            _buffer = buffer;

            buffer.Appended += OnAppended;
            buffer.Replaced += OnReplaced;

            // Whatever was logged before the view existed.
            OnReplaced(buffer.Snapshot());
        }

        public void Detach()
        {
            _buffer.Appended -= OnAppended;
            _buffer.Replaced -= OnReplaced;
        }

        private void OnAppended(string text)
        {
            _textBox.AppendText(text);
            _textBox.ScrollToEnd();
        }

        private void OnReplaced(string text)
        {
            _textBox.Text = text;
            _textBox.ScrollToEnd();
        }
    }
}
