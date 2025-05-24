using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows;
using TCClient.Commands;
using TCClient.Models;
using TCClient.Services;
using TCClient.Utils;
using System.Linq;
using TCClient.Views.Controls;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace TCClient.ViewModels
{
    public enum OrderType
    {
        Market,         // 市价下单
        Conditional     // 条件单
    }

    public class OrderViewModel : INotifyPropertyChanged
    {
        // 合约信息区
        private string _contractName;
        private decimal _latestPrice;
        private decimal _maxLeverage;
        private decimal _minTradeAmount;
        private decimal _minTradeUnit;
        private CancellationTokenSource _tickCts;

        // 推仓信息区
        private string _currentPositionStatus;
        public ObservableCollection<OrderInfo> RelatedOrders { get; set; } = new();
        private PushSummaryInfo _pushSummary;

        // 下单区
        private OrderType _selectedOrderType = OrderType.Market;
        private decimal _orderQuantity;
        private decimal _leverage = 3m;
        private decimal _stopLossPrice;
        private decimal _stopLossAmount;
        private string _orderDirection;
        private ConditionalOrderType _conditionalOrderType = ConditionalOrderType.BREAK_UP;
        private decimal _triggerPrice;

        private readonly IDatabaseService _databaseService;
        private readonly IMessageService _messageService;
        private KLineChartControl _kLineChartControl;
        private IExchangeService _exchangeService;

        // 账户信息相关字段
        private decimal _totalEquity;
        private decimal _availableBalance;
        private decimal _unrealizedPnL;
        private decimal _positionMargin;

        // 账户信息相关属性
        public decimal TotalEquity
        {
            get => _totalEquity;
            set { _totalEquity = value; OnPropertyChanged(); }
        }

        public decimal AvailableBalance
        {
            get => _availableBalance;
            set { _availableBalance = value; OnPropertyChanged(); }
        }

        public decimal UnrealizedPnL
        {
            get => _unrealizedPnL;
            set { _unrealizedPnL = value; OnPropertyChanged(); }
        }

        public decimal PositionMargin
        {
            get => _positionMargin;
            set { _positionMargin = value; OnPropertyChanged(); }
        }

        public string ContractName
        {
            get => _contractName;
            set 
            { 
                // 保存原始输入的合约名称（不带usdt后缀）
                if (!string.IsNullOrEmpty(value))
                {
                    // 如果输入的是带usdt后缀的名称，去掉后缀
                    if (value.EndsWith("usdt", StringComparison.OrdinalIgnoreCase))
                    {
                        _contractName = value.Substring(0, value.Length - 4).ToUpper();
                    }
                    else
                    {
                        _contractName = value.ToUpper();
                    }
                }
                else
                {
                    _contractName = value;
                }
                OnPropertyChanged();
            }
        }
        public decimal LatestPrice
        {
            get => _latestPrice;
            set { _latestPrice = value; OnPropertyChanged(); }
        }
        public decimal MaxLeverage
        {
            get => _maxLeverage;
            set { _maxLeverage = value; OnPropertyChanged(); }
        }
        public decimal MinTradeAmount
        {
            get => _minTradeAmount;
            set { _minTradeAmount = value; OnPropertyChanged(); }
        }
        public decimal MinTradeUnit
        {
            get => _minTradeUnit;
            set { _minTradeUnit = value; OnPropertyChanged(); }
        }
        public string CurrentPositionStatus
        {
            get => _currentPositionStatus;
            set { _currentPositionStatus = value; OnPropertyChanged(); }
        }
        public OrderType SelectedOrderType
        {
            get => _selectedOrderType;
            set 
            { 
                _selectedOrderType = value; 
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsConditionalOrder));
                OnPropertyChanged(nameof(IsMarketOrder));
            }
        }
        public decimal OrderQuantity
        {
            get => _orderQuantity;
            set 
            { 
                _orderQuantity = value;
                OnPropertyChanged();
            }
        }
        public decimal Leverage
        {
            get => _leverage;
            set
            {
                if (_leverage != value)
                {
                    _leverage = value;
                    OnPropertyChanged();
                }
            }
        }
        public decimal StopLossPrice
        {
            get => _stopLossPrice;
            set
            {
                if (_stopLossPrice != value)
                {
                    _stopLossPrice = value;
                    OnPropertyChanged();
                }
            }
        }
        public decimal StopLossAmount
        {
            get => _stopLossAmount;
            set
            {
                if (_stopLossAmount != value)
                {
                    _stopLossAmount = value;
                    OnPropertyChanged();
                }
            }
        }
        public string OrderDirection
        {
            get => _orderDirection;
            set { _orderDirection = value; OnPropertyChanged(); }
        }

        public ConditionalOrderType ConditionalOrderType
        {
            get => _conditionalOrderType;
            set 
            { 
                _conditionalOrderType = value; 
                OnPropertyChanged(); 
            }
        }

        public decimal TriggerPrice
        {
            get => _triggerPrice;
            set 
            { 
                _triggerPrice = value; 
                OnPropertyChanged(); 
            }
        }

        public bool IsConditionalOrder => SelectedOrderType == OrderType.Conditional;
        public bool IsMarketOrder => SelectedOrderType == OrderType.Market;

        public PushSummaryInfo PushSummary
        {
            get => _pushSummary;
            set
            {
                _pushSummary = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasOpenPush));
            }
        }

        public bool HasOpenPush => PushSummary != null;

        public decimal TotalFloatingPnL => PushSummary?.Orders?.Sum(o => o.FloatingPnL ?? 0m) ?? 0m;
        public decimal TotalRealPnL => PushSummary?.Orders?.Sum(o => o.RealProfit ?? 0m) ?? 0m;

        public ICommand QueryContractCommand { get; }
        public ICommand PlaceOrderCommand { get; }
        public ICommand QueryOnEnterCommand { get; }

        public KLineChartControl KLineChartControl
        {
            get => _kLineChartControl;
            set
            {
                _kLineChartControl = value;
                OnPropertyChanged();
            }
        }

        private readonly ILogger<OrderViewModel> _logger;

        // 实现 INotifyPropertyChanged 接口
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public OrderViewModel(
            IDatabaseService databaseService,
            IMessageService messageService,
            ILogger<OrderViewModel> logger)
        {
            _databaseService = databaseService;
            _messageService = messageService;
            _logger = logger;
            QueryContractCommand = new RelayCommand(QueryContractInfo);
            PlaceOrderCommand = new RelayCommand(async () => await PlaceOrderAsync());
            QueryOnEnterCommand = new RelayCommand<KeyEventArgs>(e => 
            {
                if (e.Key == Key.Enter)
                {
                    QueryContractInfo();
                }
            });
            OrderDirection = "多";
            
            // 初始化交易所服务
            _exchangeService = GetExchangeService();
            
            // 启动账户信息更新
            _ = Task.Run(() => StartAccountInfoUpdate());
        }

        // 添加辅助方法处理合约名称
        private string GetFullContractName()
        {
            if (string.IsNullOrEmpty(ContractName))
                return string.Empty;

            string formattedSymbol = ContractName.ToUpper();
            if (!formattedSymbol.EndsWith("USDT"))
            {
                formattedSymbol = $"{formattedSymbol}USDT";
            }
            return formattedSymbol;
        }

        private string GetBaseContractName()
        {
            if (string.IsNullOrEmpty(ContractName))
                return string.Empty;

            string baseName = ContractName.ToUpper();
            if (baseName.EndsWith("USDT"))
            {
                baseName = baseName.Substring(0, baseName.Length - 4);
            }
            return baseName;
        }

        private async Task LoadPushSummaryInfo()
        {
            try
            {
                if (string.IsNullOrEmpty(ContractName))
                {
                    PushSummary = null;
                    RelatedOrders.Clear();
                    return;
                }

                var accountId = TCClient.Utils.AppSession.CurrentAccountId;
                var baseContractName = GetBaseContractName();
                _logger.LogInformation("加载推仓信息 - 账户ID: {accountId}, 合约: {contractName}", accountId, baseContractName);
                
                // 使用基础合约名称（不带usdt后缀）查询推仓信息
                PushSummary = await _databaseService.GetPushSummaryInfoAsync(accountId, baseContractName);
                
                if (PushSummary != null)
                {
                    _logger.LogInformation("推仓信息加载成功 - 推仓ID: {pushId}, 订单数: {orderCount}, 总浮动盈亏: {totalFloatingPnL:N2}, 总实际盈亏: {totalRealPnL:N2}",
                        PushSummary.PushId, PushSummary.Orders?.Count ?? 0, PushSummary.Orders?.Sum(o => o.FloatingPnL ?? 0m), PushSummary.Orders?.Sum(o => o.RealProfit ?? 0m));

                    // 更新持仓订单列表
                    UpdateRelatedOrders();
                }
                else
                {
                    _logger.LogInformation("未找到推仓信息");
                    RelatedOrders.Clear();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载推仓信息失败");
                RelatedOrders.Clear();
            }
        }

        private void UpdateRelatedOrders()
        {
            try
            {
                // 清空当前列表
                RelatedOrders.Clear();

                if (PushSummary?.Orders == null || !PushSummary.Orders.Any())
                {
                    _logger.LogInformation("没有持仓订单需要更新");
                    return;
                }

                // 更新持仓订单列表
                foreach (var order in PushSummary.Orders.Where(o => o.Status.ToLower() == "open"))
                {
                    var orderInfo = new OrderInfo
                    {
                        OrderId = order.OrderId,
                        Direction = order.Direction,
                        Status = order.Status,
                        OpenPrice = order.EntryPrice,
                        ClosePrice = order.CurrentPrice ?? order.EntryPrice,
                        FloatingPnL = order.FloatingPnL ?? 0m,
                        RealProfit = order.RealProfit ?? 0m,
                        Quantity = order.Quantity,
                        StopLossPrice = order.InitialStopLoss,
                        LastUpdateTime = order.LastUpdateTime ?? DateTime.Now
                    };

                    RelatedOrders.Add(orderInfo);
                }

                _logger.LogInformation("更新持仓订单列表完成 - 订单数: {orderCount}", RelatedOrders.Count);
                
                // 触发属性更新通知
                OnPropertyChanged(nameof(RelatedOrders));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新持仓订单列表失败");
            }
        }

        private async void QueryContractInfo()
        {
            if (string.IsNullOrEmpty(ContractName))
            {
                return;
            }

            try
            {
                _logger.LogInformation("查询合约信息：{contractName}", ContractName);
                
                // 获取交易所服务
                var exchangeService = GetExchangeService();
                if (exchangeService == null)
                {
                    _logger.LogInformation("无法获取交易所服务，将使用模拟数据");
                    // 使用模拟数据
                    MaxLeverage = 20;
                    MinTradeAmount = 10;
                    MinTradeUnit = 0.001m;
                }
                else
                {
                    try
                    {
                        // 获取当前价格，使用带usdt后缀的合约名称
                        var ticker = await exchangeService.GetTickerAsync(GetFullContractName());
                        if (ticker != null)
                        {
                            LatestPrice = ticker.LastPrice;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "获取合约信息失败");
                    }
                }
                
                // 启动价格更新定时器
                await StartTickTimer();
                
                // 加载推仓信息
                await LoadPushSummaryInfo();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询合约信息失败");
            }
        }

        private async Task StartTickTimer()
        {
            try
            {
                _tickCts?.Cancel();
                _tickCts = new CancellationTokenSource();
                var token = _tickCts.Token;

                var exchangeService = GetExchangeService();
                if (exchangeService == null)
                {
                    _logger.LogError("获取交易所服务失败");
                    return;
                }

                if (string.IsNullOrEmpty(ContractName))
                {
                    _logger.LogWarning("合约名称为空，无法启动价格更新");
                    return;
                }

                _logger.LogInformation("启动价格更新定时器 - 合约: {contractName}", ContractName);

                await Task.Run(async () =>
                {
                    try
                    {
                        while (!token.IsCancellationRequested)
                        {
                            try
                            {
                                // 获取所有合约的 tick 数据
                                var ticks = await exchangeService.GetAllTickersAsync();
                                if (ticks != null && ticks.Any())
                                {
                                    // 使用完整合约名称（带USDT后缀）查找价格
                                    var tick = ticks.FirstOrDefault(t => 
                                        t.Symbol.Equals(GetFullContractName(), StringComparison.OrdinalIgnoreCase));
                                    
                                    if (tick != null && tick.LastPrice > 0)
                                    {
                                        await UpdatePriceAndPushInfo(tick.LastPrice, token);
                                        _logger.LogInformation("更新价格：{contractName} = {latestPrice}", ContractName, LatestPrice);
                                    }
                                    else
                                    {
                                        _logger.LogInformation("未找到合约 {contractName} 的价格数据", GetFullContractName());
                                    }
                                }
                                else
                                {
                                    _logger.LogInformation("获取到空的 tick 数据");
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "获取价格时出错");
                            }
                            
                            // 每秒更新一次价格
                            await Task.Delay(1000, token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // 操作被取消，正常退出
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "价格更新线程异常");
                    }
                }, token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动价格更新定时器失败");
                // 使用默认模拟价格
                LatestPrice = 100;
            }
        }

        private async Task UpdatePriceAndPushInfo(decimal latestPrice, CancellationToken token)
        {
            try
            {
                _logger.LogInformation("开始更新推仓订单信息 - 最新价格: {latestPrice}", latestPrice);
                _logger.LogInformation("当前推仓信息 - 订单数量: {orderCount}", PushSummary?.Orders?.Count ?? 0);

                if (PushSummary?.Orders == null || !PushSummary.Orders.Any())
                {
                    _logger.LogWarning("推仓订单列表为空，无法更新订单信息");
                    return;
                }

                var ordersToUpdate = new List<SimulationOrder>();
                bool hasChanges = false;

                foreach (var order in PushSummary.Orders)
                {
                    _logger.LogInformation("检查订单 {orderId} - 状态: {status}, 方向: {direction}, 数量: {quantity}", 
                        order.OrderId, 
                        order.Status, 
                        order.Direction, 
                        order.Quantity);

                    if (order.Status?.ToLower() != "open")
                    {
                        _logger.LogInformation("订单 {orderId} 状态不是open，跳过更新", order.OrderId);
                        continue;
                    }

                    // 计算浮动盈亏和实际盈亏
                    decimal floatingPnL = 0;
                    decimal realPnL = 0;

                    // 将 float 类型的数量转换为 decimal 进行计算
                    decimal quantity = (decimal)order.Quantity;

                    if (order.Direction?.ToLower() == "buy")
                    {
                        // 多单：浮动盈亏 = (最新价 - 开仓价) * 数量
                        floatingPnL = (latestPrice - order.EntryPrice) * quantity;
                        // 多单：实际盈亏 = (开仓价 - 初始止损价) * 数量
                        realPnL = (order.EntryPrice - order.InitialStopLoss) * quantity;
                        
                        // 更新最高价格：对于多单，记录开仓以来的最高价格
                        if (order.HighestPrice == null || latestPrice > order.HighestPrice)
                        {
                            order.HighestPrice = latestPrice;
                            _logger.LogInformation("订单 {orderId} 多单最高价格更新为: {highestPrice}", order.OrderId, latestPrice);
                        }
                        
                        // 更新最大浮动盈利：记录开仓以来的最大浮动盈利
                        if (order.MaxFloatingProfit == null || floatingPnL > order.MaxFloatingProfit)
                        {
                            order.MaxFloatingProfit = floatingPnL;
                            _logger.LogInformation("订单 {orderId} 最大浮动盈利更新为: {maxFloatingProfit}", order.OrderId, floatingPnL);
                        }
                    }
                    else if (order.Direction?.ToLower() == "sell")
                    {
                        // 空单：浮动盈亏 = (开仓价 - 最新价) * 数量
                        floatingPnL = (order.EntryPrice - latestPrice) * quantity;
                        // 空单：实际盈亏 = (初始止损价 - 开仓价) * 数量
                        realPnL = (order.InitialStopLoss - order.EntryPrice) * quantity;
                        
                        // 更新最高价格：对于空单，记录开仓以来的最低价格（对空单来说最低价格是最有利的）
                        if (order.HighestPrice == null || latestPrice < order.HighestPrice)
                        {
                            order.HighestPrice = latestPrice;
                            _logger.LogInformation("订单 {orderId} 空单最有利价格更新为: {lowestPrice}", order.OrderId, latestPrice);
                        }
                        
                        // 更新最大浮动盈利：记录开仓以来的最大浮动盈利
                        if (order.MaxFloatingProfit == null || floatingPnL > order.MaxFloatingProfit)
                        {
                            order.MaxFloatingProfit = floatingPnL;
                            _logger.LogInformation("订单 {orderId} 最大浮动盈利更新为: {maxFloatingProfit}", order.OrderId, floatingPnL);
                        }
                    }

                    _logger.LogInformation("订单 {orderId} 计算值 - 数量: {quantity}, 浮动盈亏: {floatingPnL}, 实际盈亏: {realPnL}", 
                        order.OrderId, 
                        quantity,
                        floatingPnL, 
                        realPnL);

                    // 检查是否有变化
                    bool priceChanged = Math.Abs((order.CurrentPrice ?? 0m) - latestPrice) > 0.0001m;
                    bool floatingPnLChanged = Math.Abs((order.FloatingPnL ?? 0m) - floatingPnL) > 0.0001m;
                    bool realPnLChanged = Math.Abs((order.RealProfit ?? 0m) - realPnL) > 0.0001m;
                    
                    // 检查最高价格是否有更新（只有当实际发生更新时才标记为已变化）
                    bool highestPriceUpdated = false;
                    if (order.Direction?.ToLower() == "buy")
                    {
                        highestPriceUpdated = (order.HighestPrice == null || latestPrice > order.HighestPrice);
                    }
                    else if (order.Direction?.ToLower() == "sell")
                    {
                        highestPriceUpdated = (order.HighestPrice == null || latestPrice < order.HighestPrice);
                    }
                    
                    // 检查最大浮动盈利是否有更新
                    bool maxFloatingProfitUpdated = (order.MaxFloatingProfit == null || floatingPnL > order.MaxFloatingProfit);

                    if (priceChanged || floatingPnLChanged || realPnLChanged || highestPriceUpdated || maxFloatingProfitUpdated)
                    {
                        _logger.LogInformation("订单 {orderId} 需要更新 - 价格变化: {priceChanged}, 浮动盈亏变化: {floatingPnLChanged}, 实际盈亏变化: {realPnLChanged}, 最高价格更新: {highestPriceUpdated}, 最大浮动盈利更新: {maxFloatingProfitUpdated}", 
                            order.OrderId, 
                            priceChanged, 
                            floatingPnLChanged, 
                            realPnLChanged,
                            highestPriceUpdated,
                            maxFloatingProfitUpdated);

                        _logger.LogInformation("更新前 - 最新价: {currentPrice}, 浮动盈亏: {floatingPnL}, 实际盈亏: {realPnL}, 最高价: {highestPrice}, 最大浮动盈利: {maxFloatingProfit}", 
                            order.CurrentPrice ?? 0m, 
                            order.FloatingPnL ?? 0m, 
                            order.RealProfit ?? 0m,
                            order.HighestPrice ?? 0m,
                            order.MaxFloatingProfit ?? 0m);

                        _logger.LogInformation("更新后 - 最新价: {latestPrice}, 浮动盈亏: {floatingPnL}, 实际盈亏: {realPnL}, 最高价: {highestPrice}, 最大浮动盈利: {maxFloatingProfit}", 
                            latestPrice, 
                            floatingPnL, 
                            realPnL,
                            order.HighestPrice ?? 0m,
                            order.MaxFloatingProfit ?? 0m);

                        order.CurrentPrice = latestPrice;
                        order.FloatingPnL = floatingPnL;
                        order.RealProfit = realPnL;
                        order.LastUpdateTime = DateTime.Now;
                        ordersToUpdate.Add(order);
                        hasChanges = true;
                    }
                    else
                    {
                        _logger.LogInformation("订单 {orderId} 无需更新 - 所有值都在阈值范围内", order.OrderId);
                    }
                }

                if (hasChanges)
                {
                    _logger.LogInformation("开始批量更新 {orderCount} 个订单到数据库", ordersToUpdate.Count);
                    foreach (var order in ordersToUpdate)
                    {
                        try
                        {
                            await _databaseService.UpdateSimulationOrderAsync(order, token);
                            _logger.LogInformation("订单 {orderId} 更新成功", order.OrderId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "更新订单 {orderId} 失败", order.OrderId);
                        }
                    }

                    // 更新UI显示
                    _logger.LogInformation("开始更新UI显示");
                    UpdateRelatedOrders();
                    await LoadPushSummaryInfo();
                    _logger.LogInformation("更新完成 - 总浮动盈亏: {totalFloatingPnL}, 总实际盈亏: {totalRealPnL}", 
                        PushSummary?.TotalFloatingPnL ?? 0m, 
                        PushSummary?.TotalRealPnL ?? 0m);
                }
                else
                {
                    _logger.LogInformation("没有订单需要更新");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新推仓订单信息时发生错误");
            }
        }

        // 获取交易所服务实例
        private IExchangeService GetExchangeService()
        {
            try
            {
                long accountId = TCClient.Utils.AppSession.CurrentAccountId;
                
                // 尝试从依赖注入获取服务工厂
                var app = System.Windows.Application.Current as App;
                if (app != null)
                {
                    // 首先尝试直接获取已注册的交易所服务
                    var exchangeService = app.Services.GetService(typeof(IExchangeService)) as IExchangeService;
                    if (exchangeService != null)
                    {
                        // 直接使用注册的交易所服务
                        return exchangeService;
                    }
                    
                    // 然后尝试使用当前下单窗口中已有的交易所服务
                    // 这是一个快速解决方案，避免UI卡死
                    var databaseService = app.Services.GetService(typeof(IDatabaseService)) as IDatabaseService;
                    var serviceFactory = app.Services.GetService(typeof(IExchangeServiceFactory)) as IExchangeServiceFactory;
                    if (serviceFactory != null && databaseService != null)
                    {
                        // 不要使用异步等待Result，这会导致死锁
                        // 因为我们需要立即创建服务，所以使用一个默认的交易账户对象
                        var defaultAccount = new TradingAccount
                        {
                            Id = accountId,
                            AccountName = "临时账户",
                            BinanceAccountId = "temp", 
                            ApiKey = "temp", 
                            ApiSecret = "temp"
                        };
                        
                        _logger.LogInformation("使用临时账户配置创建交易所服务");
                        return serviceFactory.CreateExchangeService(defaultAccount);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取交易所服务实例失败");
            }
            
            return null;
        }

        public async Task<bool> PlaceOrderAsync()
        {
            try
            {
                long accountId = TCClient.Utils.AppSession.CurrentAccountId;
                TCClient.Utils.AppSession.Log($"[调试] 当前下单使用的账户ID: {accountId}");
                string contract = ContractName;
                TCClient.Utils.AppSession.Log($"[下单] 开始，账户ID={accountId}，合约={contract}");

                // 检查是市价下单还是条件单
                if (SelectedOrderType == OrderType.Conditional)
                {
                    // 创建条件单
                    await CreateConditionalOrderAsync(accountId, contract);
                }
                else
                {
                    // 市价下单，直接执行
                    await ExecuteMarketOrderAsync(accountId, contract);
                }

                // 下单成功后刷新推仓信息
                await LoadPushSummaryInfo();
                
                // 下单成功
                return true;
            }
            catch (Exception ex)
            {
                _messageService.ShowMessage($"下单失败：{ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                TCClient.Utils.AppSession.Log($"下单失败：{ex.Message}");
                TCClient.Utils.AppSession.Log(ex.ToString());
                return false;
            }
        }

        private async Task CreateConditionalOrderAsync(long accountId, string contract)
        {
            TCClient.Utils.AppSession.Log("[下单] 创建条件单...");
            // 验证触发价格
            if (TriggerPrice <= 0)
            {
                throw new ArgumentException("请输入有效的触发价格");
            }

            // 创建条件单对象
            var conditionalOrder = new ConditionalOrder
            {
                AccountId = accountId,
                Symbol = contract,
                Direction = OrderDirection == "多" ? "BUY" : "SELL",
                ConditionType = ConditionalOrderType,
                TriggerPrice = TriggerPrice,
                Quantity = OrderQuantity,
                Leverage = (int)Leverage,
                StopLossPrice = StopLossPrice,
                Status = ConditionalOrderStatus.WAITING,
                CreateTime = DateTime.Now
            };

            TCClient.Utils.AppSession.Log($"[条件单] 创建条件单: {contract}, 触发价格: {TriggerPrice}, 类型: {ConditionalOrderType}, 方向: {conditionalOrder.Direction}");
            
            try
            {
                // 插入条件单到 conditional_orders 表
                long conditionalOrderId = await _databaseService.InsertConditionalOrderAsync(conditionalOrder);
                TCClient.Utils.AppSession.Log($"[条件单] 创建成功，条件单ID: {conditionalOrderId}");

                _messageService.ShowMessage("条件单创建成功！", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                TCClient.Utils.AppSession.Log($"[条件单] 创建失败: {ex.Message}");
                throw new Exception($"创建条件单失败: {ex.Message}");
            }
        }

        private async Task ExecuteMarketOrderAsync(long accountId, string contract)
        {
            // 1. 查询open推仓，使用原始合约名称（不带usdt后缀）
            var pushInfo = await _databaseService.GetOpenPushInfoAsync(accountId, ContractName);
            if (pushInfo == null)
            {
                TCClient.Utils.AppSession.Log("[下单] 未找到open推仓，准备新建推仓信息...");
                pushInfo = await _databaseService.CreatePushInfoAsync(accountId, ContractName);
                TCClient.Utils.AppSession.Log($"[下单] 新建推仓信息成功，推仓ID={pushInfo.Id}");
            }
            else
            {
                TCClient.Utils.AppSession.Log($"[下单] 已存在open推仓，推仓ID={pushInfo.Id}");
            }

            // 2. 插入订单
            var order = new SimulationOrder
            {
                OrderId = Guid.NewGuid().ToString(),
                AccountId = accountId,
                Contract = ContractName, // 使用原始合约名称
                ContractSize = 1m,
                Direction = OrderDirection == "多" ? "buy" : "sell",
                Quantity = (float)OrderQuantity,
                EntryPrice = LatestPrice,
                InitialStopLoss = StopLossPrice,
                CurrentStopLoss = StopLossPrice,
                Leverage = Convert.ToInt32(Math.Round((double)Leverage)),
                Margin = OrderQuantity * LatestPrice / Leverage,
                TotalValue = OrderQuantity * LatestPrice,
                Status = "open",
                OpenTime = DateTime.Now
            };
            TCClient.Utils.AppSession.Log("[下单] 开始插入订单...");
            long orderId = await _databaseService.InsertSimulationOrderAsync(order);
            TCClient.Utils.AppSession.Log($"[下单] 订单插入成功，订单ID={orderId}");

            // 3. 插入推仓-订单关联
            TCClient.Utils.AppSession.Log($"[下单] 插入推仓-订单关联，推仓ID={pushInfo.Id}，订单ID={orderId}");
            await _databaseService.InsertPushOrderRelAsync(pushInfo.Id, orderId);
            TCClient.Utils.AppSession.Log("[下单] 推仓-订单关联插入成功");

            _messageService.ShowMessage("下单成功！", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            TCClient.Utils.AppSession.Log("[下单] 市价下单成功");
        }

        public void UpdateKLinePeriod(string period)
        {
            _logger.LogInformation("更新K线周期: {period}", period);
        }

        /// <summary>
        /// 演示如何使用最高价格和最大浮动盈利字段实现交易策略
        /// 这个方法展示了几种常见的策略应用场景
        /// </summary>
        /// <param name="order">订单对象</param>
        /// <param name="currentPrice">当前价格</param>
        /// <returns>策略建议：Hold-持有, TakeProfit-止盈, AdjustStopLoss-调整止损</returns>
        public string EvaluateStrategyDecision(SimulationOrder order, decimal currentPrice)
        {
            if (order == null || order.HighestPrice == null || order.MaxFloatingProfit == null)
                return "Hold";

            bool isLong = order.Direction?.ToLower() == "buy";
            decimal quantity = (decimal)order.Quantity;
            decimal currentFloatingPnL = isLong ? 
                (currentPrice - order.EntryPrice) * quantity : 
                (order.EntryPrice - currentPrice) * quantity;

            // 策略1：回撤止盈策略
            // 当价格从最高点回撤超过一定比例时，考虑止盈
            decimal drawdownPercentage = 0;
            if (isLong && order.HighestPrice > 0)
            {
                drawdownPercentage = (order.HighestPrice.Value - currentPrice) / order.HighestPrice.Value;
            }
            else if (!isLong && order.HighestPrice > 0)
            {
                drawdownPercentage = (currentPrice - order.HighestPrice.Value) / order.HighestPrice.Value;
            }

            if (drawdownPercentage > 0.05m) // 回撤超过5%
            {
                _logger.LogInformation("订单 {orderId} 触发回撤止盈策略 - 回撤比例: {drawdownPercentage:P2}", 
                    order.OrderId, drawdownPercentage);
                return "TakeProfit";
            }

            // 策略2：浮盈保护策略
            // 当浮动盈利从最大值回撤超过一定比例时，考虑止盈
            if (order.MaxFloatingProfit > 0)
            {
                decimal profitDrawdown = (order.MaxFloatingProfit.Value - currentFloatingPnL) / order.MaxFloatingProfit.Value;
                if (profitDrawdown > 0.3m) // 浮盈回撤超过30%
                {
                    _logger.LogInformation("订单 {orderId} 触发浮盈保护策略 - 浮盈回撤: {profitDrawdown:P2}", 
                        order.OrderId, profitDrawdown);
                    return "TakeProfit";
                }
            }

            // 策略3：追踪止损策略
            // 根据最高价格动态调整止损位置
            if (order.MaxFloatingProfit > 100m) // 最大浮盈超过100元时启用追踪止损
            {
                decimal trailingStopDistance = isLong ? 
                    order.HighestPrice.Value * 0.03m : // 多单：从最高价向下3%
                    order.HighestPrice.Value * 0.03m;  // 空单：从最低价向上3%

                decimal newStopLoss = isLong ?
                    order.HighestPrice.Value - trailingStopDistance :
                    order.HighestPrice.Value + trailingStopDistance;

                // 只有当新止损价格更有利时才调整
                bool shouldAdjustStopLoss = isLong ?
                    newStopLoss > order.CurrentStopLoss :
                    newStopLoss < order.CurrentStopLoss;

                if (shouldAdjustStopLoss)
                {
                    _logger.LogInformation("订单 {orderId} 建议调整追踪止损 - 从 {oldStopLoss} 调整到 {newStopLoss}", 
                        order.OrderId, order.CurrentStopLoss, newStopLoss);
                    return "AdjustStopLoss";
                }
            }

            return "Hold";
        }

        private void StartAccountInfoUpdate()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        await UpdateAccountInfoAsync();
                        await Task.Delay(5000); // 每5秒更新一次
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "更新账户信息失败");
                        await Task.Delay(1000); // 发生错误时等待1秒后重试
                    }
                }
            });
        }

        private async Task UpdateAccountInfoAsync()
        {
            try
            {
                // 从数据库获取账户信息
                var accountId = TCClient.Utils.AppSession.CurrentAccountId;
                var account = await _databaseService.GetTradingAccountByIdAsync(accountId);
                
                if (account != null)
                {
                    // 使用Dispatcher确保在UI线程上更新属性
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        TotalEquity = account.Equity;
                        AvailableBalance = account.Equity; // 暂时使用总权益作为可用余额
                        UnrealizedPnL = 0m; // 暂时设为0，后续可以从数据库获取
                        PositionMargin = 0m; // 暂时设为0，后续可以从数据库获取
                    });

                    _logger.LogInformation("已更新账户信息 - 总权益: {totalEquity:N2}", TotalEquity);
                }
                else
                {
                    _logger.LogInformation("未找到ID为{accountId}的交易账户", accountId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新账户信息失败");
                _logger.LogError(ex, "异常堆栈: {ex}");
            }
        }
    }

    public class OrderInfo
    {
        public string OrderId { get; set; }
        public string Direction { get; set; }
        public string Status { get; set; }
        public decimal OpenPrice { get; set; }
        public decimal ClosePrice { get; set; }
        public decimal FloatingPnL { get; set; }
        public decimal RealProfit { get; set; }
        public float Quantity { get; set; }
        public decimal StopLossPrice { get; set; }
        public DateTime LastUpdateTime { get; set; }
    }
} 