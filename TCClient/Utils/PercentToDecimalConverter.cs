using System;
using System.Globalization;
using System.Windows.Data;

namespace TCClient.Utils
{
    public class PercentToDecimalConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is decimal decimalValue)
            {
                return decimalValue * 100;
            }
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string stringValue && decimal.TryParse(stringValue, out decimal decimalValue))
            {
                return decimalValue / 100;
            }
            return 0m;
        }
    }
} 