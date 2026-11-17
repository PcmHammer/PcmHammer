using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace PCMHammerAvalonia.Helpers;

public class HexStringToIntegerConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue)
            return intValue.ToString("X4");
        return "0000";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string text && !string.IsNullOrWhiteSpace(text))
        {
            if (int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int result))
                return result & 0xFFFF;
        }

        return AvaloniaProperty.UnsetValue;
    }
}