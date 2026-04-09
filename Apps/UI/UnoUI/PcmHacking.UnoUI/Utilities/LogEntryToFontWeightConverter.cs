using System;
using System.Globalization;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml;
using PcmHacking.UnoUI.Presentation;
using Microsoft.UI.Text;
using PcmHacking.UnoUI.Services;

namespace PcmHacking.UnoUI.Utilities
{
    public class LogEntryToFontWeightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is LogEntry entry && entry.Type == LogType.User)
            {
                return FontWeights.Bold;
            }
            return FontWeights.Normal;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}