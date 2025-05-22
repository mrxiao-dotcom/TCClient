using TCClient.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Extensions.Logging;
using TCClient.Services;

namespace TCClient.Services
{
    public class ExchangeServiceFactory : IExchangeServiceFactory
    {
        private readonly ILoggerFactory _loggerFactory;
        private static readonly List<IExchangeService> _services = new List<IExchangeService>();
        private static readonly string _logFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "TCClient_Factory.log");

        public ExchangeServiceFactory(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
        }

        public IExchangeService CreateExchangeService(TradingAccount account)
        {
            LogToFile($"创建交易服务实例: 账户={account?.AccountName ?? "默认账户"}");
            
            var logger = _loggerFactory.CreateLogger<BinanceExchangeService>();
            var service = new BinanceExchangeService(logger, account?.ApiKey, account?.ApiSecret);
            
            // 跟踪所有创建的服务实例，以便在关闭时释放
            lock (_services)
            {
                _services.Add(service);
                LogToFile($"交易服务实例已创建并跟踪，当前实例数: {_services.Count}");
            }
            
            return service;
        }

        private static void LogToFile(string message)
        {
            try
            {
                var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
                File.AppendAllText(_logFilePath, logMessage + Environment.NewLine);
            }
            catch
            {
                // 忽略日志写入失败
            }
        }

        public void Dispose()
        {
            lock (_services)
            {
                foreach (var service in _services)
                {
                    try
                    {
                        if (service is IDisposable disposable)
                        {
                            disposable.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        LogToFile($"释放服务实例时出错: {ex.Message}");
                    }
                }
                _services.Clear();
                LogToFile("所有服务实例已释放");
            }
        }
    }

    // 模拟的交易服务实现
    public class MockExchangeService : IExchangeService
    {
        private readonly Random _random = new Random();
        private decimal _lastPrice = 100m;

        public async Task<List<KLineData>> GetKLineDataAsync(string symbol, string interval, int limit)
        {
            await Task.Delay(100); // 模拟网络延迟
            var result = new List<KLineData>();
            var now = DateTime.Now;
            var baseTime = now.AddMinutes(-limit * GetIntervalMinutes(interval));

            for (int i = 0; i < limit; i++)
            {
                var time = baseTime.AddMinutes(i * GetIntervalMinutes(interval));
                var open = _lastPrice;
                var high = open * (1 + (decimal)(_random.NextDouble() * 0.02));
                var low = open * (1 - (decimal)(_random.NextDouble() * 0.02));
                var close = (high + low) / 2;
                _lastPrice = close;

                result.Add(new KLineData
                {
                    Time = time,
                    Open = (double)open,
                    High = (double)high,
                    Low = (double)low,
                    Close = (double)close,
                    Volume = (double)(_random.NextDouble() * 1000)
                });
            }

            return result;
        }

        public async Task<AccountInfo> GetAccountInfoAsync()
        {
            await Task.Delay(100); // 模拟网络延迟
            return new AccountInfo
            {
                TotalEquity = 10000m,
                AvailableBalance = 8000m,
                PositionMargin = 2000m,
                UnrealizedPnL = 500m,
                MarginBalance = 9500m,
                MaintenanceMargin = 100m,
                InitialMargin = 2000m,
                WalletBalance = 9000m,
                UpdateTime = DateTime.Now
            };
        }

        public async Task<bool> PlaceOrderAsync(string symbol, string direction, decimal quantity, decimal price, decimal stopLossPrice, decimal leverage)
        {
            await Task.Delay(100); // 模拟网络延迟
            TCClient.Utils.AppSession.Log($"[模拟] 下单成功 - 合约: {symbol}, 方向: {direction}, 数量: {quantity}, 价格: {price}, 止损: {stopLossPrice}, 杠杆: {leverage}");
            return true;
        }

        public async Task<bool> CancelOrderAsync(string orderId)
        {
            await Task.Delay(100); // 模拟网络延迟
            TCClient.Utils.AppSession.Log($"[模拟] 取消订单成功 - 订单ID: {orderId}");
            return true;
        }

        public async Task<TickerInfo> GetTickerAsync(string symbol)
        {
            await Task.Delay(100); // 模拟网络延迟
            _lastPrice = _lastPrice * (1 + (decimal)((_random.NextDouble() - 0.5) * 0.01));
            return new TickerInfo
            {
                Symbol = symbol,
                LastPrice = _lastPrice,
                BidPrice = _lastPrice * 0.999m,
                AskPrice = _lastPrice * 1.001m,
                Volume = (decimal)(_random.NextDouble() * 1000),
                Timestamp = DateTime.Now,
                PriceChangePercent = (decimal)((_random.NextDouble() - 0.5) * 2) // 模拟-1%到1%的价格变化
            };
        }

        public async Task<List<TickerInfo>> GetAllTickersAsync()
        {
            await Task.Delay(100); // 模拟网络延迟
            
            // 模拟一些常见的合约
            var symbols = new[] { "BTC", "ETH", "BNB", "SOL", "XRP", "DOGE", "ADA", "DOT", "AVAX", "MATIC" };
            var tickers = new List<TickerInfo>();
            
            foreach (var symbol in symbols)
            {
                var fullSymbol = $"{symbol}USDT";
                var basePrice = symbol switch
                {
                    "BTC" => 50000m,
                    "ETH" => 3000m,
                    "BNB" => 400m,
                    "SOL" => 100m,
                    "XRP" => 0.5m,
                    "DOGE" => 0.1m,
                    "ADA" => 0.5m,
                    "DOT" => 7m,
                    "AVAX" => 30m,
                    "MATIC" => 1m,
                    _ => 100m
                };
                
                // 为每个合约生成一个随机价格波动
                var priceChange = (decimal)((_random.NextDouble() - 0.5) * 0.02); // -1% 到 1%
                var currentPrice = basePrice * (1 + priceChange);
                
                tickers.Add(new TickerInfo
                {
                    Symbol = fullSymbol,
                    LastPrice = currentPrice,
                    BidPrice = currentPrice * 0.999m,
                    AskPrice = currentPrice * 1.001m,
                    Volume = (decimal)(_random.NextDouble() * 1000000),
                    Timestamp = DateTime.Now,
                    PriceChangePercent = priceChange * 100 // 转换为百分比
                });
            }
            
            TCClient.Utils.AppSession.Log($"[模拟] 获取所有合约行情数据成功 - 共 {tickers.Count} 个合约");
            return tickers;
        }

        private int GetIntervalMinutes(string interval)
        {
            return interval switch
            {
                "1m" => 1,
                "5m" => 5,
                "15m" => 15,
                "30m" => 30,
                "1h" => 60,
                "4h" => 240,
                "1d" => 1440,
                "1w" => 10080,
                _ => 1
            };
        }

        public void Dispose()
        {
            // 无需清理资源
        }
    }
} 