using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using TCClient.Models;
using TCClient.Services;
using Microsoft.Extensions.Logging;

namespace TCClient.Views
{
    /// <summary>
    /// 止损单查看窗口
    /// </summary>
    public partial class StopLossOrdersWindow : Window
    {
        private readonly IDatabaseService _databaseService;
        private readonly ILogger<StopLossOrdersWindow> _logger;
        private readonly SimulationOrder _order;

        public StopLossOrdersWindow(SimulationOrder order, IDatabaseService databaseService, ILogger<StopLossOrdersWindow> logger = null)
        {
            InitializeComponent();
            _order = order ?? throw new ArgumentNullException(nameof(order));
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
            _logger = logger;

            // 设置窗口标题和订单信息
            TitleTextBlock.Text = $"订单止损单详情 - {order.Contract}";
            OrderInfoTextBlock.Text = $"订单ID: {order.OrderId} | 方向: {order.Direction} | 数量: {order.Quantity} | 开仓价: {order.EntryPrice:N4}";

            // 加载止损单数据
            Loaded += StopLossOrdersWindow_Loaded;
        }

        private async void StopLossOrdersWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadStopLossOrdersAsync();
        }

        /// <summary>
        /// 加载止损单数据
        /// </summary>
        private async Task LoadStopLossOrdersAsync()
        {
            try
            {
                _logger?.LogInformation("开始加载订单 {orderId} 的止损单", _order.OrderId);

                // 获取与该模拟订单关联的所有止损止盈单
                var stopLossOrders = await GetStopLossOrdersBySimulationOrderIdAsync(_order.Id);

                StopLossDataGrid.ItemsSource = stopLossOrders;

                _logger?.LogInformation("成功加载止损单，共 {count} 条", stopLossOrders.Count);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "加载止损单失败");
                MessageBox.Show($"加载止损单失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 根据模拟订单ID获取止损止盈单
        /// </summary>
        private async Task<List<StopTakeOrder>> GetStopLossOrdersBySimulationOrderIdAsync(long simulationOrderId)
        {
            try
            {
                // 由于IDatabaseService接口中没有根据模拟订单ID查询的方法，我们需要先获取账户的所有止损单，然后筛选
                var allStopTakeOrders = await _databaseService.GetStopTakeOrdersAsync(_order.AccountId);
                
                var result = new List<StopTakeOrder>();
                foreach (var order in allStopTakeOrders)
                {
                    if (order.SimulationOrderId == simulationOrderId)
                    {
                        result.Add(order);
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "查询止损单失败");
                throw;
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadStopLossOrdersAsync();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    /// <summary>
    /// 止损单状态颜色转换器
    /// </summary>
    public class StopLossStatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string status)
            {
                return status switch
                {
                    "WAITING" => new SolidColorBrush(Colors.Orange),
                    "SET" => new SolidColorBrush(Colors.Blue),
                    "TRIGGERED" => new SolidColorBrush(Colors.Purple),
                    "EXECUTED" => new SolidColorBrush(Colors.Green),
                    "CANCELLED" => new SolidColorBrush(Colors.Gray),
                    "FAILED" => new SolidColorBrush(Colors.Red),
                    _ => new SolidColorBrush(Colors.Black)
                };
            }
            return new SolidColorBrush(Colors.Black);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 