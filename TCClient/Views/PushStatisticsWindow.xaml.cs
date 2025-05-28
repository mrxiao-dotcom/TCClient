using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TCClient.ViewModels;
using TCClient.Models;
using Microsoft.Extensions.DependencyInjection;

namespace TCClient.Views
{
    /// <summary>
    /// 推仓统计窗口
    /// </summary>
    public partial class PushStatisticsWindow : Window
    {
        public PushStatisticsWindow(PushStatisticsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            
            // 添加调试事件处理器
            this.Loaded += PushStatisticsWindow_Loaded;
        }

        private void PushStatisticsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 查找DataGrid并添加SelectionChanged事件处理器
            var openPushDataGrid = FindName("OpenPushDataGrid") as DataGrid;
            if (openPushDataGrid != null)
            {
                openPushDataGrid.SelectionChanged += OpenPushDataGrid_SelectionChanged;
            }

            var closedPushDataGrid = FindName("ClosedPushDataGrid") as DataGrid;
            if (closedPushDataGrid != null)
            {
                closedPushDataGrid.SelectionChanged += ClosedPushDataGrid_SelectionChanged;
            }
        }

        private void OpenPushDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var dataGrid = sender as DataGrid;
            var selectedItem = dataGrid?.SelectedItem;
            
            if (DataContext is PushStatisticsViewModel viewModel)
            {
                System.Diagnostics.Debug.WriteLine($"OpenPushDataGrid选择改变: {selectedItem?.GetType().Name}");
                if (selectedItem != null)
                {
                    var pushInfo = selectedItem as TCClient.Models.PushSummaryInfo;
                    System.Diagnostics.Debug.WriteLine($"选中推仓ID: {pushInfo?.PushId}, 合约: {pushInfo?.Contract}");
                }
            }
        }

        private void ClosedPushDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var dataGrid = sender as DataGrid;
            var selectedItem = dataGrid?.SelectedItem;
            
            if (DataContext is PushStatisticsViewModel viewModel)
            {
                System.Diagnostics.Debug.WriteLine($"ClosedPushDataGrid选择改变: {selectedItem?.GetType().Name}");
                if (selectedItem != null)
                {
                    var pushInfo = selectedItem as TCClient.Models.PushSummaryInfo;
                    System.Diagnostics.Debug.WriteLine($"选中推仓ID: {pushInfo?.PushId}, 合约: {pushInfo?.Contract}");
                }
            }
        }

        /// <summary>
        /// 查看止损单事件处理器
        /// </summary>
        private void ViewStopLossOrders_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 检查窗口是否正在关闭或已关闭
                if (!IsLoaded || !IsVisible)
                {
                    return;
                }
                // 获取当前选中的订单
                var menuItem = sender as MenuItem;
                var contextMenu = menuItem?.Parent as ContextMenu;
                var dataGrid = contextMenu?.PlacementTarget as DataGrid;
                var selectedOrder = dataGrid?.SelectedItem as SimulationOrder;

                if (selectedOrder == null)
                {
                    MessageBox.Show("请先选择一个订单", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 对于已完结的订单，也可以查看历史止损单记录
                // 不再限制只有持仓中的订单才能查看止损单

                // 获取数据库服务
                var viewModel = DataContext as PushStatisticsViewModel;
                if (viewModel == null)
                {
                    MessageBox.Show("无法获取视图模型", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 通过App获取服务
                var app = System.Windows.Application.Current as App;
                var databaseService = app?.Services?.GetService(typeof(TCClient.Services.IDatabaseService)) as TCClient.Services.IDatabaseService;
                var logger = app?.Services?.GetService(typeof(Microsoft.Extensions.Logging.ILogger<StopLossOrdersWindow>)) as Microsoft.Extensions.Logging.ILogger<StopLossOrdersWindow>;

                if (databaseService == null)
                {
                    MessageBox.Show("无法获取数据库服务", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 再次检查窗口状态，确保在显示对话框前窗口仍然有效
                if (!IsLoaded || !IsVisible)
                {
                    return;
                }

                // 创建并显示止损单查看窗口
                var stopLossWindow = new StopLossOrdersWindow(selectedOrder, databaseService, logger)
                {
                    Owner = this
                };
                
                // 使用Dispatcher确保在UI线程中显示对话框
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (IsLoaded && IsVisible)
                        {
                            stopLossWindow.ShowDialog();
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // 窗口已关闭，忽略此异常
                    }
                }));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"查看止损单失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// 大于零转换器
    /// </summary>
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

    /// <summary>
    /// 小于零转换器
    /// </summary>
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

    /// <summary>
    /// 布尔值到可见性转换器
    /// </summary>
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public static readonly BooleanToVisibilityConverter Instance = new BooleanToVisibilityConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                return visibility == Visibility.Visible;
            }
            return false;
        }
    }
} 