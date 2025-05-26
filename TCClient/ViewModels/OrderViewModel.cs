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
        private decimal _realTimeEquity;
        private int _opportunityCount;
        private decimal _singleRiskAmount;
        private string _riskCalculationFormula;

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

        /// <summary>
        /// 实时权益（从trading_accounts表的equity字段读取）
        /// </summary>
        public decimal RealTimeEquity
        {
            get => _realTimeEquity;
            set { _realTimeEquity = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 风险次数（从trading_accounts表的opportunity_count字段读取）
        /// </summary>
        public int OpportunityCount
        {
            get => _opportunityCount;
            set { _opportunityCount = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 单笔风险金（实时权益 ÷ 风险次数）
        /// </summary>
        public decimal SingleRiskAmount
        {
            get => _singleRiskAmount;
            set 
            { 
                _singleRiskAmount = value; 
                OnPropertyChanged();
                OnPropertyChanged(nameof(AvailableRiskAmount));
                OnPropertyChanged(nameof(AvailableRiskAmountFormula));
            }
        }

        /// <summary>
        /// 风险金计算公式显示
        /// </summary>
        public string RiskCalculationFormula
        {
            get => _riskCalculationFormula;
            set { _riskCalculationFormula = value; OnPropertyChanged(); }
        }

        public string ContractName
        {
            get => _contractName;
            set 
            { 
                // 统一将合约名称转换为大写格式，确保与数据库中的格式一致
                if (!string.IsNullOrEmpty(value))
                {
                    // 如果输入的是带usdt后缀的名称，去掉后缀并转换为大写
                    if (value.EndsWith("usdt", StringComparison.OrdinalIgnoreCase))
                    {
                        _contractName = value.Substring(0, value.Length - 4).ToUpper();
                    }
                    else
                    {
                        // 直接转换为大写，确保与数据库存储格式一致
                        _contractName = value.ToUpper();
                    }
                    
                    Utils.LogManager.Log("OrderViewModel", $"合约名称设置: 输入='{value}' -> 处理后='{_contractName}'");
                    
                    // 自动触发推仓信息查询
                    _ = Task.Run(async () => 
                    {
                        try
                        {
                            Utils.LogManager.Log("OrderViewModel", "开始异步加载推仓信息...");
                            _logger.LogInformation("ContractName setter: 开始异步加载推仓信息");
                            await LoadPushSummaryInfo();
                            Utils.LogManager.Log("OrderViewModel", "异步加载推仓信息完成");
                            _logger.LogInformation("ContractName setter: 异步加载推仓信息完成");
                        }
                        catch (Exception ex)
                        {
                            Utils.LogManager.LogException("OrderViewModel", ex, "异步加载推仓信息失败");
                            _logger.LogError(ex, "ContractName setter: 异步加载推仓信息失败");
                        }
                    });
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
                OnPropertyChanged(nameof(HasOpenPositions));
                OnPropertyChanged(nameof(OpenOrders));
                OnPropertyChanged(nameof(DisplayTotalOrderCount));
                OnPropertyChanged(nameof(DisplayOpenOrderCount));
                OnPropertyChanged(nameof(DisplayClosedOrderCount));
                OnPropertyChanged(nameof(TotalFloatingPnL));
                OnPropertyChanged(nameof(TotalRealPnL));
                OnPropertyChanged(nameof(AvailableRiskAmount));
                OnPropertyChanged(nameof(AvailableRiskAmountFormula));
            }
        }

        public bool HasOpenPush => PushSummary != null && PushSummary.PushId > 0;
        public bool HasOpenPositions => PushSummary?.Orders?.Any(o => o.Status.ToLower() == "open") ?? false;

        // 只显示状态为 "open" 的订单
        public List<SimulationOrder> OpenOrders => PushSummary?.Orders?.Where(o => o.Status.ToLower() == "open").ToList() ?? new List<SimulationOrder>();

        // 推仓信息区显示的数据
        public int DisplayTotalOrderCount => PushSummary?.Orders?.Count ?? 0;
        public int DisplayOpenOrderCount => OpenOrders.Count;
        public int DisplayClosedOrderCount => (PushSummary?.Orders?.Count ?? 0) - OpenOrders.Count;

        // 总浮动盈亏：所有订单的浮动盈亏之和
        public decimal TotalFloatingPnL => PushSummary?.Orders?.Sum(o => o.FloatingPnL ?? 0m) ?? 0m;
        
        // 总实际盈亏：所有订单的实际盈亏之和（平仓订单的实际盈亏等于其浮动盈亏）
        public decimal TotalRealPnL => PushSummary?.Orders?.Sum(o => 
        {
            if (o.Status?.ToLower() == "closed")
            {
                // 平仓订单：实际盈亏等于浮动盈亏
                return o.FloatingPnL ?? 0m;
            }
            else
            {
                // 开仓订单：使用原有的实际盈亏
                return o.RealProfit ?? 0m;
            }
        }) ?? 0m;

        // 可用风险金：单笔风险金 + 累计实际盈亏
        public decimal AvailableRiskAmount => SingleRiskAmount + TotalRealPnL;
        
        // 可用风险金计算公式
        public string AvailableRiskAmountFormula => $"{SingleRiskAmount:N2} + {TotalRealPnL:N2} = {AvailableRiskAmount:N2}";

        public ICommand QueryContractCommand { get; }
        public ICommand PlaceOrderCommand { get; }
        public ICommand QueryOnEnterCommand { get; }
        public ICommand CloseAllPositionsCommand { get; }

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
            CloseAllPositionsCommand = new RelayCommand(async () => await CloseAllPositionsAsync(), () => HasOpenPositions);
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
                
                Utils.LogManager.Log("OrderViewModel", $"加载推仓信息开始");
                Utils.LogManager.Log("OrderViewModel", $"原始合约名称: '{ContractName}'");
                Utils.LogManager.Log("OrderViewModel", $"处理后合约名称: '{baseContractName}'");
                Utils.LogManager.Log("OrderViewModel", $"账户ID: {accountId}");
                
                _logger.LogInformation("=== 开始加载推仓信息 ===");
                _logger.LogInformation("账户ID: {accountId}", accountId);
                _logger.LogInformation("原始合约名称: '{contractName}'", ContractName);
                _logger.LogInformation("处理后合约名称: '{baseContractName}'", baseContractName);
                _logger.LogInformation("合约名称长度: {length}", baseContractName?.Length ?? 0);
                
                // 使用基础合约名称（不带usdt后缀）查询推仓信息
                Utils.LogManager.Log("OrderViewModel", $"调用数据库查询: GetPushSummaryInfoAsync({accountId}, '{baseContractName}')");
                _logger.LogInformation("调用数据库查询: GetPushSummaryInfoAsync({accountId}, '{baseContractName}')", accountId, baseContractName);
                
                PushSummary = await _databaseService.GetPushSummaryInfoAsync(accountId, baseContractName);
                
                Utils.LogManager.Log("OrderViewModel", $"数据库查询结果: {(PushSummary == null ? "null" : "有数据")}");
                _logger.LogInformation("数据库查询完成");
                
                if (PushSummary != null)
                {
                    _logger.LogInformation("推仓信息加载成功 - 推仓ID: {pushId}, 订单数: {orderCount}, 总浮动盈亏: {totalFloatingPnL:N2}, 总实际盈亏: {totalRealPnL:N2}",
                        PushSummary.PushId, PushSummary.Orders?.Count ?? 0, PushSummary.Orders?.Sum(o => o.FloatingPnL ?? 0m), PushSummary.Orders?.Sum(o => o.RealProfit ?? 0m));

                    // 更新持仓订单列表
                    UpdateRelatedOrders();
                }
                else
                {
                    _logger.LogInformation("未找到推仓信息 - 返回的PushSummary为null");
                    RelatedOrders.Clear();
                }
                
                _logger.LogInformation("=== 推仓信息加载完成 ===");
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
                                        // 更新最新价格
                                        LatestPrice = tick.LastPrice;
                                        await UpdatePriceAndPushInfo(tick.LastPrice, token);
                                        _logger.LogInformation("更新价格：{contractName} = {latestPrice}", ContractName, LatestPrice);
                                    }
                                    else
                                    {
                                        _logger.LogInformation("未找到合约 {contractName} 的价格数据", GetFullContractName());
                                    }
                                    
                                    // 更新K线图控件的自定义合约列表价格
                                    UpdateKLineContractPrices(ticks);
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
                        // 多单：实际盈亏 = (止损价 - 开仓价) * 数量（止损时的亏损金额，通常为负值）
                        realPnL = (order.InitialStopLoss - order.EntryPrice) * quantity;
                        
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
                        // 空单：实际盈亏 = (开仓价 - 止损价) * 数量（止损时的亏损金额，通常为负值）
                        realPnL = (order.EntryPrice - order.InitialStopLoss) * quantity;
                        
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
                    
                    // 触发属性更新通知，让UI重新计算显示的值
                    OnPropertyChanged(nameof(PushSummary));
                    OnPropertyChanged(nameof(TotalFloatingPnL));
                    OnPropertyChanged(nameof(TotalRealPnL));
                    OnPropertyChanged(nameof(OpenOrders));
                    
                    UpdateRelatedOrders();
                    _logger.LogInformation("更新完成 - 总浮动盈亏: {totalFloatingPnL}, 总实际盈亏: {totalRealPnL}", 
                        TotalFloatingPnL, 
                        TotalRealPnL);
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
            // 验证下单参数
            if (OrderQuantity <= 0)
            {
                throw new ArgumentException($"订单数量必须大于0，当前数量：{OrderQuantity}");
            }
            
            if (LatestPrice <= 0)
            {
                throw new ArgumentException($"价格必须大于0，当前价格：{LatestPrice}");
            }
            
            if (Leverage <= 0)
            {
                throw new ArgumentException($"杠杆必须大于0，当前杠杆：{Leverage}");
            }
            
            // 记录下单参数
            TCClient.Utils.AppSession.Log($"[下单参数] 数量: {OrderQuantity}, 价格: {LatestPrice}, 杠杆: {Leverage}, 方向: {OrderDirection}");
            _logger.LogInformation("下单参数 - 数量: {quantity}, 价格: {price}, 杠杆: {leverage}, 方向: {direction}", 
                OrderQuantity, LatestPrice, Leverage, OrderDirection);
            
            // 创建订单对象
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
            
            TCClient.Utils.AppSession.Log("[下单] 开始创建订单和推仓信息...");
            // InsertSimulationOrderAsync 方法已经包含了推仓信息的创建和关联，无需重复操作
            long orderId = await _databaseService.InsertSimulationOrderAsync(order);
            TCClient.Utils.AppSession.Log($"[下单] 订单和推仓信息创建成功，订单ID={orderId}");

            _messageService.ShowMessage("下单成功！", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            TCClient.Utils.AppSession.Log("[下单] 市价下单成功");
        }

        public void UpdateKLinePeriod(string period)
        {
            _logger.LogInformation("更新K线周期: {period}", period);
        }

        /// <summary>
        /// 一键平仓：平掉当前合约的所有持仓订单
        /// </summary>
        public async Task CloseAllPositionsAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(ContractName))
                {
                    _messageService.ShowMessage("请先选择合约", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (PushSummary?.Orders == null || !PushSummary.Orders.Any(o => o.Status.ToLower() == "open"))
                {
                    _messageService.ShowMessage("当前合约没有持仓订单", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var openOrders = PushSummary.Orders.Where(o => o.Status.ToLower() == "open").ToList();
                var result = _messageService.ShowMessage(
                    $"确定要平掉 {ContractName} 合约的所有 {openOrders.Count} 个持仓订单吗？\n\n" +
                    $"当前总浮动盈亏：{TotalFloatingPnL:N2} 元\n" +
                    $"当前总实际盈亏：{TotalRealPnL:N2} 元",
                    "确认平仓",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }

                _logger.LogInformation("开始一键平仓 - 合约: {contractName}, 订单数: {orderCount}", ContractName, openOrders.Count);
                Utils.LogManager.Log("OrderViewModel", $"开始一键平仓 - 合约: {ContractName}, 订单数: {openOrders.Count}");

                var accountId = TCClient.Utils.AppSession.CurrentAccountId;
                var closeTime = DateTime.Now;
                var closePrice = LatestPrice; // 使用当前最新价格作为平仓价格

                int successCount = 0;
                int failCount = 0;

                foreach (var order in openOrders)
                {
                    try
                    {
                        // 计算最终的浮动盈亏
                        decimal finalFloatingPnL = 0;
                        decimal quantity = (decimal)order.Quantity;

                        if (order.Direction?.ToLower() == "buy")
                        {
                            // 多单：浮动盈亏 = (平仓价 - 开仓价) * 数量
                            finalFloatingPnL = (closePrice - order.EntryPrice) * quantity;
                        }
                        else if (order.Direction?.ToLower() == "sell")
                        {
                            // 空单：浮动盈亏 = (开仓价 - 平仓价) * 数量
                            finalFloatingPnL = (order.EntryPrice - closePrice) * quantity;
                        }

                        // 更新订单状态为已平仓
                        order.Status = "closed";
                        order.CloseTime = closeTime;
                        order.ClosePrice = closePrice;
                        order.CurrentPrice = closePrice;
                        order.FloatingPnL = finalFloatingPnL;
                        order.RealProfit = finalFloatingPnL; // 平仓后，实际盈亏等于浮动盈亏
                        order.LastUpdateTime = closeTime;

                        // 更新数据库中的订单
                        await _databaseService.UpdateSimulationOrderAsync(order);

                        _logger.LogInformation("订单 {orderId} 平仓成功 - 平仓价: {closePrice}, 最终盈亏: {finalPnL:N2}", 
                            order.OrderId, closePrice, finalFloatingPnL);
                        Utils.LogManager.Log("OrderViewModel", $"订单 {order.OrderId} 平仓成功 - 平仓价: {closePrice}, 最终盈亏: {finalFloatingPnL:N2}");

                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "平仓订单 {orderId} 失败", order.OrderId);
                        Utils.LogManager.LogException("OrderViewModel", ex, $"平仓订单 {order.OrderId} 失败");
                        failCount++;
                    }
                }

                // 更新推仓信息状态
                if (PushSummary != null && successCount > 0)
                {
                    try
                    {
                        // 检查是否所有订单都已平仓
                        var remainingOpenOrders = PushSummary.Orders.Count(o => o.Status.ToLower() == "open") - successCount;
                        if (remainingOpenOrders <= 0)
                        {
                            // 所有订单都已平仓，更新推仓状态为已完结
                            await _databaseService.UpdatePushInfoStatusAsync(PushSummary.PushId, "closed", closeTime);
                            _logger.LogInformation("推仓 {pushId} 状态更新为已完结", PushSummary.PushId);
                            Utils.LogManager.Log("OrderViewModel", $"推仓 {PushSummary.PushId} 状态更新为已完结");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "更新推仓状态失败");
                        Utils.LogManager.LogException("OrderViewModel", ex, "更新推仓状态失败");
                    }
                }

                // 刷新推仓信息
                await LoadPushSummaryInfo();

                // 显示结果
                if (failCount == 0)
                {
                    _messageService.ShowMessage($"平仓完成！成功平仓 {successCount} 个订单", "平仓成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    _messageService.ShowMessage($"平仓完成！成功 {successCount} 个，失败 {failCount} 个", "平仓结果", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                _logger.LogInformation("一键平仓完成 - 成功: {successCount}, 失败: {failCount}", successCount, failCount);
                Utils.LogManager.Log("OrderViewModel", $"一键平仓完成 - 成功: {successCount}, 失败: {failCount}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "一键平仓失败");
                Utils.LogManager.LogException("OrderViewModel", ex, "一键平仓失败");
                _messageService.ShowMessage($"平仓失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 更新K线图控件的自定义合约列表价格
        /// </summary>
        /// <param name="tickers">价格数据</param>
        private void UpdateKLineContractPrices(IEnumerable<TickerInfo> tickers)
        {
            try
            {
                // 通过KLineChartControl属性更新价格
                if (KLineChartControl != null)
                {
                    KLineChartControl.UpdateContractPrices(tickers);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新K线图合约价格失败");
            }
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
                        // 检查用户是否已登录
                        if (TCClient.Utils.AppSession.IsLoggedIn && TCClient.Utils.AppSession.CurrentAccountId > 0)
                        {
                            await UpdateAccountInfoAsync();
                            await Task.Delay(5000); // 每5秒更新一次
                        }
                        else
                        {
                            // 未登录时等待更长时间再检查
                            await Task.Delay(10000); // 等待10秒
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "更新账户信息失败");
                        await Task.Delay(5000); // 发生错误时等待5秒后重试
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
                    // 计算单笔风险金和公式
                    decimal singleRisk = 0m;
                    string formula = "";
                    
                    if (account.OpportunityCount > 0)
                    {
                        singleRisk = account.Equity / account.OpportunityCount;
                        formula = $"{account.Equity:N2} ÷ {account.OpportunityCount} = {singleRisk:N2}";
                    }
                    else
                    {
                        formula = $"{account.Equity:N2} ÷ 0 = 无法计算（风险次数为0）";
                    }

                    // 使用Dispatcher确保在UI线程上更新属性
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        // 原有属性
                        TotalEquity = account.Equity;
                        AvailableBalance = account.Equity; // 暂时使用总权益作为可用余额
                        UnrealizedPnL = 0m; // 暂时设为0，后续可以从数据库获取
                        PositionMargin = 0m; // 暂时设为0，后续可以从数据库获取
                        
                        // 新增属性
                        RealTimeEquity = account.Equity;
                        OpportunityCount = account.OpportunityCount;
                        SingleRiskAmount = singleRisk;
                        RiskCalculationFormula = formula;
                    });

                    _logger.LogInformation("已更新账户信息 - 实时权益: {equity:N2}, 风险次数: {count}, 单笔风险金: {singleRisk:N2}", 
                        account.Equity, account.OpportunityCount, singleRisk);
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