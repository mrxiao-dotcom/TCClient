using System;
using System.Globalization;
using System.Windows.Data;

namespace TCClient.Converters
{
    public class PriceFormatConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double doubleValue)
            {
                return FormatPrice(doubleValue);
            }
            else if (value is decimal decimalValue)
            {
                return FormatPrice((double)decimalValue);
            }
            else if (value is float floatValue)
            {
                return FormatPrice(floatValue);
            }

            return value?.ToString() ?? "0";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// 根据价格大小自动格式化小数位数
        /// </summary>
        /// <param name="price">价格</param>
        /// <returns>格式化后的价格字符串</returns>
        private string FormatPrice(double price)
        {
            if (price == 0)
                return "0";

            // 取绝对值进行判断
            double absPrice = Math.Abs(price);

            // 如果价格小于0.0001，显示8位小数
            if (absPrice < 0.0001)
            {
                return price.ToString("F8", CultureInfo.InvariantCulture);
            }
            // 如果价格小于0.01，显示6位小数
            else if (absPrice < 0.01)
            {
                return price.ToString("F6", CultureInfo.InvariantCulture);
            }
            // 如果价格小于1，显示4位小数
            else if (absPrice < 1)
            {
                return price.ToString("F4", CultureInfo.InvariantCulture);
            }
            // 如果价格小于100，显示2位小数
            else if (absPrice < 100)
            {
                return price.ToString("F2", CultureInfo.InvariantCulture);
            }
            // 如果价格大于等于100，显示2位小数
            else
            {
                return price.ToString("F2", CultureInfo.InvariantCulture);
            }
        }
    }
} 