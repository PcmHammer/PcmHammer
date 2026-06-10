// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PcmHacking.UnoUI.Presentation;

public partial record DataLoggingDeleteConfirmationModel
{
    private readonly INavigator navigator;

    public IState<string> ParameterName => State<string>.Value(this, () => string.Empty);

    public DataLoggingDeleteConfirmationModel(
        INavigator navigator,
        ParameterEditContext editContext)
    {
        this.navigator = navigator;
        this.ParameterName.SetAsync(editContext.Input?.Parameter?.Name ?? string.Empty);
    }
}

