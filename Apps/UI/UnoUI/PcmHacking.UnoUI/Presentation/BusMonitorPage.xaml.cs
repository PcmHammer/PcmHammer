// SPDX-License-Identifier: GPL-3.0-only
namespace PcmHacking.UnoUI.Presentation;

public sealed partial class BusMonitorPage : Page
{
    private BusMonitorModel? model;

    public BusMonitorPage()
    {
        this.InitializeComponent();

        this.DataContextChanged += (sender, e) =>
        {
            this.model = (this.DataContext as BusMonitorViewModel)?.Model as BusMonitorModel ?? this.model;
        };
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        // The monitor holds the connection, so leaving the page must stop it.
        this.model?.NavigatedAway();
    }
}
