// SPDX-License-Identifier: GPL-3.0-only
namespace PCMLoggerAvalonia.ViewModels;

/// <summary>One choice in the gauge parameter picker: a parameter the PCM supports.</summary>
public sealed record ParameterOption(string Id, string Name, string Units)
{
    public override string ToString() => this.Name;
}
