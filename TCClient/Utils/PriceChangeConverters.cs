using System;
using System.Globalization;
using System.Windows.Data;

namespace TCClient.Views.Controls
{
    public class GreaterThanZeroConverter : IValueConverter
    {
        public static readonly GreaterThanZeroConverter Instance = new GreaterThanZeroConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is decimal decimalValue)
            {
                return decimalValue > 0;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class LessThanZeroConverter : IValueConverter
    {
        public static readonly LessThanZeroConverter Instance = new LessThanZeroConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is decimal decimalValue)
            {
                return decimalValue < 0;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 