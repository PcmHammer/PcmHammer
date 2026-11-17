using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PCMHammerAvalonia.Helpers;

public class WriteTypeDisplayTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
            return string.Empty;

        string stringValue = value.ToString() ?? string.Empty;

        return stringValue switch
        {
            "Full" => "Clone (Full Flash)",
            "OsPlusCalibrationPlusBoot" => "OS, Calibration, Boot",
            "Parameters" => "Parameters",
            _ => stringValue
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}