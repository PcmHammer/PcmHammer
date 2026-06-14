// SPDX-License-Identifier: GPL-3.0-only
namespace PcmHacking.UnoUI.Presentation;

public sealed partial class ControllerFunctionsPage : Page
{
    private ControllerFunctionsModel? model;

    public ControllerFunctionsPage()
    {
        this.InitializeComponent();

        this.DataContextChanged += (sender, e) =>
        {
            this.model = (this.DataContext as ControllerFunctionsViewModel)?.Model as ControllerFunctionsModel ?? this.model;
        };
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        this.model?.NavigatedAway();
    }
}

