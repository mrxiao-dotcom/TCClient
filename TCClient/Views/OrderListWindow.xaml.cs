using System;
using System.Windows;
using TCClient.ViewModels;
using TCClient.Services;

namespace TCClient.Views
{
    public partial class OrderListWindow : Window
    {
        private readonly OrderListViewModel _viewModel;

        public OrderListWindow(IDatabaseService databaseService, IMessageService messageService, IExchangeService exchangeService, long accountId)
        {
            InitializeComponent();
            
            // 创建视图模型并设置为数据上下文
            _viewModel = new OrderListViewModel(databaseService, messageService, exchangeService);
            DataContext = _viewModel;
            
            // 初始化视图模型
            _viewModel.Initialize(accountId);
            
            // 设置窗口标题
            Title = $"订单列表 - 账户ID: {accountId}";
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            
            // 在这里可以添加清理资源的代码
        }
    }
} 