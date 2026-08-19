using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Data;

namespace PCMHammer.Helpers
{
    public class GreaterThanZeroToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int intValue)
                return intValue > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (value is double doubleValue)
                return doubleValue > 0.0 ? Visibility.Visible : Visibility.Collapsed;
            if (value is string stringValue)
                return !IsAnyTimeRemaining(stringValue) ? Visibility.Visible : Visibility.Collapsed;
            return Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // if visible, return 1, else return 0
            if (value is Visibility visibility)
                return visibility == Visibility.Visible ? 1 : 0;
            return 0;
        }
        private bool IsAnyTimeRemaining(string timeRemainingString)
        {
            if (string.IsNullOrEmpty(timeRemainingString))
                return false;
            return timeRemainingString[..2].Equals("00") && timeRemainingString[3..5].Equals("00");
        }
    }
}
