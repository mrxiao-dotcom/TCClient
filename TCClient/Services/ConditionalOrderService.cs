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
            try
            {
                if (_cts != null && !_cts.Token.IsCancellationRequested)
                {
                    _cts.Cancel();
                }
                _isRunning = false;
                LogManager.Log("ConditionalOrderService", "条件单监控服务已停止");
            }
            catch (ObjectDisposedException)
            {
                // CancellationTokenSource已被释放，忽略
                LogManager.Log("ConditionalOrderService", "条件单监控服务停止时CancellationTokenSource已释放");
            }
            catch (Exception ex)
            {
                LogManager.LogException("ConditionalOrderService", ex, "停止条件单监控服务时发生异常");
            }
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
                                
                                try
                                {
                                    // 获取最新价格
                                    decimal currentPrice = await GetLatestPriceAsync(symbol);
                                    
                                    // 如果价格为0，说明获取失败，跳过本次处理
                                    if (currentPrice <= 0)
                                    {
                                        LogManager.Log("ConditionalOrderService", $"合约 {symbol} 价格获取失败（价格为0），跳过 {orders.Count} 个条件单的检查");
                                        continue;
                                    }
                                    
                                    // 更新价格缓存
                                    _lastPrices[symbol] = currentPrice;
                                    
                                    LogManager.Log("ConditionalOrderService", $"合约 {symbol} 当前价格: {currentPrice}，检查 {orders.Count} 个条件单");
                                    
                                    // 检查该交易对的所有条件单
                                    foreach (var order in orders)
                                    {
                                        await CheckAndExecuteConditionalOrderAsync(order, currentPrice);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    LogManager.LogException("ConditionalOrderService", ex, $"处理合约 {symbol} 时发生错误");
                                    
                                    // 即使单个合约处理失败，也要继续处理其他合约
                                    LogManager.Log("ConditionalOrderService", $"合约 {symbol} 处理失败，继续处理其他合约");
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
                // 正常取消，不记录为异常
                LogManager.Log("ConditionalOrderService", "条件单监控循环正常取消");
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
                
                // 创建订单对象
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
                
                LogManager.Log("ConditionalOrderService", "[执行条件单] 开始创建订单和推仓信息...");
                // InsertSimulationOrderAsync 方法已经包含了推仓信息的创建和关联，无需重复操作
                long orderId = await _databaseService.InsertSimulationOrderAsync(simulationOrder);
                LogManager.Log("ConditionalOrderService", $"[执行条件单] 订单和推仓信息创建成功，订单ID={orderId}");

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
            return await _databaseService.GetWaitingConditionalOrdersAsync();
        }

        /// <summary>
        /// 获取指定交易对的最新价格
        /// </summary>
        private async Task<decimal> GetLatestPriceAsync(string symbol)
        {
            try
            {
                // 格式化合约名称，确保有USDT后缀
                string formattedSymbol = symbol.ToUpper();
                if (!formattedSymbol.EndsWith("USDT"))
                {
                    formattedSymbol = $"{formattedSymbol}USDT";
                }

                // 通过交易所API获取最新价格
                var ticker = await _exchangeService.GetTickerAsync(formattedSymbol);
                
                if (ticker != null && ticker.LastPrice > 0)
                {
                    // 成功获取价格，更新缓存
                    _lastPrices[symbol] = ticker.LastPrice;
                    return ticker.LastPrice;
                }
                
                // ticker为null或价格无效，使用缓存价格
                if (_lastPrices.ContainsKey(symbol))
                {
                    LogManager.Log("ConditionalOrderService", $"获取 {symbol} 价格失败(ticker为null)，使用缓存价格: {_lastPrices[symbol]}");
                    return _lastPrices[symbol];
                }
                
                // 既没有获取到价格，也没有缓存价格
                LogManager.Log("ConditionalOrderService", $"⚠️ 无法获取合约 {symbol} 的价格，且无缓存价格可用");
                LogManager.Log("ConditionalOrderService", "这通常是由网络连接问题或API服务器响应慢造成的");
                LogManager.Log("ConditionalOrderService", $"跳过合约 {symbol} 的条件单检查，等待下次循环重试");
                
                return 0; // 返回0表示价格获取失败
            }
            catch (Exception ex)
            {
                LogManager.LogException("ConditionalOrderService", ex, $"获取 {symbol} 最新价格时发生异常");
                
                // 如果获取失败，使用缓存价格
                if (_lastPrices.ContainsKey(symbol))
                {
                    LogManager.Log("ConditionalOrderService", $"发生异常后使用缓存价格: {_lastPrices[symbol]}");
                    return _lastPrices[symbol];
                }
                
                LogManager.Log("ConditionalOrderService", $"合约 {symbol} 价格获取异常且无缓存，返回0跳过本次检查");
                return 0; // 返回0表示价格获取失败
            }
        }

        /// <summary>
        /// 更新条件单状态
        /// </summary>
        private async Task UpdateOrderStatusAsync(long orderId, ConditionalOrderStatus status)
        {
            await _databaseService.UpdateConditionalOrderStatusAsync(orderId, status);
            LogManager.Log("ConditionalOrderService", $"条件单 ID={orderId} 状态已更新为 {status}");
        }

        /// <summary>
        /// 更新条件单为已执行状态
        /// </summary>
        private async Task UpdateOrderToExecutedAsync(long orderId, string executionOrderId)
        {
            await _databaseService.UpdateConditionalOrderToExecutedAsync(orderId, executionOrderId);
            LogManager.Log("ConditionalOrderService", $"条件单 ID={orderId} 状态已更新为已执行，关联订单ID={executionOrderId}");
        }

        /// <summary>
        /// 更新条件单为失败状态
        /// </summary>
        private async Task UpdateOrderToFailedAsync(long orderId, string errorMessage)
        {
            await _databaseService.UpdateConditionalOrderToFailedAsync(orderId, errorMessage);
            LogManager.Log("ConditionalOrderService", $"条件单 ID={orderId} 状态已更新为失败，错误信息: {errorMessage}");
        }

        public void Dispose()
        {
            try
            {
                Stop();
            }
            catch (Exception ex)
            {
                LogManager.LogException("ConditionalOrderService", ex, "Dispose时停止服务失败");
            }
            finally
            {
                try
                {
                    _cts?.Dispose();
                }
                catch (Exception ex)
                {
                    LogManager.LogException("ConditionalOrderService", ex, "Dispose时释放CancellationTokenSource失败");
                }
            }
        }
    }
} 