using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PCMHammerAvalonia.Helpers;

public class GreaterThanZeroConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intVal) return intVal > 0;
        if (value is double dblVal) return dblVal > 0;
        if (value is long lngVal) return lngVal > 0;

        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}