using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PCMHammer.Helpers
{
    public class TabIndexToVisibilityConverter : IValueConverter
    {
        /// <summary>
        /// Converts a selected index to a Visibility value.
        /// </summary>
        /// <param name="value">The current selected index (int).</param>
        /// <param name="targetType">The type of the binding target property (Visibility).</param>
        /// <param name="parameter">The target index to compare against (string or int from XAML).</param>
        /// <param name="culture">The culture to use in the converter.</param>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int currentIndex && parameter != null)
            {
                // Parse the ConverterParameter passed from XAML
                if (int.TryParse(parameter.ToString(), out int targetIndex))
                {
                    return currentIndex == targetIndex ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            return Visibility.Collapsed;
        }

        /// <summary>
        /// ConvertBack is not supported because Visibility cannot be mapped back to a specific index uniquely.
        /// </summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return DependencyProperty.UnsetValue;
        }
    }
}