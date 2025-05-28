using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows;
using TCClient.Models;
using TCClient.Services;
using TCClient.Utils;
using Microsoft.Extensions.Logging;

namespace TCClient.ViewModels
{
    /// <summary>
    /// 推仓统计ViewModel
    /// </summary>
    public class PushStatisticsViewModel : INotifyPropertyChanged
    {
        private readonly IDatabaseService _databaseService;
        private readonly ILogger<PushStatisticsViewModel> _logger;

        // 综合信息
        private int _totalPushCount;
        private int _openPushCount;
        private int _closedPushCount;
        private decimal _totalFloatingPnL;
        private decimal _totalRealPnL;

        // 持仓中推仓列表
        private ObservableCollection<PushSummaryInfo> _openPushList = new();
        private PushSummaryInfo _selectedOpenPush;
        private ObservableCollection<SimulationOrder> _openPushOrders = new();

        // 已完结推仓列表
        private ObservableCollection<PushSummaryInfo> _closedPushList = new();
        private PushSummaryInfo _selectedClosedPush;
        private ObservableCollection<SimulationOrder> _closedPushOrders = new();

        // 加载状态
        private bool _isLoading;

        public PushStatisticsViewModel(IDatabaseService databaseService, ILogger<PushStatisticsViewModel> logger)
        {
            _databaseService = databaseService;
            _logger = logger;

            // 初始化命令
            RefreshCommand = new RelayCommand(async () => await RefreshDataAsync());
            
            // 自动加载数据
            _ = Task.Run(async () => await RefreshDataAsync());
        }

        #region 属性

        /// <summary>
        /// 总推仓记录数
        /// </summary>
        public int TotalPushCount
        {
            get => _totalPushCount;
            set
            {
                _totalPushCount = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 持仓中推仓数量
        /// </summary>
        public int OpenPushCount
        {
            get => _openPushCount;
            set
            {
                _openPushCount = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 已完结推仓数量
        /// </summary>
        public int ClosedPushCount
        {
            get => _closedPushCount;
            set
            {
                _closedPushCount = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 总浮动盈亏
        /// </summary>
        public decimal TotalFloatingPnL
        {
            get => _totalFloatingPnL;
            set
            {
                _totalFloatingPnL = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 总实际盈亏
        /// </summary>
        public decimal TotalRealPnL
        {
            get => _totalRealPnL;
            set
            {
                _totalRealPnL = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 持仓中推仓列表
        /// </summary>
        public ObservableCollection<PushSummaryInfo> OpenPushList
        {
            get => _openPushList;
            set
            {
                _openPushList = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 选中的持仓中推仓
        /// </summary>
        public PushSummaryInfo SelectedOpenPush
        {
            get => _selectedOpenPush;
            set
            {
                _selectedOpenPush = value;
                OnPropertyChanged();
                _ = LoadOpenPushOrdersAsync();
            }
        }

        /// <summary>
        /// 持仓中推仓的订单列表
        /// </summary>
        public ObservableCollection<SimulationOrder> OpenPushOrders
        {
            get => _openPushOrders;
            set
            {
                _openPushOrders = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 已完结推仓列表
        /// </summary>
        public ObservableCollection<PushSummaryInfo> ClosedPushList
        {
            get => _closedPushList;
            set
            {
                _closedPushList = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 选中的已完结推仓
        /// </summary>
        public PushSummaryInfo SelectedClosedPush
        {
            get => _selectedClosedPush;
            set
            {
                _selectedClosedPush = value;
                OnPropertyChanged();
                _ = LoadClosedPushOrdersAsync();
            }
        }

        /// <summary>
        /// 已完结推仓的订单列表
        /// </summary>
        public ObservableCollection<SimulationOrder> ClosedPushOrders
        {
            get => _closedPushOrders;
            set
            {
                _closedPushOrders = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 是否正在加载
        /// </summary>
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }

        #endregion

        #region 命令

        public ICommand RefreshCommand { get; }

        #endregion

        #region 方法

        /// <summary>
        /// 刷新数据
        /// </summary>
        public async Task RefreshDataAsync()
        {
            try
            {
                IsLoading = true;
                _logger?.LogInformation("开始刷新推仓统计数据");

                var accountId = AppSession.CurrentAccountId;
                _logger?.LogInformation("当前账户ID: {accountId}", accountId);
                
                if (accountId <= 0)
                {
                    _logger?.LogWarning("当前账户ID无效，无法加载推仓统计数据");
                    return;
                }

                // 获取所有推仓信息
                var allPushInfos = await _databaseService.GetAllPushInfosAsync(accountId);
                _logger?.LogInformation("获取到推仓信息数量: {count}", allPushInfos.Count);
                
                // 分类统计
                var openPushInfos = allPushInfos.Where(p => p.Status.ToLower() == "open").ToList();
                var closedPushInfos = allPushInfos.Where(p => p.Status.ToLower() == "closed").ToList();

                // 更新综合信息
                TotalPushCount = allPushInfos.Count;
                OpenPushCount = openPushInfos.Count;
                ClosedPushCount = closedPushInfos.Count;

                // 计算总盈亏
                decimal totalFloating = 0m;
                decimal totalReal = 0m;

                foreach (var pushInfo in allPushInfos)
                {
                    if (pushInfo.Orders != null)
                    {
                        totalFloating += pushInfo.Orders.Sum(o => o.FloatingPnL ?? 0m);
                        totalReal += pushInfo.Orders.Sum(o => o.RealProfit ?? 0m);
                    }
                }

                TotalFloatingPnL = totalFloating;
                TotalRealPnL = totalReal;

                // 更新列表
                OpenPushList.Clear();
                foreach (var push in openPushInfos)
                {
                    OpenPushList.Add(push);
                }

                ClosedPushList.Clear();
                foreach (var push in closedPushInfos)
                {
                    ClosedPushList.Add(push);
                }

                _logger?.LogInformation("推仓统计数据刷新完成 - 总数: {total}, 持仓中: {open}, 已完结: {closed}", 
                    TotalPushCount, OpenPushCount, ClosedPushCount);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "刷新推仓统计数据失败");
                LogManager.LogException("PushStatisticsViewModel", ex, "刷新推仓统计数据失败");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// 加载持仓中推仓的订单
        /// </summary>
        private async Task LoadOpenPushOrdersAsync()
        {
            try
            {
                if (SelectedOpenPush == null)
                {
                    _logger?.LogInformation("SelectedOpenPush为空，清空订单列表");
                    // 确保在UI线程中清空集合
                    if (Application.Current.Dispatcher.CheckAccess())
                    {
                        OpenPushOrders.Clear();
                    }
                    else
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() => OpenPushOrders.Clear());
                    }
                    return;
                }

                _logger?.LogInformation("开始加载持仓中推仓订单 - 推仓ID: {pushId}, 合约: {contract}", 
                    SelectedOpenPush.PushId, SelectedOpenPush.Contract);

                var orders = await _databaseService.GetPushOrdersAsync(SelectedOpenPush.PushId);
                
                _logger?.LogInformation("从数据库获取到 {count} 个订单", orders.Count);
                
                // 记录每个订单的详细信息
                foreach (var order in orders)
                {
                    _logger?.LogInformation("订单详情 - ID: {orderId}, 合约: {contract}, 方向: {direction}, 数量: {quantity}, 状态: {status}", 
                        order.OrderId, order.Contract, order.Direction, order.Quantity, order.Status);
                }
                
                // 确保在UI线程中更新集合
                if (Application.Current.Dispatcher.CheckAccess())
                {
                    OpenPushOrders.Clear();
                    foreach (var order in orders)
                    {
                        OpenPushOrders.Add(order);
                    }
                    _logger?.LogInformation("UI线程中更新完成，OpenPushOrders.Count = {count}", OpenPushOrders.Count);
                }
                else
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        OpenPushOrders.Clear();
                        foreach (var order in orders)
                        {
                            OpenPushOrders.Add(order);
                        }
                        _logger?.LogInformation("Dispatcher线程中更新完成，OpenPushOrders.Count = {count}", OpenPushOrders.Count);
                    });
                }

                _logger?.LogInformation("持仓中推仓订单加载完成 - 订单数: {count}", orders.Count);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "加载持仓中推仓订单失败");
                LogManager.LogException("PushStatisticsViewModel", ex, "加载持仓中推仓订单失败");
            }
        }

        /// <summary>
        /// 加载已完结推仓的订单
        /// </summary>
        private async Task LoadClosedPushOrdersAsync()
        {
            try
            {
                if (SelectedClosedPush == null)
                {
                    _logger?.LogInformation("SelectedClosedPush为空，清空订单列表");
                    // 确保在UI线程中清空集合
                    if (Application.Current.Dispatcher.CheckAccess())
                    {
                        ClosedPushOrders.Clear();
                    }
                    else
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() => ClosedPushOrders.Clear());
                    }
                    return;
                }

                _logger?.LogInformation("开始加载已完结推仓订单 - 推仓ID: {pushId}, 合约: {contract}", 
                    SelectedClosedPush.PushId, SelectedClosedPush.Contract);

                var orders = await _databaseService.GetPushOrdersAsync(SelectedClosedPush.PushId);
                
                _logger?.LogInformation("从数据库获取到 {count} 个订单", orders.Count);
                
                // 记录每个订单的详细信息
                foreach (var order in orders)
                {
                    _logger?.LogInformation("订单详情 - ID: {orderId}, 合约: {contract}, 方向: {direction}, 数量: {quantity}, 状态: {status}", 
                        order.OrderId, order.Contract, order.Direction, order.Quantity, order.Status);
                }
                
                // 确保在UI线程中更新集合
                if (Application.Current.Dispatcher.CheckAccess())
                {
                    ClosedPushOrders.Clear();
                    foreach (var order in orders)
                    {
                        ClosedPushOrders.Add(order);
                    }
                    _logger?.LogInformation("UI线程中更新完成，ClosedPushOrders.Count = {count}", ClosedPushOrders.Count);
                }
                else
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        ClosedPushOrders.Clear();
                        foreach (var order in orders)
                        {
                            ClosedPushOrders.Add(order);
                        }
                        _logger?.LogInformation("Dispatcher线程中更新完成，ClosedPushOrders.Count = {count}", ClosedPushOrders.Count);
                    });
                }

                _logger?.LogInformation("已完结推仓订单加载完成 - 订单数: {count}", orders.Count);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "加载已完结推仓订单失败");
                LogManager.LogException("PushStatisticsViewModel", ex, "加载已完结推仓订单失败");
            }
        }



        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }

    /// <summary>
    /// 简单的命令实现
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Func<Task> _executeAsync;
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Func<Task> executeAsync, Func<bool> canExecute = null)
        {
            _executeAsync = executeAsync;
            _canExecute = canExecute;
        }

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute?.Invoke() ?? true;
        }

        public async void Execute(object parameter)
        {
            if (_executeAsync != null)
            {
                await _executeAsync();
            }
            else
            {
                _execute?.Invoke();
            }
        }
    }
} 