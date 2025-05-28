using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.IO;
using Newtonsoft.Json;
using TCClient.Services;
using TCClient.Models;
using Microsoft.Extensions.DependencyInjection;

namespace TCClient.Views
{
    /// <summary>
    /// 寻找机会窗口
    /// </summary>
    public partial class FindOpportunityWindow : Window
    {
        private readonly IExchangeService _exchangeService;
        private readonly IDatabaseService _databaseService;
        private readonly FavoriteContractsService _favoriteContractsService;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isAnalyzing = false;

        // 数据集合
        private ObservableCollection<MarketRankingItem> _topGainers = new ObservableCollection<MarketRankingItem>();
        private ObservableCollection<MarketRankingItem> _topLosers = new ObservableCollection<MarketRankingItem>();
        private ObservableCollection<BreakoutItem> _break5DayHigh = new ObservableCollection<BreakoutItem>();
        private ObservableCollection<BreakoutItem> _break10DayHigh = new ObservableCollection<BreakoutItem>();
        private ObservableCollection<BreakoutItem> _break20DayHigh = new ObservableCollection<BreakoutItem>();
        private ObservableCollection<BreakoutItem> _break5DayLow = new ObservableCollection<BreakoutItem>();
        private ObservableCollection<BreakoutItem> _break10DayLow = new ObservableCollection<BreakoutItem>();
        private ObservableCollection<BreakoutItem> _break20DayLow = new ObservableCollection<BreakoutItem>();

        // 历史价格缓存
        private Dictionary<string, HistoricalPriceData> _historicalDataCache = new Dictionary<string, HistoricalPriceData>();
        
        // 可交易合约缓存
        private static HashSet<string> _tradableSymbolsCache = null;
        private static DateTime _tradableSymbolsCacheTime = DateTime.MinValue;
        private const int TRADABLE_SYMBOLS_CACHE_HOURS = 24; // 缓存24小时

        // 倒计时相关
        private DispatcherTimer _countdownTimer;
        private DateTime _nextUpdateTime;
        private int _updateIntervalSeconds = 30; // 默认30秒更新一次数据，可通过UI修改

        // 本地文件缓存相关
        private static readonly string CacheDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cache");
        private const string CACHE_FILE_SUFFIX = "NDAYRANGE.json";

        public FindOpportunityWindow()
        {
            InitializeComponent();
            
            // 获取服务
            var app = Application.Current as App;
            _exchangeService = app?.Services?.GetService<IExchangeService>();
            _databaseService = app?.Services?.GetService<IDatabaseService>();
            _favoriteContractsService = app?.Services?.GetService<FavoriteContractsService>();

            // 绑定数据源
            TopGainersDataGrid.ItemsSource = _topGainers;
            TopLosersDataGrid.ItemsSource = _topLosers;
            Break5DayHighDataGrid.ItemsSource = _break5DayHigh;
            Break10DayHighDataGrid.ItemsSource = _break10DayHigh;
            Break20DayHighDataGrid.ItemsSource = _break20DayHigh;
            Break5DayLowDataGrid.ItemsSource = _break5DayLow;
            Break10DayLowDataGrid.ItemsSource = _break10DayLow;
            Break20DayLowDataGrid.ItemsSource = _break20DayLow;

            // 初始化倒计时器
            InitializeCountdownTimer();
            
            // 初始化右键菜单
            InitializeContextMenus();
            
            // 清理过期的缓存文件
            CleanupOldCacheFiles();
            
            // 窗口加载完成后自动开始涨跌幅排行的定期更新
            Loaded += async (sender, e) =>
            {
                try
                {
                    // 立即加载一次涨跌幅排行数据
                    await LoadInitialMarketRankings();
                    
                    // 启动定期更新倒计时
                    StartMarketDataCountdown();
                }
                catch (Exception ex)
                {
                    Utils.AppSession.Log($"初始化市场数据失败: {ex.Message}");
                }
            };
        }

        /// <summary>
        /// 启动分析按钮点击事件
        /// </summary>
        private async void StartAnalysisButton_Click(object sender, RoutedEventArgs e)
        {
            // 检查是否是自动触发（sender为null）
            bool isAutoTrigger = sender == null;
            
            if (_isAnalyzing && !isAutoTrigger) return; // 如果是手动点击且正在分析，则返回

            try
            {
                if (!isAutoTrigger)
                {
                    // 只有手动点击时才设置分析状态和UI
                    _isAnalyzing = true;
                    _cancellationTokenSource = new CancellationTokenSource();
                    
                    // 更新UI状态
                    StartAnalysisButton.IsEnabled = false;
                    StopAnalysisButton.IsEnabled = true;
                    FindBreakoutButton.IsEnabled = false;
                    
                    ProgressText.Text = "正在获取市场数据...";
                    AnalysisProgressBar.Value = 0;
                }
                else
                {
                    // 自动触发时，只更新进度文本
                    ProgressText.Text = "自动更新市场数据...";
                }

                var cancellationToken = isAutoTrigger ? CancellationToken.None : _cancellationTokenSource.Token;
                await AnalyzeMarketRankings(cancellationToken);
                
                if (!isAutoTrigger && !_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    FindBreakoutButton.IsEnabled = true;
                    ProgressText.Text = "涨跌幅分析完成，可以开始寻找突破";
                    AnalysisProgressBar.Value = 100;
                    
                    // 启动自动更新倒计时
                    StartCountdown();
                }
                else if (isAutoTrigger)
                {
                    // 自动触发时，只更新进度文本
                    ProgressText.Text = $"数据已更新 - {DateTime.Now:HH:mm:ss}";
                }
            }
            catch (OperationCanceledException)
            {
                if (!isAutoTrigger)
                {
                    ProgressText.Text = "分析已取消";
                }
            }
            catch (Exception ex)
            {
                if (!isAutoTrigger)
                {
                    MessageBox.Show($"分析失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    ProgressText.Text = "分析失败";
                }
                else
                {
                    ProgressText.Text = $"自动更新失败: {ex.Message}";
                    Utils.AppSession.Log($"自动更新失败: {ex.Message}");
                }
            }
            finally
            {
                if (!isAutoTrigger)
                {
                    _isAnalyzing = false;
                    StartAnalysisButton.IsEnabled = true;
                    StopAnalysisButton.IsEnabled = false;
                    
                    // 注意：不要停止倒计时，让涨跌幅排行继续自动更新
                    // 即使分析失败或被取消，市场数据的自动更新也应该继续
                    // if (_cancellationTokenSource?.Token.IsCancellationRequested == true)
                    // {
                    //     StopCountdown();
                    // }
                }
            }
        }

        /// <summary>
        /// 找寻突破按钮点击事件
        /// </summary>
        private async void FindBreakoutButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isAnalyzing) return;

            try
            {
                _isAnalyzing = true;
                _cancellationTokenSource = new CancellationTokenSource();
                
                // 更新UI状态
                FindBreakoutButton.IsEnabled = false;
                StopAnalysisButton.IsEnabled = true;
                
                ProgressText.Text = "正在分析突破情况...";
                AnalysisProgressBar.Value = 0;

                await AnalyzeBreakouts(_cancellationTokenSource.Token);
                
                if (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    ProgressText.Text = "突破分析完成";
                    AnalysisProgressBar.Value = 100;
                }
            }
            catch (OperationCanceledException)
            {
                ProgressText.Text = "突破分析已取消";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"突破分析失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                ProgressText.Text = "突破分析失败";
            }
            finally
            {
                _isAnalyzing = false;
                FindBreakoutButton.IsEnabled = true;
                StopAnalysisButton.IsEnabled = false;
            }
        }

        /// <summary>
        /// 停止分析按钮点击事件
        /// </summary>
        private void StopAnalysisButton_Click(object sender, RoutedEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            ProgressText.Text = "正在停止分析...";
            // 注意：不要停止倒计时，让涨跌幅排行继续自动更新
            // StopCountdown(); // 移除这行，保持市场数据的自动更新
        }

        /// <summary>
        /// 获取可交易合约列表（带缓存）
        /// </summary>
        private async Task<HashSet<string>> GetTradableSymbolsAsync(CancellationToken cancellationToken)
        {
            // 检查缓存是否有效
            var now = DateTime.Now;
            if (_tradableSymbolsCache != null && 
                (now - _tradableSymbolsCacheTime).TotalHours < TRADABLE_SYMBOLS_CACHE_HOURS)
            {
                Utils.AppSession.Log($"使用缓存的可交易合约数据，共 {_tradableSymbolsCache.Count} 个合约");
                return _tradableSymbolsCache;
            }

            // 获取可交易合约
            Utils.AppSession.Log("正在获取可交易合约列表...");
            var tradableSymbols = await _exchangeService.GetTradableSymbolsAsync();
            
            if (tradableSymbols == null || !tradableSymbols.Any())
            {
                Utils.AppSession.Log("警告：无法获取可交易合约列表，使用空列表");
                return new HashSet<string>();
            }

            // 更新缓存
            _tradableSymbolsCache = new HashSet<string>(tradableSymbols, StringComparer.OrdinalIgnoreCase);
            _tradableSymbolsCacheTime = now;
            
            Utils.AppSession.Log($"成功获取并缓存 {_tradableSymbolsCache.Count} 个可交易合约");
            return _tradableSymbolsCache;
        }

        /// <summary>
        /// 分析市场涨跌幅排行
        /// </summary>
        private async Task AnalyzeMarketRankings(CancellationToken cancellationToken)
        {
            if (_exchangeService == null)
            {
                throw new InvalidOperationException("交易所服务未初始化");
            }

            // 清空现有数据
            Dispatcher.Invoke(() =>
            {
                _topGainers.Clear();
                _topLosers.Clear();
            });

            // 获取可交易合约列表
            var tradableSymbols = await GetTradableSymbolsAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // 获取所有交易对的24小时统计数据
            var tickers = await _exchangeService.GetAllTickersAsync();
            if (tickers == null || !tickers.Any())
            {
                throw new InvalidOperationException("无法获取市场数据");
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 过滤USDT交易对并计算涨跌幅，只包含可交易的合约
            var usdtPairs = tickers
                .Where(t => t.Symbol.EndsWith("USDT") && 
                           t.PriceChangePercent != 0 && 
                           tradableSymbols.Contains(t.Symbol)) // 只包含可交易的合约
                .Select((t, index) => new MarketRankingItem
                {
                    Rank = index + 1,
                    Symbol = t.Symbol.Replace("USDT", ""), // 移除USDT后缀显示
                    CurrentPrice = t.LastPrice,
                    ChangePercent = t.PriceChangePercent / 100m, // 转换为小数
                    Volume24h = t.Volume, // 24小时成交量（币量）
                    QuoteVolume24h = t.QuoteVolume // 24小时成交额（USDT）
                })
                .ToList();

            Utils.AppSession.Log($"过滤后的可交易USDT合约数量: {usdtPairs.Count}");

            cancellationToken.ThrowIfCancellationRequested();

            // 按涨幅排序，取前10
            var topGainers = usdtPairs
                .Where(p => p.ChangePercent > 0)
                .OrderByDescending(p => p.ChangePercent)
                .Take(10)
                .ToList();

            // 按跌幅排序，取前10
            var topLosers = usdtPairs
                .Where(p => p.ChangePercent < 0)
                .OrderBy(p => p.ChangePercent)
                .Take(10)
                .ToList();

            // 更新排名
            for (int i = 0; i < topGainers.Count; i++)
            {
                topGainers[i].Rank = i + 1;
            }
            for (int i = 0; i < topLosers.Count; i++)
            {
                topLosers[i].Rank = i + 1;
            }

            // 更新UI
            Dispatcher.Invoke(() =>
            {
                foreach (var item in topGainers)
                {
                    _topGainers.Add(item);
                }
                foreach (var item in topLosers)
                {
                    _topLosers.Add(item);
                }
                
                AnalysisProgressBar.Value = 50;
            });

            await Task.Delay(100, cancellationToken); // 给UI一点时间更新
        }

        /// <summary>
        /// 分析突破情况
        /// </summary>
        private async Task AnalyzeBreakouts(CancellationToken cancellationToken)
        {
            if (_databaseService == null)
            {
                throw new InvalidOperationException("数据库服务未初始化");
            }

            // 清空现有突破数据
            Dispatcher.Invoke(() =>
            {
                _break5DayHigh.Clear();
                _break10DayHigh.Clear();
                _break20DayHigh.Clear();
                _break5DayLow.Clear();
                _break10DayLow.Clear();
                _break20DayLow.Clear();
            });

            // 获取可交易合约列表
            var tradableSymbols = await GetTradableSymbolsAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // 获取所有合约的历史数据
            await LoadHistoricalData(tradableSymbols, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            // 获取当前价格数据
            var currentPrices = await GetCurrentPrices(tradableSymbols, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            // 分析各种突破情况
            var breakouts = AnalyzeAllBreakouts(currentPrices);

            // 记录突破分析结果
            Utils.AppSession.Log($"突破分析完成 - 5天新高: {breakouts.Break5DayHigh.Count}, 10天新高: {breakouts.Break10DayHigh.Count}, 20天新高: {breakouts.Break20DayHigh.Count}");
            Utils.AppSession.Log($"突破分析完成 - 5天新低: {breakouts.Break5DayLow.Count}, 10天新低: {breakouts.Break10DayLow.Count}, 20天新低: {breakouts.Break20DayLow.Count}");

            // 更新UI
            Dispatcher.Invoke(() =>
            {
                foreach (var item in breakouts.Break5DayHigh)
                    _break5DayHigh.Add(item);
                foreach (var item in breakouts.Break10DayHigh)
                    _break10DayHigh.Add(item);
                foreach (var item in breakouts.Break20DayHigh)
                    _break20DayHigh.Add(item);
                foreach (var item in breakouts.Break5DayLow)
                    _break5DayLow.Add(item);
                foreach (var item in breakouts.Break10DayLow)
                    _break10DayLow.Add(item);
                foreach (var item in breakouts.Break20DayLow)
                    _break20DayLow.Add(item);
            });
        }

        /// <summary>
        /// 获取今天的缓存文件路径
        /// </summary>
        private string GetTodayCacheFilePath()
        {
            var today = DateTime.Now.ToString("yyyyMMdd");
            var fileName = $"{today}{CACHE_FILE_SUFFIX}";
            return Path.Combine(CacheDirectory, fileName);
        }

        /// <summary>
        /// 保存历史数据到本地文件
        /// </summary>
        private async Task SaveHistoricalDataToCacheAsync(Dictionary<string, HistoricalPriceData> data)
        {
            try
            {
                // 确保缓存目录存在
                if (!Directory.Exists(CacheDirectory))
                {
                    Directory.CreateDirectory(CacheDirectory);
                }

                var cacheFilePath = GetTodayCacheFilePath();
                var json = JsonConvert.SerializeObject(data, Formatting.Indented);
                await File.WriteAllTextAsync(cacheFilePath, json);
                
                Utils.AppSession.Log($"历史数据已保存到缓存文件: {cacheFilePath}，共 {data.Count} 个合约");
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"保存历史数据缓存失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 从本地文件加载历史数据
        /// </summary>
        private async Task<Dictionary<string, HistoricalPriceData>> LoadHistoricalDataFromCacheAsync()
        {
            try
            {
                var cacheFilePath = GetTodayCacheFilePath();
                if (!File.Exists(cacheFilePath))
                {
                    Utils.AppSession.Log($"今天的缓存文件不存在: {cacheFilePath}");
                    return null;
                }

                var json = await File.ReadAllTextAsync(cacheFilePath);
                var data = JsonConvert.DeserializeObject<Dictionary<string, HistoricalPriceData>>(json);
                
                Utils.AppSession.Log($"从缓存文件加载历史数据成功: {cacheFilePath}，共 {data?.Count ?? 0} 个合约");
                return data;
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"从缓存文件加载历史数据失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 清理过期的缓存文件
        /// </summary>
        private void CleanupOldCacheFiles()
        {
            try
            {
                if (!Directory.Exists(CacheDirectory))
                    return;

                var files = Directory.GetFiles(CacheDirectory, $"*{CACHE_FILE_SUFFIX}");
                var today = DateTime.Now.Date;
                
                foreach (var file in files)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    var dateStr = fileName.Replace(CACHE_FILE_SUFFIX.Replace(".json", ""), "");
                    
                    if (DateTime.TryParseExact(dateStr, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var fileDate))
                    {
                        // 删除7天前的缓存文件
                        if ((today - fileDate).TotalDays > 7)
                        {
                            File.Delete(file);
                            Utils.AppSession.Log($"删除过期缓存文件: {file}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"清理过期缓存文件失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 加载历史数据
        /// </summary>
        private async Task LoadHistoricalData(HashSet<string> tradableSymbols, CancellationToken cancellationToken)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressText.Text = "正在加载历史K线数据...";
                AnalysisProgressBar.Value = 10;
            });

            // 清理过期的缓存文件
            CleanupOldCacheFiles();

            // 首先尝试从本地缓存文件加载
            var cachedData = await LoadHistoricalDataFromCacheAsync();
            if (cachedData != null && cachedData.Any())
            {
                // 过滤出可交易的合约数据
                _historicalDataCache.Clear();
                foreach (var kvp in cachedData)
                {
                    var symbol = kvp.Key;
                    // 检查是否为可交易合约
                    if (tradableSymbols.Contains(symbol) || 
                        tradableSymbols.Contains($"{symbol}USDT") ||
                        tradableSymbols.Contains(symbol.Replace("USDT", "")))
                    {
                        _historicalDataCache[symbol] = kvp.Value;
                    }
                }

                Utils.AppSession.Log($"从缓存文件加载历史数据完成，共 {_historicalDataCache.Count} 个可交易合约");
                
                Dispatcher.Invoke(() =>
                {
                    ProgressText.Text = "从缓存文件加载历史数据完成";
                    AnalysisProgressBar.Value = 50;
                });
                return;
            }

            // 如果没有缓存文件，则从数据库获取数据
            Utils.AppSession.Log("未找到今天的缓存文件，开始从数据库获取历史数据...");
            
            // 获取所有合约列表
            var allSymbols = await _databaseService.GetAllSymbolsAsync();
            if (allSymbols == null || !allSymbols.Any())
            {
                throw new InvalidOperationException("数据库中没有K线数据");
            }

            // 过滤出可交易的合约（数据库中的symbol格式可能是BTCUSDT，需要匹配）
            var symbols = allSymbols
                .Where(symbol => tradableSymbols.Contains(symbol) || 
                               tradableSymbols.Contains($"{symbol}USDT"))
                .ToList();

            Utils.AppSession.Log($"数据库中共有 {allSymbols.Count} 个合约，其中可交易的有 {symbols.Count} 个");

            var totalSymbols = symbols.Count;
            var processedSymbols = 0;
            var newHistoricalData = new Dictionary<string, HistoricalPriceData>();

            foreach (var symbol in symbols)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // 获取过去21天的数据（确保有足够的数据计算20天最高/最低）
                    var endDate = DateTime.Now.Date;
                    var startDate = endDate.AddDays(-21);

                    var klineData = await _databaseService.GetKlineDataAsync(symbol, startDate, endDate);
                    if (klineData != null && klineData.Any())
                    {
                        Utils.AppSession.Log($"合约 {symbol} 获取到 {klineData.Count} 条K线数据");
                        
                        // 确保有足够的数据
                        if (klineData.Count >= 20)
                        {
                            var historicalData = new HistoricalPriceData
                            {
                                Symbol = symbol,
                                High5Day = klineData.TakeLast(5).Max(k => k.HighPrice),
                                Low5Day = klineData.TakeLast(5).Min(k => k.LowPrice),
                                High10Day = klineData.TakeLast(10).Max(k => k.HighPrice),
                                Low10Day = klineData.TakeLast(10).Min(k => k.LowPrice),
                                High20Day = klineData.TakeLast(20).Max(k => k.HighPrice),
                                Low20Day = klineData.TakeLast(20).Min(k => k.LowPrice)
                            };

                            // 统一使用不带USDT后缀的格式作为key，避免重复
                            var normalizedSymbol = symbol.EndsWith("USDT") ? symbol.Replace("USDT", "") : symbol;
                            historicalData.Symbol = normalizedSymbol; // 确保Symbol属性也是标准化的
                            _historicalDataCache[normalizedSymbol] = historicalData;
                            newHistoricalData[normalizedSymbol] = historicalData;
                            
                            Utils.AppSession.Log($"合约 {symbol} 历史数据缓存成功 - 5天最高: {historicalData.High5Day}, 5天最低: {historicalData.Low5Day}");
                        }
                        else
                        {
                            Utils.AppSession.Log($"合约 {symbol} 数据不足，只有 {klineData.Count} 条，需要至少20条");
                        }
                    }
                    else
                    {
                        Utils.AppSession.Log($"合约 {symbol} 未获取到K线数据");
                    }
                }
                catch (Exception ex)
                {
                    // 记录错误但继续处理其他合约
                    Utils.AppSession.Log($"加载{symbol}历史数据失败: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"加载{symbol}历史数据失败: {ex.Message}");
                }

                processedSymbols++;
                var progress = 10 + (processedSymbols * 40 / totalSymbols); // 10-50%的进度
                Dispatcher.Invoke(() =>
                {
                    AnalysisProgressBar.Value = progress;
                    ProgressText.Text = $"正在加载历史数据... ({processedSymbols}/{totalSymbols})";
                });
            }

            // 保存新获取的数据到缓存文件
            if (newHistoricalData.Any())
            {
                await SaveHistoricalDataToCacheAsync(newHistoricalData);
            }
        }

        /// <summary>
        /// 获取当前价格数据
        /// </summary>
        private async Task<Dictionary<string, decimal>> GetCurrentPrices(HashSet<string> tradableSymbols, CancellationToken cancellationToken)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressText.Text = "正在获取当前价格...";
                AnalysisProgressBar.Value = 60;
            });

            var currentPrices = new Dictionary<string, decimal>();

            if (_exchangeService != null)
            {
                try
                {
                    var tickers = await _exchangeService.GetAllTickersAsync();
                    if (tickers != null)
                    {
                        foreach (var ticker in tickers.Where(t => t.Symbol.EndsWith("USDT") && 
                                                                 tradableSymbols.Contains(t.Symbol)))
                        {
                            // 统一使用不带USDT后缀的格式作为key
                            var normalizedSymbol = ticker.Symbol.Replace("USDT", ""); // BTC
                            currentPrices[normalizedSymbol] = ticker.LastPrice;
                        }
                    }
                    
                    Utils.AppSession.Log($"获取到 {currentPrices.Count} 个可交易合约的当前价格");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"获取当前价格失败: {ex.Message}");
                }
            }

            Dispatcher.Invoke(() => AnalysisProgressBar.Value = 80);
            return currentPrices;
        }

        /// <summary>
        /// 分析所有突破情况
        /// </summary>
        private BreakoutAnalysisResult AnalyzeAllBreakouts(Dictionary<string, decimal> currentPrices)
        {
            var result = new BreakoutAnalysisResult();

            Utils.AppSession.Log($"开始分析突破情况 - 历史数据缓存: {_historicalDataCache.Count} 个合约, 当前价格: {currentPrices.Count} 个合约");
            
            // 调试信息：显示历史数据缓存的前几个合约
            var historicalSymbols = _historicalDataCache.Keys.Take(5).ToList();
            Utils.AppSession.Log($"历史数据缓存示例: {string.Join(", ", historicalSymbols)}");
            
            // 调试信息：显示当前价格的前几个合约
            var priceSymbols = currentPrices.Keys.Take(5).ToList();
            Utils.AppSession.Log($"当前价格示例: {string.Join(", ", priceSymbols)}");

            int matchedCount = 0;
            int unmatchedCount = 0;

            foreach (var kvp in _historicalDataCache)
            {
                var symbol = kvp.Key;
                var historicalData = kvp.Value;

                if (!currentPrices.TryGetValue(symbol, out var currentPrice) || currentPrice <= 0)
                {
                    unmatchedCount++;
                    // 只记录前几个未匹配的合约，避免日志过多
                    if (unmatchedCount <= 3)
                    {
                        Utils.AppSession.Log($"未匹配到当前价格的合约: {symbol}");
                    }
                    continue;
                }

                matchedCount++;
                // 只记录前几个合约的详细信息，避免日志过多
                if (matchedCount <= 3)
                {
                    Utils.AppSession.Log($"分析合约 {symbol}: 当前价格={currentPrice}, 5天最高={historicalData.High5Day}, 5天最低={historicalData.Low5Day}");
                }

                // 检查5天突破
                if (currentPrice > historicalData.High5Day)
                {
                    var breakPercent = (currentPrice - historicalData.High5Day) / historicalData.High5Day;
                    result.Break5DayHigh.Add(new BreakoutItem
                    {
                        Symbol = symbol,
                        CurrentPrice = currentPrice,
                        HighPrice = historicalData.High5Day,
                        BreakPercent = breakPercent
                    });
                    Utils.AppSession.Log($"发现5天新高突破: {symbol}, 当前价格={currentPrice}, 5天最高={historicalData.High5Day}, 突破幅度={breakPercent:P2}");
                }
                if (currentPrice < historicalData.Low5Day)
                {
                    var breakPercent = (historicalData.Low5Day - currentPrice) / historicalData.Low5Day;
                    result.Break5DayLow.Add(new BreakoutItem
                    {
                        Symbol = symbol,
                        CurrentPrice = currentPrice,
                        LowPrice = historicalData.Low5Day,
                        BreakPercent = breakPercent
                    });
                    Utils.AppSession.Log($"发现5天新低跌破: {symbol}, 当前价格={currentPrice}, 5天最低={historicalData.Low5Day}, 跌破幅度={breakPercent:P2}");
                }

                // 检查10天突破
                if (currentPrice > historicalData.High10Day)
                {
                    var breakPercent = (currentPrice - historicalData.High10Day) / historicalData.High10Day;
                    result.Break10DayHigh.Add(new BreakoutItem
                    {
                        Symbol = symbol,
                        CurrentPrice = currentPrice,
                        HighPrice = historicalData.High10Day,
                        BreakPercent = breakPercent
                    });
                    Utils.AppSession.Log($"发现10天新高突破: {symbol}, 当前价格={currentPrice}, 10天最高={historicalData.High10Day}, 突破幅度={breakPercent:P2}");
                }
                if (currentPrice < historicalData.Low10Day)
                {
                    var breakPercent = (historicalData.Low10Day - currentPrice) / historicalData.Low10Day;
                    result.Break10DayLow.Add(new BreakoutItem
                    {
                        Symbol = symbol,
                        CurrentPrice = currentPrice,
                        LowPrice = historicalData.Low10Day,
                        BreakPercent = breakPercent
                    });
                    Utils.AppSession.Log($"发现10天新低跌破: {symbol}, 当前价格={currentPrice}, 10天最低={historicalData.Low10Day}, 跌破幅度={breakPercent:P2}");
                }

                // 检查20天突破
                if (currentPrice > historicalData.High20Day)
                {
                    var breakPercent = (currentPrice - historicalData.High20Day) / historicalData.High20Day;
                    result.Break20DayHigh.Add(new BreakoutItem
                    {
                        Symbol = symbol,
                        CurrentPrice = currentPrice,
                        HighPrice = historicalData.High20Day,
                        BreakPercent = breakPercent
                    });
                    Utils.AppSession.Log($"发现20天新高突破: {symbol}, 当前价格={currentPrice}, 20天最高={historicalData.High20Day}, 突破幅度={breakPercent:P2}");
                }
                if (currentPrice < historicalData.Low20Day)
                {
                    var breakPercent = (historicalData.Low20Day - currentPrice) / historicalData.Low20Day;
                    result.Break20DayLow.Add(new BreakoutItem
                    {
                        Symbol = symbol,
                        CurrentPrice = currentPrice,
                        LowPrice = historicalData.Low20Day,
                        BreakPercent = breakPercent
                    });
                    Utils.AppSession.Log($"发现20天新低跌破: {symbol}, 当前价格={currentPrice}, 20天最低={historicalData.Low20Day}, 跌破幅度={breakPercent:P2}");
                }
            }

            // 按突破幅度排序
            result.Break5DayHigh = result.Break5DayHigh.OrderByDescending(x => x.BreakPercent).ToList();
            result.Break10DayHigh = result.Break10DayHigh.OrderByDescending(x => x.BreakPercent).ToList();
            result.Break20DayHigh = result.Break20DayHigh.OrderByDescending(x => x.BreakPercent).ToList();
            result.Break5DayLow = result.Break5DayLow.OrderByDescending(x => x.BreakPercent).ToList();
            result.Break10DayLow = result.Break10DayLow.OrderByDescending(x => x.BreakPercent).ToList();
            result.Break20DayLow = result.Break20DayLow.OrderByDescending(x => x.BreakPercent).ToList();

            Utils.AppSession.Log($"突破分析完成 - 匹配合约: {matchedCount}, 未匹配合约: {unmatchedCount}");
            Utils.AppSession.Log($"突破统计 - 5天新高: {result.Break5DayHigh.Count}, 10天新高: {result.Break10DayHigh.Count}, 20天新高: {result.Break20DayHigh.Count}");
            Utils.AppSession.Log($"突破统计 - 5天新低: {result.Break5DayLow.Count}, 10天新低: {result.Break10DayLow.Count}, 20天新低: {result.Break20DayLow.Count}");

            return result;
        }

        /// <summary>
        /// 初始化倒计时器
        /// </summary>
        private void InitializeCountdownTimer()
        {
            _countdownTimer = new DispatcherTimer();
            _countdownTimer.Interval = TimeSpan.FromSeconds(1); // 每秒更新一次
            _countdownTimer.Tick += CountdownTimer_Tick;
        }

        /// <summary>
        /// 倒计时器事件
        /// </summary>
        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                // 检查窗口是否正在关闭或已关闭
                if (!IsLoaded || !IsVisible)
                {
                    _countdownTimer?.Stop();
                    Utils.AppSession.Log("倒计时器停止：窗口未加载或不可见");
                    return;
                }

                var now = DateTime.Now;
                Utils.AppSession.Log($"倒计时器Tick - 当前时间: {now:HH:mm:ss}, 下次更新时间: {_nextUpdateTime:HH:mm:ss}");
                
                if (now >= _nextUpdateTime)
                {
                    Utils.AppSession.Log("触发自动更新市场数据 - 模拟点击启动分析按钮");
                    
                    // 直接触发启动分析按钮的点击事件，这样可以确保UI正确更新
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (IsLoaded && IsVisible && !_isAnalyzing)
                            {
                                // 模拟点击启动分析按钮
                                StartAnalysisButton_Click(null, null);
                            }
                        }
                        catch (Exception ex)
                        {
                            Utils.AppSession.Log($"自动触发启动分析失败: {ex.Message}");
                        }
                    }));
                    
                    // 设置下次更新时间
                    _nextUpdateTime = now.AddSeconds(_updateIntervalSeconds);
                    Utils.AppSession.Log($"设置下次更新时间: {_nextUpdateTime:HH:mm:ss}");
                }

                // 更新倒计时显示
                var remainingSeconds = (_nextUpdateTime - now).TotalSeconds;
                if (remainingSeconds > 0)
                {
                    // 使用BeginInvoke避免阻塞，并检查窗口状态
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (IsLoaded && IsVisible && CountdownText != null && CountdownProgressBar != null)
                            {
                                CountdownText.Text = $"下次更新: {remainingSeconds:F0}秒";
                                CountdownProgressBar.Value = (_updateIntervalSeconds - remainingSeconds) / _updateIntervalSeconds * 100;
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            // 窗口正在关闭，停止计时器
                            _countdownTimer?.Stop();
                        }
                    }));
                }
                else
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (IsLoaded && IsVisible && CountdownText != null && CountdownProgressBar != null)
                            {
                                CountdownText.Text = "正在更新...";
                                CountdownProgressBar.Value = 100;
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            // 窗口正在关闭，停止计时器
                            _countdownTimer?.Stop();
                        }
                    }));
                }
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"倒计时器异常: {ex.Message}");
                _countdownTimer?.Stop();
            }
        }



        /// <summary>
        /// 启动倒计时
        /// </summary>
        private void StartCountdown()
        {
            _nextUpdateTime = DateTime.Now.AddSeconds(_updateIntervalSeconds);
            _countdownTimer?.Start();
            
            Dispatcher.Invoke(() =>
            {
                CountdownText.Text = $"下次更新: {_updateIntervalSeconds}秒";
                CountdownProgressBar.Value = 0;
            });
        }

        /// <summary>
        /// 启动市场数据定期更新倒计时（仅用于涨跌幅排行）
        /// </summary>
        private void StartMarketDataCountdown()
        {
            _nextUpdateTime = DateTime.Now.AddSeconds(_updateIntervalSeconds);
            _countdownTimer?.Start();
            
            Utils.AppSession.Log($"启动市场数据倒计时 - 下次更新时间: {_nextUpdateTime:HH:mm:ss}");
            
            Dispatcher.Invoke(() =>
            {
                CountdownText.Text = $"下次更新: {_updateIntervalSeconds}秒";
                CountdownProgressBar.Value = 0;
            });
        }

        /// <summary>
        /// 加载初始市场排行数据
        /// </summary>
        private async Task LoadInitialMarketRankings()
        {
            try
            {
                if (_exchangeService == null)
                {
                    Utils.AppSession.Log("交易所服务未初始化，无法加载市场数据");
                    return;
                }

                Dispatcher.Invoke(() =>
                {
                    ProgressText.Text = "正在加载市场排行数据...";
                    AnalysisProgressBar.Value = 0;
                });

                // 获取可交易合约列表
                var tradableSymbols = await GetTradableSymbolsAsync(CancellationToken.None);
                
                // 获取最新的ticker数据
                var tickers = await _exchangeService.GetAllTickersAsync();
                if (tickers == null || !tickers.Any())
                {
                    Utils.AppSession.Log("无法获取市场ticker数据");
                    return;
                }

                // 更新涨跌幅排行
                var usdtPairs = tickers
                    .Where(t => t.Symbol.EndsWith("USDT") && 
                               t.PriceChangePercent != 0 && 
                               tradableSymbols.Contains(t.Symbol))
                    .Select((t, index) => new MarketRankingItem
                    {
                        Rank = index + 1,
                        Symbol = t.Symbol.Replace("USDT", ""),
                        CurrentPrice = t.LastPrice,
                        ChangePercent = t.PriceChangePercent / 100m,
                        Volume24h = t.Volume,
                        QuoteVolume24h = t.QuoteVolume
                    })
                    .ToList();

                var topGainers = usdtPairs
                    .Where(p => p.ChangePercent > 0)
                    .OrderByDescending(p => p.ChangePercent)
                    .Take(10)
                    .ToList();

                var topLosers = usdtPairs
                    .Where(p => p.ChangePercent < 0)
                    .OrderBy(p => p.ChangePercent)
                    .Take(10)
                    .ToList();

                // 更新排名
                for (int i = 0; i < topGainers.Count; i++)
                {
                    topGainers[i].Rank = i + 1;
                }
                for (int i = 0; i < topLosers.Count; i++)
                {
                    topLosers[i].Rank = i + 1;
                }

                // 更新UI
                Dispatcher.Invoke(() =>
                {
                    _topGainers.Clear();
                    _topLosers.Clear();
                    
                    foreach (var item in topGainers)
                    {
                        _topGainers.Add(item);
                    }
                    foreach (var item in topLosers)
                    {
                        _topLosers.Add(item);
                    }
                    
                    ProgressText.Text = $"市场排行数据已加载 - {DateTime.Now:HH:mm:ss}";
                    AnalysisProgressBar.Value = 50;
                });

                Utils.AppSession.Log($"初始市场排行数据加载完成，涨幅前10: {topGainers.Count}个，跌幅前10: {topLosers.Count}个");
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"加载初始市场排行数据失败: {ex.Message}");
                Dispatcher.Invoke(() =>
                {
                    ProgressText.Text = $"加载市场数据失败: {ex.Message}";
                });
            }
        }

        /// <summary>
        /// 停止倒计时
        /// </summary>
        private void StopCountdown()
        {
            _countdownTimer?.Stop();
            
            Dispatcher.Invoke(() =>
            {
                CountdownText.Text = "";
                CountdownProgressBar.Value = 0;
            });
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // 在窗口开始关闭时立即停止所有操作
                _countdownTimer?.Stop();
                _cancellationTokenSource?.Cancel();
                _isAnalyzing = false;
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"窗口关闭准备时清理资源失败: {ex.Message}");
            }
            finally
            {
                base.OnClosing(e);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                // 完全清理资源
                _countdownTimer?.Stop();
                _countdownTimer = null;
                
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                
                _isAnalyzing = false;
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"窗口关闭时清理资源失败: {ex.Message}");
            }
            finally
            {
                base.OnClosed(e);
            }
        }

        /// <summary>
        /// 初始化右键菜单
        /// </summary>
        private void InitializeContextMenus()
        {
            // 为所有DataGrid添加右键菜单
            AddContextMenuToDataGrid(TopGainersDataGrid);
            AddContextMenuToDataGrid(TopLosersDataGrid);
            AddContextMenuToDataGrid(Break5DayHighDataGrid);
            AddContextMenuToDataGrid(Break10DayHighDataGrid);
            AddContextMenuToDataGrid(Break20DayHighDataGrid);
            AddContextMenuToDataGrid(Break5DayLowDataGrid);
            AddContextMenuToDataGrid(Break10DayLowDataGrid);
            AddContextMenuToDataGrid(Break20DayLowDataGrid);
        }

        /// <summary>
        /// 为DataGrid添加右键菜单
        /// </summary>
        private void AddContextMenuToDataGrid(System.Windows.Controls.DataGrid dataGrid)
        {
            var contextMenu = new System.Windows.Controls.ContextMenu();
            
            var addToFavoriteMenuItem = new System.Windows.Controls.MenuItem
            {
                Header = "加入自选",
                Icon = new System.Windows.Controls.TextBlock { Text = "⭐", FontSize = 14 }
            };
            
            addToFavoriteMenuItem.Click += async (sender, e) =>
            {
                await AddToFavoriteAsync(dataGrid);
            };
            
            contextMenu.Items.Add(addToFavoriteMenuItem);
            dataGrid.ContextMenu = contextMenu;
        }

        /// <summary>
        /// 刷新间隔输入框文本变化事件
        /// </summary>
        private void RefreshIntervalTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            try
            {
                var textBox = sender as System.Windows.Controls.TextBox;
                if (textBox == null) return;

                if (int.TryParse(textBox.Text, out int interval))
                {
                    // 限制范围在1-3000秒之间
                    if (interval >= 1 && interval <= 3000)
                    {
                        _updateIntervalSeconds = interval;
                        Utils.AppSession.Log($"刷新间隔已更新为: {_updateIntervalSeconds}秒");
                        
                        // 如果倒计时器正在运行，重新设置下次更新时间
                        if (_countdownTimer != null && _countdownTimer.IsEnabled)
                        {
                            _nextUpdateTime = DateTime.Now.AddSeconds(_updateIntervalSeconds);
                            Utils.AppSession.Log($"重新设置下次更新时间: {_nextUpdateTime:HH:mm:ss}");
                        }
                    }
                    else
                    {
                        // 超出范围，恢复到之前的值
                        textBox.Text = _updateIntervalSeconds.ToString();
                        textBox.SelectionStart = textBox.Text.Length; // 光标移到末尾
                    }
                }
                else if (!string.IsNullOrEmpty(textBox.Text))
                {
                    // 输入的不是数字，恢复到之前的值
                    textBox.Text = _updateIntervalSeconds.ToString();
                    textBox.SelectionStart = textBox.Text.Length; // 光标移到末尾
                }
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"刷新间隔设置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 将选中的合约加入自选
        /// </summary>
        private async Task AddToFavoriteAsync(System.Windows.Controls.DataGrid dataGrid)
        {
            try
            {
                if (dataGrid.SelectedItem == null)
                {
                    MessageBox.Show("请先选择要加入自选的合约", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string symbol = null;
                
                // 根据不同的数据类型获取合约名称
                if (dataGrid.SelectedItem is MarketRankingItem marketItem)
                {
                    symbol = marketItem.Symbol;
                }
                else if (dataGrid.SelectedItem is BreakoutItem breakoutItem)
                {
                    symbol = breakoutItem.Symbol;
                }

                if (string.IsNullOrEmpty(symbol))
                {
                    MessageBox.Show("无法获取合约名称", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 检查自选合约服务是否可用
                if (_favoriteContractsService == null)
                {
                    MessageBox.Show("自选合约服务不可用", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 检查是否已经在自选列表中
                var isAlreadyFavorite = await _favoriteContractsService.IsFavoriteContractAsync(symbol);
                if (isAlreadyFavorite)
                {
                    MessageBox.Show($"合约 {symbol} 已在自选列表中", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 添加到自选列表
                await _favoriteContractsService.AddFavoriteContractAsync(symbol);
                
                MessageBox.Show($"合约 {symbol} 已成功加入自选列表", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                Utils.AppSession.Log($"用户将合约 {symbol} 加入自选列表");
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"加入自选失败: {ex.Message}");
                MessageBox.Show($"加入自选失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// 市场排行项目
    /// </summary>
    public class MarketRankingItem
    {
        public int Rank { get; set; }
        public string Symbol { get; set; }
        public decimal CurrentPrice { get; set; }
        public decimal ChangePercent { get; set; }
        public decimal Volume24h { get; set; } // 24小时成交量（币量）
        public decimal QuoteVolume24h { get; set; } // 24小时成交额（USDT）
        
        /// <summary>
        /// 格式化显示成交额，自动选择万/亿单位
        /// </summary>
        public string FormattedQuoteVolume
        {
            get
            {
                if (QuoteVolume24h >= 100000000) // 1亿以上显示亿
                {
                    return $"{QuoteVolume24h / 100000000:F2}亿";
                }
                else if (QuoteVolume24h >= 10000) // 1万以上显示万
                {
                    return $"{QuoteVolume24h / 10000:F2}万";
                }
                else
                {
                    return $"{QuoteVolume24h:F0}";
                }
            }
        }
        
        /// <summary>
        /// 格式化显示成交量
        /// </summary>
        public string FormattedVolume
        {
            get
            {
                if (Volume24h >= 100000) // 10万以上显示万
                {
                    return $"{Volume24h / 10000:F2}万";
                }
                else if (Volume24h >= 1000) // 1千以上显示千
                {
                    return $"{Volume24h / 1000:F2}千";
                }
                else
                {
                    return $"{Volume24h:F2}";
                }
            }
        }
    }

    /// <summary>
    /// 突破项目
    /// </summary>
    public class BreakoutItem
    {
        public string Symbol { get; set; }
        public decimal CurrentPrice { get; set; }
        public decimal HighPrice { get; set; }
        public decimal LowPrice { get; set; }
        public decimal BreakPercent { get; set; }
    }

    /// <summary>
    /// 历史价格数据
    /// </summary>
    public class HistoricalPriceData
    {
        public string Symbol { get; set; }
        public decimal High5Day { get; set; }
        public decimal Low5Day { get; set; }
        public decimal High10Day { get; set; }
        public decimal Low10Day { get; set; }
        public decimal High20Day { get; set; }
        public decimal Low20Day { get; set; }
    }

    /// <summary>
    /// 突破分析结果
    /// </summary>
    public class BreakoutAnalysisResult
    {
        public List<BreakoutItem> Break5DayHigh { get; set; } = new List<BreakoutItem>();
        public List<BreakoutItem> Break10DayHigh { get; set; } = new List<BreakoutItem>();
        public List<BreakoutItem> Break20DayHigh { get; set; } = new List<BreakoutItem>();
        public List<BreakoutItem> Break5DayLow { get; set; } = new List<BreakoutItem>();
        public List<BreakoutItem> Break10DayLow { get; set; } = new List<BreakoutItem>();
        public List<BreakoutItem> Break20DayLow { get; set; } = new List<BreakoutItem>();
    }
} 