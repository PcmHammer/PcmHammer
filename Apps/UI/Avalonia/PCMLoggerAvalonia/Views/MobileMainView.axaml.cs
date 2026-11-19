using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using PCMLoggerAvalonia.Controls;
using PCMLoggerAvalonia.ViewModels;

namespace PCMLoggerAvalonia.Views;

public partial class MobileMainView : UserControl
{
    private ScrollViewer? _debugScroller;
    private INotifyCollectionChanged? _debugMessages;
    private MonitorChart? _monitorChart;
    private MainWindowViewModel? _monitorVm;

    public MobileMainView()
    {
        InitializeComponent();
        this.DataContextChanged += this.OnDataContextChangedForMonitor;
    }

    private void OnDataContextChangedForMonitor(object? sender, EventArgs e)
    {
        if (_monitorVm != null)
        {
            _monitorVm.PropertyChanged -= this.OnMonitorVmPropertyChanged;
        }

        _monitorVm = this.DataContext as MainWindowViewModel;

        if (_monitorVm != null)
        {
            _monitorVm.PropertyChanged += this.OnMonitorVmPropertyChanged;
            this.SyncMonitorPickerFromVm();
        }

        ComboBox? picker = this.FindControl<ComboBox>("MonitorPicker");
        if (picker != null)
        {
            picker.SelectionChanged -= this.MonitorPicker_SelectionChanged;
            picker.SelectionChanged += this.MonitorPicker_SelectionChanged;
        }
    }

    private void OnMonitorVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedMonitor))
        {
            this.SyncMonitorPickerFromVm();
        }
    }

    private void SyncMonitorPickerFromVm()
    {
        ComboBox? picker = this.FindControl<ComboBox>("MonitorPicker");
        if (picker == null || _monitorVm == null) return;

        MonitorTab? target = _monitorVm.SelectedMonitor;
        if (!ReferenceEquals(picker.SelectedItem, target))
        {
            picker.SelectedItem = target;
        }
    }

    private void MonitorPicker_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_monitorVm == null) return;

        MonitorTab? picked = e.AddedItems.OfType<MonitorTab>().FirstOrDefault();
        if (!ReferenceEquals(_monitorVm.SelectedMonitor, picked))
        {
            _monitorVm.SelectedMonitor = picked;
        }
    }

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

    private void DebugMessages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScrollViewer? scroller = _debugScroller;
        if (scroller == null) return;

        double maxOffset = System.Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height);
        if (scroller.Offset.Y < maxOffset - 24) return;

        this.ScrollDebugToEnd();
    }

    private void ScrollDebugToEnd()
    {
        ScrollViewer? scroller = _debugScroller;
        if (scroller == null) return;

        Dispatcher.UIThread.Post(
            () => scroller.Offset = new Vector(scroller.Offset.X, double.MaxValue),
            DispatcherPriority.Background);
    }

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

    private async void AddGauge_Click(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is MainWindowViewModel vm)
        {
            vm.AddGauge();
        }
    }

    private async void GaugeCard_DoubleTapped(object? sender, TappedEventArgs e)
    {
        // Gauge editing requires a Window owner — no-op on mobile for now.
    }

    private async void ClearCodes_Click(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is not MainWindowViewModel vm) return;

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

        if (await dialog.ShowAsync(TopLevel.GetTopLevel(this)) == FAContentDialogResult.Primary)
        {
            await vm.ClearTroubleCodesAsync();
        }
    }

#if ANDROID
    private async void SelectDevice_Click(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is not MainWindowViewModel vm) return;

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

        if (await dialog.ShowAsync(TopLevel.GetTopLevel(this)) == FAContentDialogResult.Primary
            && list.SelectedIndex >= 0
            && list.SelectedIndex < devices.Count)
        {
            vm.SelectAndroidDevice(devices[list.SelectedIndex]);
        }
    }
#else
    private void SelectDevice_Click(object? sender, RoutedEventArgs e) { }
#endif
}
