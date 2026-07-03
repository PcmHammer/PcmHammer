using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PCMHammer.Helpers
{
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolValue = value is bool b && b;

            // If a parameter is passed (e.g., "Inverse"), flip the boolean
            if (parameter != null)
                boolValue = !boolValue;

            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isVisible = value is Visibility v && v == Visibility.Visible;
            return parameter != null ? !isVisible : isVisible;
        }
    }
}
