using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TCClient.Models;
using TCClient.Utils;
using Microsoft.Extensions.Logging;

namespace TCClient.Services
{
    /// <summary>
    /// 止损监控服务，负责监控订单价格并自动触发止损
    /// </summary>
    public class StopLossMonitorService : IDisposable
    {
        private readonly IDatabaseService _databaseService;
        private readonly IExchangeService _exchangeService;
        private readonly ILogger<StopLossMonitorService> _logger;
        private CancellationTokenSource _cts;
        private bool _isRunning;
        private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(2); // 2秒检查一次
        private readonly Dictionary<string, decimal> _lastPrices = new Dictionary<string, decimal>();

        public StopLossMonitorService(
            IDatabaseService databaseService,
            IExchangeService exchangeService,
            ILogger<StopLossMonitorService> logger = null)
        {
            _databaseService = databaseService;
            _exchangeService = exchangeService;
            _logger = logger;
        }

        /// <summary>
        /// 启动止损监控服务
        /// </summary>
        public void Start()
        {
            if (_isRunning)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _isRunning = true;
            
            // 启动后台任务定期检查止损
            Task.Run(MonitorStopLossAsync, _cts.Token);
            
            LogManager.Log("StopLossMonitorService", "止损监控服务已启动");
            _logger?.LogInformation("止损监控服务已启动");
        }

        /// <summary>
        /// 停止止损监控服务
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
                LogManager.Log("StopLossMonitorService", "止损监控服务已停止");
                _logger?.LogInformation("止损监控服务已停止");
            }
            catch (ObjectDisposedException)
            {
                // CancellationTokenSource已被释放，忽略
                LogManager.Log("StopLossMonitorService", "止损监控服务停止时CancellationTokenSource已释放");
                _logger?.LogInformation("止损监控服务停止时CancellationTokenSource已释放");
            }
            catch (Exception ex)
            {
                LogManager.LogException("StopLossMonitorService", ex, "停止止损监控服务时发生异常");
                _logger?.LogError(ex, "停止止损监控服务时发生异常");
            }
        }

        /// <summary>
        /// 止损监控主循环
        /// </summary>
        private async Task MonitorStopLossAsync()
        {
            try
            {
                LogManager.Log("StopLossMonitorService", "止损监控循环开始");
                _logger?.LogInformation("止损监控循环开始");
                
                while (!_cts.Token.IsCancellationRequested && _isRunning)
                {
                    try
                    {
                        // 获取所有开仓状态的订单
                        var openOrders = await GetOpenOrdersAsync();
                        
                        if (openOrders.Count > 0)
                        {
                            LogManager.Log("StopLossMonitorService", $"找到 {openOrders.Count} 个开仓订单需要监控");
                            _logger?.LogInformation("找到 {orderCount} 个开仓订单需要监控", openOrders.Count);
                            
                            // 按合约分组处理
                            var contractGroups = openOrders.GroupBy(o => o.Contract).ToList();
                            
                            foreach (var contractGroup in contractGroups)
                            {
                                string contract = contractGroup.Key;
                                var orders = contractGroup.ToList();
                                
                                try
                                {
                                    // 获取最新价格
                                    decimal currentPrice = await GetLatestPriceAsync(contract);
                                    _lastPrices[contract] = currentPrice;
                                    
                                    // 检查该合约的所有订单
                                    foreach (var order in orders)
                                    {
                                        await CheckAndExecuteStopLossAsync(order, currentPrice);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    LogManager.LogException("StopLossMonitorService", ex, $"处理合约 {contract} 时发生错误");
                                    _logger?.LogError(ex, "处理合约 {contract} 时发生错误", contract);
                                }
                            }
                        }
                        
                        // 检查推仓状态（每次循环都检查）
                        await CheckAllPushStatusAsync();
                    }
                    catch (Exception ex)
                    {
                        LogManager.LogException("StopLossMonitorService", ex, "监控止损时发生错误");
                        _logger?.LogError(ex, "监控止损时发生错误");
                    }
                    
                    // 等待指定时间间隔
                    await Task.Delay(_checkInterval, _cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消，不记录为异常
                LogManager.Log("StopLossMonitorService", "止损监控循环正常取消");
                _logger?.LogInformation("止损监控循环正常取消");
            }
            catch (Exception ex)
            {
                LogManager.LogException("StopLossMonitorService", ex, "止损监控循环异常终止");
                _logger?.LogError(ex, "止损监控循环异常终止");
            }
            finally
            {
                _isRunning = false;
                LogManager.Log("StopLossMonitorService", "止损监控循环已结束");
                _logger?.LogInformation("止损监控循环已结束");
            }
        }

        /// <summary>
        /// 获取所有开仓状态的订单
        /// </summary>
        private async Task<List<SimulationOrder>> GetOpenOrdersAsync()
        {
            try
            {
                // 这里需要在数据库服务中添加获取所有开仓订单的方法
                return await _databaseService.GetAllOpenOrdersAsync();
            }
            catch (Exception ex)
            {
                LogManager.LogException("StopLossMonitorService", ex, "获取开仓订单失败");
                _logger?.LogError(ex, "获取开仓订单失败");
                return new List<SimulationOrder>();
            }
        }

        /// <summary>
        /// 获取合约的最新价格
        /// </summary>
        private async Task<decimal> GetLatestPriceAsync(string contract)
        {
            try
            {
                // 格式化合约名称，确保有USDT后缀
                string symbol = contract.ToUpper();
                if (!symbol.EndsWith("USDT"))
                {
                    symbol = $"{symbol}USDT";
                }
                
                var ticker = await _exchangeService.GetTickerAsync(symbol);
                if (ticker != null && ticker.LastPrice > 0)
                {
                    return ticker.LastPrice;
                }
                
                // 如果获取失败，返回上次的价格
                if (_lastPrices.ContainsKey(contract))
                {
                    LogManager.Log("StopLossMonitorService", $"获取 {contract} 价格失败，使用上次价格: {_lastPrices[contract]}");
                    return _lastPrices[contract];
                }
                
                throw new Exception($"无法获取合约 {contract} 的价格");
            }
            catch (Exception ex)
            {
                LogManager.LogException("StopLossMonitorService", ex, $"获取合约 {contract} 价格失败");
                _logger?.LogError(ex, "获取合约 {contract} 价格失败", contract);
                throw;
            }
        }

        /// <summary>
        /// 检查订单是否触发止损，如果触发则执行止损
        /// </summary>
        private async Task CheckAndExecuteStopLossAsync(SimulationOrder order, decimal currentPrice)
        {
            try
            {
                bool shouldStopLoss = false;
                string stopLossReason = "";
                
                // 首先检查是否需要更新移动止损
                await CheckAndUpdateTrailingStopLossAsync(order, currentPrice);
                
                // 检查是否触发止损
                if (order.Direction?.ToLower() == "buy")
                {
                    // 多单：当前价格 <= 止损价格
                    if (currentPrice <= order.CurrentStopLoss)
                    {
                        shouldStopLoss = true;
                        stopLossReason = $"多单止损触发 (当前价格 {currentPrice} <= 止损价格 {order.CurrentStopLoss})";
                    }
                }
                else if (order.Direction?.ToLower() == "sell")
                {
                    // 空单：当前价格 >= 止损价格
                    if (currentPrice >= order.CurrentStopLoss)
                    {
                        shouldStopLoss = true;
                        stopLossReason = $"空单止损触发 (当前价格 {currentPrice} >= 止损价格 {order.CurrentStopLoss})";
                    }
                }
                
                if (shouldStopLoss)
                {
                    LogManager.Log("StopLossMonitorService", $"订单 {order.OrderId} 触发止损: {stopLossReason}");
                    _logger?.LogInformation("订单 {orderId} 触发止损: {reason}", order.OrderId, stopLossReason);
                    
                    // 执行止损
                    await ExecuteStopLossAsync(order, currentPrice);
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("StopLossMonitorService", ex, $"检查订单 {order.OrderId} 止损时发生错误");
                _logger?.LogError(ex, "检查订单 {orderId} 止损时发生错误", order.OrderId);
            }
        }

        /// <summary>
        /// 执行止损操作
        /// </summary>
        private async Task ExecuteStopLossAsync(SimulationOrder order, decimal currentPrice)
        {
            try
            {
                LogManager.Log("StopLossMonitorService", $"开始执行订单 {order.OrderId} 的止损操作");
                _logger?.LogInformation("开始执行订单 {orderId} 的止损操作", order.OrderId);
                
                var closeTime = DateTime.Now;
                var closePrice = currentPrice; // 使用当前价格作为平仓价格
                
                // 计算最终的浮动盈亏
                decimal finalFloatingPnL = 0;
                decimal quantity = (decimal)order.Quantity;

                if (order.Direction?.ToLower() == "buy")
                {
                    // 多单：浮动盈亏 = (平仓价 - 开仓价) * 数量 * 合约面值
                    finalFloatingPnL = (closePrice - order.EntryPrice) * quantity * order.ContractSize;
                }
                else if (order.Direction?.ToLower() == "sell")
                {
                    // 空单：浮动盈亏 = (开仓价 - 平仓价) * 数量 * 合约面值
                    finalFloatingPnL = (order.EntryPrice - closePrice) * quantity * order.ContractSize;
                }

                // 更新订单状态为已平仓
                order.Status = "closed";
                order.CloseTime = closeTime;
                order.ClosePrice = closePrice;
                order.CurrentPrice = closePrice;
                order.FloatingPnL = finalFloatingPnL;
                order.RealProfit = finalFloatingPnL; // 平仓后，实际盈亏等于浮动盈亏
                order.CloseType = "stop_loss"; // 标记为止损平仓
                order.LastUpdateTime = closeTime;

                // 更新数据库中的订单
                await _databaseService.UpdateSimulationOrderAsync(order);

                // 取消该订单相关的所有委托单
                await CancelRelatedOrdersAsync(order.Id, order.Contract, order.AccountId);

                LogManager.Log("StopLossMonitorService", $"订单 {order.OrderId} 止损完成 - 平仓价: {closePrice}, 最终盈亏: {finalFloatingPnL:N2}");
                _logger?.LogInformation("订单 {orderId} 止损完成 - 平仓价: {closePrice}, 最终盈亏: {finalPnL:N2}", 
                    order.OrderId, closePrice, finalFloatingPnL);

                // 检查推仓状态是否需要更新
                await CheckAndUpdatePushStatusAsync(order.AccountId, order.Contract);
            }
            catch (Exception ex)
            {
                LogManager.LogException("StopLossMonitorService", ex, $"执行订单 {order.OrderId} 止损失败");
                _logger?.LogError(ex, "执行订单 {orderId} 止损失败", order.OrderId);
            }
        }

        /// <summary>
        /// 检查并更新移动止损
        /// </summary>
        /// <param name="order">订单信息</param>
        /// <param name="currentPrice">当前价格</param>
        private async Task CheckAndUpdateTrailingStopLossAsync(SimulationOrder order, decimal currentPrice)
        {
            try
            {
                bool isLong = order.Direction?.ToLower() == "buy";
                bool priceUpdated = false;
                bool stopLossUpdated = false;
                decimal originalStopLoss = order.CurrentStopLoss;
                
                // 更新最高价格（多单）或最低价格（空单）
                if (isLong)
                {
                    // 多单：记录最高价格
                    if (order.HighestPrice == null || currentPrice > order.HighestPrice)
                    {
                        order.HighestPrice = currentPrice;
                        priceUpdated = true;
                        LogManager.Log("StopLossMonitorService", $"订单 {order.OrderId} 多单最高价格更新为: {currentPrice}");
                        _logger?.LogInformation("订单 {orderId} 多单最高价格更新为: {highestPrice}", order.OrderId, currentPrice);
                    }
                }
                else
                {
                    // 空单：记录最低价格（对空单来说最低价格是最有利的）
                    if (order.HighestPrice == null || currentPrice < order.HighestPrice)
                    {
                        order.HighestPrice = currentPrice;
                        priceUpdated = true;
                        LogManager.Log("StopLossMonitorService", $"订单 {order.OrderId} 空单最有利价格（最低价）更新为: {currentPrice}");
                        _logger?.LogInformation("订单 {orderId} 空单最有利价格（最低价）更新为: {lowestPrice}", order.OrderId, currentPrice);
                    }
                }
                
                // 如果价格创新高/新低，计算移动止损
                if (priceUpdated && order.HighestPrice.HasValue)
                {
                    // 计算开仓价格与初始止损价格的距离比例
                    decimal initialStopDistance = Math.Abs(order.EntryPrice - order.InitialStopLoss);
                    decimal initialStopRatio = initialStopDistance / order.EntryPrice;
                    
                    // 使用相同的比例计算新的止损价格
                    decimal newStopLoss;
                    if (isLong)
                    {
                        // 多单：新止损价 = 最高价 * (1 - 初始止损比例)
                        newStopLoss = order.HighestPrice.Value * (1 - initialStopRatio);
                        
                        // 确保新止损价格比当前止损价格更有利（更高）
                        if (newStopLoss > order.CurrentStopLoss)
                        {
                            order.CurrentStopLoss = newStopLoss;
                            stopLossUpdated = true;
                            LogManager.Log("StopLossMonitorService", 
                                $"订单 {order.OrderId} 多单移动止损更新: {originalStopLoss:F4} -> {newStopLoss:F4} (最高价: {order.HighestPrice.Value:F4}, 止损比例: {initialStopRatio:P2})");
                            _logger?.LogInformation("订单 {orderId} 多单移动止损更新: {oldStopLoss:F4} -> {newStopLoss:F4}", 
                                order.OrderId, originalStopLoss, newStopLoss);
                        }
                    }
                    else
                    {
                        // 空单：新止损价 = 最低价 * (1 + 初始止损比例)
                        newStopLoss = order.HighestPrice.Value * (1 + initialStopRatio);
                        
                        // 确保新止损价格比当前止损价格更有利（更低）
                        if (newStopLoss < order.CurrentStopLoss)
                        {
                            order.CurrentStopLoss = newStopLoss;
                            stopLossUpdated = true;
                            LogManager.Log("StopLossMonitorService", 
                                $"订单 {order.OrderId} 空单移动止损更新: {originalStopLoss:F4} -> {newStopLoss:F4} (最低价: {order.HighestPrice.Value:F4}, 止损比例: {initialStopRatio:P2})");
                            _logger?.LogInformation("订单 {orderId} 空单移动止损更新: {oldStopLoss:F4} -> {newStopLoss:F4}", 
                                order.OrderId, originalStopLoss, newStopLoss);
                        }
                    }
                }
                
                // 如果有任何更新，保存到数据库
                if (priceUpdated || stopLossUpdated)
                {
                    // 更新当前价格和最后更新时间
                    order.CurrentPrice = currentPrice;
                    order.LastUpdateTime = DateTime.Now;
                    
                    await _databaseService.UpdateSimulationOrderAsync(order);
                    
                    if (stopLossUpdated)
                    {
                        LogManager.Log("StopLossMonitorService", $"订单 {order.OrderId} 移动止损已保存到数据库");
                        _logger?.LogInformation("订单 {orderId} 移动止损已保存到数据库", order.OrderId);
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("StopLossMonitorService", ex, $"更新订单 {order.OrderId} 移动止损时发生错误");
                _logger?.LogError(ex, "更新订单 {orderId} 移动止损时发生错误", order.OrderId);
            }
        }

        /// <summary>
        /// 取消指定订单相关的所有委托单（条件单和止损止盈单）
        /// </summary>
        /// <param name="simulationOrderId">模拟订单ID</param>
        /// <param name="contract">合约名称</param>
        /// <param name="accountId">账户ID</param>
        private async Task CancelRelatedOrdersAsync(long simulationOrderId, string contract, long accountId)
        {
            try
            {
                LogManager.Log("StopLossMonitorService", $"开始取消订单 {simulationOrderId} 相关的委托单");
                _logger?.LogInformation("开始取消订单 {orderId} 相关的委托单", simulationOrderId);

                int cancelledCount = 0;

                // 1. 取消相关的条件单（按合约和账户查找等待状态的条件单）
                try
                {
                    var conditionalOrders = await _databaseService.GetConditionalOrdersAsync(accountId);
                    var waitingConditionalOrders = conditionalOrders
                        .Where(o => o.Symbol == contract && o.Status == ConditionalOrderStatus.WAITING)
                        .ToList();

                    foreach (var conditionalOrder in waitingConditionalOrders)
                    {
                        try
                        {
                            await _databaseService.CancelConditionalOrderAsync(conditionalOrder.Id);
                            cancelledCount++;
                            LogManager.Log("StopLossMonitorService", $"取消条件单成功 - ID: {conditionalOrder.Id}");
                            _logger?.LogInformation("取消条件单成功 - ID: {id}", conditionalOrder.Id);
                        }
                        catch (Exception ex)
                        {
                            LogManager.LogException("StopLossMonitorService", ex, $"取消条件单失败 - ID: {conditionalOrder.Id}");
                            _logger?.LogError(ex, "取消条件单失败 - ID: {id}", conditionalOrder.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogManager.LogException("StopLossMonitorService", ex, "查询或取消条件单时发生错误");
                    _logger?.LogError(ex, "查询或取消条件单时发生错误");
                }

                // 2. 取消相关的止损止盈单（按模拟订单ID查找等待状态的止损止盈单）
                try
                {
                    var stopTakeOrders = await _databaseService.GetStopTakeOrdersAsync(accountId);
                    var waitingStopTakeOrders = stopTakeOrders
                        .Where(o => o.SimulationOrderId == simulationOrderId && o.Status == "WAITING")
                        .ToList();

                    foreach (var stopTakeOrder in waitingStopTakeOrders)
                    {
                        try
                        {
                            await _databaseService.CancelStopTakeOrderAsync(stopTakeOrder.Id);
                            cancelledCount++;
                            LogManager.Log("StopLossMonitorService", 
                                $"取消止损止盈单成功 - ID: {stopTakeOrder.Id}, 类型: {stopTakeOrder.OrderType}");
                            _logger?.LogInformation("取消止损止盈单成功 - ID: {id}, 类型: {type}", 
                                stopTakeOrder.Id, stopTakeOrder.OrderType);
                        }
                        catch (Exception ex)
                        {
                            LogManager.LogException("StopLossMonitorService", ex, $"取消止损止盈单失败 - ID: {stopTakeOrder.Id}");
                            _logger?.LogError(ex, "取消止损止盈单失败 - ID: {id}", stopTakeOrder.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogManager.LogException("StopLossMonitorService", ex, "查询或取消止损止盈单时发生错误");
                    _logger?.LogError(ex, "查询或取消止损止盈单时发生错误");
                }

                LogManager.Log("StopLossMonitorService", 
                    $"订单 {simulationOrderId} 相关委托单取消完成 - 共取消 {cancelledCount} 个委托单");
                _logger?.LogInformation("订单 {orderId} 相关委托单取消完成 - 共取消 {count} 个委托单", 
                    simulationOrderId, cancelledCount);
            }
            catch (Exception ex)
            {
                LogManager.LogException("StopLossMonitorService", ex, $"取消订单 {simulationOrderId} 相关委托单时发生错误");
                _logger?.LogError(ex, "取消订单 {orderId} 相关委托单时发生错误", simulationOrderId);
            }
        }

        /// <summary>
        /// 检查并更新推仓状态
        /// </summary>
        private async Task CheckAndUpdatePushStatusAsync(long accountId, string contract)
        {
            try
            {
                // 获取该合约的推仓信息
                var pushSummary = await _databaseService.GetPushSummaryInfoAsync(accountId, contract);
                if (pushSummary != null)
                {
                    // 检查是否所有订单都已平仓
                    var openOrdersCount = pushSummary.Orders?.Count(o => o.Status.ToLower() == "open") ?? 0;
                    
                    if (openOrdersCount == 0)
                    {
                        // 所有订单都已平仓，更新推仓状态为已完结
                        await _databaseService.UpdatePushInfoStatusAsync(pushSummary.PushId, "closed", DateTime.Now);
                        
                        LogManager.Log("StopLossMonitorService", $"推仓 {pushSummary.PushId} 所有订单已平仓，状态更新为已完结");
                        _logger?.LogInformation("推仓 {pushId} 所有订单已平仓，状态更新为已完结", pushSummary.PushId);
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("StopLossMonitorService", ex, "检查推仓状态时发生错误");
                _logger?.LogError(ex, "检查推仓状态时发生错误");
            }
        }

        /// <summary>
        /// 检查并更新所有推仓状态
        /// </summary>
        private async Task CheckAllPushStatusAsync()
        {
            try
            {
                // 获取所有合约的推仓信息
                var pushSummaries = await _databaseService.GetAllPushSummaryInfosAsync();
                
                foreach (var pushSummary in pushSummaries)
                {
                    // 检查是否所有订单都已平仓
                    var openOrdersCount = pushSummary.Orders?.Count(o => o.Status.ToLower() == "open") ?? 0;
                    
                    if (openOrdersCount == 0)
                    {
                        // 所有订单都已平仓，更新推仓状态为已完结
                        await _databaseService.UpdatePushInfoStatusAsync(pushSummary.PushId, "closed", DateTime.Now);
                        
                        LogManager.Log("StopLossMonitorService", $"推仓 {pushSummary.PushId} 所有订单已平仓，状态更新为已完结");
                        _logger?.LogInformation("推仓 {pushId} 所有订单已平仓，状态更新为已完结", pushSummary.PushId);
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("StopLossMonitorService", ex, "检查所有推仓状态时发生错误");
                _logger?.LogError(ex, "检查所有推仓状态时发生错误");
            }
        }

        public void Dispose()
        {
            try
            {
                Stop();
            }
            catch (Exception ex)
            {
                LogManager.LogException("StopLossMonitorService", ex, "Dispose时停止服务失败");
                _logger?.LogError(ex, "Dispose时停止服务失败");
            }
            finally
            {
                try
                {
                    _cts?.Dispose();
                }
                catch (Exception ex)
                {
                    LogManager.LogException("StopLossMonitorService", ex, "Dispose时释放CancellationTokenSource失败");
                    _logger?.LogError(ex, "Dispose时释放CancellationTokenSource失败");
                }
            }
        }
    }
} 