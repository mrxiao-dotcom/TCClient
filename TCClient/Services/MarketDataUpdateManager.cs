using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Timer = System.Timers.Timer;
using System.Timers;
using System.Linq;
using TCClient.ViewModels;

namespace TCClient.Services
{
    public class MarketDataUpdateManager : IDisposable
    {
        private readonly BinanceApiService _binanceApi;
        private Timer _tickTimer;           // 5秒更新Tick
        private Timer _kline5mTimer;        // 5分钟更新5分钟K线
        private Timer _kline15mTimer;       // 15分钟更新15分钟K线
        private Timer _kline1hTimer;        // 1小时更新1小时K线
        private Timer _kline1dTimer;        // 1天更新日线K线
        
        private readonly Dictionary<string, List<BinanceKLineData>> _klineCache;
        private readonly object _cacheLock = new object();
        
        public event Action<string, decimal> PriceUpdated;
        public event Action<string, TickerData> TickerUpdated;
        public event Action<string, string, List<BinanceKLineData>> KLineUpdated;

        public MarketDataUpdateManager(string apiKey = "", string secretKey = "")
        {
            _binanceApi = new BinanceApiService(apiKey, secretKey);
            _klineCache = new Dictionary<string, List<BinanceKLineData>>();
            
            // 初始化定时器
            InitializeTimers();
        }

        private void InitializeTimers()
        {
            // Tick数据更新 - 每5秒
            _tickTimer = new Timer(5000);
            _tickTimer.Elapsed += OnTickTimerElapsed;
            
            // 5分钟K线更新 - 每5分钟
            _kline5mTimer = new Timer(5 * 60 * 1000);
            _kline5mTimer.Elapsed += OnKLine5mTimerElapsed;
            
            // 15分钟K线更新 - 每15分钟
            _kline15mTimer = new Timer(15 * 60 * 1000);
            _kline15mTimer.Elapsed += OnKLine15mTimerElapsed;
            
            // 1小时K线更新 - 每小时
            _kline1hTimer = new Timer(60 * 60 * 1000);
            _kline1hTimer.Elapsed += OnKLine1hTimerElapsed;
            
            // 日线K线更新 - 每天
            _kline1dTimer = new Timer(24 * 60 * 60 * 1000);
            _kline1dTimer.Elapsed += OnKLine1dTimerElapsed;
        }

        public void StartMonitoring(List<string> symbols)
        {
            if (symbols == null || !symbols.Any())
                return;

            MonitoredSymbols = symbols;
            
            // 启动所有定时器
            _tickTimer.Start();
            _kline5mTimer.Start();
            _kline15mTimer.Start();
            _kline1hTimer.Start();
            _kline1dTimer.Start();
            
            // 立即获取一次数据
            _ = Task.Run(async () =>
            {
                await UpdateAllTickData();
                await UpdateAllKLineData();
            });
        }

        public void StopMonitoring()
        {
            _tickTimer?.Stop();
            _kline5mTimer?.Stop();
            _kline15mTimer?.Stop();
            _kline1hTimer?.Stop();
            _kline1dTimer?.Stop();
        }

        public List<string> MonitoredSymbols { get; private set; } = new List<string>();

        private async void OnTickTimerElapsed(object sender, ElapsedEventArgs e)
        {
            await UpdateAllTickData();
        }

        private async void OnKLine5mTimerElapsed(object sender, ElapsedEventArgs e)
        {
            await UpdateKLineData("5m");
        }

        private async void OnKLine15mTimerElapsed(object sender, ElapsedEventArgs e)
        {
            await UpdateKLineData("15m");
        }

        private async void OnKLine1hTimerElapsed(object sender, ElapsedEventArgs e)
        {
            await UpdateKLineData("1h");
        }

        private async void OnKLine1dTimerElapsed(object sender, ElapsedEventArgs e)
        {
            await UpdateKLineData("1d");
        }

        private async Task UpdateAllTickData()
        {
            try
            {
                foreach (var symbol in MonitoredSymbols)
                {
                    // 获取当前价格
                    var currentPrice = await _binanceApi.GetCurrentPriceAsync(symbol);
                    if (currentPrice > 0)
                    {
                        PriceUpdated?.Invoke(symbol, currentPrice);
                    }

                    // 获取24小时统计数据
                    var tickerData = await _binanceApi.GetTickerDataAsync(symbol);
                    if (tickerData != null)
                    {
                        TickerUpdated?.Invoke(symbol, tickerData);
                    }

                    // 避免请求过于频繁
                    await Task.Delay(100);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"更新Tick数据失败: {ex.Message}");
            }
        }

        private async Task UpdateKLineData(string interval)
        {
            try
            {
                foreach (var symbol in MonitoredSymbols)
                {
                    var klineData = await _binanceApi.GetKLineDataAsync(symbol, interval, 100);
                    if (klineData.Any())
                    {
                        // 缓存K线数据
                        var cacheKey = $"{symbol}_{interval}";
                        lock (_cacheLock)
                        {
                            _klineCache[cacheKey] = klineData;
                        }

                        // 触发K线更新事件
                        KLineUpdated?.Invoke(symbol, interval, klineData);
                    }

                    // 避免请求过于频繁
                    await Task.Delay(200);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"更新{interval}K线数据失败: {ex.Message}");
            }
        }

        private async Task UpdateAllKLineData()
        {
            await UpdateKLineData("5m");
            await Task.Delay(1000);
            await UpdateKLineData("15m");
            await Task.Delay(1000);
            await UpdateKLineData("1h");
            await Task.Delay(1000);
            await UpdateKLineData("1d");
        }

        /// <summary>
        /// 获取缓存的K线数据
        /// </summary>
        /// <param name="symbol">交易对</param>
        /// <param name="interval">时间间隔</param>
        /// <returns>K线数据</returns>
        public List<BinanceKLineData> GetCachedKLineData(string symbol, string interval)
        {
            var cacheKey = $"{symbol}_{interval}";
            lock (_cacheLock)
            {
                return _klineCache.TryGetValue(cacheKey, out var data) ? data : new List<BinanceKLineData>();
            }
        }

        /// <summary>
        /// 添加监控的交易对
        /// </summary>
        /// <param name="symbol">交易对</param>
        public void AddSymbol(string symbol)
        {
            if (!MonitoredSymbols.Contains(symbol))
            {
                MonitoredSymbols.Add(symbol);
                
                System.Diagnostics.Debug.WriteLine($"添加监控合约: {symbol}");
                
                // 立即获取该交易对的数据
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // 首先获取5分钟K线数据（最重要，用于回撤计算）
                        System.Diagnostics.Debug.WriteLine($"开始获取 {symbol} 的5分钟K线数据...");
                        var klineData = await _binanceApi.GetKLineDataAsync(symbol, "5m", 100);
                        if (klineData.Any())
                        {
                            // 缓存K线数据
                            var cacheKey = $"{symbol}_5m";
                            lock (_cacheLock)
                            {
                                _klineCache[cacheKey] = klineData;
                            }
                            
                            System.Diagnostics.Debug.WriteLine($"成功获取 {symbol} 的5分钟K线数据: {klineData.Count} 根");
                            
                            // 触发K线更新事件
                            KLineUpdated?.Invoke(symbol, "5m", klineData);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"警告: {symbol} 的5分钟K线数据为空");
                        }

                        await Task.Delay(300);

                        // 获取Tick数据
                        System.Diagnostics.Debug.WriteLine($"开始获取 {symbol} 的价格数据...");
                        var currentPrice = await _binanceApi.GetCurrentPriceAsync(symbol);
                        if (currentPrice > 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"获取 {symbol} 当前价格: {currentPrice}");
                            PriceUpdated?.Invoke(symbol, currentPrice);
                        }

                        await Task.Delay(300);

                        var tickerData = await _binanceApi.GetTickerDataAsync(symbol);
                        if (tickerData != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"获取 {symbol} Ticker数据成功");
                            TickerUpdated?.Invoke(symbol, tickerData);
                        }

                        // 获取其他时间周期的K线数据
                        await Task.Delay(500);
                        await UpdateSingleSymbolKLineData(symbol, "15m");
                        await Task.Delay(500);
                        await UpdateSingleSymbolKLineData(symbol, "1h");
                        await Task.Delay(500);
                        await UpdateSingleSymbolKLineData(symbol, "1d");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"获取 {symbol} 数据失败: {ex.Message}");
                    }
                });
            }
        }

        /// <summary>
        /// 更新单个交易对的K线数据
        /// </summary>
        /// <param name="symbol">交易对</param>
        /// <param name="interval">时间间隔</param>
        private async Task UpdateSingleSymbolKLineData(string symbol, string interval)
        {
            try
            {
                var klineData = await _binanceApi.GetKLineDataAsync(symbol, interval, 100);
                if (klineData.Any())
                {
                    // 缓存K线数据
                    var cacheKey = $"{symbol}_{interval}";
                    lock (_cacheLock)
                    {
                        _klineCache[cacheKey] = klineData;
                    }

                    // 触发K线更新事件
                    KLineUpdated?.Invoke(symbol, interval, klineData);
                    
                    System.Diagnostics.Debug.WriteLine($"更新 {symbol} {interval} K线数据: {klineData.Count} 根");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"更新 {symbol} {interval} K线数据失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 移除监控的交易对
        /// </summary>
        /// <param name="symbol">交易对</param>
        public void RemoveSymbol(string symbol)
        {
            MonitoredSymbols.Remove(symbol);
            
            // 清除缓存
            lock (_cacheLock)
            {
                var keysToRemove = _klineCache.Keys.Where(k => k.StartsWith($"{symbol}_")).ToList();
                foreach (var key in keysToRemove)
                {
                    _klineCache.Remove(key);
                }
            }
        }

        /// <summary>
        /// 测试API连接
        /// </summary>
        /// <returns>是否连接成功</returns>
        public async Task<bool> TestConnectionAsync()
        {
            return await _binanceApi.TestConnectivityAsync();
        }

        /// <summary>
        /// 计算做多回撤数据（基于5分钟K线数据）
        /// </summary>
        /// <param name="symbol">交易对</param>
        /// <param name="currentPrice">当前价格</param>
        /// <returns>回撤信息</returns>
        public DrawdownInfo CalculateLongDrawdown(string symbol, decimal currentPrice)
        {
            var klineData = GetCachedKLineData(symbol, "5m");
            if (!klineData.Any())
            {
                System.Diagnostics.Debug.WriteLine($"警告: {symbol} 没有5分钟K线数据，无法计算回撤");
                return new DrawdownInfo();
            }

            System.Diagnostics.Debug.WriteLine($"计算 {symbol} 做多回撤，使用 {klineData.Count} 根5分钟K线数据");

            // 按时间排序，确保数据顺序正确
            var sortedKlines = klineData.OrderBy(k => k.OpenTime).ToList();
            
            // 找到最高价和对应的K线
            var highestCandle = sortedKlines.OrderByDescending(k => k.High).First();
            var highPrice = (double)highestCandle.High;
            var highTime = highestCandle.OpenTime;

            System.Diagnostics.Debug.WriteLine($"{symbol} 最高价: {highPrice:F4} 时间: {highTime:yyyy-MM-dd HH:mm:ss}");

            // 从最高价时间点之后，找到最低价（这是最大回撤点）
            var candlesAfterHigh = sortedKlines.Where(k => k.OpenTime > highTime).ToList();
            
            double maxDrawdown = 0;
            double lowestAfterHigh = highPrice;
            DateTime maxDrawdownTime = highTime;
            
            if (candlesAfterHigh.Any())
            {
                var lowestCandle = candlesAfterHigh.OrderBy(k => k.Low).First();
                lowestAfterHigh = (double)lowestCandle.Low;
                maxDrawdownTime = lowestCandle.OpenTime;
                
                // 计算最大回撤百分比（负数表示下跌）
                maxDrawdown = (lowestAfterHigh - highPrice) / highPrice * 100;
                
                System.Diagnostics.Debug.WriteLine($"{symbol} 最高价后最低价: {lowestAfterHigh:F4} 时间: {maxDrawdownTime:yyyy-MM-dd HH:mm:ss}");
                System.Diagnostics.Debug.WriteLine($"{symbol} 最大回撤: {maxDrawdown:F2}%");
            }

            // 计算当前回撤（从最高价到当前价格）
            var currentDrawdown = ((double)currentPrice - highPrice) / highPrice * 100;
            
            // 计算时间（分钟）
            var maxDrawdownMinutes = (int)(maxDrawdownTime - highTime).TotalMinutes;
            var currentDrawdownMinutes = (int)(DateTime.Now - highTime).TotalMinutes;

            System.Diagnostics.Debug.WriteLine($"{symbol} 当前价格: {currentPrice:F4}, 当前回撤: {currentDrawdown:F2}%");
            System.Diagnostics.Debug.WriteLine($"{symbol} 最大回撤时间: {maxDrawdownMinutes}分钟, 当前回撤时间: {currentDrawdownMinutes}分钟");

            return new DrawdownInfo
            {
                RecentHighPrice = highPrice,
                HighPriceTime = highTime,
                MaxDrawdown = Math.Abs(maxDrawdown), // 显示为正数
                CurrentDrawdown = Math.Abs(currentDrawdown), // 显示为正数
                MaxDrawdownMinutes = maxDrawdownMinutes,
                CurrentDrawdownMinutes = currentDrawdownMinutes
            };
        }

        /// <summary>
        /// 计算做空回撤数据（基于5分钟K线数据）
        /// </summary>
        /// <param name="symbol">交易对</param>
        /// <param name="currentPrice">当前价格</param>
        /// <returns>回撤信息</returns>
        public DrawdownInfo CalculateShortDrawdown(string symbol, decimal currentPrice)
        {
            var klineData = GetCachedKLineData(symbol, "5m");
            if (!klineData.Any())
            {
                System.Diagnostics.Debug.WriteLine($"警告: {symbol} 没有5分钟K线数据，无法计算回撤");
                return new DrawdownInfo();
            }

            System.Diagnostics.Debug.WriteLine($"计算 {symbol} 做空回撤，使用 {klineData.Count} 根5分钟K线数据");

            // 按时间排序，确保数据顺序正确
            var sortedKlines = klineData.OrderBy(k => k.OpenTime).ToList();
            
            // 找到最低价和对应的K线
            var lowestCandle = sortedKlines.OrderBy(k => k.Low).First();
            var lowPrice = (double)lowestCandle.Low;
            var lowTime = lowestCandle.OpenTime;

            System.Diagnostics.Debug.WriteLine($"{symbol} 最低价: {lowPrice:F4} 时间: {lowTime:yyyy-MM-dd HH:mm:ss}");

            // 从最低价时间点之后，找到最高价（这是最大回撤点）
            var candlesAfterLow = sortedKlines.Where(k => k.OpenTime > lowTime).ToList();
            
            double maxDrawdown = 0;
            double highestAfterLow = lowPrice;
            DateTime maxDrawdownTime = lowTime;
            
            if (candlesAfterLow.Any())
            {
                var highestCandle = candlesAfterLow.OrderByDescending(k => k.High).First();
                highestAfterLow = (double)highestCandle.High;
                maxDrawdownTime = highestCandle.OpenTime;
                
                // 计算最大回撤百分比（对于做空，价格上涨是回撤）
                maxDrawdown = (highestAfterLow - lowPrice) / lowPrice * 100;
                
                System.Diagnostics.Debug.WriteLine($"{symbol} 最低价后最高价: {highestAfterLow:F4} 时间: {maxDrawdownTime:yyyy-MM-dd HH:mm:ss}");
                System.Diagnostics.Debug.WriteLine($"{symbol} 最大回撤: {maxDrawdown:F2}%");
            }

            // 计算当前回撤（从最低价到当前价格，对于做空，价格上涨是回撤）
            var currentDrawdown = ((double)currentPrice - lowPrice) / lowPrice * 100;
            
            // 计算时间（分钟）
            var maxDrawdownMinutes = (int)(maxDrawdownTime - lowTime).TotalMinutes;
            var currentDrawdownMinutes = (int)(DateTime.Now - lowTime).TotalMinutes;

            System.Diagnostics.Debug.WriteLine($"{symbol} 当前价格: {currentPrice:F4}, 当前回撤: {currentDrawdown:F2}%");
            System.Diagnostics.Debug.WriteLine($"{symbol} 最大回撤时间: {maxDrawdownMinutes}分钟, 当前回撤时间: {currentDrawdownMinutes}分钟");

            return new DrawdownInfo
            {
                RecentLowPrice = lowPrice,
                LowPriceTime = lowTime,
                MaxDrawdown = Math.Abs(maxDrawdown), // 显示为正数
                CurrentDrawdown = Math.Abs(currentDrawdown), // 显示为正数
                MaxDrawdownMinutes = maxDrawdownMinutes,
                CurrentDrawdownMinutes = currentDrawdownMinutes
            };
        }

        public void Dispose()
        {
            StopMonitoring();
            
            _tickTimer?.Dispose();
            _kline5mTimer?.Dispose();
            _kline15mTimer?.Dispose();
            _kline1hTimer?.Dispose();
            _kline1dTimer?.Dispose();
            
            _binanceApi?.Dispose();
        }
    }

    // 回撤信息模型
    public class DrawdownInfo
    {
        public double RecentHighPrice { get; set; }
        public double RecentLowPrice { get; set; }
        public DateTime HighPriceTime { get; set; }
        public DateTime LowPriceTime { get; set; }
        public double MaxDrawdown { get; set; }
        public double CurrentDrawdown { get; set; }
        public int MaxDrawdownMinutes { get; set; }
        public int CurrentDrawdownMinutes { get; set; }
    }
} 