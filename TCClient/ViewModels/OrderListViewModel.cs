using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using TCClient.Commands;
using TCClient.Models;
using TCClient.Services;
using System.Linq;

namespace TCClient.ViewModels
{
    public class OrderListViewModel : INotifyPropertyChanged
    {
        private readonly IDatabaseService _databaseService;
        private readonly IMessageService _messageService;
        private readonly IExchangeService _exchangeService;
        private bool _isLoading;
        private long _accountId;

        // 市价单集合
        private ObservableCollection<SimulationOrder> _marketOrders = new ObservableCollection<SimulationOrder>();
        public ObservableCollection<SimulationOrder> MarketOrders
        {
            get => _marketOrders;
            set { _marketOrders = value; OnPropertyChanged(); }
        }

        // 条件单集合
        private ObservableCollection<ConditionalOrder> _conditionalOrders = new ObservableCollection<ConditionalOrder>();
        public ObservableCollection<ConditionalOrder> ConditionalOrders
        {
            get => _conditionalOrders;
            set { _conditionalOrders = value; OnPropertyChanged(); }
        }

        // 选中的市价单
        private SimulationOrder _selectedMarketOrder;
        public SimulationOrder SelectedMarketOrder
        {
            get => _selectedMarketOrder;
            set { _selectedMarketOrder = value; OnPropertyChanged(); }
        }

        // 选中的条件单
        private ConditionalOrder _selectedConditionalOrder;
        public ConditionalOrder SelectedConditionalOrder
        {
            get => _selectedConditionalOrder;
            set { _selectedConditionalOrder = value; OnPropertyChanged(); }
        }

        // 加载状态
        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        // 命令
        public ICommand RefreshCommand { get; }
        public ICommand CancelMarketOrderCommand { get; }
        public ICommand CancelConditionalOrderCommand { get; }

        public OrderListViewModel(
            IDatabaseService databaseService,
            IMessageService messageService,
            IExchangeService exchangeService)
        {
            _databaseService = databaseService;
            _messageService = messageService;
            _exchangeService = exchangeService;

            // 初始化命令
            RefreshCommand = new RelayCommand(async () => await LoadOrdersAsync());
            CancelMarketOrderCommand = new RelayCommand<SimulationOrder>(async (order) => await CancelMarketOrderAsync(order));
            CancelConditionalOrderCommand = new RelayCommand<ConditionalOrder>(async (order) => await CancelConditionalOrderAsync(order));
        }

        public void Initialize(long accountId)
        {
            _accountId = accountId;
            _ = LoadOrdersAsync();
        }

        public async Task LoadOrdersAsync()
        {
            try
            {
                IsLoading = true;
                
                // 清空列表
                MarketOrders.Clear();
                ConditionalOrders.Clear();

                // 加载市价单（模拟订单）
                var marketOrders = await _databaseService.GetSimulationOrdersAsync((int)_accountId);
                if (marketOrders != null)
                {
                    // 只显示状态为open的订单
                    var openOrders = marketOrders.Where(o => o.Status == "open").ToList();
                    foreach (var order in openOrders)
                    {
                        MarketOrders.Add(order);
                    }
                }

                // TODO: 加载条件单 - 这里需要在IDatabaseService中实现GetConditionalOrdersAsync方法
                // 目前只是创建一个空列表
                // var conditionalOrders = await _databaseService.GetConditionalOrdersAsync(_accountId);
                
                // 更新UI显示
                OnPropertyChanged(nameof(MarketOrders));
                OnPropertyChanged(nameof(ConditionalOrders));
            }
            catch (Exception ex)
            {
                _messageService.ShowMessage($"加载订单列表失败：{ex.Message}", "错误", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task CancelMarketOrderAsync(SimulationOrder order)
        {
            if (order == null) return;

            try
            {
                var result = _messageService.ShowMessage($"确定要取消订单 {order.OrderId} 吗？", "确认操作", 
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
                
                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    // 设置订单状态为已取消
                    order.Status = "cancelled";
                    order.CloseTime = DateTime.Now;
                    
                    // 更新数据库
                    bool success = await _databaseService.UpdateSimulationOrderAsync(order);
                    if (success)
                    {
                        _messageService.ShowMessage("订单已成功取消", "操作成功", 
                            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                        
                        // 刷新列表
                        await LoadOrdersAsync();
                    }
                    else
                    {
                        _messageService.ShowMessage("取消订单失败", "操作失败", 
                            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                _messageService.ShowMessage($"取消订单时发生错误：{ex.Message}", "错误", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private async Task CancelConditionalOrderAsync(ConditionalOrder order)
        {
            if (order == null) return;

            try
            {
                var result = _messageService.ShowMessage($"确定要取消条件单 ID={order.Id} 吗？", "确认操作", 
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
                
                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    // TODO: 实现取消条件单的逻辑
                    // 需要在IDatabaseService中添加UpdateConditionalOrderAsync方法
                    
                    // 示例代码：
                    // order.Status = ConditionalOrderStatus.CANCELLED;
                    // bool success = await _databaseService.UpdateConditionalOrderAsync(order);
                    
                    // 临时消息，表示功能待实现
                    _messageService.ShowMessage("取消条件单功能暂未实现", "提示", 
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    
                    // 刷新列表
                    await LoadOrdersAsync();
                }
            }
            catch (Exception ex)
            {
                _messageService.ShowMessage($"取消条件单时发生错误：{ex.Message}", "错误", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 