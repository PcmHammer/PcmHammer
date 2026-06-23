using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Data;

namespace PCMHammer.Helpers
{
    [ValueConversion(typeof(object), typeof(bool))]
    public class NullToBooleanConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            // Check for null. DependencyProperty.UnsetValue handles WPF's uninitialized state.
            bool isNotNull = value is not null && value != DependencyProperty.UnsetValue;

            return isNotNull;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            // Binding back from Boolean to a Nullable object is generally not supported 
            // or logical in a generic sense.
            return DependencyProperty.UnsetValue;
        }
    }
}
