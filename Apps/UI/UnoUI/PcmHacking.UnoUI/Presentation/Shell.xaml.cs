// SPDX-License-Identifier: GPL-3.0-only
namespace PcmHacking.UnoUI.Presentation;

public sealed partial class Shell : UserControl, IContentControlProvider
{
    public Shell()
    {
        this.InitializeComponent();
    }
    public ContentControl ContentControl => Splash;
}
