using CommunityToolkit.Mvvm.ComponentModel;

namespace PCMLoggerAvalonia.ViewModels;

/// <summary>
/// One parameter's live reading as shown in the grid. The identity (id/name/units) is fixed for the
/// session; only <see cref="Value"/> updates each row, so the grid rebinds in place rather than
/// being rebuilt every frame.
/// </summary>
public partial class ReadingRow : ObservableObject
{
    public string ParameterId { get; }
    public string Name { get; }
    public string Units { get; }

    [ObservableProperty]
    private string _value = string.Empty;

    public ReadingRow(string parameterId, string name, string units)
    {
        this.ParameterId = parameterId;
        this.Name = name;
        this.Units = units;
    }
}
