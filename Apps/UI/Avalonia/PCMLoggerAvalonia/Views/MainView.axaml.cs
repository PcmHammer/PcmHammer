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

public partial class MainView : UserControl
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

    // Below this view width (logical px) the fixed-width PID side-pane is hidden so the tab area gets
    // the whole screen. A phone is ~384 logical px wide; a desktop window is well above this.
    private const double NarrowWidthThreshold = 640;

    private MainWindowViewModel? _responsiveVm;

    public MainView()
    {
        InitializeComponent();

        // Re-evaluate the responsive layout when the view is resized or the view model changes.
        this.SizeChanged += (_, _) => this.UpdateResponsiveLayout();
        this.DataContextChanged += this.OnDataContextChangedForLayout;
    }

    private void OnDataContextChangedForLayout(object? sender, EventArgs e)
    {
        if (_responsiveVm != null)
        {
            _responsiveVm.PropertyChanged -= this.OnResponsiveVmPropertyChanged;
        }

        _responsiveVm = this.DataContext as MainWindowViewModel;
        if (_responsiveVm != null)
        {
            _responsiveVm.PropertyChanged += this.OnResponsiveVmPropertyChanged;
        }

        this.UpdateResponsiveLayout();
    }

    private void OnResponsiveVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.ShowPids))
        {
            this.UpdateResponsiveLayout();
        }
    }

    // The side pane is shown only when there is room for it AND the user wants PIDs. On a narrow phone
    // it is hidden regardless, so the tabs are not squeezed into a sliver.
    private void UpdateResponsiveLayout()
    {
        if (this.PidPane == null)
        {
            return;
        }

        bool wideEnough = this.Bounds.Width >= NarrowWidthThreshold;
        bool showPids = _responsiveVm?.ShowPids ?? false;
        this.PidPane.IsVisible = wideEnough && showPids;
    }

    // The hosting Window on desktop, or null when this view is a mobile single-view root (no Window to
    // own a modal dialog). Dialog callers guard on null so the desktop path works and mobile does not
    // throw; mobile gets its own non-modal flows later.
    private Window? OwnerWindow => TopLevel.GetTopLevel(this) as Window;

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

        // TopLevel passed explicitly so this works on a mobile single-view (no owning Window) too.
        if (await dialog.ShowAsync(TopLevel.GetTopLevel(this)) == FAContentDialogResult.Primary)
        {
            await vm.ClearTroubleCodesAsync();
        }
    }

#if ANDROID
    // Mobile device picker: list paired Bluetooth adapters (and any USB serial ports) in a content
    // dialog; the chosen one is auto-detected on Connect. FAContentDialog shows in the single-view
    // top-level overlay, so no owning Window is needed.
    private async void SelectDevice_Click(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        IReadOnlyList<MainWindowViewModel.AndroidDeviceChoice> devices = vm.GetAndroidDevices();

        ListBox list = new()
        {
            ItemsSource = devices.Select(d => d.Display).ToList(),
            MaxHeight = 320,
        };

        FAContentDialog dialog = new()
        {
            Title = "Select OBD2 Device",
            Content = devices.Count > 0
                ? list
                : new TextBlock
                {
                    Text = "No paired Bluetooth adapters or serial ports were found. "
                        + "Pair your OBD adapter in Android Settings first, then try again.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
            PrimaryButtonText = "Connect",
            CloseButtonText = "Cancel",
            DefaultButton = FAContentDialogButton.Close,
        };

        // Pass the TopLevel explicitly: on a mobile single-view there is no owning Window, so the
        // parameterless ShowAsync() cannot find an overlay layer and throws (visual is null).
        if (await dialog.ShowAsync(TopLevel.GetTopLevel(this)) == FAContentDialogResult.Primary
            && list.SelectedIndex >= 0
            && list.SelectedIndex < devices.Count)
        {
            vm.SelectAndroidDevice(devices[list.SelectedIndex]);
        }
    }
#else
    private async void SelectDevice_Click(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        Window? owner = this.OwnerWindow;
        if (owner == null)
        {
            // Mobile single-view: the modal device picker is a desktop flow; a mobile picker comes later.
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
        await dialog.ShowDialog(owner);

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
#endif

    private async Task EditGaugeAsync(GaugeTile tile)
    {
        MainWindowViewModel? vm = this.DataContext as MainWindowViewModel;
        List<ParameterOption> options = vm?.AvailableParameters.ToList() ?? new List<ParameterOption>();

        Window? owner = this.OwnerWindow;
        if (owner != null)
        {
            await new GaugeEditDialog(tile, options).ShowDialog(owner);
        }

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
