// SPDX-License-Identifier: GPL-3.0-only
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PCMHammer.Helpers;

/// <summary>
/// Attached behaviours for the log list panes (Results, Debug, Bus Monitor): keep the newest line in
/// view while parked at the bottom, and copy the selected lines on Ctrl+C. Both are properties of the
/// control rather than of the data, so they live here rather than in a view model - the same split the
/// old LogTextBoxBehavior used for a TextBox.
/// </summary>
public static class LogListBehaviors
{
    #region AutoScrollToEnd

    public static readonly DependencyProperty AutoScrollToEndProperty =
        DependencyProperty.RegisterAttached(
            "AutoScrollToEnd",
            typeof(bool),
            typeof(LogListBehaviors),
            new PropertyMetadata(false, OnAutoScrollToEndChanged));

    // Whether the view was at the bottom before the last content growth; stashed on the ScrollViewer.
    private static readonly DependencyProperty AtBottomProperty =
        DependencyProperty.RegisterAttached(
            "AtBottom",
            typeof(bool),
            typeof(LogListBehaviors),
            new PropertyMetadata(true));

    public static bool GetAutoScrollToEnd(DependencyObject element) =>
        (bool)element.GetValue(AutoScrollToEndProperty);

    public static void SetAutoScrollToEnd(DependencyObject element, bool value) =>
        element.SetValue(AutoScrollToEndProperty, value);

    private static void OnAutoScrollToEndChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl items)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            items.Loaded += OnLoaded;
            if (items.IsLoaded)
            {
                AttachAutoScroll(items);
            }
        }
        else
        {
            items.Loaded -= OnLoaded;
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => AttachAutoScroll((ItemsControl)sender);

    private static void AttachAutoScroll(ItemsControl items)
    {
        if (FindScrollViewer(items) is not ScrollViewer viewer)
        {
            return;
        }

        viewer.ScrollChanged -= OnScrollChanged;
        viewer.ScrollChanged += OnScrollChanged;
        viewer.SetValue(AtBottomProperty, true);
    }

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        ScrollViewer viewer = (ScrollViewer)sender;

        if (e.ExtentHeightChange == 0)
        {
            // A user scroll or a resize, not new content: remember whether we are parked at the bottom.
            bool atBottom = viewer.VerticalOffset >= viewer.ScrollableHeight - 0.5;
            viewer.SetValue(AtBottomProperty, atBottom);
        }
        else if ((bool)viewer.GetValue(AtBottomProperty))
        {
            // Content grew while parked at the bottom: follow it down. With virtualization the offsets
            // are in item units, but ScrollToVerticalOffset clamps to the bottom either way.
            viewer.ScrollToVerticalOffset(viewer.ExtentHeight);
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer found)
        {
            return found;
        }

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            if (FindScrollViewer(VisualTreeHelper.GetChild(root, i)) is ScrollViewer viewer)
            {
                return viewer;
            }
        }

        return null;
    }

    #endregion

    #region SelectionCopy

    public static readonly DependencyProperty SelectionCopyProperty =
        DependencyProperty.RegisterAttached(
            "SelectionCopy",
            typeof(bool),
            typeof(LogListBehaviors),
            new PropertyMetadata(false, OnSelectionCopyChanged));

    public static bool GetSelectionCopy(DependencyObject element) =>
        (bool)element.GetValue(SelectionCopyProperty);

    public static void SetSelectionCopy(DependencyObject element, bool value) =>
        element.SetValue(SelectionCopyProperty, value);

    private static void OnSelectionCopyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox list)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            list.KeyDown += OnListKeyDown;
        }
        else
        {
            list.KeyDown -= OnListKeyDown;
        }
    }

    private static void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.C || (Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        ListBox list = (ListBox)sender;
        if (list.SelectedItems.Count == 0)
        {
            return;
        }

        // SelectedItems is in selection order; sort by list position so copied text reads top-to-bottom.
        StringBuilder builder = new();
        foreach (object? item in list.Items)
        {
            if (list.SelectedItems.Contains(item))
            {
                builder.AppendLine(item as string);
            }
        }

        SetClipboardText(builder.ToString());
        e.Handled = true;
    }

    // Clipboard.SetText throws when another process holds the clipboard open (clipboard managers, RDP,
    // some IDEs). Retry via SetDataObject and swallow a final failure so copying can never crash the app.
    private static void SetClipboardText(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetDataObject(text, copy: true);
        }
        catch (COMException)
        {
        }
    }

    #endregion
}
