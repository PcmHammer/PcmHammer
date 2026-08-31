// SPDX-License-Identifier: GPL-3.0-only
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PCMHammer.Viewmodels
{
    /// <summary>
    /// Host side of the Bus Monitor tab. The tab and its Save entry stay hidden until Tools > Bus
    /// Monitor is used, as WinForms only adds its tab page on demand.
    /// </summary>
    public partial class MainWindowViewModel
    {
        /// <summary>Last, so the Results (0) and Debug (3) indexes the copy buttons use stay put.</summary>
        private const int BusMonitorTabIndex = 4;

        /// <summary>Kept for the window's life so captured traffic survives switching tabs.</summary>
        public BusMonitorViewModel BusMonitor { get; private set; } = null!;

        [ObservableProperty]
        public partial bool IsBusMonitorVisible { get; set; }

        [ObservableProperty]
        public partial int SelectedTabIndex { get; set; }

        // Always available, as in WinForms: revealing a tab touches no hardware.
        [RelayCommand]
        public void ShowBusMonitor()
        {
            IsBusMonitorVisible = true;
            BusMonitor.RefreshCapability();
            SelectedTabIndex = BusMonitorTabIndex;
        }

        [RelayCommand]
        public async Task SaveBusMonitorLog() => await SaveLogFileAsync("BusMonitor", BusMonitor.Lines.Snapshot());
    }
}
