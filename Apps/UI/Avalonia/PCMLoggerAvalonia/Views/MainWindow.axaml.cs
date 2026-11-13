using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using PcmHacking;
using PCMLoggerAvalonia.Controls;
using PCMLoggerAvalonia.ViewModels;

namespace PCMLoggerAvalonia.Views;

public partial class MainWindow : Window
{
    // Gauge drag-to-reposition state.
    private GaugeTile? _dragTile;
    private DashboardPanel? _dragPanel;
    private Point _dragStart;
    private double _tileStartLeft;
    private double _tileStartTop;
    private bool _dragging;

    // Auto-scroll state for the Debug tab (follow the tail as lines arrive).
    private ScrollViewer? _debugScroller;
    private INotifyCollectionChanged? _debugMessages;

    public MainWindow()
    {
        InitializeComponent();
    }

    // Keep the Debug log scrolled to the newest line, so it stays current without manual scrolling.
    private void DebugScroller_Loaded(object? sender, RoutedEventArgs e)
    {
        _debugScroller = sender as ScrollViewer;

        if (this.DataContext is MainWindowViewModel vm
            && vm.DebugMessages is INotifyCollectionChanged messages
            && !ReferenceEquals(_debugMessages, messages))
        {
            if (_debugMessages != null)
            {
                _debugMessages.CollectionChanged -= DebugMessages_CollectionChanged;
            }

            _debugMessages = messages;
            _debugMessages.CollectionChanged += DebugMessages_CollectionChanged;
        }

        this.ScrollDebugToEnd();
    }

    // Wire the strip chart's drag-zoom / double-click-fit (playback) to the view model.
    private MonitorChart? _monitorChart;
    private void MonitorChart_Loaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not MonitorChart chart || ReferenceEquals(_monitorChart, chart))
        {
            return;
        }

        _monitorChart = chart;
        chart.ZoomRequested += (rightEdge, window) =>
            (this.DataContext as MainWindowViewModel)?.ApplyPlaybackZoom(rightEdge, window);
        chart.ResetZoomRequested += () =>
            (this.DataContext as MainWindowViewModel)?.ResetPlaybackZoom();
    }

    private void DebugMessages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScrollViewer? scroller = _debugScroller;
        if (scroller == null)
        {
            return;
        }

        // Only follow the tail when the view is already at (or within a line of) the bottom - the
        // change fires before the new line lays out, so this reflects where the user was. A user who
        // has scrolled up to read history is left where they are.
        double maxOffset = Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height);
        if (scroller.Offset.Y < maxOffset - 24)
        {
            return;
        }

        this.ScrollDebugToEnd();
    }

    private void ScrollDebugToEnd()
    {
        ScrollViewer? scroller = _debugScroller;
        if (scroller == null)
        {
            return;
        }

        // After the new line is laid out (Background priority), pin the view to the bottom.
        Dispatcher.UIThread.Post(
            () => scroller.Offset = new Vector(scroller.Offset.X, double.MaxValue),
            DispatcherPriority.Background);
    }

    private async void AddGauge_Click(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is MainWindowViewModel vm)
        {
            GaugeTile tile = vm.AddGauge();
            await this.EditGaugeAsync(tile);
        }
    }

    // Clear is confirmed first, like the WinForms tab: the codes are the evidence, and erasing them
    // unseen is how a fault gets diagnosed twice. The dialog lives in the view (as the device/gauge
    // dialogs do); the clear itself is the shared backend, driven by the view model.
    private async void ClearCodes_Click(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        FAContentDialog dialog = new()
        {
            Title = "Clear Trouble Codes",
            Content = new TextBlock
            {
                Text = vm.ClearConfirmationText,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            },
            PrimaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = FAContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == FAContentDialogResult.Primary)
        {
            await vm.ClearTroubleCodesAsync();
        }
    }

    private async void SelectDevice_Click(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        IReadOnlyList<SerialPortInfo> ports = vm.GetSerialPorts();
        IReadOnlyList<string> j2534Devices = vm.GetJ2534Devices();
        DeviceSelectDialog dialog = new(
            ports,
            j2534Devices,
            MainWindowViewModel.IsJ2534Supported,
            vm.UseJ2534,
            vm.SelectedPortName,
            vm.SelectedDeviceType,
            vm.SelectedJ2534Device,
            vm.Enable4x);
        await dialog.ShowDialog(this);

        if (dialog.Accepted)
        {
            vm.UseJ2534 = dialog.UseJ2534;
            vm.SelectedPortName = dialog.PortName;
            vm.SelectedDeviceType = dialog.DeviceType;
            vm.SelectedJ2534Device = dialog.J2534DeviceType;
            vm.Enable4x = dialog.Enable4x;
            vm.UpdateDeviceSummary();
        }
    }

    private async Task EditGaugeAsync(GaugeTile tile)
    {
        MainWindowViewModel? vm = this.DataContext as MainWindowViewModel;
        List<ParameterOption> options = vm?.AvailableParameters.ToList() ?? new List<ParameterOption>();
        await new GaugeEditDialog(tile, options).ShowDialog(this);

        // The parameter may have changed; re-point the feed.
        vm?.ReindexGauges();
    }

    private async void GaugeCard_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control control && control.DataContext is GaugeTile tile)
        {
            await this.EditGaugeAsync(tile);
        }
    }

    // Right-click a gauge: Edit / Remove menu. Left-press starts a drag-to-reposition (double-click
    // still edits, since a drag only begins once the pointer actually moves).
    private void GaugeCard_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not GaugeTile tile)
        {
            return;
        }

        PointerPointProperties props = e.GetCurrentPoint(control).Properties;

        if (props.IsRightButtonPressed)
        {
            MenuFlyout flyout = new();

            MenuItem edit = new() { Header = "Edit gauge…" };
            edit.Click += async (_, _) => await this.EditGaugeAsync(tile);

            MenuItem remove = new() { Header = "Remove gauge" };
            remove.Click += (_, _) => (this.DataContext as MainWindowViewModel)?.RemoveGauge(tile);

            flyout.Items.Add(edit);
            flyout.Items.Add(remove);
            flyout.ShowAt(control, showAtPointer: true);
            e.Handled = true;
            return;
        }

        if (props.IsLeftButtonPressed)
        {
            _dragPanel = control.FindAncestorOfType<DashboardPanel>();
            if (_dragPanel != null)
            {
                _dragTile = tile;
                _dragStart = e.GetPosition(_dragPanel);
                _tileStartLeft = tile.Left;
                _tileStartTop = tile.Top;
                _dragging = false;
            }
        }
    }

    private void GaugeCard_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragTile == null || _dragPanel == null)
        {
            return;
        }

        if (!e.GetCurrentPoint(_dragPanel).Properties.IsLeftButtonPressed)
        {
            this.EndGaugeDrag();
            return;
        }

        Point pos = e.GetPosition(_dragPanel);
        double dx = pos.X - _dragStart.X;
        double dy = pos.Y - _dragStart.Y;

        if (!_dragging && (Math.Abs(dx) + Math.Abs(dy)) < 4)
        {
            return;
        }

        double w = _dragPanel.Bounds.Width;
        double h = _dragPanel.Bounds.Height;
        if (w < 1 || h < 1)
        {
            return;
        }

        _dragging = true;
        _dragTile.Left = Math.Max(0, Math.Min(1 - _dragTile.RelWidth, _tileStartLeft + (dx / w)));
        _dragTile.Top = Math.Max(0, Math.Min(1 - _dragTile.RelHeight, _tileStartTop + (dy / h)));
        _dragPanel.InvalidateArrange();
    }

    private void GaugeCard_PointerReleased(object? sender, PointerReleasedEventArgs e) => this.EndGaugeDrag();

    private void EndGaugeDrag()
    {
        _dragTile = null;
        _dragPanel = null;
        _dragging = false;
    }
}
