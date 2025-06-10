using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using TCClient.Services;
using TCClient.Utils;

namespace TCClient.Views
{
    /// <summary>
    /// 后台服务管理窗口
    /// </summary>
    public partial class ServiceManagerWindow : Window
    {
        private readonly BackgroundServiceManager _serviceManager;
        private readonly DateTime _startTime;

        public ServiceManagerWindow()
        {
            InitializeComponent();
            _startTime = DateTime.Now;
            
            // 获取服务管理器
            var app = (App)Application.Current;
            _serviceManager = app.Services.GetRequiredService<BackgroundServiceManager>();
            
            // 初始化界面
            LoadCurrentSettings();
            RefreshServiceStatus();
            
            // 设置启动时间
            StartTimeText.Text = _startTime.ToString("yyyy-MM-dd HH:mm:ss");
            ConfigFileText.Text = "已加载";
        }

        /// <summary>
        /// 加载当前设置到界面
        /// </summary>
        private void LoadCurrentSettings()
        {
            try
            {
                var options = _serviceManager.GetCurrentOptions();
                
                FindOpportunityTimerCheckBox.IsChecked = options.EnableFindOpportunityTimer;
                ConditionalOrderCheckBox.IsChecked = options.EnableConditionalOrderService;
                StopLossMonitorCheckBox.IsChecked = options.EnableStopLossMonitorService;
                OrderPriceUpdaterCheckBox.IsChecked = options.EnableOrderPriceUpdater;
                AccountInfoUpdaterCheckBox.IsChecked = options.EnableAccountInfoUpdater;
                AccountQueryTimerCheckBox.IsChecked = options.EnableAccountQueryTimer;
                
                LogManager.Log("ServiceManagerWindow", "当前设置已加载到界面");
            }
            catch (Exception ex)
            {
                LogManager.LogException("ServiceManagerWindow", ex, "加载当前设置失败");
                MessageBox.Show($"加载设置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 刷新服务运行状态
        /// </summary>
        private void RefreshServiceStatus()
        {
            try
            {
                var status = _serviceManager.GetServiceStatus();
                
                // 更新状态文本和颜色
                ConditionalOrderStatusText.Text = status.ContainsKey("ConditionalOrder") && status["ConditionalOrder"] ? "运行中" : "已停止";
                ConditionalOrderStatusText.Foreground = status.ContainsKey("ConditionalOrder") && status["ConditionalOrder"] ? 
                    System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Red;
                
                StopLossMonitorStatusText.Text = status.ContainsKey("StopLossMonitor") && status["StopLossMonitor"] ? "运行中" : "已停止";
                StopLossMonitorStatusText.Foreground = status.ContainsKey("StopLossMonitor") && status["StopLossMonitor"] ? 
                    System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Red;
                
                LogManager.Log("ServiceManagerWindow", $"服务状态已刷新: 条件单={ConditionalOrderStatusText.Text}, 止损={StopLossMonitorStatusText.Text}");
            }
            catch (Exception ex)
            {
                LogManager.LogException("ServiceManagerWindow", ex, "刷新服务状态失败");
            }
        }

        /// <summary>
        /// 从界面获取当前设置
        /// </summary>
        private BackgroundServiceOptions GetCurrentUISettings()
        {
            return new BackgroundServiceOptions
            {
                EnableFindOpportunityTimer = FindOpportunityTimerCheckBox.IsChecked ?? false,
                EnableConditionalOrderService = ConditionalOrderCheckBox.IsChecked ?? false,
                EnableStopLossMonitorService = StopLossMonitorCheckBox.IsChecked ?? false,
                EnableOrderPriceUpdater = OrderPriceUpdaterCheckBox.IsChecked ?? false,
                EnableAccountInfoUpdater = AccountInfoUpdaterCheckBox.IsChecked ?? false,
                EnableAccountQueryTimer = AccountQueryTimerCheckBox.IsChecked ?? false
            };
        }

        /// <summary>
        /// 仅启用寻找机会
        /// </summary>
        private void FindOpportunityOnlyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LogManager.Log("ServiceManagerWindow", "用户点击了'仅启用寻找机会'按钮");
                
                var result = MessageBox.Show(
                    "这将停止所有其他后台服务，只保留寻找机会窗口的市场数据更新功能。\n\n" +
                    "停止的服务包括：\n" +
                    "• 条件单监控服务\n" +
                    "• 止损监控服务\n" +
                    "• 订单价格更新器\n" +
                    "• 账户信息更新器\n" +
                    "• 账户查询定时器\n\n" +
                    "是否继续？",
                    "确认操作",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    _serviceManager.StartFindOpportunityOnlyMode();
                    LoadCurrentSettings();
                    RefreshServiceStatus();
                    
                    MessageBox.Show("已切换到寻找机会专用模式！", "操作成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    LogManager.Log("ServiceManagerWindow", "已成功切换到寻找机会专用模式");
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("ServiceManagerWindow", ex, "切换到寻找机会专用模式失败");
                MessageBox.Show($"操作失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 仅启用交易监控
        /// </summary>
        private void TradingOnlyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LogManager.Log("ServiceManagerWindow", "用户点击了'仅启用交易监控'按钮");
                
                var result = MessageBox.Show(
                    "这将只启用交易相关的监控服务，停止其他功能。\n\n" +
                    "启用的服务：\n" +
                    "• 条件单监控服务\n" +
                    "• 止损监控服务\n\n" +
                    "停止的服务：\n" +
                    "• 寻找机会定时器\n" +
                    "• 订单价格更新器\n" +
                    "• 账户信息更新器\n" +
                    "• 账户查询定时器\n\n" +
                    "是否继续？",
                    "确认操作",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    var options = new BackgroundServiceOptions
                    {
                        EnableConditionalOrderService = true,
                        EnableStopLossMonitorService = true,
                        EnableFindOpportunityTimer = false,
                        EnableOrderPriceUpdater = false,
                        EnableAccountInfoUpdater = false,
                        EnableAccountQueryTimer = false
                    };
                    
                    _serviceManager.UpdateOptions(options);
                    _serviceManager.RestartAllServices();
                    LoadCurrentSettings();
                    RefreshServiceStatus();
                    
                    MessageBox.Show("已切换到交易监控专用模式！", "操作成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    LogManager.Log("ServiceManagerWindow", "已成功切换到交易监控专用模式");
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("ServiceManagerWindow", ex, "切换到交易监控专用模式失败");
                MessageBox.Show($"操作失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 启用所有服务
        /// </summary>
        private void AllServicesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LogManager.Log("ServiceManagerWindow", "用户点击了'启用所有服务'按钮");
                
                var options = new BackgroundServiceOptions
                {
                    EnableConditionalOrderService = true,
                    EnableStopLossMonitorService = true,
                    EnableFindOpportunityTimer = true,
                    EnableOrderPriceUpdater = true,
                    EnableAccountInfoUpdater = true,
                    EnableAccountQueryTimer = true
                };
                
                _serviceManager.UpdateOptions(options);
                _serviceManager.RestartAllServices();
                LoadCurrentSettings();
                RefreshServiceStatus();
                
                MessageBox.Show("所有后台服务已启用！", "操作成功", MessageBoxButton.OK, MessageBoxImage.Information);
                LogManager.Log("ServiceManagerWindow", "已成功启用所有后台服务");
            }
            catch (Exception ex)
            {
                LogManager.LogException("ServiceManagerWindow", ex, "启用所有服务失败");
                MessageBox.Show($"操作失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 停止所有服务
        /// </summary>
        private void StopAllButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LogManager.Log("ServiceManagerWindow", "用户点击了'停止所有服务'按钮");
                
                var result = MessageBox.Show(
                    "这将停止所有后台服务！\n\n" +
                    "停止后：\n" +
                    "• 不会自动监控条件单\n" +
                    "• 不会自动执行止损\n" +
                    "• 不会自动更新市场数据\n" +
                    "• 不会自动更新价格和账户信息\n\n" +
                    "是否继续？",
                    "确认停止",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    var options = new BackgroundServiceOptions
                    {
                        EnableConditionalOrderService = false,
                        EnableStopLossMonitorService = false,
                        EnableFindOpportunityTimer = false,
                        EnableOrderPriceUpdater = false,
                        EnableAccountInfoUpdater = false,
                        EnableAccountQueryTimer = false
                    };
                    
                    _serviceManager.UpdateOptions(options);
                    _serviceManager.StopAllServices();
                    LoadCurrentSettings();
                    RefreshServiceStatus();
                    
                    MessageBox.Show("所有后台服务已停止！", "操作成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    LogManager.Log("ServiceManagerWindow", "已成功停止所有后台服务");
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("ServiceManagerWindow", ex, "停止所有服务失败");
                MessageBox.Show($"操作失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 刷新状态
        /// </summary>
        private void RefreshStatusButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LogManager.Log("ServiceManagerWindow", "用户点击了'刷新状态'按钮");
                RefreshServiceStatus();
                MessageBox.Show("状态已刷新！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogManager.LogException("ServiceManagerWindow", ex, "刷新状态失败");
                MessageBox.Show($"刷新失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 应用设置
        /// </summary>
        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LogManager.Log("ServiceManagerWindow", "用户点击了'应用设置'按钮");
                
                var newOptions = GetCurrentUISettings();
                _serviceManager.UpdateOptions(newOptions);
                _serviceManager.RestartAllServices();
                RefreshServiceStatus();
                
                MessageBox.Show("设置已应用并重启相关服务！", "操作成功", MessageBoxButton.OK, MessageBoxImage.Information);
                LogManager.Log("ServiceManagerWindow", "用户设置已成功应用");
            }
            catch (Exception ex)
            {
                LogManager.LogException("ServiceManagerWindow", ex, "应用设置失败");
                MessageBox.Show($"应用设置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 关闭窗口
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            LogManager.Log("ServiceManagerWindow", "服务管理窗口关闭");
            this.Close();
        }
    }
} 