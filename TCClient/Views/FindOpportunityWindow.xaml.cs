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
        private VolumeAnalysisService _volumeAnalysisService;
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

        // 放量合约相关
        private ObservableCollection<VolumeBreakoutItem> _volumeBreakouts = new ObservableCollection<VolumeBreakoutItem>();
        private int _volumeDays = 7; // 默认与过去7天比较
        private double _volumeMultiplier = 2.0; // 默认2倍放量
        
        // 设置管理
        private Utils.SettingsManager.AppSettings _appSettings;

        // 历史价格缓存
        private Dictionary<string, HistoricalPriceData> _historicalDataCache = new Dictionary<string, HistoricalPriceData>();
        
        // 成交额平均值缓存 - 专门用于放量分析
        private static Dictionary<string, decimal> _avgVolumeCache = new Dictionary<string, decimal>();
        private static DateTime _avgVolumeCacheTime = DateTime.MinValue;
        
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
        
        // 市场统计相关
        private List<TickerInfo> _allTickerData = new List<TickerInfo>();
        
        // K线图窗口管理 - 防止重复打开
        private static readonly Dictionary<string, KLineFloatingWindow> _openKLineWindows = new Dictionary<string, KLineFloatingWindow>();
        private static readonly object _windowLock = new object();

        public FindOpportunityWindow()
        {
            InitializeComponent();
            
            // 获取服务
            var app = Application.Current as App;
            _exchangeService = app?.Services?.GetService<IExchangeService>();
            _databaseService = app?.Services?.GetService<IDatabaseService>();
            _favoriteContractsService = app?.Services?.GetService<FavoriteContractsService>();
            _volumeAnalysisService = new VolumeAnalysisService(_databaseService);

            // 绑定数据源
            TopGainersDataGrid.ItemsSource = _topGainers;
            TopLosersDataGrid.ItemsSource = _topLosers;
            Break5DayHighDataGrid.ItemsSource = _break5DayHigh;
            Break10DayHighDataGrid.ItemsSource = _break10DayHigh;
            Break20DayHighDataGrid.ItemsSource = _break20DayHigh;
            Break5DayLowDataGrid.ItemsSource = _break5DayLow;
            Break10DayLowDataGrid.ItemsSource = _break10DayLow;
            Break20DayLowDataGrid.ItemsSource = _break20DayLow;
            VolumeBreakoutDataGrid.ItemsSource = _volumeBreakouts;

            // 初始化倒计时器
            InitializeCountdownTimer();
            
            // 初始化右键菜单
            InitializeContextMenus();
            
            // 添加双击事件处理
            TopGainersDataGrid.MouseDoubleClick += DataGrid_MouseDoubleClick;
            TopLosersDataGrid.MouseDoubleClick += DataGrid_MouseDoubleClick;
            Break5DayHighDataGrid.MouseDoubleClick += DataGrid_MouseDoubleClick;
            Break10DayHighDataGrid.MouseDoubleClick += DataGrid_MouseDoubleClick;
            Break20DayHighDataGrid.MouseDoubleClick += DataGrid_MouseDoubleClick;
            Break5DayLowDataGrid.MouseDoubleClick += DataGrid_MouseDoubleClick;
            Break10DayLowDataGrid.MouseDoubleClick += DataGrid_MouseDoubleClick;
            Break20DayLowDataGrid.MouseDoubleClick += DataGrid_MouseDoubleClick;
            VolumeBreakoutDataGrid.MouseDoubleClick += DataGrid_MouseDoubleClick;
            
            // 清理过期的缓存文件
            CleanupOldCacheFiles();
            
            // 清理成交额缓存
            _volumeAnalysisService?.CleanupExpiredCache();
            
            // 窗口加载完成后自动开始涨跌幅排行的定期更新
            Loaded += async (sender, e) =>
            {
                try
                {
                    // 加载设置
                    await LoadSettingsAsync();
                    
                    // 立即加载一次涨跌幅排行数据
                    await LoadInitialMarketRankings();
                    
                    // 启动定期更新倒计时
                    StartMarketDataCountdown();
                }
                catch (TaskCanceledException tcEx)
                {
                    Utils.AppSession.Log($"初始化市场数据时网络连接超时: {tcEx.Message}");
                    AddAnalysisLog("初始化市场数据时网络连接超时，请检查网络连接");
                    
                    // 显示网络异常提示
                    Dispatcher.Invoke(() =>
                    {
                        var result = Utils.NetworkExceptionHandler.ShowNetworkExceptionDialog(
                            this, tcEx, "启动时获取市场数据失败", false);
                        
                        if (result == System.Windows.MessageBoxResult.Yes)
                        {
                            // 用户选择重试，延迟重试
                            _ = Task.Delay(2000).ContinueWith(async _ =>
                            {
                                try
                                {
                                    await LoadInitialMarketRankings();
                                    StartMarketDataCountdown();
                                }
                                catch (Exception retryEx)
                                {
                                    Utils.AppSession.Log($"重试初始化市场数据失败: {retryEx.Message}");
                                    AddAnalysisLog($"重试失败: {retryEx.Message}");
                                }
                            });
                        }
                    });
                }
                catch (Exception ex)
                {
                    Utils.AppSession.Log($"初始化市场数据失败: {ex.Message}");
                    AddAnalysisLog($"初始化市场数据失败: {ex.Message}");
                    
                    // 检查是否为网络相关异常
                    if (Utils.NetworkExceptionHandler.IsNetworkException(ex))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            var result = Utils.NetworkExceptionHandler.ShowNetworkExceptionDialog(
                                this, ex, "启动时获取市场数据失败", false);
                            
                            if (result == System.Windows.MessageBoxResult.Yes)
                            {
                                // 用户选择重试，延迟重试
                                _ = Task.Delay(2000).ContinueWith(async _ =>
                                {
                                    try
                                    {
                                        await LoadInitialMarketRankings();
                                        StartMarketDataCountdown();
                                    }
                                    catch (Exception retryEx)
                                    {
                                        Utils.AppSession.Log($"重试初始化市场数据失败: {retryEx.Message}");
                                        AddAnalysisLog($"重试失败: {retryEx.Message}");
                                    }
                                });
                            }
                        });
                    }
                    else
                    {
                        // 非网络异常，显示一般错误提示
                        Dispatcher.Invoke(() =>
                        {
                            System.Windows.MessageBox.Show(
                                this,
                                $"初始化窗口时发生错误：{ex.Message}\n\n程序将继续运行，但部分功能可能不可用。",
                                "初始化错误",
                                System.Windows.MessageBoxButton.OK,
                                System.Windows.MessageBoxImage.Warning);
                        });
                    }
                }
            };
        }

        /// <summary>
        /// 加载设置
        /// </summary>
        private async Task LoadSettingsAsync()
        {
            try
            {
                _appSettings = await Utils.SettingsManager.LoadSettingsAsync();
                
                // 应用设置到UI
                _volumeDays = _appSettings.FindOpportunity.VolumeDays;
                _volumeMultiplier = _appSettings.FindOpportunity.VolumeMultiplier;
                _updateIntervalSeconds = _appSettings.FindOpportunity.UpdateIntervalSeconds;
                
                Dispatcher.Invoke(() =>
                {
                    VolumeDaysTextBox.Text = _volumeDays.ToString();
                    VolumeMultiplierTextBox.Text = _volumeMultiplier.ToString();
                    RefreshIntervalTextBox.Text = _updateIntervalSeconds.ToString();
                });
                
                Utils.AppSession.Log($"设置加载完成: 放量天数={_volumeDays}, 放量倍数={_volumeMultiplier}, 刷新间隔={_updateIntervalSeconds}秒");
                AddAnalysisLog($"设置加载完成: 放量天数={_volumeDays}, 放量倍数={_volumeMultiplier}, 刷新间隔={_updateIntervalSeconds}秒");
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"加载设置失败: {ex.Message}");
                _appSettings = new Utils.SettingsManager.AppSettings(); // 使用默认设置
            }
        }

        /// <summary>
        /// 保存设置
        /// </summary>
        private async Task SaveSettingsAsync()
        {
            try
            {
                if (_appSettings == null)
                    _appSettings = new Utils.SettingsManager.AppSettings();

                _appSettings.FindOpportunity.VolumeDays = _volumeDays;
                _appSettings.FindOpportunity.VolumeMultiplier = _volumeMultiplier;
                _appSettings.FindOpportunity.UpdateIntervalSeconds = _updateIntervalSeconds;

                await Utils.SettingsManager.SaveSettingsAsync(_appSettings);
                Utils.AppSession.Log("设置已保存");
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"保存设置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 添加分析日志
        /// </summary>
        private void AddAnalysisLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                var logEntry = $"[{timestamp}] {message}";
                
                if (AnalysisLogTextBox.Text.Length > 0)
                {
                    AnalysisLogTextBox.Text += Environment.NewLine;
                }
                AnalysisLogTextBox.Text += logEntry;
                
                // 自动滚动到最底部
                AnalysisLogTextBox.ScrollToEnd();
                
                // 限制日志长度，保留最近500行
                var lines = AnalysisLogTextBox.Text.Split(Environment.NewLine.ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > 500)
                {
                    var recentLines = lines.Skip(lines.Length - 500).ToArray();
                    AnalysisLogTextBox.Text = string.Join(Environment.NewLine, recentLines);
                }
            });
        }

        /// <summary>
        /// 清空日志按钮事件
        /// </summary>
        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            AnalysisLogTextBox.Text = "";
            AddAnalysisLog("日志已清空");
        }

        /// <summary>
        /// 刷新市场统计按钮事件
        /// </summary>
        private async void RefreshMarketStatsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RefreshMarketStatsButton.IsEnabled = false;
                AddAnalysisLog("开始刷新市场统计数据...");
                await UpdateMarketStatistics();
                AddAnalysisLog("市场统计数据刷新完成");
            }
            catch (Exception ex)
            {
                AddAnalysisLog($"刷新市场统计失败: {ex.Message}");
            }
            finally
            {
                RefreshMarketStatsButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// 更新市场统计信息
        /// </summary>
        private async Task UpdateMarketStatistics()
        {
            try
            {
                if (_exchangeService == null)
                {
                    AddAnalysisLog("交易所服务未初始化");
                    return;
                }

                // 获取24小时ticker数据
                var tickers = await _exchangeService.GetAllTickersAsync();
                if (tickers == null || !tickers.Any())
                {
                    AddAnalysisLog("未获取到ticker数据");
                    return;
                }

                _allTickerData = tickers.ToList();
                AddAnalysisLog($"获取到 {_allTickerData.Count} 个合约的ticker数据");

                // 计算涨跌家数
                var risingCount = _allTickerData.Count(t => t.PriceChangePercent > 0);
                var fallingCount = _allTickerData.Count(t => t.PriceChangePercent < 0);
                var flatCount = _allTickerData.Count(t => t.PriceChangePercent == 0);

                // 计算24小时总成交额
                var totalVolume = _allTickerData.Sum(t => t.QuoteVolume);

                // 更新UI
                Dispatcher.Invoke(() =>
                {
                    RisingCountText.Text = risingCount.ToString();
                    FallingCountText.Text = fallingCount.ToString();
                    TotalVolumeText.Text = FormatVolume(totalVolume);
                    MarketStatsUpdateTime.Text = DateTime.Now.ToString("HH:mm:ss");
                });

                AddAnalysisLog($"统计完成: 上涨{risingCount}家, 下跌{fallingCount}家, 平盘{flatCount}家");
                AddAnalysisLog($"24h总成交额: {FormatVolume(totalVolume)} USDT");
            }
            catch (Exception ex)
            {
                AddAnalysisLog($"更新市场统计失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 格式化成交额显示
        /// </summary>
        private string FormatVolume(decimal volume)
        {
            if (volume >= 1_000_000_000_000m) // 万亿
            {
                return $"{volume / 1_000_000_000_000m:F2}T";
            }
            else if (volume >= 1_000_000_000m) // 十亿
            {
                return $"{volume / 1_000_000_000m:F2}B";
            }
            else if (volume >= 1_000_000m) // 百万
            {
                return $"{volume / 1_000_000m:F2}M";
            }
            else if (volume >= 1_000m) // 千
            {
                return $"{volume / 1_000m:F2}K";
            }
            else
            {
                return $"{volume:F2}";
            }
        }

        /// <summary>
        /// 启动分析按钮点击事件
        /// </summary>
        private async void StartAnalysisButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isAnalyzing)
            {
                MessageBox.Show("分析正在进行中，请稍候...", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                _isAnalyzing = true;
                _cancellationTokenSource = new CancellationTokenSource();

                // 更新UI状态
                StartAnalysisButton.IsEnabled = false;
                FindBreakoutButton.IsEnabled = false;
                StopAnalysisButton.IsEnabled = true;

                // 清空涨跌幅排行数据（保留放量分析结果和突破分析结果）
                Dispatcher.Invoke(() =>
                {
                    _topGainers.Clear();
                    _topLosers.Clear();
                    // 注意：不清空_volumeBreakouts，保持之前的放量分析结果显示
                    // 注意：不清空突破分析结果，保持之前的突破分析结果显示
                    Utils.AppSession.Log("💡 市场排行刷新: 仅清空涨跌幅数据，保留放量分析和突破分析结果");
                });

                Utils.AppSession.Log("开始市场分析...");

                // 并行执行市场排行分析和突破分析（不包括放量分析，避免清空现有放量数据）
                var analysisTask1 = AnalyzeMarketRankings(_cancellationTokenSource.Token);
                var analysisTask2 = AnalyzeBreakouts(_cancellationTokenSource.Token);
                
                // 注意：这里不自动执行放量分析，保持现有的放量分析结果
                // 用户需要手动点击"成交额突破分析"按钮来重新分析放量数据
                Utils.AppSession.Log("💡 完整市场分析模式: 更新排行榜 + 突破分析，保留现有的放量分析结果");
                AddAnalysisLog("🚀 执行完整分析: 排行榜 + 突破分析，放量分析结果保持不变");

                await Task.WhenAll(analysisTask1, analysisTask2);

                if (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    Utils.AppSession.Log("市场分析完成");
                    
                    // 启动自动刷新倒计时
                    StartCountdown();
                }
            }
            catch (OperationCanceledException)
            {
                Utils.AppSession.Log("市场分析已取消");
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"市场分析失败: {ex.Message}");
                MessageBox.Show($"分析失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isAnalyzing = false;
                StartAnalysisButton.IsEnabled = true;
                FindBreakoutButton.IsEnabled = true; // 确保无论什么情况下都启用找寻突破按钮
                StopAnalysisButton.IsEnabled = false;
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
        /// 成交额突破分析按钮点击事件
        /// </summary>
        private async void VolumeAnalysisButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isAnalyzing)
            {
                MessageBox.Show("其他分析正在进行中，请稍候...", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                _isAnalyzing = true;
                _cancellationTokenSource = new CancellationTokenSource();

                // 更新UI状态
                VolumeAnalysisButton.IsEnabled = false;
                StopAnalysisButton.IsEnabled = true;
                
                Utils.AppSession.Log("🚀 开始单独执行成交额突破分析...");
                AddAnalysisLog("🚀 开始单独执行成交额突破分析...");
                
                ProgressText.Text = "正在分析成交额突破...";
                AnalysisProgressBar.Value = 0;

                // 执行放量分析
                await AnalyzeVolumeBreakouts(_cancellationTokenSource.Token);

                if (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    ProgressText.Text = "成交额突破分析完成";
                    AnalysisProgressBar.Value = 100;
                    Utils.AppSession.Log("✅ 成交额突破分析成功完成");
                    AddAnalysisLog("✅ 成交额突破分析成功完成");
                }
            }
            catch (OperationCanceledException)
            {
                ProgressText.Text = "成交额突破分析已取消";
                Utils.AppSession.Log("⏹️ 成交额突破分析被用户取消");
                AddAnalysisLog("⏹️ 成交额突破分析被用户取消");
            }
            catch (Exception ex)
            {
                var errorMsg = $"成交额突破分析失败：{ex.Message}";
                ProgressText.Text = "成交额突破分析失败";
                Utils.AppSession.Log($"❌ {errorMsg}");
                AddAnalysisLog($"❌ {errorMsg}");
                
                // 检查是否为网络异常
                if (Utils.NetworkExceptionHandler.IsNetworkException(ex))
                {
                    Utils.NetworkExceptionHandler.ShowNetworkExceptionDialog(
                        this, ex, "成交额突破分析", true);
                }
                else
                {
                    MessageBox.Show(errorMsg, "分析失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                _isAnalyzing = false;
                VolumeAnalysisButton.IsEnabled = true;
                StopAnalysisButton.IsEnabled = false;
            }
        }

        /// <summary>
        /// 获取可交易合约列表（带缓存）
        /// </summary>
        private async Task<HashSet<string>> GetTradableSymbolsAsync(CancellationToken cancellationToken)
        {
            try
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
            catch (TaskCanceledException tcEx)
            {
                Utils.AppSession.Log($"获取可交易合约列表超时: {tcEx.Message}");
                
                // 如果有缓存数据，即使过期也优先使用
                if (_tradableSymbolsCache != null && _tradableSymbolsCache.Any())
                {
                    Utils.AppSession.Log($"网络超时，使用过期缓存数据，共 {_tradableSymbolsCache.Count} 个合约");
                    return _tradableSymbolsCache;
                }
                
                // 抛出异常让上层处理
                throw;
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"获取可交易合约列表失败: {ex.Message}");
                
                // 如果有缓存数据，即使过期也优先使用
                if (_tradableSymbolsCache != null && _tradableSymbolsCache.Any())
                {
                    Utils.AppSession.Log($"网络异常，使用过期缓存数据，共 {_tradableSymbolsCache.Count} 个合约");
                    return _tradableSymbolsCache;
                }
                
                // 检查是否为网络相关异常
                if (Utils.NetworkExceptionHandler.IsNetworkException(ex))
                {
                    // 抛出异常让上层处理网络异常
                    throw;
                }
                
                // 对于其他异常，返回空列表
                Utils.AppSession.Log("使用空的可交易合约列表");
                return new HashSet<string>();
            }
        }

        /// <summary>
        /// 分析市场排行
        /// </summary>
        private async Task AnalyzeMarketRankings(CancellationToken cancellationToken)
        {
            try
            {
                Utils.AppSession.Log("开始分析市场排行...");
                AddAnalysisLog("开始分析市场排行...");
                
                var tickers = await _exchangeService.GetAllTickersAsync();
                if (tickers == null || !tickers.Any())
                {
                    Utils.AppSession.Log("未获取到市场数据");
                    AddAnalysisLog("未获取到市场数据");
                    return;
                }

                // 保存ticker数据用于市场统计
                _allTickerData = tickers.ToList();

                // 获取可交易合约列表
                var tradableSymbols = await GetTradableSymbolsAsync(cancellationToken);
                Utils.AppSession.Log($"获取到 {tradableSymbols.Count} 个可交易合约");
                AddAnalysisLog($"获取到 {tradableSymbols.Count} 个可交易合约");

                // 过滤USDT交易对和可交易合约
                var usdtTickers = tickers
                    .Where(t => t.Symbol.EndsWith("USDT") && tradableSymbols.Contains(t.Symbol))
                    .ToList();
                Utils.AppSession.Log($"获取到 {usdtTickers.Count} 个可交易的USDT交易对");
                AddAnalysisLog($"获取到 {usdtTickers.Count} 个可交易的USDT交易对");

                // 按涨幅排序（前20名）
                var topGainers = usdtTickers
                    .Where(t => t.PriceChangePercent > 0)
                    .OrderByDescending(t => t.PriceChangePercent)
                    .Take(20)
                    .Select((t, index) => new MarketRankingItem
                    {
                        Rank = index + 1,
                        Symbol = t.Symbol,
                        CurrentPrice = t.LastPrice,
                        ChangePercent = t.PriceChangePercent / 100,
                        Volume24h = t.Volume,
                        QuoteVolume24h = t.QuoteVolume
                    })
                    .ToList();

                // 按跌幅排序（前20名）
                var topLosers = usdtTickers
                    .Where(t => t.PriceChangePercent < 0)
                    .OrderBy(t => t.PriceChangePercent)
                    .Take(20)
                    .Select((t, index) => new MarketRankingItem
                    {
                        Rank = index + 1,
                        Symbol = t.Symbol,
                        CurrentPrice = t.LastPrice,
                        ChangePercent = t.PriceChangePercent / 100,
                        Volume24h = t.Volume,
                        QuoteVolume24h = t.QuoteVolume
                    })
                    .ToList();

                // 计算市场统计数据
                var risingCount = usdtTickers.Count(t => t.PriceChangePercent > 0);
                var fallingCount = usdtTickers.Count(t => t.PriceChangePercent < 0);
                var flatCount = usdtTickers.Count(t => t.PriceChangePercent == 0);
                var totalVolume = usdtTickers.Sum(t => t.QuoteVolume);

                // 更新UI
                Dispatcher.Invoke(() =>
                {
                    _topGainers.Clear();
                    foreach (var item in topGainers)
                    {
                        _topGainers.Add(item);
                    }

                    _topLosers.Clear();
                    foreach (var item in topLosers)
                    {
                        _topLosers.Add(item);
                    }

                    // 更新市场统计
                    RisingCountText.Text = risingCount.ToString();
                    FallingCountText.Text = fallingCount.ToString();
                    TotalVolumeText.Text = FormatVolume(totalVolume);
                    MarketStatsUpdateTime.Text = DateTime.Now.ToString("HH:mm:ss");
                });

                Utils.AppSession.Log($"市场排行分析完成: 涨幅榜 {topGainers.Count} 个，跌幅榜 {topLosers.Count} 个");
                AddAnalysisLog($"市场统计: 上涨{risingCount}家, 下跌{fallingCount}家, 平盘{flatCount}家");
                AddAnalysisLog($"24h总成交额: {FormatVolume(totalVolume)} USDT");
                
                // 添加调试信息：记录前几名合约信息
                if (topGainers.Any())
                {
                    AddAnalysisLog($"涨幅榜前3名: {string.Join(", ", topGainers.Take(3).Select(g => $"{g.Symbol}({g.ChangePercent:P2})"))}");
                }
                if (topLosers.Any())
                {
                    AddAnalysisLog($"跌幅榜前3名: {string.Join(", ", topLosers.Take(3).Select(l => $"{l.Symbol}({l.ChangePercent:P2})"))}");
                }
            }
            catch (TaskCanceledException tcEx)
            {
                Utils.AppSession.Log($"分析市场排行超时: {tcEx.Message}");
                AddAnalysisLog("分析市场排行超时，请检查网络连接");
                Utils.NetworkExceptionHandler.LogNetworkException("分析市场排行", tcEx);
                
                Dispatcher.Invoke(() =>
                {
                    ProgressText.Text = "市场排行分析超时";
                });
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"分析市场排行失败: {ex.Message}");
                AddAnalysisLog($"分析市场排行失败: {ex.Message}");
                
                if (Utils.NetworkExceptionHandler.IsNetworkException(ex))
                {
                    Utils.NetworkExceptionHandler.LogNetworkException("分析市场排行", ex);
                    AddAnalysisLog("网络连接异常，建议检查网络或稍后重试");
                    
                    Dispatcher.Invoke(() =>
                    {
                        ProgressText.Text = "网络连接异常，市场排行分析失败";
                    });
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        ProgressText.Text = "市场排行分析失败";
                    });
                }
            }
        }

        /// <summary>
        /// 分析放量合约
        /// </summary>
        private async Task AnalyzeVolumeBreakouts(CancellationToken cancellationToken)
        {
            try
            {
                Utils.AppSession.Log("开始分析放量合约...");
                AddAnalysisLog("开始分析放量合约...");
                
                // 清空之前的放量分析结果
                Dispatcher.Invoke(() =>
                {
                    Utils.AppSession.Log("清空之前的放量合约数据");
                    _volumeBreakouts.Clear();
                });
                
                // 获取当前设置
                int volumeDays = _volumeDays;
                double volumeMultiplier = _volumeMultiplier;
                
                Dispatcher.Invoke(() =>
                {
                    if (int.TryParse(VolumeDaysTextBox.Text, out int days) && days > 0)
                        volumeDays = days;
                    if (double.TryParse(VolumeMultiplierTextBox.Text, out double multiplier) && multiplier > 0)
                        volumeMultiplier = multiplier;
                });

                Utils.AppSession.Log($"放量分析参数: 天数={volumeDays}, 倍数={volumeMultiplier}");
                AddAnalysisLog($"放量分析参数: 天数={volumeDays}, 倍数={volumeMultiplier}");

                var tickers = await _exchangeService.GetAllTickersAsync();
                if (tickers == null || !tickers.Any())
                {
                    Utils.AppSession.Log("未获取到市场数据");
                    AddAnalysisLog("未获取到市场数据");
                    return;
                }

                // 获取可交易合约列表
                var tradableSymbols = await GetTradableSymbolsAsync(cancellationToken);
                
                // 过滤USDT交易对和可交易合约
                var usdtTickers = tickers
                    .Where(t => t.Symbol.EndsWith("USDT") && tradableSymbols.Contains(t.Symbol))
                    .ToList();
                Utils.AppSession.Log($"开始分析 {usdtTickers.Count} 个可交易USDT交易对的放量情况");
                AddAnalysisLog($"开始分析 {usdtTickers.Count} 个可交易USDT交易对的放量情况");

                var volumeBreakouts = new List<VolumeBreakoutItem>();
                int processedCount = 0;
                int foundCount = 0;

                // 分批处理以避免API限制
                var batchSize = 10;
                for (int i = 0; i < usdtTickers.Count; i += batchSize)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    var batch = usdtTickers.Skip(i).Take(batchSize);
                    var batchTasks = batch.Select(async ticker =>
                    {
                        try
                        {
                            processedCount++;
                            if (processedCount % 50 == 0)
                            {
                                Utils.AppSession.Log($"已处理 {processedCount}/{usdtTickers.Count} 个合约");
                                AddAnalysisLog($"已处理 {processedCount}/{usdtTickers.Count} 个合约");
                            }

                            // 获取历史成交量数据
                            var avgVolume = await GetAverageVolumeAsync(ticker.Symbol, volumeDays, cancellationToken);
                            
                            // 详细记录分析过程
                            if (processedCount <= 10) // 只记录前10个合约的详细信息
                            {
                                AddAnalysisLog($"合约 {ticker.Symbol}: 当前成交额={ticker.QuoteVolume:F2}, 平均成交额={avgVolume:F2}, 阈值={avgVolume * (decimal)volumeMultiplier:F2}");
                            }
                            
                            if (avgVolume > 0 && ticker.QuoteVolume > avgVolume * (decimal)volumeMultiplier)
                            {
                                foundCount++;
                                var volumeMultiplierActual = (double)(ticker.QuoteVolume / avgVolume);
                                Utils.AppSession.Log($"发现放量合约: {ticker.Symbol}, 当前成交额: {ticker.QuoteVolume:F2}, 平均成交额: {avgVolume:F2}, 放量倍数: {volumeMultiplierActual:F2}");
                                AddAnalysisLog($"✅ 发现放量合约: {ticker.Symbol}, 放量倍数: {volumeMultiplierActual:F2}");
                                
                                return new VolumeBreakoutItem
                                {
                                    Symbol = ticker.Symbol,
                                    CurrentPrice = ticker.LastPrice,
                                    ChangePercent = ticker.PriceChangePercent / 100,
                                    QuoteVolume24h = ticker.QuoteVolume,
                                    AvgQuoteVolume = avgVolume,
                                    VolumeMultiplier = volumeMultiplierActual
                                };
                            }
                            else if (avgVolume <= 0)
                            {
                                if (processedCount <= 5) // 只记录前5个失败的详细信息
                                {
                                    AddAnalysisLog($"❌ 合约 {ticker.Symbol}: 无法获取历史成交额数据");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Utils.AppSession.Log($"分析合约 {ticker.Symbol} 放量失败: {ex.Message}");
                            AddAnalysisLog($"分析合约 {ticker.Symbol} 放量失败: {ex.Message}");
                        }
                        return null;
                    });

                    var batchResults = await Task.WhenAll(batchTasks);
                    volumeBreakouts.AddRange(batchResults.Where(r => r != null));

                    // 短暂延迟避免API限制
                    await Task.Delay(100, cancellationToken);
                }

                Utils.AppSession.Log($"放量分析处理完成: 处理了 {processedCount} 个合约，发现 {foundCount} 个放量合约");
                AddAnalysisLog($"放量分析处理完成: 处理了 {processedCount} 个合约，发现 {foundCount} 个放量合约");

                // 按放量倍数排序
                var sortedBreakouts = volumeBreakouts
                    .OrderByDescending(v => v.VolumeMultiplier)
                    .Take(50) // 取前50名
                    .Select((v, index) => 
                    {
                        v.Rank = index + 1;
                        return v;
                    })
                    .ToList();

                // 更新UI - 确保在UI线程中执行并保持数据持久性
                await Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        Utils.AppSession.Log($"开始更新放量合约UI，共有 {sortedBreakouts.Count} 个合约");
                        
                        // 清空现有数据
                    _volumeBreakouts.Clear();
                        
                        // 添加新数据
                    foreach (var item in sortedBreakouts)
                    {
                        _volumeBreakouts.Add(item);
                            Utils.AppSession.Log($"添加放量合约到UI: {item.Symbol}, 排名: {item.Rank}, 放量倍数: {item.VolumeMultiplier:F1}");
                        }
                        
                        // 强制刷新DataGrid
                        if (VolumeBreakoutDataGrid != null)
                        {
                            VolumeBreakoutDataGrid.Items.Refresh();
                            Utils.AppSession.Log($"放量合约DataGrid已刷新，当前项数: {VolumeBreakoutDataGrid.Items.Count}");
                        }
                        
                        Utils.AppSession.Log($"✅ 放量合约UI更新完成，ObservableCollection中有 {_volumeBreakouts.Count} 个项目");
                    }
                    catch (Exception uiEx)
                    {
                        Utils.AppSession.Log($"❌ 更新放量合约UI时发生错误: {uiEx.Message}");
                        AddAnalysisLog($"❌ 更新放量合约UI失败: {uiEx.Message}");
                    }
                });

                Utils.AppSession.Log($"放量分析完成: 发现 {sortedBreakouts.Count} 个放量合约，已更新到UI");
                AddAnalysisLog($"✅ 放量分析完成: 发现 {sortedBreakouts.Count} 个放量合约，已成功显示在列表中");
            }
            catch (TaskCanceledException tcEx)
            {
                Utils.AppSession.Log($"分析放量合约超时: {tcEx.Message}");
                AddAnalysisLog("分析放量合约超时，请检查网络连接");
                Utils.NetworkExceptionHandler.LogNetworkException("分析放量合约", tcEx);
                
                Dispatcher.Invoke(() =>
                {
                    ProgressText.Text = "放量分析超时";
                });
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"分析放量合约失败: {ex.Message}");
                Utils.AppSession.Log($"异常堆栈: {ex.StackTrace}");
                AddAnalysisLog($"分析放量合约失败: {ex.Message}");
                
                if (Utils.NetworkExceptionHandler.IsNetworkException(ex))
                {
                    Utils.NetworkExceptionHandler.LogNetworkException("分析放量合约", ex);
                    AddAnalysisLog("网络连接异常，建议检查网络或稍后重试");
                    
                    Dispatcher.Invoke(() =>
                    {
                        ProgressText.Text = "网络异常，放量分析失败";
                    });
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        ProgressText.Text = "放量分析失败";
                    });
                }
            }
        }

        /// <summary>
        /// 获取平均成交量（从数据库kline_data表获取，带缓存功能）
        /// </summary>
        private async Task<decimal> GetAverageVolumeAsync(string symbol, int days, CancellationToken cancellationToken)
        {
            try
            {
                // 详细记录调试信息（针对前几个合约）
                var shouldLog = symbol.GetHashCode() % 50 == 0; // 2%的合约记录详细日志
                
                if (shouldLog)
                {
                    AddAnalysisLog($"🔍 合约 {symbol}: 获取过去 {days} 天的平均成交额（缓存优化）");
                }
                
                // 使用带缓存的成交额分析服务
                var avgVolume = await _volumeAnalysisService.GetAverageVolumeAsync(symbol, days, cancellationToken);
                
                if (avgVolume <= 0)
                {
                    if (shouldLog)
                    {
                        AddAnalysisLog($"❌ 合约 {symbol}: 没有找到有效的成交额数据");
                    }
                    Utils.AppSession.Log($"合约 {symbol}: 没有找到有效的成交额数据");
                    return 0;
                }
                
                if (shouldLog)
                {
                    AddAnalysisLog($"✅ 合约 {symbol}: 获取到过去 {days} 天平均成交额: {avgVolume:F2}");
                }
                
                Utils.AppSession.Log($"合约 {symbol}: 成功获取平均成交额 {avgVolume:F2}（过去 {days} 天）");
                return avgVolume;
            }
            catch (OperationCanceledException)
            {
                Utils.AppSession.Log($"获取 {symbol} 平均成交量被取消");
                return 0;
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"获取 {symbol} 平均成交量失败: {ex.Message}");
                AddAnalysisLog($"❌ 获取 {symbol} 平均成交量失败: {ex.Message}");
                
                // 记录更详细的错误信息
                if (ex.InnerException != null)
                {
                    AddAnalysisLog($"  详细错误: {ex.InnerException.Message}");
                }
                
                return 0;
            }
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

            Utils.AppSession.Log("🔍 ===== 开始突破分析诊断 =====");
            AddAnalysisLog("🔍 开始突破分析，正在诊断数据状态...");

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

            try
            {
                // 诊断步骤1: 检查数据库连接和K线数据
                Utils.AppSession.Log("🔍 步骤1: 检查数据库K线数据...");
                AddAnalysisLog("📊 步骤1: 检查数据库中的K线数据...");
                
                var allDbSymbols = await _databaseService.GetAllSymbolsAsync();
                if (allDbSymbols == null || !allDbSymbols.Any())
                {
                    var errorMsg = "❌ 数据库中没有K线数据！请检查数据库连接和kline_data表";
                    Utils.AppSession.Log(errorMsg);
                    AddAnalysisLog(errorMsg);
                    
                    Dispatcher.Invoke(() =>
                    {
                        ProgressText.Text = "数据库中没有K线数据";
                    });
                    return;
                }
                
                Utils.AppSession.Log($"✅ 数据库中共有 {allDbSymbols.Count} 个合约的K线数据");
                AddAnalysisLog($"✅ 数据库中共有 {allDbSymbols.Count} 个合约的K线数据");
                AddAnalysisLog($"  前10个合约: {string.Join(", ", allDbSymbols.Take(10))}");

                // 诊断步骤2: 检查可交易合约列表
                Utils.AppSession.Log("🔍 步骤2: 获取可交易合约列表...");
                AddAnalysisLog("📈 步骤2: 获取可交易合约列表...");
                
            var tradableSymbols = await GetTradableSymbolsAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

                if (tradableSymbols == null || !tradableSymbols.Any())
                {
                    var errorMsg = "❌ 无法获取可交易合约列表！请检查网络连接";
                    Utils.AppSession.Log(errorMsg);
                    AddAnalysisLog(errorMsg);
                    
                    Dispatcher.Invoke(() =>
                    {
                        ProgressText.Text = "无法获取可交易合约列表";
                    });
                    return;
                }
                
                Utils.AppSession.Log($"✅ 获取到 {tradableSymbols.Count} 个可交易合约");
                AddAnalysisLog($"✅ 获取到 {tradableSymbols.Count} 个可交易合约");
                AddAnalysisLog($"  前10个合约: {string.Join(", ", tradableSymbols.Take(10))}");

                // 诊断步骤3: 检查合约匹配情况
                Utils.AppSession.Log("🔍 步骤3: 检查数据库合约与可交易合约的匹配情况...");
                AddAnalysisLog("🔗 步骤3: 检查合约匹配情况...");
                
                var matchedSymbols = allDbSymbols
                    .Where(symbol => tradableSymbols.Contains(symbol) || 
                                   tradableSymbols.Contains($"{symbol}USDT") ||
                                   tradableSymbols.Contains(symbol.Replace("USDT", "")))
                    .ToList();
                
                Utils.AppSession.Log($"✅ 匹配的合约数量: {matchedSymbols.Count}");
                AddAnalysisLog($"✅ 匹配的合约数量: {matchedSymbols.Count}");
                if (matchedSymbols.Count > 0)
                {
                    AddAnalysisLog($"  匹配的合约示例: {string.Join(", ", matchedSymbols.Take(10))}");
                }

                if (matchedSymbols.Count == 0)
                {
                    var errorMsg = "❌ 数据库合约与可交易合约无匹配！格式可能不一致";
                    Utils.AppSession.Log(errorMsg);
                    AddAnalysisLog(errorMsg);
                    AddAnalysisLog($"  数据库格式示例: {string.Join(", ", allDbSymbols.Take(5))}");
                    AddAnalysisLog($"  可交易格式示例: {string.Join(", ", tradableSymbols.Take(5))}");
                    
                    Dispatcher.Invoke(() =>
                    {
                        ProgressText.Text = "合约格式不匹配，无法分析";
                    });
                    return;
                }

                // 诊断步骤4: 加载历史数据
                Utils.AppSession.Log("🔍 步骤4: 开始加载历史数据...");
                AddAnalysisLog("📚 步骤4: 加载历史数据...");

            await LoadHistoricalData(tradableSymbols, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

                Utils.AppSession.Log($"✅ 历史数据加载完成，缓存中有 {_historicalDataCache.Count} 个合约");
                AddAnalysisLog($"✅ 历史数据加载完成，缓存中有 {_historicalDataCache.Count} 个合约");
                
                if (_historicalDataCache.Count == 0)
                {
                    var errorMsg = "❌ 历史数据缓存为空！可能是数据库中没有符合条件的K线数据";
                    Utils.AppSession.Log(errorMsg);
                    AddAnalysisLog(errorMsg);
                    
                    Dispatcher.Invoke(() =>
                    {
                        ProgressText.Text = "历史数据为空，无法分析";
                    });
                    return;
                }

                // 诊断步骤5: 获取当前价格
                Utils.AppSession.Log("🔍 步骤5: 获取当前价格数据...");
                AddAnalysisLog("💰 步骤5: 获取当前价格数据...");

            var currentPrices = await GetCurrentPrices(tradableSymbols, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

                Utils.AppSession.Log($"✅ 当前价格获取完成，共 {currentPrices.Count} 个合约");
                AddAnalysisLog($"✅ 当前价格获取完成，共 {currentPrices.Count} 个合约");
                
                if (currentPrices.Count == 0)
                {
                    var errorMsg = "❌ 无法获取当前价格数据！请检查网络连接";
                    Utils.AppSession.Log(errorMsg);
                    AddAnalysisLog(errorMsg);
                    
                    Dispatcher.Invoke(() =>
                    {
                        ProgressText.Text = "无法获取当前价格，无法分析";
                    });
                    return;
                }

                // 诊断步骤6: 检查数据匹配情况
                Utils.AppSession.Log("🔍 步骤6: 检查历史数据与当前价格的匹配情况...");
                AddAnalysisLog("🔗 步骤6: 检查数据匹配情况...");
                
                var historicalSymbols = _historicalDataCache.Keys.ToHashSet();
                var priceSymbols = currentPrices.Keys.ToHashSet();
                var commonSymbols = historicalSymbols.Intersect(priceSymbols).ToList();
                
                Utils.AppSession.Log($"✅ 历史数据合约: {historicalSymbols.Count}, 当前价格合约: {priceSymbols.Count}, 共同合约: {commonSymbols.Count}");
                AddAnalysisLog($"✅ 数据匹配: 历史数据 {historicalSymbols.Count} 个，当前价格 {priceSymbols.Count} 个，匹配 {commonSymbols.Count} 个");
                
                if (commonSymbols.Count == 0)
                {
                    var errorMsg = "❌ 历史数据与当前价格无匹配合约！格式不一致";
                    Utils.AppSession.Log(errorMsg);
                    AddAnalysisLog(errorMsg);
                    AddAnalysisLog($"  历史数据示例: {string.Join(", ", historicalSymbols.Take(5))}");
                    AddAnalysisLog($"  当前价格示例: {string.Join(", ", priceSymbols.Take(5))}");
                    
                    Dispatcher.Invoke(() =>
                    {
                        ProgressText.Text = "数据格式不匹配，无法分析";
                    });
                    return;
                }

                // 诊断步骤7: 执行突破分析
                Utils.AppSession.Log("🔍 步骤7: 开始执行突破分析...");
                AddAnalysisLog("🚀 步骤7: 执行突破分析...");

            var breakouts = AnalyzeAllBreakouts(currentPrices);

            // 记录突破分析结果
            Utils.AppSession.Log($"突破分析完成 - 5天新高: {breakouts.Break5DayHigh.Count}, 10天新高: {breakouts.Break10DayHigh.Count}, 20天新高: {breakouts.Break20DayHigh.Count}");
            Utils.AppSession.Log($"突破分析完成 - 5天新低: {breakouts.Break5DayLow.Count}, 10天新低: {breakouts.Break10DayLow.Count}, 20天新低: {breakouts.Break20DayLow.Count}");
                
                AddAnalysisLog($"✅ 突破分析完成!");
                AddAnalysisLog($"  📈 新高突破: 5天 {breakouts.Break5DayHigh.Count} 个, 10天 {breakouts.Break10DayHigh.Count} 个, 20天 {breakouts.Break20DayHigh.Count} 个");
                AddAnalysisLog($"  📉 新低跌破: 5天 {breakouts.Break5DayLow.Count} 个, 10天 {breakouts.Break10DayLow.Count} 个, 20天 {breakouts.Break20DayLow.Count} 个");

                if (breakouts.Break5DayHigh.Count == 0 && breakouts.Break10DayHigh.Count == 0 && 
                    breakouts.Break20DayHigh.Count == 0 && breakouts.Break5DayLow.Count == 0 && 
                    breakouts.Break10DayLow.Count == 0 && breakouts.Break20DayLow.Count == 0)
                {
                    AddAnalysisLog("ℹ️ 当前没有发现突破情况，这可能是正常的市场状态");
                }

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
                        
                    ProgressText.Text = "突破分析完成";
                    AnalysisProgressBar.Value = 100;
                });

                Utils.AppSession.Log("🔍 ===== 突破分析诊断完成 =====");
                AddAnalysisLog("🎉 突破分析诊断完成，数据已更新到界面");
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"❌ 突破分析过程中发生异常: {ex.Message}");
                AddAnalysisLog($"❌ 分析失败: {ex.Message}");
                
                if (ex.InnerException != null)
                {
                    AddAnalysisLog($"  详细错误: {ex.InnerException.Message}");
                }
                
                Dispatcher.Invoke(() =>
                {
                    ProgressText.Text = "突破分析失败";
                });
                
                throw; // 重新抛出异常让上层处理
            }
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
                // 过滤出可交易的合约数据，并验证缓存有效性
                _historicalDataCache.Clear();
                int validCacheCount = 0;
                int invalidCacheCount = 0;
                
                Utils.AppSession.Log($"🔍 缓存文件包含 {cachedData.Count} 个合约");
                Utils.AppSession.Log($"🔍 可交易合约数量: {tradableSymbols.Count}");
                Utils.AppSession.Log($"🔍 可交易合约示例: {string.Join(", ", tradableSymbols.Take(10))}");
                
                foreach (var kvp in cachedData)
                {
                    var symbol = kvp.Key;
                    var data = kvp.Value;
                    
                    // 检查是否为可交易合约
                    bool isMatch1 = tradableSymbols.Contains(symbol);
                    bool isMatch2 = tradableSymbols.Contains($"{symbol}USDT");
                    bool isMatch3 = tradableSymbols.Contains(symbol.Replace("USDT", ""));
                    bool isTradeableSymbol = isMatch1 || isMatch2 || isMatch3;
                    
                    if (validCacheCount < 5) // 只记录前5个的详细匹配情况
                    {
                        Utils.AppSession.Log($"🔍 合约 {symbol}: 直接匹配={isMatch1}, 加USDT匹配={isMatch2}, 去USDT匹配={isMatch3}, 最终={isTradeableSymbol}");
                    }
                    
                    if (isTradeableSymbol)
                    {
                        // 验证缓存数据是否还有效（当天内）
                        if (data.IsValid)
                    {
                            _historicalDataCache[symbol] = data;
                            validCacheCount++;
                        }
                        else
                        {
                            invalidCacheCount++;
                            Utils.AppSession.Log($"合约 {symbol} 缓存数据已过期: 缓存时间={data.CacheTime:yyyy-MM-dd HH:mm:ss}");
                        }
                    }
                }

                Utils.AppSession.Log($"🔍 缓存过滤结果: 原始={cachedData.Count}, 可交易匹配数={validCacheCount + invalidCacheCount}, 有效={validCacheCount}, 过期={invalidCacheCount}");

                if (validCacheCount > 0)
                {
                    Utils.AppSession.Log($"从缓存文件加载历史数据完成，有效缓存: {validCacheCount} 个，过期缓存: {invalidCacheCount} 个");
                
                Dispatcher.Invoke(() =>
                {
                        ProgressText.Text = $"从缓存加载完成 - 有效: {validCacheCount} 个";
                    AnalysisProgressBar.Value = 50;
                });
                return;
                }
                else
                {
                    Utils.AppSession.Log("所有缓存数据都已过期，需要重新获取数据");
                }
            }

            // 如果没有缓存文件，则从数据库获取数据
            Utils.AppSession.Log("未找到今天的缓存文件，开始从数据库获取历史数据...");
            
            // 获取所有合约列表
            var allSymbols = await _databaseService.GetAllSymbolsAsync();
            if (allSymbols == null || !allSymbols.Any())
            {
                throw new InvalidOperationException("数据库中没有K线数据");
            }

            Utils.AppSession.Log($"🔍 数据库合约过滤分析:");
            Utils.AppSession.Log($"🔍 数据库中所有合约数量: {allSymbols.Count}");
            Utils.AppSession.Log($"🔍 数据库合约示例: {string.Join(", ", allSymbols.Take(10))}");
            Utils.AppSession.Log($"🔍 可交易合约数量: {tradableSymbols.Count}");
            Utils.AppSession.Log($"🔍 可交易合约示例: {string.Join(", ", tradableSymbols.Take(10))}");

            // 详细过滤分析
            var directMatches = allSymbols.Where(symbol => tradableSymbols.Contains(symbol)).ToList();
            var usdtMatches = allSymbols.Where(symbol => !tradableSymbols.Contains(symbol) && tradableSymbols.Contains($"{symbol}USDT")).ToList();
            
            Utils.AppSession.Log($"🔍 直接匹配的合约数量: {directMatches.Count}");
            if (directMatches.Count > 0)
                Utils.AppSession.Log($"🔍 直接匹配示例: {string.Join(", ", directMatches.Take(10))}");
            
            Utils.AppSession.Log($"🔍 需要加USDT后缀匹配的合约数量: {usdtMatches.Count}");
            if (usdtMatches.Count > 0)
                Utils.AppSession.Log($"🔍 加USDT匹配示例: {string.Join(", ", usdtMatches.Take(10))}");

            // 过滤出可交易的合约（数据库中的symbol格式可能是BTCUSDT，需要匹配）
            var symbols = allSymbols
                .Where(symbol => tradableSymbols.Contains(symbol) || 
                               tradableSymbols.Contains($"{symbol}USDT"))
                .ToList();

            Utils.AppSession.Log($"🔍 最终过滤结果: 数据库中共有 {allSymbols.Count} 个合约，其中可交易的有 {symbols.Count} 个");
            
            // 如果过滤后的数量明显偏少，进行进一步分析
            if (symbols.Count < allSymbols.Count / 2)
            {
                Utils.AppSession.Log($"⚠️  过滤后的合约数量异常偏少！进行深度分析:");
                
                // 分析数据库中的合约格式
                var dbUsdtSymbols = allSymbols.Where(s => s.EndsWith("USDT")).ToList();
                var dbNonUsdtSymbols = allSymbols.Where(s => !s.EndsWith("USDT")).ToList();
                
                Utils.AppSession.Log($"🔍 数据库中带USDT后缀的合约: {dbUsdtSymbols.Count} 个");
                if (dbUsdtSymbols.Count > 0)
                    Utils.AppSession.Log($"   示例: {string.Join(", ", dbUsdtSymbols.Take(10))}");
                
                Utils.AppSession.Log($"🔍 数据库中不带USDT后缀的合约: {dbNonUsdtSymbols.Count} 个");
                if (dbNonUsdtSymbols.Count > 0)
                    Utils.AppSession.Log($"   示例: {string.Join(", ", dbNonUsdtSymbols.Take(10))}");
                
                // 分析可交易合约格式
                var tradeableUsdtSymbols = tradableSymbols.Where(s => s.EndsWith("USDT")).ToList();
                var tradeableNonUsdtSymbols = tradableSymbols.Where(s => !s.EndsWith("USDT")).ToList();
                
                Utils.AppSession.Log($"🔍 可交易合约中带USDT后缀的: {tradeableUsdtSymbols.Count} 个");
                if (tradeableUsdtSymbols.Count > 0)
                    Utils.AppSession.Log($"   示例: {string.Join(", ", tradeableUsdtSymbols.Take(10))}");
                
                Utils.AppSession.Log($"🔍 可交易合约中不带USDT后缀的: {tradeableNonUsdtSymbols.Count} 个");
                if (tradeableNonUsdtSymbols.Count > 0)
                    Utils.AppSession.Log($"   示例: {string.Join(", ", tradeableNonUsdtSymbols.Take(10))}");
                
                // 尝试其他匹配逻辑
                var altMatches = allSymbols.Where(dbSymbol => 
                {
                    // 尝试多种匹配方式
                    var normalizedDb = dbSymbol.Replace("USDT", "");
                    return tradableSymbols.Any(ts => 
                        ts == dbSymbol || 
                        ts == $"{dbSymbol}USDT" || 
                        ts == normalizedDb ||
                        ts.Replace("USDT", "") == normalizedDb
                    );
                }).ToList();
                
                Utils.AppSession.Log($"🔍 使用增强匹配逻辑的结果: {altMatches.Count} 个合约");
                if (altMatches.Count > symbols.Count)
                {
                    Utils.AppSession.Log($"⚠️  增强匹配找到更多合约，建议优化过滤逻辑");
                    symbols = altMatches; // 使用增强匹配的结果
                }
            }

            var totalSymbols = symbols.Count;
            var processedSymbols = 0;
            var newHistoricalData = new Dictionary<string, HistoricalPriceData>();

            foreach (var symbol in symbols)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // 获取过去25天的数据（确保有足够的数据计算20天最高/最低，考虑周末和节假日）
                    var endDate = DateTime.Now.Date.AddDays(1); // 包含今天
                    var startDate = endDate.AddDays(-25);

                    Utils.AppSession.Log($"合约 {symbol}: 查询K线数据，时间范围 {startDate:yyyy-MM-dd} 到 {endDate:yyyy-MM-dd}");

                    var klineData = await _databaseService.GetKlineDataAsync(symbol, startDate, endDate);
                    if (klineData != null && klineData.Any())
                    {
                        Utils.AppSession.Log($"合约 {symbol} 获取到 {klineData.Count} 条K线数据，时间范围: {klineData.Min(k => k.OpenTime):yyyy-MM-dd} 到 {klineData.Max(k => k.OpenTime):yyyy-MM-dd}");
                        
                        // 确保有足够的数据
                        if (klineData.Count >= 20)
                        {
                            // 按时间排序确保正确性
                            var sortedData = klineData.OrderBy(k => k.OpenTime).ToList();
                            
                            // 记录数据范围用于调试
                            var dataRange = $"时间范围: {sortedData.First().OpenTime:yyyy-MM-dd} 到 {sortedData.Last().OpenTime:yyyy-MM-dd}";
                            var priceRange = $"价格范围: 最高={sortedData.Max(k => k.HighPrice):F2}, 最低={sortedData.Min(k => k.LowPrice):F2}";
                            Utils.AppSession.Log($"合约 {symbol} 排序后的K线数据 - {dataRange}, {priceRange}");
                            
                            var historicalData = new HistoricalPriceData
                            {
                                Symbol = symbol,
                                // 价格数据
                                High5Day = sortedData.TakeLast(5).Max(k => k.HighPrice),
                                Low5Day = sortedData.TakeLast(5).Min(k => k.LowPrice),
                                High10Day = sortedData.TakeLast(10).Max(k => k.HighPrice),
                                Low10Day = sortedData.TakeLast(10).Min(k => k.LowPrice),
                                High20Day = sortedData.TakeLast(20).Max(k => k.HighPrice),
                                Low20Day = sortedData.TakeLast(20).Min(k => k.LowPrice),
                                // 成交额数据（过滤掉0值）
                                AvgQuoteVolume5Day = sortedData.TakeLast(5).Where(k => k.QuoteVolume > 0).DefaultIfEmpty().Average(k => k?.QuoteVolume ?? 0),
                                AvgQuoteVolume7Day = sortedData.TakeLast(7).Where(k => k.QuoteVolume > 0).DefaultIfEmpty().Average(k => k?.QuoteVolume ?? 0),
                                AvgQuoteVolume10Day = sortedData.TakeLast(10).Where(k => k.QuoteVolume > 0).DefaultIfEmpty().Average(k => k?.QuoteVolume ?? 0),
                                AvgQuoteVolume20Day = sortedData.TakeLast(20).Where(k => k.QuoteVolume > 0).DefaultIfEmpty().Average(k => k?.QuoteVolume ?? 0),
                                CacheTime = DateTime.Now
                            };

                            // 统一使用不带USDT后缀的格式作为key，避免重复
                            var normalizedSymbol = symbol.EndsWith("USDT") ? symbol.Replace("USDT", "") : symbol;
                            historicalData.Symbol = normalizedSymbol; // 确保Symbol属性也是标准化的
                            _historicalDataCache[normalizedSymbol] = historicalData;
                            newHistoricalData[normalizedSymbol] = historicalData;
                            
                            Utils.AppSession.Log($"合约 {symbol} 历史数据缓存成功 - 标准化: {normalizedSymbol}");
                            Utils.AppSession.Log($"  价格数据: 5天最高={historicalData.High5Day:F4}, 最低={historicalData.Low5Day:F4}");
                            Utils.AppSession.Log($"  价格数据: 10天最高={historicalData.High10Day:F4}, 最低={historicalData.Low10Day:F4}");
                            Utils.AppSession.Log($"  价格数据: 20天最高={historicalData.High20Day:F4}, 最低={historicalData.Low20Day:F4}");
                            Utils.AppSession.Log($"  成交额: 5天={historicalData.AvgQuoteVolume5Day:F0}, 7天={historicalData.AvgQuoteVolume7Day:F0}");
                        }
                        else
                        {
                            Utils.AppSession.Log($"合约 {symbol} 数据不足，只有 {klineData.Count} 条，需要至少20条");
                            Utils.AppSession.Log($"  数据时间分布: {string.Join(", ", klineData.Take(5).Select(k => k.OpenTime.ToString("MM-dd")))}...");
                        }
                    }
                    else
                    {
                        Utils.AppSession.Log($"合约 {symbol} 未获取到K线数据 - 检查数据库中是否存在此合约的数据");
                        
                        // 尝试检查数据库中是否有任何该合约的数据
                        try
                        {
                            var testData = await _databaseService.GetKlineDataAsync(symbol, DateTime.Now.AddDays(-5), DateTime.Now);
                            Utils.AppSession.Log($"  测试查询结果: {testData?.Count ?? 0} 条数据");
                        }
                        catch (Exception testEx)
                        {
                            Utils.AppSession.Log($"  测试查询失败: {testEx.Message}");
                        }
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
                    Utils.AppSession.Log($"开始获取当前价格，可交易合约数量: {tradableSymbols.Count}");
                    Utils.AppSession.Log($"可交易合约示例: {string.Join(", ", tradableSymbols.Take(10))}");
                    
                    var tickers = await _exchangeService.GetAllTickersAsync();
                    if (tickers != null)
                    {
                        Utils.AppSession.Log($"从交易所获取到 {tickers.Count} 个合约的价格数据");
                        
                        var usdtTickers = tickers.Where(t => t.Symbol.EndsWith("USDT")).ToList();
                        Utils.AppSession.Log($"其中USDT合约有 {usdtTickers.Count} 个");
                        
                        var matchedTickers = usdtTickers.Where(t => tradableSymbols.Contains(t.Symbol)).ToList();
                        Utils.AppSession.Log($"匹配的可交易USDT合约有 {matchedTickers.Count} 个");
                        
                        if (matchedTickers.Count > 0)
                        {
                            Utils.AppSession.Log($"匹配合约示例: {string.Join(", ", matchedTickers.Take(10).Select(t => t.Symbol))}");
                        }

                        foreach (var ticker in matchedTickers)
                        {
                            // 统一使用不带USDT后缀的格式作为key
                            var normalizedSymbol = ticker.Symbol.Replace("USDT", ""); // BTC
                            currentPrices[normalizedSymbol] = ticker.LastPrice;
                            
                            // 记录前几个合约的价格信息
                            if (currentPrices.Count <= 5)
                            {
                                Utils.AppSession.Log($"价格数据: {ticker.Symbol} -> {normalizedSymbol} = {ticker.LastPrice}");
                            }
                        }
                    }
                    else
                    {
                        Utils.AppSession.Log("从交易所获取价格数据失败：返回null");
                    }
                    
                    Utils.AppSession.Log($"✅ 当前价格获取完成，共 {currentPrices.Count} 个合约");
                    
                    if (currentPrices.Count == 0)
                    {
                        Utils.AppSession.Log("❌ 警告：未获取到任何当前价格数据！");
                        Utils.AppSession.Log($"  可交易合约数量: {tradableSymbols.Count}");
                        Utils.AppSession.Log($"  交易所返回ticker数量: {tickers?.Count ?? 0}");
                    }
                }
                catch (Exception ex)
                {
                    Utils.AppSession.Log($"获取当前价格失败: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Utils.AppSession.Log($"  内部异常: {ex.InnerException.Message}");
                    }
                    System.Diagnostics.Debug.WriteLine($"获取当前价格失败: {ex.Message}");
                }
            }
            else
            {
                Utils.AppSession.Log("❌ 交易所服务未初始化，无法获取当前价格");
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
                    Dispatcher.Invoke(() =>
                    {
                        ProgressText.Text = "交易所服务未初始化";
                    });
                    return;
                }

                Dispatcher.Invoke(() =>
                {
                    ProgressText.Text = "正在加载市场排行数据...";
                    AnalysisProgressBar.Value = 0;
                });

                // 获取可交易合约列表
                var tradableSymbols = await GetTradableSymbolsAsync(CancellationToken.None);
                
                Dispatcher.Invoke(() =>
                {
                    ProgressText.Text = "正在获取市场数据...";
                    AnalysisProgressBar.Value = 25;
                });
                
                // 获取最新的ticker数据
                var tickers = await _exchangeService.GetAllTickersAsync();
                if (tickers == null || !tickers.Any())
                {
                    Utils.AppSession.Log("无法获取市场ticker数据");
                    Dispatcher.Invoke(() =>
                    {
                        ProgressText.Text = "无法获取市场数据，请检查网络连接";
                        AnalysisProgressBar.Value = 0;
                    });
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
            catch (TaskCanceledException tcEx)
            {
                Utils.AppSession.Log($"加载初始市场排行数据超时: {tcEx.Message}");
                Dispatcher.Invoke(() =>
                {
                    ProgressText.Text = "获取市场数据超时，请检查网络连接";
                    AnalysisProgressBar.Value = 0;
                });
                
                // 记录网络异常
                Utils.NetworkExceptionHandler.LogNetworkException("加载初始市场排行数据", tcEx);
                
                // 抛出异常让上层处理
                throw;
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"加载初始市场排行数据失败: {ex.Message}");
                Dispatcher.Invoke(() =>
                {
                    ProgressText.Text = $"加载市场数据失败: {ex.Message}";
                    AnalysisProgressBar.Value = 0;
                });
                
                // 如果是网络异常，记录并抛出让上层处理
                if (Utils.NetworkExceptionHandler.IsNetworkException(ex))
                {
                    Utils.NetworkExceptionHandler.LogNetworkException("加载初始市场排行数据", ex);
                    throw;
                }
                
                // 对于非网络异常，只记录日志不抛出
                Utils.AppSession.Log($"非网络异常，程序继续运行: {ex.GetType().Name}");
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
                // 保存设置
                _ = SaveSettingsAsync();
                
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

        /// <summary>
        /// 数据表格双击事件处理 - 打开K线浮窗
        /// </summary>
        private void DataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                var dataGrid = sender as System.Windows.Controls.DataGrid;
                if (dataGrid?.SelectedItem == null) 
                {
                    Utils.AppSession.Log("双击事件：未选中任何项目");
                    return;
                }

                string symbol = null;
                string itemType = "未知";
                
                // 根据不同的数据类型获取合约名称
                if (dataGrid.SelectedItem is MarketRankingItem marketItem)
                {
                    symbol = marketItem.Symbol;
                    itemType = "市场排行";
                }
                else if (dataGrid.SelectedItem is BreakoutItem breakoutItem)
                {
                    symbol = breakoutItem.Symbol;
                    itemType = "突破合约";
                }
                else if (dataGrid.SelectedItem is VolumeBreakoutItem volumeItem)
                {
                    symbol = volumeItem.Symbol;
                    itemType = "放量合约";
                }

                if (!string.IsNullOrEmpty(symbol))
                {
                    Utils.AppSession.Log($"双击打开K线图：{itemType} - {symbol}");
                    OpenKLineWindow(symbol);
                }
                else
                {
                    Utils.AppSession.Log($"双击事件：无法获取合约名称，数据类型：{dataGrid.SelectedItem?.GetType().Name}");
                    AddAnalysisLog("❌ 无法识别选中的合约，请重新选择");
                }
            }
            catch (NullReferenceException nrEx)
            {
                Utils.AppSession.Log($"双击事件处理出现空引用异常: {nrEx.Message}");
                Utils.AppSession.Log($"异常堆栈: {nrEx.StackTrace}");
                AddAnalysisLog("❌ K线图控件未正确初始化，请稍后重试");
                
                MessageBox.Show(
                    "K线图控件初始化异常\n\n" +
                    "可能的原因：\n" +
                    "• K线图控件(chart)为空，未正确初始化\n" +
                    "• 程序启动时UI组件加载不完整\n" +
                    "• 内存不足导致控件创建失败\n" +
                    "• UI线程阻塞或竞争条件\n\n" +
                    "建议解决方案：\n" +
                    "• 等待3-5秒后重新双击\n" +
                    "• 重启程序以重新初始化所有组件\n" +
                    "• 检查系统内存使用情况\n" +
                    "• 如果问题持续，请联系技术支持",
                    "K线图初始化失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"双击事件处理失败: {ex.Message}");
                Utils.AppSession.Log($"异常类型: {ex.GetType().FullName}");
                Utils.AppSession.Log($"异常堆栈: {ex.StackTrace}");
                AddAnalysisLog($"❌ 打开K线图失败: {ex.Message}");
                
                // 检查是否为网络异常
                if (Utils.NetworkExceptionHandler.IsNetworkException(ex))
                {
                    Utils.NetworkExceptionHandler.ShowNetworkExceptionDialog(
                        this, ex, "打开K线图时网络连接失败", false);
                }
                else
                {
                    MessageBox.Show(
                        $"打开K线图失败：{ex.Message}\n\n" +
                        "可能的原因：\n" +
                        "• 程序资源不足或内存不够\n" +
                        "• K线图控件初始化异常\n" +
                        "• 系统环境或显示驱动问题\n" +
                        "• UI组件加载失败\n\n" +
                        "建议：\n" +
                        "• 稍后重试或重启程序\n" +
                        "• 关闭其他占用内存的程序\n" +
                        "• 更新显卡驱动程序",
                        "K线图打开失败",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 打开K线浮窗（防止重复打开）
        /// </summary>
        private void OpenKLineWindow(string symbol)
        {
            try
            {
                Utils.AppSession.Log($"🔨 准备打开K线浮窗: {symbol}");
                
                // 使用锁确保线程安全
                lock (_windowLock)
                {
                    // 检查是否已经打开了该合约的窗口
                    if (_openKLineWindows.TryGetValue(symbol, out var existingWindow))
                    {
                        try
                        {
                            if (existingWindow != null && existingWindow.IsLoaded)
                            {
                                Utils.AppSession.Log($"⚠️ 合约 {symbol} 的K线窗口已经打开，激活现有窗口");
                                AddAnalysisLog($"💡 合约 {symbol} 的K线窗口已存在，激活窗口");
                                
                                // 激活现有窗口
                                existingWindow.Activate();
                                existingWindow.WindowState = WindowState.Normal; // 如果被最小化则还原
                                return;
                            }
                        }
                        catch
                        {
                            // 如果访问窗口属性出错，说明窗口已关闭
                        }
                        
                        // 清理已关闭的窗口引用
                        _openKLineWindows.Remove(symbol);
                        Utils.AppSession.Log($"🧹 清理合约 {symbol} 的已关闭窗口引用");
                    }
                    
                    // 检查必要的服务是否可用
                    if (_exchangeService == null)
                    {
                        Utils.AppSession.Log("❌ 交易所服务未初始化");
                        AddAnalysisLog("❌ 交易所服务未初始化，无法打开K线图");
                        MessageBox.Show(
                            "交易所服务未初始化\n\n" +
                            "这通常表示程序启动过程中出现了问题。\n\n" +
                            "请重启程序以重新初始化所有服务。",
                            "服务初始化失败",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        return;
                    }

                    Utils.AppSession.Log($"✅ 交易所服务正常，开始创建新的K线窗口");
                var klineWindow = new KLineFloatingWindow(symbol, _exchangeService);
                    
                    // 添加窗口关闭事件处理，确保从字典中移除
                    klineWindow.Closed += (sender, e) =>
                    {
                        lock (_windowLock)
                        {
                            _openKLineWindows.Remove(symbol);
                            Utils.AppSession.Log($"🔄 K线窗口关闭，移除合约 {symbol} 的窗口引用");
                        }
                    };
                    
                    // 添加到字典中
                    _openKLineWindows[symbol] = klineWindow;
                    
                    Utils.AppSession.Log($"✅ K线浮窗创建成功，准备显示");
                klineWindow.Show();
                    
                    Utils.AppSession.Log($"✅ 成功打开合约 {symbol} 的K线浮窗");
                AddAnalysisLog($"📈 打开K线图: {symbol}");
                }
            }
            catch (NullReferenceException nrEx)
            {
                Utils.AppSession.Log($"❌ 创建K线浮窗时出现空引用异常: {nrEx.Message}");
                Utils.AppSession.Log($"异常堆栈: {nrEx.StackTrace}");
                AddAnalysisLog($"❌ K线图控件初始化失败: chart为null");
                
                MessageBox.Show(
                    $"K线图控件初始化失败 (chart为空)\n\n" +
                    "检测到的问题：\n" +
                    "• K线图控件(chart)在创建时为null\n" +
                    "• 可能是XAML布局加载异常\n" +
                    "• UI组件依赖项缺失\n\n" +
                    "详细错误：{nrEx.Message}\n\n" +
                    "解决方案：\n" +
                    "• 重启程序重新加载UI组件\n" +
                    "• 检查.NET Framework版本\n" +
                    "• 确保WPF运行时完整安装",
                    "K线图控件异常",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (ArgumentException argEx)
            {
                Utils.AppSession.Log($"❌ 创建K线浮窗时参数异常: {argEx.Message}");
                AddAnalysisLog($"❌ 合约参数异常: {symbol}");
                
                MessageBox.Show(
                    $"合约参数无效：{symbol}\n\n" +
                    "错误详情：{argEx.Message}\n\n" +
                    "请检查合约名称是否正确。",
                    "参数错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (OutOfMemoryException memEx)
            {
                Utils.AppSession.Log($"❌ 内存不足，无法创建K线浮窗: {memEx.Message}");
                AddAnalysisLog("❌ 系统内存不足，无法打开K线图");
                
                MessageBox.Show(
                    "系统内存不足，无法创建K线图窗口\n\n" +
                    "建议解决方案：\n" +
                    "• 关闭其他应用程序释放内存\n" +
                    "• 重启程序\n" +
                    "• 增加系统虚拟内存",
                    "内存不足",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"❌ 打开K线浮窗失败: {ex.Message}");
                Utils.AppSession.Log($"异常类型: {ex.GetType().FullName}");
                Utils.AppSession.Log($"异常堆栈: {ex.StackTrace}");
                AddAnalysisLog($"❌ 打开K线浮窗失败: {ex.Message}");
                
                // 检查是否为网络异常
                if (Utils.NetworkExceptionHandler.IsNetworkException(ex))
                {
                    Utils.NetworkExceptionHandler.ShowNetworkExceptionDialog(
                        this, ex, "创建K线图窗口时网络连接失败", false);
                }
                else
                {
                    MessageBox.Show(
                        $"打开K线浮窗失败\n\n" +
                        "错误类型：{ex.GetType().Name}\n" +
                        "错误消息：{ex.Message}\n\n" +
                        "可能的原因：\n" +
                        "• UI组件初始化失败\n" +
                        "• 显示驱动或硬件问题\n" +
                        "• 程序文件损坏\n" +
                        "• 系统环境配置异常\n\n" +
                        "建议：\n" +
                        "• 重启程序\n" +
                        "• 更新显卡驱动\n" +
                        "• 重新安装程序",
                        "K线浮窗创建失败",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
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
    /// 历史价格数据（包含价格和成交额缓存）
    /// </summary>
    public class HistoricalPriceData
    {
        public string Symbol { get; set; }
        
        // 价格相关数据
        public decimal High5Day { get; set; }
        public decimal Low5Day { get; set; }
        public decimal High10Day { get; set; }
        public decimal Low10Day { get; set; }
        public decimal High20Day { get; set; }
        public decimal Low20Day { get; set; }
        
        // 成交额相关数据
        public decimal AvgQuoteVolume5Day { get; set; }  // 5天平均成交额
        public decimal AvgQuoteVolume7Day { get; set; }  // 7天平均成交额
        public decimal AvgQuoteVolume10Day { get; set; } // 10天平均成交额
        public decimal AvgQuoteVolume20Day { get; set; } // 20天平均成交额
        
        // 缓存时间戳，用于验证数据新鲜度
        public DateTime CacheTime { get; set; } = DateTime.Now;
        
        /// <summary>
        /// 检查缓存是否还有效（当天内有效）
        /// </summary>
        public bool IsValid => CacheTime.Date == DateTime.Now.Date;
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

    /// <summary>
    /// 放量突破项目
    /// </summary>
    public class VolumeBreakoutItem
    {
        public int Rank { get; set; }
        public string Symbol { get; set; }
        public decimal CurrentPrice { get; set; }
        public decimal ChangePercent { get; set; }
        public decimal QuoteVolume24h { get; set; } // 24小时成交额（USDT）
        public decimal AvgQuoteVolume { get; set; } // 平均成交额
        public double VolumeMultiplier { get; set; } // 放量倍数
        
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
        /// 格式化显示平均成交额
        /// </summary>
        public string FormattedAvgQuoteVolume
        {
            get
            {
                if (AvgQuoteVolume >= 100000000) // 1亿以上显示亿
                {
                    return $"{AvgQuoteVolume / 100000000:F2}亿";
                }
                else if (AvgQuoteVolume >= 10000) // 1万以上显示万
                {
                    return $"{AvgQuoteVolume / 10000:F2}万";
                }
                else
                {
                    return $"{AvgQuoteVolume:F0}";
                }
            }
        }
    }
} 