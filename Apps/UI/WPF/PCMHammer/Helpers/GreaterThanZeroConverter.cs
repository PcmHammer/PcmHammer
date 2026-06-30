using System.Globalization;
using System.Windows.Data;

namespace PCMHammer.Helpers
{
    [ValueConversion(typeof(double), typeof(bool))]
    public class GreaterThanZeroConverter : IValueConverter
    {
        // Converts a numeric value to a boolean (True if > 0)
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return false;

            // Handle different numeric types gracefully
            if (value is double d) return d > 0;
            if (value is int i) return i > 0;
            if (value is float f) return f > 0;
            if (value is long l) return l > 0;

            return false;
        }

        // ConvertBack is not used for this type of UI-only trigger evaluation
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
