using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Threading.Tasks;
using Timer = System.Timers.Timer;
using System.Timers;
using System.Linq;
using TCClient.Services;

namespace TCClient.ViewModels
{
    public class DrawdownContractItem : INotifyPropertyChanged
    {
        private string _symbol = string.Empty;
        private double _volume24h;
        private double _change24h;
        private double _currentPrice;
        private double _recentHighPrice;
        private double _recentLowPrice;
        private DateTime _highPriceTime;
        private DateTime _lowPriceTime;
        private double _maxDrawdown;
        private double _currentDrawdown;
        private int _maxDrawdownMinutes;
        private int _currentDrawdownMinutes;

        public string Symbol
        {
            get => _symbol;
            set { _symbol = value; OnPropertyChanged(); }
        }

        public double Volume24h
        {
            get => _volume24h;
            set { _volume24h = value; OnPropertyChanged(); }
        }

        public double Change24h
        {
            get => _change24h;
            set { _change24h = value; OnPropertyChanged(); }
        }

        public double CurrentPrice
        {
            get => _currentPrice;
            set { _currentPrice = value; OnPropertyChanged(); }
        }

        public double RecentHighPrice
        {
            get => _recentHighPrice;
            set { _recentHighPrice = value; OnPropertyChanged(); }
        }

        public double RecentLowPrice
        {
            get => _recentLowPrice;
            set { _recentLowPrice = value; OnPropertyChanged(); }
        }

        public DateTime HighPriceTime
        {
            get => _highPriceTime;
            set { _highPriceTime = value; OnPropertyChanged(); }
        }

        public DateTime LowPriceTime
        {
            get => _lowPriceTime;
            set { _lowPriceTime = value; OnPropertyChanged(); }
        }

        public double MaxDrawdown
        {
            get => _maxDrawdown;
            set { _maxDrawdown = value; OnPropertyChanged(); }
        }

        public double CurrentDrawdown
        {
            get => _currentDrawdown;
            set { _currentDrawdown = value; OnPropertyChanged(); }
        }

        public int MaxDrawdownMinutes
        {
            get => _maxDrawdownMinutes;
            set { _maxDrawdownMinutes = value; OnPropertyChanged(); }
        }

        public int CurrentDrawdownMinutes
        {
            get => _currentDrawdownMinutes;
            set { _currentDrawdownMinutes = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class DrawdownAlertViewModel : INotifyPropertyChanged, IDisposable
    {
        private MarketDataUpdateManager _marketDataManager;
        private DrawdownWatchlistService _watchlistService;
        private DrawdownContractItem? _selectedLongContract;
        private DrawdownContractItem? _selectedShortContract;

        public ObservableCollection<DrawdownContractItem> LongContracts { get; }
        public ObservableCollection<DrawdownContractItem> ShortContracts { get; }

        public DrawdownContractItem? SelectedLongContract
        {
            get => _selectedLongContract;
            set { _selectedLongContract = value; OnPropertyChanged(); }
        }

        public DrawdownContractItem? SelectedShortContract
        {
            get => _selectedShortContract;
            set { _selectedShortContract = value; OnPropertyChanged(); }
        }

        public ICommand AddLongContractCommand { get; }
        public ICommand RemoveLongContractCommand { get; }
        public ICommand AddShortContractCommand { get; }
        public ICommand RemoveShortContractCommand { get; }

        public DrawdownAlertViewModel()
        {
            LongContracts = new ObservableCollection<DrawdownContractItem>();
            ShortContracts = new ObservableCollection<DrawdownContractItem>();

            AddLongContractCommand = new RelayCommand(AddLongContract);
            RemoveLongContractCommand = new RelayCommand(RemoveLongContract, () => SelectedLongContract != null);
            AddShortContractCommand = new RelayCommand(AddShortContract);
            RemoveShortContractCommand = new RelayCommand(RemoveShortContract, () => SelectedShortContract != null);

            // 初始化自选列表服务
            _watchlistService = new DrawdownWatchlistService();
            
            // 异步初始化
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                // 1. 加载保存的自选列表
                await LoadSavedWatchlistAsync();
                
                // 2. 初始化市场数据管理器
                await InitializeMarketDataManagerAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"初始化失败: {ex.Message}");
            }
        }

        private async Task LoadSavedWatchlistAsync()
        {
            try
            {
                var watchlistData = await _watchlistService.GetWatchlistDataAsync();
                
                // 加载做多合约
                foreach (var symbol in watchlistData.LongContracts)
                {
                    var contract = new DrawdownContractItem
                    {
                        Symbol = symbol,
                        Volume24h = 0,
                        Change24h = 0,
                        CurrentPrice = 0,
                        RecentHighPrice = 0,
                        HighPriceTime = DateTime.Now,
                        MaxDrawdown = 0,
                        CurrentDrawdown = 0,
                        MaxDrawdownMinutes = 0,
                        CurrentDrawdownMinutes = 0
                    };
                    LongContracts.Add(contract);
                }
                
                // 加载做空合约
                foreach (var symbol in watchlistData.ShortContracts)
                {
                    var contract = new DrawdownContractItem
                    {
                        Symbol = symbol,
                        Volume24h = 0,
                        Change24h = 0,
                        CurrentPrice = 0,
                        RecentLowPrice = 0,
                        LowPriceTime = DateTime.Now,
                        MaxDrawdown = 0,
                        CurrentDrawdown = 0,
                        MaxDrawdownMinutes = 0,
                        CurrentDrawdownMinutes = 0
                    };
                    ShortContracts.Add(contract);
                }
                
                System.Diagnostics.Debug.WriteLine($"已加载保存的自选列表: 做多 {watchlistData.LongContracts.Count} 个, 做空 {watchlistData.ShortContracts.Count} 个");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载保存的自选列表失败: {ex.Message}");
            }
        }

        private async Task SaveWatchlistAsync()
        {
            try
            {
                var longContracts = LongContracts.Select(c => c.Symbol).ToList();
                var shortContracts = ShortContracts.Select(c => c.Symbol).ToList();
                
                await _watchlistService.SaveWatchlistDataAsync(longContracts, shortContracts);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存自选列表失败: {ex.Message}");
            }
        }

        private async Task InitializeMarketDataManagerAsync()
        {
            try
            {
                // 从配置中获取API Key和Secret
                var config = await BinanceApiConfigService.GetConfigAsync();
                
                string apiKey = "";
                string secretKey = "";
                
                if (BinanceApiConfigService.IsValidConfig(config))
                {
                    apiKey = config.ApiKey;
                    secretKey = config.SecretKey;
                    System.Diagnostics.Debug.WriteLine("使用配置的币安API密钥");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("未配置币安API密钥，使用公共API");
                }

                // 初始化市场数据管理器
                _marketDataManager = new MarketDataUpdateManager(apiKey, secretKey);
                
                // 订阅事件
                _marketDataManager.PriceUpdated += OnPriceUpdated;
                _marketDataManager.TickerUpdated += OnTickerUpdated;
                _marketDataManager.KLineUpdated += OnKLineUpdated;

                // 启动监控
                StartMonitoring();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"初始化市场数据管理器失败: {ex.Message}");
                
                // 如果初始化失败，使用无API密钥的版本
                _marketDataManager = new MarketDataUpdateManager("", "");
                _marketDataManager.PriceUpdated += OnPriceUpdated;
                _marketDataManager.TickerUpdated += OnTickerUpdated;
                _marketDataManager.KLineUpdated += OnKLineUpdated;
                StartMonitoring();
            }
        }

        private void AddSampleData()
        {
            // 添加示例做多合约
            LongContracts.Add(new DrawdownContractItem
            {
                Symbol = "BTCUSDT",
                Volume24h = 125000,
                Change24h = 2.45,
                CurrentPrice = 44190.00,
                RecentHighPrice = 45000.00,
                HighPriceTime = DateTime.Now.AddHours(-2),
                MaxDrawdown = -3.2,
                CurrentDrawdown = -1.8,
                MaxDrawdownMinutes = 45,
                CurrentDrawdownMinutes = 15
            });

            // 添加示例做空合约
            ShortContracts.Add(new DrawdownContractItem
            {
                Symbol = "ETHUSDT",
                Volume24h = 89000,
                Change24h = -1.23,
                CurrentPrice = 2834.50,
                RecentLowPrice = 2800.00,
                LowPriceTime = DateTime.Now.AddHours(-1),
                MaxDrawdown = -2.8,
                CurrentDrawdown = -1.2,
                MaxDrawdownMinutes = 30,
                CurrentDrawdownMinutes = 10
            });
        }

        private async void AddLongContract()
        {
            var dialog = new TCClient.Views.ContractInputDialog()
            {
                Owner = System.Windows.Application.Current.MainWindow
            };

            if (dialog.ShowDialog() == true)
            {
                var contractCode = dialog.ContractCode;
                
                // 检查是否已经存在
                if (LongContracts.Any(c => c.Symbol.Equals(contractCode, StringComparison.OrdinalIgnoreCase)))
                {
                    System.Windows.MessageBox.Show($"合约 {contractCode} 已在监控列表中", "提示", 
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    return;
                }

                var newContract = new DrawdownContractItem
                {
                    Symbol = contractCode,
                    Volume24h = 0,
                    Change24h = 0,
                    CurrentPrice = 0,
                    RecentHighPrice = 0,
                    HighPriceTime = DateTime.Now,
                    MaxDrawdown = 0,
                    CurrentDrawdown = 0,
                    MaxDrawdownMinutes = 0,
                    CurrentDrawdownMinutes = 0
                };

                LongContracts.Add(newContract);
                SelectedLongContract = newContract;
                
                // 添加到市场数据监控
                _marketDataManager.AddSymbol(contractCode);
                
                // 保存到本地文件
                await SaveWatchlistAsync();
            }
        }

        private async void RemoveLongContract()
        {
            if (SelectedLongContract != null)
            {
                var symbol = SelectedLongContract.Symbol;
                LongContracts.Remove(SelectedLongContract);
                SelectedLongContract = null;
                
                // 如果该合约不在做空列表中，则从监控中移除
                if (!ShortContracts.Any(c => c.Symbol == symbol))
                {
                    _marketDataManager.RemoveSymbol(symbol);
                }
                
                // 保存到本地文件
                await SaveWatchlistAsync();
            }
        }

        private async void AddShortContract()
        {
            var dialog = new TCClient.Views.ContractInputDialog()
            {
                Owner = System.Windows.Application.Current.MainWindow
            };

            if (dialog.ShowDialog() == true)
            {
                var contractCode = dialog.ContractCode;
                
                // 检查是否已经存在
                if (ShortContracts.Any(c => c.Symbol.Equals(contractCode, StringComparison.OrdinalIgnoreCase)))
                {
                    System.Windows.MessageBox.Show($"合约 {contractCode} 已在监控列表中", "提示", 
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    return;
                }

                var newContract = new DrawdownContractItem
                {
                    Symbol = contractCode,
                    Volume24h = 0,
                    Change24h = 0,
                    CurrentPrice = 0,
                    RecentLowPrice = 0,
                    LowPriceTime = DateTime.Now,
                    MaxDrawdown = 0,
                    CurrentDrawdown = 0,
                    MaxDrawdownMinutes = 0,
                    CurrentDrawdownMinutes = 0
                };

                ShortContracts.Add(newContract);
                SelectedShortContract = newContract;
                
                // 添加到市场数据监控
                _marketDataManager.AddSymbol(contractCode);
                
                // 保存到本地文件
                await SaveWatchlistAsync();
            }
        }

        private async void RemoveShortContract()
        {
            if (SelectedShortContract != null)
            {
                var symbol = SelectedShortContract.Symbol;
                ShortContracts.Remove(SelectedShortContract);
                SelectedShortContract = null;
                
                // 如果该合约不在做多列表中，则从监控中移除
                if (!LongContracts.Any(c => c.Symbol == symbol))
                {
                    _marketDataManager.RemoveSymbol(symbol);
                }
                
                // 保存到本地文件
                await SaveWatchlistAsync();
            }
        }



        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void StartMonitoring()
        {
            var allSymbols = LongContracts.Select(c => c.Symbol)
                .Concat(ShortContracts.Select(c => c.Symbol))
                .Distinct()
                .ToList();
            
            if (allSymbols.Any())
            {
                _marketDataManager.StartMonitoring(allSymbols);
            }
        }

        private void OnPriceUpdated(string symbol, decimal price)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                // 更新做多合约价格
                var longContract = LongContracts.FirstOrDefault(c => c.Symbol == symbol);
                if (longContract != null)
                {
                    longContract.CurrentPrice = (double)price;
                    
                    // 计算回撤
                    var drawdownInfo = _marketDataManager.CalculateLongDrawdown(symbol, price);
                    UpdateContractDrawdown(longContract, drawdownInfo);
                }

                // 更新做空合约价格
                var shortContract = ShortContracts.FirstOrDefault(c => c.Symbol == symbol);
                if (shortContract != null)
                {
                    shortContract.CurrentPrice = (double)price;
                    
                    // 计算回撤
                    var drawdownInfo = _marketDataManager.CalculateShortDrawdown(symbol, price);
                    UpdateContractDrawdown(shortContract, drawdownInfo);
                }
            });
        }

        private void OnTickerUpdated(string symbol, TickerData tickerData)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                // 更新做多合约统计数据
                var longContract = LongContracts.FirstOrDefault(c => c.Symbol == symbol);
                if (longContract != null)
                {
                    longContract.Volume24h = (double)(tickerData.QuoteVolume / 10000); // 转换为万
                    longContract.Change24h = (double)tickerData.PriceChangePercent;
                }

                // 更新做空合约统计数据
                var shortContract = ShortContracts.FirstOrDefault(c => c.Symbol == symbol);
                if (shortContract != null)
                {
                    shortContract.Volume24h = (double)(tickerData.QuoteVolume / 10000); // 转换为万
                    shortContract.Change24h = (double)tickerData.PriceChangePercent;
                }
            });
        }

        private void OnKLineUpdated(string symbol, string interval, List<BinanceKLineData> klineData)
        {
            // K线数据已经在MarketDataUpdateManager中缓存，这里可以做额外处理
            System.Diagnostics.Debug.WriteLine($"K线数据更新: {symbol} {interval} - {klineData.Count}根K线");
        }

        private void UpdateContractDrawdown(DrawdownContractItem contract, DrawdownInfo drawdownInfo)
        {
            if (drawdownInfo.RecentHighPrice > 0)
            {
                contract.RecentHighPrice = drawdownInfo.RecentHighPrice;
                contract.HighPriceTime = drawdownInfo.HighPriceTime;
            }
            
            if (drawdownInfo.RecentLowPrice > 0)
            {
                contract.RecentLowPrice = drawdownInfo.RecentLowPrice;
                contract.LowPriceTime = drawdownInfo.LowPriceTime;
            }
            
            contract.MaxDrawdown = drawdownInfo.MaxDrawdown;
            contract.CurrentDrawdown = drawdownInfo.CurrentDrawdown;
            contract.MaxDrawdownMinutes = drawdownInfo.MaxDrawdownMinutes;
            contract.CurrentDrawdownMinutes = drawdownInfo.CurrentDrawdownMinutes;
        }

        public void Dispose()
        {
            // 保存当前的自选列表
            try
            {
                SaveWatchlistAsync().Wait(1000); // 等待最多1秒
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"程序关闭时保存自选列表失败: {ex.Message}");
            }
            
            _marketDataManager?.Dispose();
        }
    }
} 