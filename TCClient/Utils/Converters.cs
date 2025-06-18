using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace TCClient.Utils
{
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public static readonly BooleanToVisibilityConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                bool invertResult = parameter is string strParam && strParam.ToLower() == "invert";
                
                if (invertResult)
                {
                    return boolValue ? Visibility.Collapsed : Visibility.Visible;
                }
                else
                {
                    return boolValue ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                bool invertResult = parameter is string strParam && strParam.ToLower() == "invert";
                
                if (invertResult)
                {
                    return visibility != Visibility.Visible;
                }
                else
                {
                    return visibility == Visibility.Visible;
                }
            }
            return false;
        }
    }

    public class InverseBooleanConverter : IValueConverter
    {
        public static readonly InverseBooleanConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool boolValue ? !boolValue : true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool boolValue ? !boolValue : false;
        }
    }

    public class PnLColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is decimal decimalValue)
            {
                return decimalValue > 0 ? Brushes.Green : decimalValue < 0 ? Brushes.Red : Brushes.White;
            }
            return Brushes.White;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class EnumToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return Visibility.Collapsed;

            string enumValue = value.ToString();
            string targetValue = parameter.ToString();

            return enumValue.Equals(targetValue, StringComparison.OrdinalIgnoreCase) 
                ? Visibility.Visible 
                : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 价格格式转换器：根据价格大小动态调整显示精度
    /// 大于等于0.0001的显示4位小数，小于0.0001的显示8位小数
    /// </summary>
    public class PriceFormatConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return "--";

            decimal price = 0m;
            
            // 处理不同类型的输入值
            if (value is decimal decimalValue)
            {
                price = decimalValue;
            }
            else if (value is double doubleValue)
            {
                price = (decimal)doubleValue;
            }
            else if (value is float floatValue)
            {
                price = (decimal)floatValue;
            }
            else if (decimal.TryParse(value.ToString(), out decimal parsedValue))
            {
                price = parsedValue;
            }
            else
            {
                return value.ToString();
            }

            // 如果价格为0，显示为"--"
            if (price == 0)
                return "--";

            // 根据价格大小决定显示精度
            if (Math.Abs(price) >= 0.0001m)
            {
                // 大于等于0.0001的显示4位小数
                return price.ToString("F4", culture);
            }
            else
            {
                // 小于0.0001的显示8位小数
                return price.ToString("F8", culture);
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string stringValue && decimal.TryParse(stringValue, out decimal result))
            {
                return result;
            }
            return 0m;
        }
    }

    public class NotNullToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class StgToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int stg)
            {
                return stg switch
                {
                    1 or 2 => new SolidColorBrush(Colors.Red),      // 多头显示红色
                    -1 or -2 => new SolidColorBrush(Colors.Green),  // 空头显示绿色
                    0 => new SolidColorBrush(Colors.Gray),          // 空仓显示灰色
                    _ => new SolidColorBrush(Colors.Black)          // 其他显示黑色
                };
            }
            return new SolidColorBrush(Colors.Black);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class IndexConverter : IValueConverter
    {
        public static readonly IndexConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int index)
            {
                var offset = parameter != null ? System.Convert.ToInt32(parameter) : 0;
                return (index + offset).ToString();
            }
            return "1";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 