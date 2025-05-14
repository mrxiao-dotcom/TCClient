using System;
using System.Globalization;
using System.Windows.Data;

namespace TCClient.Utils
{
    public class PercentToDecimalConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // decimal -> string
            if (value is decimal dec)
                return dec.ToString();
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // string -> decimal
            var str = value as string;
            if (string.IsNullOrWhiteSpace(str)) return 0m;
            str = str.Replace("%", "").Trim();
            if (decimal.TryParse(str, out var result))
                return result;
            return 0m;
        }
    }
} 