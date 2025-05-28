using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using TCClient.ViewModels;

namespace TCClient.Views
{
    /// <summary>
    /// 账户查询窗口
    /// </summary>
    public partial class AccountQueryWindow : Window
    {
        private readonly AccountQueryViewModel _viewModel;

        public AccountQueryWindow(AccountQueryViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;
            
            // 窗口关闭时释放资源
            Closed += (s, e) => _viewModel.Dispose();
            
            // 窗口加载时刷新数据
            Loaded += Window_Loaded;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // 窗口加载完成后立即刷新数据
                await _viewModel.RefreshDataAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载账户数据失败：{ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// 盈亏颜色转换器
    /// </summary>
    public class PnLColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is decimal pnl)
            {
                if (pnl > 0)
                    return new SolidColorBrush(Colors.Green);
                else if (pnl < 0)
                    return new SolidColorBrush(Colors.Red);
                else
                    return new SolidColorBrush(Colors.Gray);
            }
            return new SolidColorBrush(Colors.Black);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 