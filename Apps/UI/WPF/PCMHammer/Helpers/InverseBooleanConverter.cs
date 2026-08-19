using System.Globalization;
using System.Windows.Data;

namespace PCMHammer.Helpers
{
    [ValueConversion(typeof(bool), typeof(bool))]
    public class InverseBooleanConverter : IValueConverter
    {
        // Converts ViewModel bool to View bool (Inverted)
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool booleanValue) return !booleanValue;
            return false; // Default fallback
        }

        // Converts View bool back to ViewModel bool (Inverted back)
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool booleanValue) return !booleanValue;
            return false;
        }
    }
}
