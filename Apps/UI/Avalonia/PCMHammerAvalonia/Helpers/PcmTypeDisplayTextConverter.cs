using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PCMHammerAvalonia.Helpers;

public class PcmTypeDisplayTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
            return string.Empty;

        string stringValue = value.ToString() ?? string.Empty;

        return stringValue.Equals("Undefined", StringComparison.OrdinalIgnoreCase)
            ? "Auto (Query OSID)"
            : stringValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}