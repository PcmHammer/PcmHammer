// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using PcmHacking;

namespace PCMLoggerAvalonia.ViewModels;

/// <summary>
/// One row in the Parameters tab: a PID the connected PCM supports, with whether it is logged
/// (<see cref="Enabled"/>), whether it is zoomed, and which units (conversion) to log it in. Mirrors
/// the WinForms PcmLogger Parameters tab (Enable / Zoom / Name / Units).
/// </summary>
/// <remarks>
/// The underlying <see cref="Parameter"/> and the chosen <see cref="Conversion"/> come straight from
/// the shared <c>ParameterDatabase</c>; <see cref="ToColumn"/> turns a selected row into the shared
/// <see cref="LogColumn"/> the logging engine consumes, so the heavy lifting stays in PcmLibrary.
/// </remarks>
public sealed class ParameterRow : ObservableObject
{
    private readonly List<Conversion> _conversions;

    public ParameterRow(Parameter parameter)
    {
        this.Parameter = parameter;
        _conversions = parameter.Conversions?.ToList() ?? new List<Conversion>();
        this.UnitOptions = _conversions.Select(c => c.Units).ToList();
        _selectedUnit = this.UnitOptions.FirstOrDefault() ?? string.Empty;
    }

    public Parameter Parameter { get; }

    public string Id => this.Parameter.Id;

    public string Name => this.Parameter.Name;

    /// <summary>The units offered by this parameter's conversions, for the Units dropdown.</summary>
    public IReadOnlyList<string> UnitOptions { get; }

    private bool _enabled;
    /// <summary>True when this parameter is logged (the Enable checkbox).</summary>
    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }

    private bool _zoom;
    /// <summary>True when this parameter is shown in the larger "zoomed" display.</summary>
    public bool Zoom { get => _zoom; set => SetProperty(ref _zoom, value); }

    private string _selectedUnit;
    public string SelectedUnit { get => _selectedUnit; set => SetProperty(ref _selectedUnit, value); }

    private bool _matchesFilter = true;
    /// <summary>False while the Parameters search box hides this row.</summary>
    public bool MatchesFilter { get => _matchesFilter; set => SetProperty(ref _matchesFilter, value); }

    /// <summary>The conversion for the chosen units (falls back to the first the parameter offers).</summary>
    public Conversion? SelectedConversion =>
        _conversions.FirstOrDefault(c => c.Units == _selectedUnit) ?? _conversions.FirstOrDefault();

    /// <summary>Turn an enabled row into the shared LogColumn the logging engine consumes.</summary>
    public LogColumn? ToColumn()
    {
        Conversion? conversion = this.SelectedConversion;
        return conversion == null ? null : new LogColumn(this.Parameter, conversion, this.Zoom);
    }
}
