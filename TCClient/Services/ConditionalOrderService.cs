using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TCClient.Models;
using TCClient.Utils;

namespace TCClient.Services
{
    /// <summary>
    /// 条件单处理服务，负责检查和触发条件单
    /// </summary>
    public class ConditionalOrderService : IDisposable
    {
        private readonly IDatabaseService _databaseService;
        private readonly IExchangeService _exchangeService;
        private readonly IMessageService _messageService;
        private CancellationTokenSource _cts;
        private bool _isRunning;
        private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(1);
        private readonly Dictionary<string, decimal> _lastPrices = new Dictionary<string, decimal>();

        public ConditionalOrderService(
            IDatabaseService databaseService,
            IExchangeService exchangeService,
            IMessageService messageService)
        {
            _databaseService = databaseService;
            _exchangeService = exchangeService;
            _messageService = messageService;
        }

        /// <summary>
        /// 启动条件单监控服务
        /// </summary>
        public void Start()
        {
            if (_isRunning)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _isRunning = true;
            
            // 启动后台任务定期检查条件单
            Task.Run(MonitorConditionalOrdersAsync, _cts.Token);
            
            LogManager.Log("ConditionalOrderService", "条件单监控服务已启动");
        }

        /// <summary>
        /// 停止条件单监控服务
        /// </summary>
        public void Stop()
        {
            _cts?.Cancel();
            _isRunning = false;
            LogManager.Log("ConditionalOrderService", "条件单监控服务已停止");
        }

        /// <summary>
        /// 条件单监控主循环
        /// </summary>
        private async Task MonitorConditionalOrdersAsync()
        {
            try
            {
                LogManager.Log("ConditionalOrderService", "条件单监控循环开始");
                
                while (!_cts.Token.IsCancellationRequested && _isRunning)
                {
                    try
                    {
                        // TODO: 从数据库获取所有等待中的条件单
                        var waitingOrders = await GetWaitingConditionalOrdersAsync();
                        
                        if (waitingOrders.Count > 0)
                        {
                            LogManager.Log("ConditionalOrderService", $"找到 {waitingOrders.Count} 个待触发条件单");
                            
                            // 按交易对分组处理
                            var symbolGroups = new Dictionary<string, List<ConditionalOrder>>();
                            foreach (var order in waitingOrders)
                            {
                                if (!symbolGroups.ContainsKey(order.Symbol))
                                {
                                    symbolGroups[order.Symbol] = new List<ConditionalOrder>();
                                }
                                symbolGroups[order.Symbol].Add(order);
                            }
                            
                            // 获取每个交易对的最新价格并检查条件单
                            foreach (var symbolGroup in symbolGroups)
                            {
                                string symbol = symbolGroup.Key;
                                List<ConditionalOrder> orders = symbolGroup.Value;
                                
                                // 获取最新价格
                                decimal currentPrice = await GetLatestPriceAsync(symbol);
                                _lastPrices[symbol] = currentPrice;
                                
                                // 检查该交易对的所有条件单
                                foreach (var order in orders)
                                {
                                    await CheckAndExecuteConditionalOrderAsync(order, currentPrice);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogManager.LogException("ConditionalOrderService", ex, "监控条件单时发生错误");
                    }
                    
                    // 等待指定时间间隔
                    await Task.Delay(_checkInterval, _cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消，忽略
            }
            catch (Exception ex)
            {
                LogManager.LogException("ConditionalOrderService", ex, "条件单监控循环异常终止");
            }
            finally
            {
                _isRunning = false;
                LogManager.Log("ConditionalOrderService", "条件单监控循环已结束");
            }
        }

        /// <summary>
        /// 检查条件单是否满足触发条件，满足则执行
        /// </summary>
        private async Task CheckAndExecuteConditionalOrderAsync(ConditionalOrder order, decimal currentPrice)
        {
            try
            {
                bool shouldTrigger = false;
                
                // 检查是否满足触发条件
                switch (order.ConditionType)
                {
                    case ConditionalOrderType.BREAK_UP:
                        // 向上突破: 当前价格 >= 触发价格
                        shouldTrigger = currentPrice >= order.TriggerPrice;
                        break;
                    
                    case ConditionalOrderType.BREAK_DOWN:
                        // 向下突破: 当前价格 <= 触发价格
                        shouldTrigger = currentPrice <= order.TriggerPrice;
                        break;
                }
                
                if (shouldTrigger)
                {
                    LogManager.Log("ConditionalOrderService", $"条件单 ID={order.Id} 已触发 (当前价格={currentPrice}, 触发价格={order.TriggerPrice})");
                    
                    // 标记为已触发
                    await UpdateOrderStatusAsync(order.Id, ConditionalOrderStatus.TRIGGERED);
                    
                    // 执行订单
                    await ExecuteConditionalOrderAsync(order, currentPrice);
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("ConditionalOrderService", ex, $"检查条件单 ID={order.Id} 时发生错误");
            }
        }

        /// <summary>
        /// 执行已触发的条件单
        /// </summary>
        private async Task ExecuteConditionalOrderAsync(ConditionalOrder order, decimal currentPrice)
        {
            try
            {
                LogManager.Log("ConditionalOrderService", $"开始执行条件单 ID={order.Id}");
                
                // 1. 查询open推仓
                var pushInfo = await _databaseService.GetOpenPushInfoAsync(order.AccountId, order.Symbol);
                if (pushInfo == null)
                {
                    LogManager.Log("ConditionalOrderService", "[执行条件单] 未找到open推仓，准备新建推仓信息...");
                    pushInfo = await _databaseService.CreatePushInfoAsync(order.AccountId, order.Symbol);
                    LogManager.Log("ConditionalOrderService", $"[执行条件单] 新建推仓信息成功，推仓ID={pushInfo.Id}");
                }
                else
                {
                    LogManager.Log("ConditionalOrderService", $"[执行条件单] 已存在open推仓，推仓ID={pushInfo.Id}");
                }

                // 2. 插入订单
                var simulationOrder = new SimulationOrder
                {
                    OrderId = Guid.NewGuid().ToString(),
                    AccountId = order.AccountId,
                    Contract = order.Symbol,
                    ContractSize = 1, // TODO: 实际合约面值
                    Direction = order.Direction.ToLower(),
                    Quantity = (int)order.Quantity,
                    EntryPrice = currentPrice,
                    InitialStopLoss = order.StopLossPrice ?? 0,
                    CurrentStopLoss = order.StopLossPrice ?? 0,
                    Leverage = order.Leverage,
                    Margin = order.Quantity * currentPrice / order.Leverage, // 简化的保证金计算
                    TotalValue = order.Quantity * currentPrice, // 简化的总市值计算
                    Status = "open",
                    OpenTime = DateTime.Now
                };
                
                LogManager.Log("ConditionalOrderService", "[执行条件单] 开始插入订单...");
                long orderId = await _databaseService.InsertSimulationOrderAsync(simulationOrder);
                LogManager.Log("ConditionalOrderService", $"[执行条件单] 订单插入成功，订单ID={orderId}");

                // 3. 插入推仓-订单关联
                LogManager.Log("ConditionalOrderService", $"[执行条件单] 插入推仓-订单关联，推仓ID={pushInfo.Id}，订单ID={orderId}");
                await _databaseService.InsertPushOrderRelAsync(pushInfo.Id, orderId);
                LogManager.Log("ConditionalOrderService", "[执行条件单] 推仓-订单关联插入成功");

                // 4. 更新条件单状态为已执行，记录执行的订单ID
                await UpdateOrderToExecutedAsync(order.Id, simulationOrder.OrderId);
                LogManager.Log("ConditionalOrderService", $"[执行条件单] 条件单ID={order.Id}已更新为已执行状态，关联订单ID={simulationOrder.OrderId}");

                // 5. 显示通知
                _messageService.ShowMessage($"条件单已触发并执行! 交易对: {order.Symbol}, 方向: {order.Direction}, 价格: {currentPrice}", 
                    "条件单执行", 
                    System.Windows.MessageBoxButton.OK, 
                    System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogManager.LogException("ConditionalOrderService", ex, $"执行条件单 ID={order.Id} 时发生错误");
                
                // 记录失败状态
                try
                {
                    await UpdateOrderToFailedAsync(order.Id, ex.Message);
                }
                catch
                {
                    // 忽略更新失败的异常
                }
            }
        }

        /// <summary>
        /// 获取所有等待中的条件单
        /// </summary>
        private async Task<List<ConditionalOrder>> GetWaitingConditionalOrdersAsync()
        {
            // TODO: 实现从数据库获取等待中的条件单的逻辑
            // 模拟数据
            await Task.Delay(10);
            return new List<ConditionalOrder>();
        }

        /// <summary>
        /// 获取指定交易对的最新价格
        /// </summary>
        private async Task<decimal> GetLatestPriceAsync(string symbol)
        {
            try
            {
                // 通过交易所API获取最新价格
                var ticker = await _exchangeService.GetTickerAsync(symbol);
                return ticker.LastPrice;
            }
            catch (Exception ex)
            {
                LogManager.LogException("ConditionalOrderService", ex, $"获取 {symbol} 最新价格失败");
                
                // 如果获取失败，使用上次的价格，如果没有则返回0
                return _lastPrices.ContainsKey(symbol) ? _lastPrices[symbol] : 0;
            }
        }

        /// <summary>
        /// 更新条件单状态
        /// </summary>
        private async Task UpdateOrderStatusAsync(long orderId, ConditionalOrderStatus status)
        {
            // TODO: 实现更新条件单状态的数据库操作
            await Task.Delay(10);
            LogManager.Log("ConditionalOrderService", $"条件单 ID={orderId} 状态已更新为 {status}");
        }

        /// <summary>
        /// 更新条件单为已执行状态
        /// </summary>
        private async Task UpdateOrderToExecutedAsync(long orderId, string executionOrderId)
        {
            // TODO: 实现更新条件单为已执行状态的数据库操作
            await Task.Delay(10);
            LogManager.Log("ConditionalOrderService", $"条件单 ID={orderId} 状态已更新为已执行，关联订单ID={executionOrderId}");
        }

        /// <summary>
        /// 更新条件单为失败状态
        /// </summary>
        private async Task UpdateOrderToFailedAsync(long orderId, string errorMessage)
        {
            // TODO: 实现更新条件单为失败状态的数据库操作
            await Task.Delay(10);
            LogManager.Log("ConditionalOrderService", $"条件单 ID={orderId} 状态已更新为失败，错误信息: {errorMessage}");
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }
    }
} 