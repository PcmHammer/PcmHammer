using System.Globalization;
using System.Windows.Data;

namespace PCMHammer.Helpers
{
    public class HexStringToIntegerConverter : IValueConverter
    {
        // Converts int (ViewModel) to Hex String (View)
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int intValue) 
                return intValue.ToString("X4"); // Formats as 4-digit upper-case Hex
            return "0000";
        }

        // Converts Hex String (View) back to int (ViewModel)
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string text = (string)value;
            if (string.IsNullOrWhiteSpace(text)) return 0;
            if (int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int result))
                return result & 0xFFFF; // Keep it 16-bit
            return System.Windows.DependencyProperty.UnsetValue; // Tells WPF the input is invalid
        }
    }
}