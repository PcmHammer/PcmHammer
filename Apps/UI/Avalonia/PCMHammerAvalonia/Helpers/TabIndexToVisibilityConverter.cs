using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace PCMHammerAvalonia.Helpers;

public class TabIndexToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Converts a selected index to a boolean value for Avalonia's IsVisible property.
    /// </summary>
    /// <param name="value">The current selected index (int).</param>
    /// <param name="targetType">The target property type (bool).</param>
    /// <param name="parameter">The target index to compare against (string or int from XAML).</param>
    /// <param name="culture">The culture to use in the converter.</param>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int currentIndex && parameter != null)
        {
            if (int.TryParse(parameter.ToString(), out int targetIndex))
            {
                return currentIndex == targetIndex;
            }
        }

        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}