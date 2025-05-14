using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using TCClient.Commands;
using TCClient.Models;
using TCClient.Services;
using TCClient.Utils;

namespace TCClient.ViewModels
{
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

        // 下单区
        private decimal _orderQuantity;
        private decimal _leverage;
        private decimal _stopLossRatio;
        private string _orderDirection;

        private readonly IDatabaseService _databaseService;
        private readonly IMessageService _messageService;

        public string ContractName
        {
            get => _contractName;
            set { _contractName = value; OnPropertyChanged(); }
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
        public decimal OrderQuantity
        {
            get => _orderQuantity;
            set { _orderQuantity = value; OnPropertyChanged(); }
        }
        public decimal Leverage
        {
            get => _leverage;
            set { _leverage = value; OnPropertyChanged(); }
        }
        public decimal StopLossRatio
        {
            get => _stopLossRatio;
            set { _stopLossRatio = value; OnPropertyChanged(); }
        }
        public string OrderDirection
        {
            get => _orderDirection;
            set { _orderDirection = value; OnPropertyChanged(); }
        }

        public ICommand QueryContractCommand { get; }
        public ICommand PlaceOrderCommand { get; }

        public OrderViewModel()
        {
            _databaseService = ServiceLocator.GetService<IDatabaseService>();
            _messageService = ServiceLocator.GetService<IMessageService>();
            QueryContractCommand = new RelayCommand(QueryContractInfo);
            PlaceOrderCommand = new RelayCommand(async () => await PlaceOrderAsync());
            OrderDirection = "多";
        }

        private void QueryContractInfo()
        {
            // TODO: 查询合约信息（最大杠杆、最小交易金额、最小交易单元等）
            // 这里用模拟数据
            MaxLeverage = 20;
            MinTradeAmount = 10;
            MinTradeUnit = 0.001m;
            StartTickTimer();
            // TODO: 查询推仓信息和相关订单
            CurrentPositionStatus = "open";
            RelatedOrders.Clear();
            // 示例订单
            // RelatedOrders.Add(new OrderInfo { OrderId = "1001", Direction = "多", Status = "持仓中", OpenPrice = 100, ClosePrice = 0 });
        }

        private void StartTickTimer()
        {
            _tickCts?.Cancel();
            _tickCts = new CancellationTokenSource();
            var token = _tickCts.Token;
            Task.Run(async () =>
            {
                var rand = new Random();
                while (!token.IsCancellationRequested)
                {
                    // TODO: 用apikey从交易所拉取tick数据
                    // 这里用模拟数据
                    LatestPrice = 100 + (decimal)rand.NextDouble() * 10;
                    await Task.Delay(1000, token);
                }
            }, token);
        }

        private async Task PlaceOrderAsync()
        {
            try
            {
                long accountId = TCClient.Utils.AppSession.CurrentAccountId;
                TCClient.Utils.AppSession.Log($"[调试] 当前下单使用的账户ID: {accountId}");
                string contract = ContractName;
                TCClient.Utils.AppSession.Log($"[下单] 开始，账户ID={accountId}，合约={contract}");

                // 1. 查询open推仓
                var pushInfo = await _databaseService.GetOpenPushInfoAsync(accountId, contract);
                if (pushInfo == null)
                {
                    TCClient.Utils.AppSession.Log("[下单] 未找到open推仓，准备新建推仓信息...");
                    pushInfo = await _databaseService.CreatePushInfoAsync(accountId, contract);
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
                    Contract = contract,
                    ContractSize = 1, // TODO: 实际合约面值
                    Direction = OrderDirection == "多" ? "buy" : "sell",
                    Quantity = (int)OrderQuantity,
                    EntryPrice = LatestPrice,
                    InitialStopLoss = 0, // TODO: 实际止损价
                    CurrentStopLoss = 0, // TODO: 实际止损价
                    Leverage = (int)Leverage,
                    Margin = 0, // TODO: 实际保证金
                    TotalValue = 0, // TODO: 实际总市值
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
                TCClient.Utils.AppSession.Log("下单成功！");
            }
            catch (Exception ex)
            {
                _messageService.ShowMessage($"下单失败：{ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                TCClient.Utils.AppSession.Log($"下单失败：{ex.Message}");
                TCClient.Utils.AppSession.Log(ex.ToString());
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class OrderInfo
    {
        public string OrderId { get; set; }
        public string Direction { get; set; }
        public string Status { get; set; }
        public decimal OpenPrice { get; set; }
        public decimal ClosePrice { get; set; }
    }
} 