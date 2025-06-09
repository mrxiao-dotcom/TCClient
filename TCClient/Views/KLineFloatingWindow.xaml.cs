using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TCClient.Services;
using TCClient.Utils;

namespace TCClient.Views
{
    /// <summary>
    /// K线浮窗
    /// </summary>
    public partial class KLineFloatingWindow : Window
    {
        private readonly IExchangeService _exchangeService;
        private string _currentSymbol;
        
        // 最近浏览的合约队列（最多保存20个）
        private static readonly Queue<RecentSymbolItem> _recentSymbolsQueue = new Queue<RecentSymbolItem>();
        private static readonly Dictionary<string, RecentSymbolItem> _recentSymbolsDict = new Dictionary<string, RecentSymbolItem>();
                private readonly ObservableCollection<RecentSymbolItem> _recentSymbols = new ObservableCollection<RecentSymbolItem>();

        private const int MAX_RECENT_SYMBOLS = 20;
        private static int _globalMAPeriod = 20; // 全局均线参数
        
        /// <summary>
        /// 格式化symbol确保有正确的USDT后缀
        /// </summary>
        private static string FormatSymbolWithUSDT(string symbol)
        {
            if (string.IsNullOrEmpty(symbol)) return string.Empty;
            
            string formatted = symbol.ToUpper().Trim();
            
            // 如果已经有USDT后缀，直接返回
            if (formatted.EndsWith("USDT"))
                return formatted;
            
            // 添加USDT后缀
            return $"{formatted}USDT";
        }
        
        /// <summary>
        /// 获取基础symbol（移除USDT后缀）
        /// </summary>
        private static string GetBaseSymbol(string symbol)
        {
            if (string.IsNullOrEmpty(symbol)) return string.Empty;
            
            string formatted = symbol.ToUpper().Trim();
            
            // 如果有USDT后缀，移除它
            if (formatted.EndsWith("USDT"))
                return formatted.Substring(0, formatted.Length - 4);
            
            return formatted;
        }

        public KLineFloatingWindow(string symbol, IExchangeService exchangeService)
        {
            try
            {
                Utils.AppSession.Log($"🔨 开始初始化K线浮窗: {symbol}");
                
                // 检查参数
                if (exchangeService == null)
                {
                    throw new ArgumentNullException(nameof(exchangeService), "交易所服务不能为空");
                }
                
                Utils.AppSession.Log("📋 初始化窗口组件...");
                InitializeComponent();
                _exchangeService = exchangeService;
                
                Utils.AppSession.Log("📊 绑定数据源...");
                // 检查关键控件是否正确加载
                if (RecentSymbolsDataGrid == null)
                {
                    Utils.AppSession.Log("⚠️ 警告: RecentSymbolsDataGrid为null");
                    throw new InvalidOperationException("RecentSymbolsDataGrid控件未正确加载，XAML可能存在问题");
                }
                
                // 绑定数据源
                RecentSymbolsDataGrid.ItemsSource = _recentSymbols;
                
                // 添加窗口关闭事件处理
                this.Closing += KLineFloatingWindow_Closing;
                
                Utils.AppSession.Log("🎨 初始化K线图控件...");
                // 初始化K线图控件
                InitializeKLineCharts();
                
                Utils.AppSession.Log("⚙️ 加载设置...");
                // 加载全局设置
                _ = LoadGlobalMASettingsAsync();
                
                Utils.AppSession.Log("📝 加载最近浏览记录...");
                // 加载最近浏览列表
                LoadRecentSymbols();
                
                // 如果提供了合约，则加载该合约
                if (!string.IsNullOrEmpty(symbol))
                {
                    var baseSymbol = GetBaseSymbol(symbol);
                    Utils.AppSession.Log($"📈 开始加载指定合约: 输入={symbol}, 基础={baseSymbol}");
                    _ = LoadSymbolAsync(baseSymbol);
                }
                
                Utils.AppSession.Log($"✅ K线浮窗初始化完成: {symbol}");
            }
            catch (ArgumentNullException argEx)
            {
                Utils.AppSession.Log($"❌ K线浮窗初始化失败 - 参数为空: {argEx.ParamName}");
                throw; // 重新抛出，让调用者处理
            }
            catch (InvalidOperationException invEx)
            {
                Utils.AppSession.Log($"❌ K线浮窗初始化失败 - 控件加载异常: {invEx.Message}");
                throw; // 重新抛出，让调用者处理
            }
            catch (NullReferenceException nrEx)
            {
                Utils.AppSession.Log($"❌ K线浮窗初始化失败 - 空引用异常: {nrEx.Message}");
                Utils.AppSession.Log($"异常堆栈: {nrEx.StackTrace}");
                
                // 包装成更有用的异常信息
                throw new InvalidOperationException(
                    "K线图控件(chart)初始化失败，可能是XAML布局加载异常或UI组件依赖项缺失。" +
                    "请重启程序或检查.NET Framework版本。",
                    nrEx);
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"❌ K线浮窗初始化失败 - 未知异常: {ex.Message}");
                Utils.AppSession.Log($"异常类型: {ex.GetType().FullName}");
                Utils.AppSession.Log($"异常堆栈: {ex.StackTrace}");
                throw; // 重新抛出，让调用者处理
            }
        }

        /// <summary>
        /// 初始化K线图控件
        /// </summary>
        private void InitializeKLineCharts()
        {
            try
            {
                Utils.AppSession.Log("🎨 开始初始化各个周期的K线图控件...");
                
                // 检查所有K线图控件是否已正确加载
                var charts = new[]
                {
                    (Name: "KLine5mChart", Chart: KLine5mChart),
                    (Name: "KLine30mChart", Chart: KLine30mChart),
                    (Name: "KLine1hChart", Chart: KLine1hChart),
                    (Name: "KLine1dChart", Chart: KLine1dChart)
                };
                
                foreach (var chartInfo in charts)
                {
                    if (chartInfo.Chart == null)
                    {
                        var errorMsg = $"K线图控件 {chartInfo.Name} 为null，XAML加载可能存在问题";
                        Utils.AppSession.Log($"❌ {errorMsg}");
                        throw new InvalidOperationException(errorMsg);
                    }
                    Utils.AppSession.Log($"✅ {chartInfo.Name} 控件检查通过");
                }
                
                Utils.AppSession.Log("🔧 初始化5分钟K线图...");
                KLine5mChart.Initialize(_exchangeService);
                
                Utils.AppSession.Log("🔧 初始化30分钟K线图...");
                KLine30mChart.Initialize(_exchangeService);
                
                Utils.AppSession.Log("🔧 初始化1小时K线图...");
                KLine1hChart.Initialize(_exchangeService);
                
                Utils.AppSession.Log("🔧 初始化1天K线图...");
                KLine1dChart.Initialize(_exchangeService);
                
                UpdateStatus("K线图控件初始化完成");
                Utils.AppSession.Log("✅ 所有K线图控件初始化完成");
            }
            catch (NullReferenceException nrEx)
            {
                var errorMsg = $"K线图控件初始化时出现空引用异常: {nrEx.Message}";
                Utils.AppSession.Log($"❌ {errorMsg}");
                Utils.AppSession.Log($"异常堆栈: {nrEx.StackTrace}");
                UpdateStatus("❌ K线图控件初始化失败(chart为null)");
                
                // 包装成更有用的异常
                throw new InvalidOperationException(
                    "K线图控件(chart)为空，这通常表示XAML布局文件加载失败或UI组件依赖项缺失。" +
                    "请检查XAML文件是否正确，或重启程序重新加载UI组件。",
                    nrEx);
            }
            catch (InvalidOperationException invEx)
            {
                Utils.AppSession.Log($"❌ K线图控件状态异常: {invEx.Message}");
                UpdateStatus($"❌ K线图控件状态异常: {invEx.Message}");
                throw; // 重新抛出
            }
            catch (Exception ex)
            {
                var errorMsg = $"K线图控件初始化失败: {ex.Message}";
                Utils.AppSession.Log($"❌ {errorMsg}");
                Utils.AppSession.Log($"异常类型: {ex.GetType().FullName}");
                Utils.AppSession.Log($"异常堆栈: {ex.StackTrace}");
                UpdateStatus($"❌ K线图控件初始化失败: {ex.Message}");
                throw; // 重新抛出，让调用者处理
            }
        }

        /// <summary>
        /// 从本地文件加载最近浏览的合约列表（队列方式展示）
        /// </summary>
        private async void LoadRecentSymbols()
        {
            try
            {
                Utils.AppSession.Log("📂 开始从本地文件加载最近浏览的合约列表（队列模式）");
                
                // 从本地文件加载
                var savedSymbols = await Utils.SettingsManager.LoadRecentSymbolsAsync();
                
                // 清空所有容器
                _recentSymbolsQueue.Clear();
                _recentSymbolsDict.Clear();
                _recentSymbols.Clear();
                
                if (savedSymbols != null && savedSymbols.Count > 0)
                {
                    Utils.AppSession.Log($"📋 从本地文件读取到 {savedSymbols.Count} 条浏览记录");
                    
                    // 按访问时间降序排列（最近访问的在前面，队列展示）
                    var sortedSymbols = savedSymbols
                        .OrderByDescending(s => s.LastViewTime)
                        .Take(MAX_RECENT_SYMBOLS)
                        .ToList();
                    
                    Utils.AppSession.Log($"📊 排序后取前 {MAX_RECENT_SYMBOLS} 条记录，实际获得 {sortedSymbols.Count} 条");
                    
                    // 按队列方式填充容器
                    foreach (var symbol in sortedSymbols)
                    {
                        var recentItem = new RecentSymbolItem
                        {
                            Symbol = symbol.Symbol,
                            CurrentPrice = symbol.CurrentPrice,
                            ChangePercent = symbol.ChangePercent,
                            IsPositive = symbol.IsPositive,
                            LastViewTime = symbol.LastViewTime
                        };
                        
                        // 队列方式：新的在前面
                        _recentSymbolsQueue.Enqueue(recentItem);
                        _recentSymbolsDict[symbol.Symbol] = recentItem;
                        _recentSymbols.Add(recentItem);
                        
                        Utils.AppSession.Log($"📝 加载记录: {symbol.Symbol} - {symbol.LastViewTime:MM-dd HH:mm} - {symbol.CurrentPrice:F4}");
                    }
                    
                    Utils.AppSession.Log($"✅ 成功加载了 {_recentSymbols.Count} 个最近浏览的合约到队列中");
                    Utils.AppSession.Log($"📊 队列状态: 队列={_recentSymbolsQueue.Count}, 字典={_recentSymbolsDict.Count}, UI集合={_recentSymbols.Count}");
                }
                else
                {
                    Utils.AppSession.Log("📝 没有找到保存的最近浏览记录，开始使用空队列");
                }
                
                // 刷新UI展示队列内容
                await RefreshRecentSymbolsUI();
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"❌ 从本地文件加载最近浏览合约失败: {ex.Message}");
                Utils.AppSession.Log($"异常详情: {ex.StackTrace}");
                
                // 显示内存中的数据作为备用
                _recentSymbols.Clear();
                foreach (var item in _recentSymbolsQueue.Reverse())
                {
                    _recentSymbols.Add(item);
                }
                
                Utils.AppSession.Log($"🔄 使用内存备用数据，共 {_recentSymbols.Count} 条记录");
            }
        }
        
        /// <summary>
        /// 刷新最近浏览记录的UI展示
        /// </summary>
        private async Task RefreshRecentSymbolsUI()
        {
            try
            {
                await Task.Run(() =>
                {
                    // 在UI线程中刷新界面
                    Dispatcher.Invoke(() =>
                    {
                        Utils.AppSession.Log($"🖥️ UI线程刷新浏览记录，当前队列包含 {_recentSymbols.Count} 个项目");
                        
                        // 强制刷新DataGrid
                        if (RecentSymbolsDataGrid != null)
                        {
                            RecentSymbolsDataGrid.Items.Refresh();
                            Utils.AppSession.Log($"📊 浏览记录DataGrid已刷新完成");
                            
                            // 显示队列统计信息
                            if (_recentSymbols.Count > 0)
                            {
                                var newest = _recentSymbols.FirstOrDefault();
                                var oldest = _recentSymbols.LastOrDefault();
                                Utils.AppSession.Log($"📈 队列范围: 最新={newest?.Symbol}({newest?.LastViewTime:MM-dd HH:mm}), 最旧={oldest?.Symbol}({oldest?.LastViewTime:MM-dd HH:mm})");
                            }
                        }
                        else
                        {
                            Utils.AppSession.Log("⚠️ RecentSymbolsDataGrid为null，无法刷新UI");
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"❌ 刷新浏览记录UI失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 更新浏览记录后刷新UI（专用于AddToRecentSymbolsAsync）
        /// </summary>
        private async Task RefreshRecentSymbolsAfterUpdate()
        {
            try
            {
                await Task.Run(() =>
                {
                    // 在UI线程中更新ObservableCollection
                    Dispatcher.Invoke(() =>
                    {
                        // 清空并重新填充UI集合（按队列顺序，最新的在前面）
                        _recentSymbols.Clear();
                        foreach (var item in _recentSymbolsQueue)
                        {
                            _recentSymbols.Add(item);
                        }
                        
                        Utils.AppSession.Log($"📊 UI已更新，显示 {_recentSymbols.Count} 个最近浏览合约（队列模式）");
                        
                        // 显示队列中前3个项目的详情
                        var topItems = _recentSymbols.Take(3).ToList();
                        for (int i = 0; i < topItems.Count; i++)
                        {
                            var item = topItems[i];
                            Utils.AppSession.Log($"📋 队列第{i+1}位: {item.Symbol} ({item.LastViewTime:MM-dd HH:mm})");
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"❌ 更新浏览记录UI失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 保存最近浏览列表到本地
        /// </summary>
        private async Task SaveRecentSymbolsAsync()
        {
            try
            {
                Utils.AppSession.Log($"💾 准备保存 {_recentSymbolsQueue.Count} 个最近浏览合约到本地");
                
                var symbolsToSave = _recentSymbolsQueue.Select(item => new Utils.SettingsManager.RecentSymbolItem
                {
                    Symbol = item.Symbol,
                    CurrentPrice = item.CurrentPrice,
                    ChangePercent = item.ChangePercent,
                    IsPositive = item.IsPositive,
                    LastViewTime = item.LastViewTime
                }).ToList();
                
                Utils.AppSession.Log($"💾 转换数据格式完成，开始写入文件...");
                foreach (var item in symbolsToSave.Take(3)) // 显示前3个作为示例
                {
                    Utils.AppSession.Log($"💾   - {item.Symbol}: {item.CurrentPrice:F4}, {item.ChangePercent:P2}, {item.LastViewTime:yyyy-MM-dd HH:mm:ss}");
                }
                
                await Utils.SettingsManager.SaveRecentSymbolsAsync(symbolsToSave);
                Utils.AppSession.Log($"✅ 成功保存 {symbolsToSave.Count} 个最近浏览合约到本地");
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"❌ 保存最近浏览合约失败: {ex.Message}");
                Utils.AppSession.Log($"❌ 异常详情: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// 添加合约到最近浏览
        /// </summary>
        private async Task AddToRecentSymbolsAsync(string symbol)
        {
            try
            {
                Utils.AppSession.Log($"📝 开始添加合约到最近浏览: {symbol}");
                
                // 如果已存在，先移除
                if (_recentSymbolsDict.ContainsKey(symbol))
                {
                    Utils.AppSession.Log($"📝 合约 {symbol} 已存在，先移除旧记录");
                    var existingItem = _recentSymbolsDict[symbol];
                    var queueArray = _recentSymbolsQueue.ToArray();
                    _recentSymbolsQueue.Clear();
                    foreach (var item in queueArray.Where(x => x.Symbol != symbol))
                    {
                        _recentSymbolsQueue.Enqueue(item);
                    }
                    _recentSymbolsDict.Remove(symbol);
                }

                // 获取当前价格信息
                Utils.AppSession.Log($"🔍 正在获取 {symbol} 的价格信息...");
                Models.TickerInfo ticker = null;
                try
                {
                    var fullSymbol = FormatSymbolWithUSDT(symbol);
                    Utils.AppSession.Log($"🔍 格式化后的交易对: {fullSymbol}");
                    ticker = await _exchangeService.GetTickerAsync(fullSymbol);
                    if (ticker != null)
                    {
                        Utils.AppSession.Log($"✅ 成功获取 {symbol} 价格信息: {ticker.LastPrice:F4}");
                    }
                    else
                    {
                        Utils.AppSession.Log($"⚠️ 获取 {symbol} 价格信息返回null，可能是网络连接问题");
                        Utils.AppSession.Log($"⚠️ 这通常是由网络超时或连接问题引起的，使用默认值");
                    }
                }
                catch (Exception ex)
                {
                    Utils.AppSession.Log($"❌ 获取 {symbol} 价格信息失败: {ex.Message}");
                    Utils.AppSession.Log($"❌ 可能的原因：网络连接问题、API服务器响应慢等，使用默认值");
                }

                var recentItem = new RecentSymbolItem
                {
                    Symbol = symbol,
                    CurrentPrice = ticker?.LastPrice ?? 0,
                    ChangePercent = (ticker?.PriceChangePercent ?? 0) / 100,
                    IsPositive = (ticker?.PriceChangePercent ?? 0) >= 0,
                    LastViewTime = DateTime.Now
                };

                Utils.AppSession.Log($"📊 创建最近浏览项: {symbol}, 价格: {recentItem.CurrentPrice:F4}, 涨跌幅: {recentItem.ChangePercent:P2}, 时间: {recentItem.LastViewTime:yyyy-MM-dd HH:mm:ss}");

                // 将新项添加到队列前面（最近访问的在前面）
                var tempList = new List<RecentSymbolItem> { recentItem };
                tempList.AddRange(_recentSymbolsQueue.Where(x => x.Symbol != symbol));
                
                // 重建队列
                _recentSymbolsQueue.Clear();
                foreach (var item in tempList)
                {
                    _recentSymbolsQueue.Enqueue(item);
                }
                _recentSymbolsDict[symbol] = recentItem;

                Utils.AppSession.Log($"📋 队列重建完成，当前包含 {_recentSymbolsQueue.Count} 个项目");

                // 如果超过最大数量，移除最旧的（队列末尾的）
                while (_recentSymbolsQueue.Count > MAX_RECENT_SYMBOLS)
                {
                    var oldestItem = _recentSymbolsQueue.Dequeue();
                    _recentSymbolsDict.Remove(oldestItem.Symbol);
                    Utils.AppSession.Log($"📝 移除最旧的记录: {oldestItem.Symbol}");
                }

                Utils.AppSession.Log($"📝 当前最近浏览列表包含 {_recentSymbolsQueue.Count} 个合约");

                // 更新UI显示队列内容（最新的在前面）
                await RefreshRecentSymbolsAfterUpdate();
                
                // 保存到本地
                Utils.AppSession.Log($"💾 开始保存最近浏览记录到本地...");
                await SaveRecentSymbolsAsync();
                Utils.AppSession.Log($"✅ 成功添加 {symbol} 到最近浏览并保存到本地");
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"❌ 添加到最近浏览失败: {ex.Message}");
                Utils.AppSession.Log($"❌ 异常详情: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// 加载指定合约的K线数据
        /// </summary>
        private async Task LoadSymbolAsync(string symbol)
        {
            try
            {
                // 确保_currentSymbol存储的是基础symbol（不带USDT）
                var baseSymbol = GetBaseSymbol(symbol);
                _currentSymbol = baseSymbol;
                
                Utils.AppSession.Log($"🔨 LoadSymbolAsync: 输入symbol={symbol}, 基础symbol={baseSymbol}");
                
                CurrentSymbolTextBlock.Text = $"{baseSymbol} - K线图";
                this.Title = $"{baseSymbol} - K线图";
                
                UpdateStatus("开始加载K线数据...");
                
                // 添加到最近浏览
                await AddToRecentSymbolsAsync(symbol);
                
                // 并行加载所有周期的K线数据
                var tasks = new[]
                {
                    LoadKLineDataAsync("5m", KLine5mChart),
                    LoadKLineDataAsync("30m", KLine30mChart),
                    LoadKLineDataAsync("1h", KLine1hChart),
                    LoadKLineDataAsync("1d", KLine1dChart)
                };

                await Task.WhenAll(tasks);
                
                UpdateStatus("所有K线数据加载完成");
                await UpdateContractInfoAsync();
            }
            catch (Exception ex)
            {
                UpdateStatus($"加载K线数据失败: {ex.Message}");
                Utils.AppSession.Log($"加载K线数据失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 加载指定周期的K线数据
        /// </summary>
        private async Task LoadKLineDataAsync(string period, Controls.KLineChartControl chart)
        {
            try
            {
                // 添加chart null检查
                if (chart == null)
                {
                    UpdateStatus($"⚠️ {period} 周期图表控件未初始化");
                    Utils.AppSession.Log($"LoadKLineDataAsync: {period} 周期的chart参数为null");
                    return;
                }

                // 检查交易所服务是否可用
                if (_exchangeService == null)
                {
                    UpdateStatus($"⚠️ 交易所服务未初始化");
                    Utils.AppSession.Log($"LoadKLineDataAsync: 交易所服务为null");
                    return;
                }

                UpdateStatus($"正在加载 {period} 周期数据...");
                var fullSymbol = FormatSymbolWithUSDT(_currentSymbol);
                Utils.AppSession.Log($"开始加载 {fullSymbol} 的 {period} 周期数据");
                
                await chart.SetSymbolAsync(fullSymbol);
                chart.UpdatePeriod(period);
                
                UpdateStatus($"{period} 周期数据加载完成");
                Utils.AppSession.Log($"{fullSymbol} 的 {period} 周期数据加载成功");
            }
            catch (Exception ex)
            {
                UpdateStatus($"加载 {period} 周期数据失败: {ex.Message}");
                Utils.AppSession.Log($"加载 {period} 周期数据失败: {ex.Message}");
                Utils.AppSession.Log($"异常详情: {ex.StackTrace}");
                
                // 检查是否为网络异常，如果是则显示弹窗
                if (Utils.NetworkExceptionHandler.IsNetworkException(ex))
                {
                    Utils.NetworkExceptionHandler.HandleNetworkException(ex, $"加载{period}周期K线数据");
                }
            }
        }

        /// <summary>
        /// 更新合约信息
        /// </summary>
        private async Task UpdateContractInfoAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_currentSymbol)) return;

                // 获取当前价格信息
                var fullSymbol = FormatSymbolWithUSDT(_currentSymbol);
                Utils.AppSession.Log($"🔍 获取合约信息，格式化后的交易对: {fullSymbol}");
                var ticker = await _exchangeService.GetTickerAsync(fullSymbol);
                if (ticker != null)
                {
                    ContractInfoTextBlock.Text = $"合约: {_currentSymbol}";
                    PriceInfoTextBlock.Text = $"当前价: {ticker.LastPrice:F4} | 涨跌幅: {ticker.PriceChangePercent:P2}";
                    VolumeInfoTextBlock.Text = $"24h成交额: {FormatVolume(ticker.QuoteVolume)} | 24h成交量: {FormatVolume(ticker.Volume)}";
                }
                else
                {
                    // ticker为null时的处理
                    ContractInfoTextBlock.Text = $"合约: {_currentSymbol} (价格获取失败)";
                    PriceInfoTextBlock.Text = "❌ 价格信息获取失败，可能是网络连接问题";
                    VolumeInfoTextBlock.Text = "💡 建议：点击菜单栏'设置' > '网络诊断'进行网络检测和修复";
                    Utils.AppSession.Log($"获取合约 {_currentSymbol} 的价格信息失败，ticker为null");
                    
                    // 如果是BinanceExchangeService，尝试自动重试
                    if (_exchangeService is BinanceExchangeService binanceService)
                    {
                        Utils.AppSession.Log($"🔄 尝试自动重试网络连接...");
                        var retrySuccess = await binanceService.ForceRetryConnectionAsync();
                        if (retrySuccess)
                        {
                            Utils.AppSession.Log($"✅ 网络连接重试成功，重新获取价格信息");
                            // 重新获取价格信息
                            var retryTicker = await _exchangeService.GetTickerAsync(fullSymbol);
                            if (retryTicker != null)
                            {
                                ContractInfoTextBlock.Text = $"合约: {_currentSymbol}";
                                PriceInfoTextBlock.Text = $"当前价: {retryTicker.LastPrice:F4} | 涨跌幅: {retryTicker.PriceChangePercent:P2}";
                                VolumeInfoTextBlock.Text = $"24h成交额: {FormatVolume(retryTicker.QuoteVolume)} | 24h成交量: {FormatVolume(retryTicker.Volume)}";
                                Utils.AppSession.Log($"✅ 重试后成功获取 {_currentSymbol} 价格信息");
                            }
                        }
                        else
                        {
                            Utils.AppSession.Log($"❌ 网络连接重试失败");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ContractInfoTextBlock.Text = $"获取合约信息失败: {ex.Message}";
                Utils.AppSession.Log($"获取合约信息失败: {ex.Message}");
                
                // 检查是否为网络异常，如果是则显示弹窗
                if (Utils.NetworkExceptionHandler.IsNetworkException(ex))
                {
                    Utils.NetworkExceptionHandler.HandleNetworkException(ex, "获取合约信息");
                }
            }
        }

        /// <summary>
        /// 格式化成交量显示
        /// </summary>
        private string FormatVolume(decimal volume)
        {
            if (volume >= 100000000) // 1亿以上显示亿
            {
                return $"{volume / 100000000:F2}亿";
            }
            else if (volume >= 10000) // 1万以上显示万
            {
                return $"{volume / 10000:F2}万";
            }
            else
            {
                return $"{volume:F2}";
            }
        }

        /// <summary>
        /// 更新状态信息
        /// </summary>
        private void UpdateStatus(string message)
        {
            Dispatcher.Invoke(() =>
            {
                StatusTextBlock.Text = $"{DateTime.Now:HH:mm:ss} - {message}";
            });
        }

        /// <summary>
        /// 最近浏览列表双击事件 - 加载最新的K线数据
        /// </summary>
        private async void RecentSymbolsDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (RecentSymbolsDataGrid.SelectedItem is RecentSymbolItem selectedItem)
                {
                    Utils.AppSession.Log($"📈 用户双击浏览记录，准备加载合约: {selectedItem.Symbol}");
                    Utils.AppSession.Log($"📊 记录时间: {selectedItem.LastViewTime:yyyy-MM-dd HH:mm:ss}");
                    
                    // 显示加载状态
                    UpdateStatus($"正在加载最新的 {selectedItem.Symbol} K线数据...");
                    
                    // 加载选中的合约，这会获取最新的K线数据
                    await LoadSymbolAsync(selectedItem.Symbol);
                    
                    // 更新这个合约的浏览时间
                    await AddToRecentSymbolsAsync(selectedItem.Symbol);
                    
                    UpdateStatus($"{selectedItem.Symbol} 最新K线数据已加载完成");
                    Utils.AppSession.Log($"✅ 成功从浏览记录加载合约 {selectedItem.Symbol} 的最新K线数据");
                }
                else
                {
                    Utils.AppSession.Log("⚠️ 双击浏览记录时未选中任何项目");
                }
            }
            catch (Exception ex)
            {
                var errorMsg = $"从浏览记录加载合约失败: {ex.Message}";
                UpdateStatus(errorMsg);
                Utils.AppSession.Log($"❌ {errorMsg}");
                Utils.AppSession.Log($"异常详情: {ex.StackTrace}");
                
                // 检查是否为网络异常
                if (Utils.NetworkExceptionHandler.IsNetworkException(ex))
                {
                    Utils.NetworkExceptionHandler.ShowNetworkExceptionDialog(
                        this, ex, "加载浏览记录中的K线数据", true);
                }
                else
                {
                    MessageBox.Show(
                        $"加载浏览记录失败\n\n{ex.Message}\n\n" +
                        "可能的原因：\n" +
                        "• 合约已下线或不存在\n" +
                        "• 网络连接问题\n" +
                        "• 数据服务异常\n\n" +
                        "建议：尝试手动输入合约名称或检查网络连接",
                        "加载失败",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }

        /// <summary>
        /// 清空历史按钮点击事件
        /// </summary>
        private async void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show("确定要清空所有浏览历史吗？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    _recentSymbolsQueue.Clear();
                    _recentSymbolsDict.Clear();
                    _recentSymbols.Clear();
                    
                    // 保存清空状态到本地
                    await SaveRecentSymbolsAsync();
                    
                    UpdateStatus("浏览历史已清空");
                    Utils.AppSession.Log("浏览历史已清空并保存");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"清空历史失败: {ex.Message}");
                Utils.AppSession.Log($"清空历史失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 窗口关闭事件处理
        /// </summary>
        private async void KLineFloatingWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                Utils.AppSession.Log($"📝 K线浮窗开始关闭流程，准备保存设置和最近浏览记录");
                
                // 确保当前浏览的合约被添加到记录中
                if (!string.IsNullOrEmpty(_currentSymbol))
                {
                    Utils.AppSession.Log($"💾 窗口关闭时确保当前合约 {_currentSymbol} 被保存到浏览记录");
                    await AddToRecentSymbolsAsync(_currentSymbol);
                }
                
                // 保存全局均线参数
                await SaveGlobalMASettingsAsync();
                
                // 保存最近浏览记录到本地文件
                await SaveRecentSymbolsAsync();
                
                Utils.AppSession.Log($"✅ 设置和最近浏览记录保存完成，允许窗口关闭");
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"⚠️ 窗口关闭时保存记录失败，但不阻止关闭: {ex.Message}");
            }
        }

        #region 均线参数控制

        /// <summary>
        /// 减少均线参数按钮点击事件
        /// </summary>
        private async void DecreaseMAButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_globalMAPeriod > 5) // 最小值5
                {
                    _globalMAPeriod--;
                    await UpdateGlobalMAPeriod();
                }
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"❌ 减少均线参数失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 增加均线参数按钮点击事件
        /// </summary>
        private async void IncreaseMAButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_globalMAPeriod < 200) // 最大值200
                {
                    _globalMAPeriod++;
                    await UpdateGlobalMAPeriod();
                }
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"❌ 增加均线参数失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 均线参数输入框文本输入验证
        /// </summary>
        private void MAPeriodTextBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            // 只允许输入数字
            e.Handled = !int.TryParse(e.Text, out _);
        }

        /// <summary>
        /// 均线参数输入框文本变更事件
        /// </summary>
        private async void MAPeriodTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                if (sender is TextBox textBox && !string.IsNullOrEmpty(textBox.Text))
                {
                    if (int.TryParse(textBox.Text, out int newPeriod))
                    {
                        if (newPeriod >= 5 && newPeriod <= 200)
                        {
                            _globalMAPeriod = newPeriod;
                            await UpdateGlobalMAPeriod();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"❌ 均线参数文本变更处理失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新全局均线参数到所有K线图
        /// </summary>
        private async Task UpdateGlobalMAPeriod()
        {
            try
            {
                Utils.AppSession.Log($"📊 开始更新全局均线参数为: {_globalMAPeriod}");
                
                // 更新UI显示
                Dispatcher.Invoke(() =>
                {
                    if (MAPeriodTextBox != null)
                    {
                        MAPeriodTextBox.Text = _globalMAPeriod.ToString();
                    }
                    else
                    {
                        Utils.AppSession.Log($"⚠️ MAPeriodTextBox为null，无法更新UI显示");
                    }
                });

                // 检查并更新K线图控件的均线参数
                var charts = new[]
                {
                    (Name: "KLine5mChart", Chart: KLine5mChart),
                    (Name: "KLine30mChart", Chart: KLine30mChart), 
                    (Name: "KLine1hChart", Chart: KLine1hChart),
                    (Name: "KLine1dChart", Chart: KLine1dChart)
                };

                var updateTasks = new List<Task>();

                foreach (var chartInfo in charts)
                {
                    if (chartInfo.Chart != null)
                    {
                        Utils.AppSession.Log($"✅ {chartInfo.Name} 控件可用，加入更新队列");
                        updateTasks.Add(UpdateChartMAPeriod(chartInfo.Chart));
                    }
                    else
                    {
                        Utils.AppSession.Log($"⚠️ {chartInfo.Name} 控件为null，跳过更新");
                    }
                }

                if (updateTasks.Count > 0)
                {
                    Utils.AppSession.Log($"📊 开始并行更新 {updateTasks.Count} 个K线图控件的均线参数");
                    await Task.WhenAll(updateTasks);
                    Utils.AppSession.Log($"✅ 完成 {updateTasks.Count} 个K线图控件的均线参数更新");
                }
                else
                {
                    Utils.AppSession.Log($"⚠️ 没有可用的K线图控件需要更新均线参数");
                }
                
                // 保存到设置文件
                await SaveGlobalMASettingsAsync();
                
                Utils.AppSession.Log($"✅ 全局均线参数已更新并保存");
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"❌ 更新全局均线参数失败: {ex.Message}");
                Utils.AppSession.Log($"异常类型: {ex.GetType().FullName}");
                Utils.AppSession.Log($"异常堆栈: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// 更新单个K线图控件的均线参数
        /// </summary>
        private async Task UpdateChartMAPeriod(Controls.KLineChartControl chart)
        {
            try
            {
                // 检查chart是否为null
                if (chart == null)
                {
                    Utils.AppSession.Log($"⚠️ 尝试更新均线参数时，chart控件为null，跳过此次更新");
                    return;
                }

                Utils.AppSession.Log($"📊 开始更新图表均线参数为: {_globalMAPeriod}");
                
                await Task.Run(() =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        // 在UI线程中再次检查chart是否为null（防御性编程）
                        if (chart != null)
                        {
                            chart.SetMAPeriod(_globalMAPeriod);
                            Utils.AppSession.Log($"✅ 成功更新图表均线参数为: {_globalMAPeriod}");
                        }
                        else
                        {
                            Utils.AppSession.Log($"⚠️ UI线程中检测到chart为null，无法更新均线参数");
                        }
                    });
                });
            }
            catch (NullReferenceException nrEx)
            {
                Utils.AppSession.Log($"❌ 更新图表均线参数时出现空引用异常: {nrEx.Message}");
                Utils.AppSession.Log($"异常堆栈: {nrEx.StackTrace}");
                Utils.AppSession.Log($"这通常表示K线图控件(chart)在更新过程中变为null");
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"❌ 更新图表均线参数失败: {ex.Message}");
                Utils.AppSession.Log($"异常类型: {ex.GetType().FullName}");
            }
        }

        /// <summary>
        /// 保存全局均线设置
        /// </summary>
        private async Task SaveGlobalMASettingsAsync()
        {
            try
            {
                var settings = await Utils.SettingsManager.LoadSettingsAsync();
                settings.KLine.MAPeriod = _globalMAPeriod;
                await Utils.SettingsManager.SaveSettingsAsync(settings);
                Utils.AppSession.Log($"💾 全局均线参数 {_globalMAPeriod} 已保存到设置文件");
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"❌ 保存全局均线设置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 加载全局均线设置
        /// </summary>
        private async Task LoadGlobalMASettingsAsync()
        {
            try
            {
                var settings = await Utils.SettingsManager.LoadSettingsAsync();
                _globalMAPeriod = settings.KLine.MAPeriod;
                
                Dispatcher.Invoke(() =>
                {
                    MAPeriodTextBox.Text = _globalMAPeriod.ToString();
                });
                
                Utils.AppSession.Log($"📊 已加载全局均线参数: {_globalMAPeriod}");
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"❌ 加载全局均线设置失败: {ex.Message}，使用默认值");
                _globalMAPeriod = 20;
            }
        }

        #endregion

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                Utils.AppSession.Log($"✅ K线浮窗已成功关闭");
            }
            catch (Exception ex)
            {
                Utils.AppSession.Log($"❌ K线浮窗关闭时发生错误: {ex.Message}");
            }
            finally
            {
                base.OnClosed(e);
            }
        }
    }

    /// <summary>
    /// 最近浏览的合约项目
    /// </summary>
    public class RecentSymbolItem
    {
        public string Symbol { get; set; }
        public decimal CurrentPrice { get; set; }
        public decimal ChangePercent { get; set; }
        public bool IsPositive { get; set; }
        public DateTime LastViewTime { get; set; }
    }
} 