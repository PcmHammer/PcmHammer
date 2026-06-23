using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PCMHammer.Helpers
{
    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string currentStatus = value as string ?? string.Empty;
            string rawParameter = parameter as string ?? string.Empty;

            // Check if we want an inverted ("NOT") match
            bool invert = false;
            string targetStatus = rawParameter;

            if (rawParameter.StartsWith("Not ", StringComparison.OrdinalIgnoreCase))
            {
                invert = true;
                targetStatus = rawParameter.Substring(4).Trim(); // Extract everything after "Not "
            }

            bool isMatch = currentStatus.Equals(targetStatus, StringComparison.OrdinalIgnoreCase);

            // If invert is true, we want Visible when it's NOT a match.
            // If invert is false, we want Visible when it IS a match.
            return invert ? !isMatch ? Visibility.Visible : Visibility.Collapsed : isMatch ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
