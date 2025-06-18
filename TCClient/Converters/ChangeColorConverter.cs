using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TCClient.Converters
{
    public class ChangeColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double change)
            {
                if (change > 0)
                    return Brushes.Red; // 上涨显示红色
                else if (change < 0)
                    return Brushes.Green; // 下跌显示绿色
                else
                    return Brushes.Gray; // 平盘显示灰色
            }
            
            return Brushes.Black; // 默认黑色
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 