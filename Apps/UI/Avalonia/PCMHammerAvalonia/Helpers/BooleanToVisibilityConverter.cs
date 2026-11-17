using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PCMHammerAvalonia.Helpers;

public class BooleanToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool boolValue = value is bool b && b;

        if (parameter != null)
            boolValue = !boolValue;

        return boolValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool boolValue = value is bool b && b;

        if (parameter != null)
            boolValue = !boolValue;

        return boolValue;
    }
}