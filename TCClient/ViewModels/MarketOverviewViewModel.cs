using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using TCClient.Models;
using TCClient.Services;
using TCClient.Utils;
using TCClient.Commands;
using Microsoft.Extensions.Logging;

namespace TCClient.ViewModels
{
    public class MarketOverviewViewModel : INotifyPropertyChanged
    {
        private readonly MarketOverviewService _marketOverviewService;
        private readonly ILogger<MarketOverviewViewModel> _logger;

        // 市场总览数据
        private MarketOverviewData _marketData = new MarketOverviewData();
        private bool _isLoading = false;
        private string _selectedOpportunityType = "做多机会";
        private int _loadingProgress = 0;
        private string _loadingMessage = "正在加载数据...";
        private string _selectedSymbol = string.Empty;

        // 机会数据
        private Dictionary<int, List<OpportunityData>> _longOpportunities = new Dictionary<int, List<OpportunityData>>();
        private Dictionary<int, List<OpportunityData>> _shortOpportunities = new Dictionary<int, List<OpportunityData>>();

        // 当前显示的机会数据
        private ObservableCollection<OpportunityData> _opportunities1Day = new ObservableCollection<OpportunityData>();
        private ObservableCollection<OpportunityData> _opportunities3Days = new ObservableCollection<OpportunityData>();
        private ObservableCollection<OpportunityData> _opportunities5Days = new ObservableCollection<OpportunityData>();
        private ObservableCollection<OpportunityData> _opportunities10Days = new ObservableCollection<OpportunityData>();
        private ObservableCollection<OpportunityData> _opportunities20Days = new ObservableCollection<OpportunityData>();
        private ObservableCollection<OpportunityData> _opportunities30Days = new ObservableCollection<OpportunityData>();

        public MarketOverviewViewModel(MarketOverviewService marketOverviewService, ILogger<MarketOverviewViewModel> logger = null)
        {
            _marketOverviewService = marketOverviewService;
            _logger = logger;

            // 初始化命令  
            LoadDataCommand = new Commands.RelayCommand(() => Task.Run(LoadDataAsync));
            ShowLongOpportunitiesCommand = new Commands.RelayCommand(() => Task.Run(ShowLongOpportunitiesAsync));
            ShowShortOpportunitiesCommand = new Commands.RelayCommand(() => Task.Run(ShowShortOpportunitiesAsync));
            RefreshCommand = new Commands.RelayCommand(() => Task.Run(RefreshDataAsync));
            SelectSymbolCommand = new Commands.RelayCommand<string>(SelectSymbol);

            // 加载初始数据
            _ = Task.Run(LoadDataAsync);
        }

        #region 属性

        public MarketOverviewData MarketData
        {
            get => _marketData;
            set
            {
                _marketData = value;
                OnPropertyChanged(nameof(MarketData));
                OnPropertyChanged(nameof(TodayStats));
                OnPropertyChanged(nameof(HistoricalStats));
            }
        }

        public TodayMarketStats TodayStats => _marketData?.TodayStats ?? new TodayMarketStats();

        public ObservableCollection<DailyMarketStats> HistoricalStats => 
            new ObservableCollection<DailyMarketStats>(_marketData?.HistoricalStats ?? new List<DailyMarketStats>());

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged(nameof(IsLoading));
            }
        }

        public string SelectedOpportunityType
        {
            get => _selectedOpportunityType;
            set
            {
                _selectedOpportunityType = value;
                OnPropertyChanged(nameof(SelectedOpportunityType));
                OnPropertyChanged(nameof(IsLongOpportunitySelected));
                OnPropertyChanged(nameof(IsShortOpportunitySelected));
                // 通知标题属性变化
                OnPropertyChanged(nameof(Title1Day));
                OnPropertyChanged(nameof(Title3Days));
                OnPropertyChanged(nameof(Title5Days));
                OnPropertyChanged(nameof(Title10Days));
                OnPropertyChanged(nameof(Title20Days));
                OnPropertyChanged(nameof(Title30Days));
            }
        }

        public bool IsLongOpportunitySelected => _selectedOpportunityType == "做多机会";
        public bool IsShortOpportunitySelected => _selectedOpportunityType == "做空机会";

        // 动态标题属性
        public string Title1Day => IsLongOpportunitySelected ? "当日涨幅" : "当日跌幅";
        public string Title3Days => IsLongOpportunitySelected ? "3日涨幅" : "3日跌幅";
        public string Title5Days => IsLongOpportunitySelected ? "5日涨幅" : "5日跌幅";
        public string Title10Days => IsLongOpportunitySelected ? "10日涨幅" : "10日跌幅";
        public string Title20Days => IsLongOpportunitySelected ? "20日涨幅" : "20日跌幅";
        public string Title30Days => IsLongOpportunitySelected ? "30日涨幅" : "30日跌幅";

        public int LoadingProgress
        {
            get => _loadingProgress;
            set
            {
                _loadingProgress = value;
                OnPropertyChanged(nameof(LoadingProgress));
            }
        }

        public string LoadingMessage
        {
            get => _loadingMessage;
            set
            {
                _loadingMessage = value;
                OnPropertyChanged(nameof(LoadingMessage));
            }
        }

        // 机会数据属性
        public ObservableCollection<OpportunityData> Opportunities1Day
        {
            get => _opportunities1Day;
            set
            {
                _opportunities1Day = value;
                OnPropertyChanged(nameof(Opportunities1Day));
            }
        }

        public ObservableCollection<OpportunityData> Opportunities3Days
        {
            get => _opportunities3Days;
            set
            {
                _opportunities3Days = value;
                OnPropertyChanged(nameof(Opportunities3Days));
            }
        }

        public ObservableCollection<OpportunityData> Opportunities5Days
        {
            get => _opportunities5Days;
            set
            {
                _opportunities5Days = value;
                OnPropertyChanged(nameof(Opportunities5Days));
            }
        }

        public ObservableCollection<OpportunityData> Opportunities10Days
        {
            get => _opportunities10Days;
            set
            {
                _opportunities10Days = value;
                OnPropertyChanged(nameof(Opportunities10Days));
            }
        }

        public ObservableCollection<OpportunityData> Opportunities20Days
        {
            get => _opportunities20Days;
            set
            {
                _opportunities20Days = value;
                OnPropertyChanged(nameof(Opportunities20Days));
            }
        }

        public ObservableCollection<OpportunityData> Opportunities30Days
        {
            get => _opportunities30Days;
            set
            {
                _opportunities30Days = value;
                OnPropertyChanged(nameof(Opportunities30Days));
            }
        }

        /// <summary>
        /// 当前选中的合约符号
        /// </summary>
        public string SelectedSymbol
        {
            get => _selectedSymbol;
            set
            {
                if (_selectedSymbol != value)
                {
                    _selectedSymbol = value;
                    OnPropertyChanged(nameof(SelectedSymbol));
                    UpdateHighlightedSymbol(value);
                }
            }
        }

        #endregion

        #region 命令

        public ICommand LoadDataCommand { get; }
        public ICommand ShowLongOpportunitiesCommand { get; }
        public ICommand ShowShortOpportunitiesCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand SelectSymbolCommand { get; }

        #endregion

        #region 方法

        private async Task LoadDataAsync()
        {
            try
            {
                IsLoading = true;
                LoadingProgress = 0;
                LoadingMessage = "正在检查缓存...";
                AppSession.Log("MarketOverviewViewModel: 开始加载市场总览数据");

                // 检查是否有完整的当天缓存
                bool hasCompleteCache = _marketOverviewService.HasTodayCompleteCache();

                if (hasCompleteCache)
                {
                    // 快速启动模式：使用缓存数据
                    LoadingProgress = 10;
                    LoadingMessage = "发现缓存，快速启动中...";
                    AppSession.Log("MarketOverviewViewModel: 检测到完整缓存，启用快速启动模式");

                    // 步骤1: 加载市场总览数据（仍需要实时数据）
                    LoadingProgress = 30;
                    LoadingMessage = "正在获取市场统计数据...";
                    var marketData = await _marketOverviewService.GetMarketOverviewAsync();
                    MarketData = marketData;

                    // 步骤2: 快速加载缓存的机会数据
                    LoadingProgress = 50;
                    LoadingMessage = "正在加载缓存的投资数据...";
                    
                    // 预加载做多和做空数据（并行加载）
                    var longTask = _marketOverviewService.GetLongOpportunitiesAsync();
                    var shortTask = _marketOverviewService.GetShortOpportunitiesAsync();
                    
                    LoadingProgress = 80;
                    LoadingMessage = "正在准备数据...";
                    
                    _longOpportunities = await longTask;
                    _shortOpportunities = await shortTask;
                    
                    // 默认显示做多机会
                    SelectedOpportunityType = "做多机会";
                    UpdateOpportunityCollections(_longOpportunities);

                    LoadingProgress = 100;
                    LoadingMessage = "快速启动完成";
                    AppSession.Log("MarketOverviewViewModel: 快速启动模式完成");
                }
                else
                {
                    // 首次启动模式：需要计算数据
                    LoadingProgress = 10;
                    LoadingMessage = "首次启动，正在计算数据...";
                    AppSession.Log("MarketOverviewViewModel: 未检测到完整缓存，使用首次启动模式");

                    // 步骤1: 加载市场总览数据
                    LoadingProgress = 20;
                    LoadingMessage = "正在获取市场统计数据...";
                    var marketData = await _marketOverviewService.GetMarketOverviewAsync();
                    MarketData = marketData;

                    // 步骤2: 计算投资机会数据
                    LoadingProgress = 60;
                    LoadingMessage = "正在分析投资机会...";
                    await ShowLongOpportunitiesAsync();

                    LoadingProgress = 100;
                    LoadingMessage = "数据加载完成";
                    AppSession.Log("MarketOverviewViewModel: 首次启动模式完成");
                }
            }
            catch (Exception ex)
            {
                LoadingMessage = $"加载失败: {ex.Message}";
                AppSession.Log($"MarketOverviewViewModel: 加载数据失败 - {ex.Message}");
                _logger?.LogError(ex, "加载市场总览数据失败");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ShowLongOpportunitiesAsync()
        {
            try
            {
                IsLoading = true;
                SelectedOpportunityType = "做多机会";
                AppSession.Log("MarketOverviewViewModel: 加载做多机会数据");

                _longOpportunities = await _marketOverviewService.GetLongOpportunitiesAsync();
                UpdateOpportunityCollections(_longOpportunities);

                AppSession.Log("MarketOverviewViewModel: 做多机会数据加载完成");
            }
            catch (Exception ex)
            {
                AppSession.Log($"MarketOverviewViewModel: 加载做多机会失败 - {ex.Message}");
                _logger?.LogError(ex, "加载做多机会数据失败");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ShowShortOpportunitiesAsync()
        {
            try
            {
                IsLoading = true;
                SelectedOpportunityType = "做空机会";
                AppSession.Log("MarketOverviewViewModel: 加载做空机会数据");

                _shortOpportunities = await _marketOverviewService.GetShortOpportunitiesAsync();
                UpdateOpportunityCollections(_shortOpportunities);

                AppSession.Log("MarketOverviewViewModel: 做空机会数据加载完成");
            }
            catch (Exception ex)
            {
                AppSession.Log($"MarketOverviewViewModel: 加载做空机会失败 - {ex.Message}");
                _logger?.LogError(ex, "加载做空机会数据失败");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void UpdateOpportunityCollections(Dictionary<int, List<OpportunityData>> opportunities)
        {
            try
            {
                bool isShortOpportunity = IsShortOpportunitySelected;
                
                Opportunities1Day = new ObservableCollection<OpportunityData>(
                    (opportunities.ContainsKey(1) ? opportunities[1] : new List<OpportunityData>())
                    .Select(o => { o.IsShortOpportunity = isShortOpportunity; return o; }));

                Opportunities3Days = new ObservableCollection<OpportunityData>(
                    (opportunities.ContainsKey(3) ? opportunities[3] : new List<OpportunityData>())
                    .Select(o => { o.IsShortOpportunity = isShortOpportunity; return o; }));

                Opportunities5Days = new ObservableCollection<OpportunityData>(
                    (opportunities.ContainsKey(5) ? opportunities[5] : new List<OpportunityData>())
                    .Select(o => { o.IsShortOpportunity = isShortOpportunity; return o; }));

                Opportunities10Days = new ObservableCollection<OpportunityData>(
                    (opportunities.ContainsKey(10) ? opportunities[10] : new List<OpportunityData>())
                    .Select(o => { o.IsShortOpportunity = isShortOpportunity; return o; }));

                Opportunities20Days = new ObservableCollection<OpportunityData>(
                    (opportunities.ContainsKey(20) ? opportunities[20] : new List<OpportunityData>())
                    .Select(o => { o.IsShortOpportunity = isShortOpportunity; return o; }));

                Opportunities30Days = new ObservableCollection<OpportunityData>(
                    (opportunities.ContainsKey(30) ? opportunities[30] : new List<OpportunityData>())
                    .Select(o => { o.IsShortOpportunity = isShortOpportunity; return o; }));
            }
            catch (Exception ex)
            {
                AppSession.Log($"MarketOverviewViewModel: 更新机会数据集合失败 - {ex.Message}");
                _logger?.LogError(ex, "更新机会数据集合失败");
            }
        }

        private async Task RefreshDataAsync()
        {
            try
            {
                IsLoading = true;
                LoadingProgress = 0;
                LoadingMessage = "正在刷新数据...";
                AppSession.Log("MarketOverviewViewModel: 刷新数据");

                // 只清理过期缓存，不删除当天的缓存
                _marketOverviewService.CleanupExpiredCache();

                // 步骤1: 刷新市场统计数据（这部分总是实时的）
                LoadingProgress = 30;
                LoadingMessage = "正在刷新市场统计...";
                var marketData = await _marketOverviewService.GetMarketOverviewAsync();
                MarketData = marketData;

                // 步骤2: 刷新当前选择的机会数据（利用缓存）
                LoadingProgress = 70;
                LoadingMessage = "正在刷新投资机会...";
                if (IsLongOpportunitySelected)
                {
                    if (_longOpportunities.Any())
                    {
                        // 如果已有做多数据，直接更新显示
                        UpdateOpportunityCollections(_longOpportunities);
                    }
                    else
                    {
                        // 如果没有数据，重新加载
                        await ShowLongOpportunitiesAsync();
                    }
                }
                else
                {
                    if (_shortOpportunities.Any())
                    {
                        // 如果已有做空数据，直接更新显示
                        UpdateOpportunityCollections(_shortOpportunities);
                    }
                    else
                    {
                        // 如果没有数据，重新加载
                        await ShowShortOpportunitiesAsync();
                    }
                }

                LoadingProgress = 100;
                LoadingMessage = "数据刷新完成";
                AppSession.Log("MarketOverviewViewModel: 数据刷新完成");
            }
            catch (Exception ex)
            {
                LoadingMessage = $"刷新失败: {ex.Message}";
                AppSession.Log($"MarketOverviewViewModel: 刷新数据失败 - {ex.Message}");
                _logger?.LogError(ex, "刷新数据失败");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// 选择合约符号
        /// </summary>
        private void SelectSymbol(string symbol)
        {
            if (!string.IsNullOrEmpty(symbol))
            {
                SelectedSymbol = symbol;
            }
        }

        /// <summary>
        /// 更新高亮显示的合约
        /// </summary>
        private void UpdateHighlightedSymbol(string selectedSymbol)
        {
            try
            {
                // 更新所有集合中的高亮状态
                UpdateCollectionHighlight(Opportunities1Day, selectedSymbol);
                UpdateCollectionHighlight(Opportunities3Days, selectedSymbol);
                UpdateCollectionHighlight(Opportunities5Days, selectedSymbol);
                UpdateCollectionHighlight(Opportunities10Days, selectedSymbol);
                UpdateCollectionHighlight(Opportunities20Days, selectedSymbol);
                UpdateCollectionHighlight(Opportunities30Days, selectedSymbol);
            }
            catch (Exception ex)
            {
                AppSession.Log($"MarketOverviewViewModel: 更新高亮状态失败 - {ex.Message}");
                _logger?.LogError(ex, "更新高亮状态失败");
            }
        }

        /// <summary>
        /// 更新单个集合的高亮状态
        /// </summary>
        private void UpdateCollectionHighlight(ObservableCollection<OpportunityData> collection, string selectedSymbol)
        {
            if (collection == null) return;

            foreach (var item in collection)
            {
                item.IsHighlighted = !string.IsNullOrEmpty(selectedSymbol) && item.Symbol == selectedSymbol;
            }
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }


} 