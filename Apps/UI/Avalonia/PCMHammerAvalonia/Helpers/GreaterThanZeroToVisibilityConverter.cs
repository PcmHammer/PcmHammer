using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PCMHammerAvalonia.Helpers;

public class GreaterThanZeroToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue)
            return intValue > 0;

        if (value is double doubleValue)
            return doubleValue > 0.0;

        if (value is string stringValue)
            return !IsAnyTimeRemaining(stringValue);

        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return boolValue ? 1 : 0;

        return 0;
    }

    private static bool IsAnyTimeRemaining(string timeRemainingString)
    {
        if (string.IsNullOrWhiteSpace(timeRemainingString) || timeRemainingString.Length < 5)
            return false;

        // Checks if format starts with "00:00"
        return timeRemainingString.StartsWith("00:00", StringComparison.Ordinal);
    }
}