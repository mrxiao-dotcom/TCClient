using System;
using System.Windows;
using TCClient.ViewModels;
using TCClient.Services;
using TCClient.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace TCClient.Views
{
    /// <summary>
    /// 市场总览窗口
    /// </summary>
    public partial class MarketOverviewWindow : Window
    {
        private MarketOverviewViewModel _viewModel;

        public MarketOverviewWindow()
        {
            InitializeComponent();
            InitializeViewModel();
            Loaded += MarketOverviewWindow_Loaded;
            Closed += MarketOverviewWindow_Closed;
        }

        private void InitializeViewModel()
        {
            try
            {
                // 从依赖注入容器获取服务
                var serviceProvider = ((App)Application.Current).Services;
                
                var marketOverviewService = serviceProvider.GetService<MarketOverviewService>();
                var exchangeService = serviceProvider.GetService<IExchangeService>();
                var databaseService = serviceProvider.GetService<IDatabaseService>();

                // 如果服务不存在，创建新的实例
                if (marketOverviewService == null)
                {
                    AppSession.Log("MarketOverviewWindow: 从容器创建MarketOverviewService");
                    marketOverviewService = new MarketOverviewService(exchangeService, databaseService);
                }

                // 获取推送服务
                var pushService = serviceProvider.GetService<PushNotificationService>();
                if (pushService == null)
                {
                    AppSession.Log("MarketOverviewWindow: 创建PushNotificationService");
                    pushService = new PushNotificationService();
                }

                _viewModel = new MarketOverviewViewModel(marketOverviewService, pushService);
                DataContext = _viewModel;

                AppSession.Log("MarketOverviewWindow: ViewModel初始化完成");
            }
            catch (Exception ex)
            {
                AppSession.Log($"MarketOverviewWindow: 初始化ViewModel失败 - {ex.Message}");
                MessageBox.Show($"初始化市场总览失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MarketOverviewWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                AppSession.Log("MarketOverviewWindow: 窗口已加载");
                // 窗口加载完成后的处理
            }
            catch (Exception ex)
            {
                AppSession.Log($"MarketOverviewWindow: 窗口加载事件处理失败 - {ex.Message}");
            }
        }

        private void MarketOverviewWindow_Closed(object sender, EventArgs e)
        {
            try
            {
                AppSession.Log("MarketOverviewWindow: 窗口已关闭");
                // 停止定时器
                _viewModel?.StopTimers();
                // 清理资源
                _viewModel = null;
                DataContext = null;
            }
            catch (Exception ex)
            {
                AppSession.Log($"MarketOverviewWindow: 窗口关闭事件处理失败 - {ex.Message}");
            }
        }
    }
} 