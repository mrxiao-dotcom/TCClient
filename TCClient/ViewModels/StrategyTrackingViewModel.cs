using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using TCClient.Models;
using TCClient.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using TCClient.Commands;
using System.Windows;
using TCClient.Views;
using TCClient.ViewModels;

namespace TCClient.ViewModels
{
    /// <summary>
    /// 策略追踪ViewModel
    /// </summary>
    public class StrategyTrackingViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly StrategyTrackingService _strategyTrackingService;
        private readonly ILogger<StrategyTrackingViewModel> _logger;
        private readonly DispatcherTimer _refreshTimer;

        private ObservableCollection<ProductGroup> _productGroups = new();
        private ObservableCollection<string> _groupSymbols = new();
        private ObservableCollection<NetValuePoint> _groupNetValueData = new();
        private ObservableCollection<NetValuePoint> _symbolNetValueData = new();
        private ObservableCollection<MarketVolumePoint> _marketVolumeData = new();
        private ProductGroup? _selectedGroup;
        private string? _selectedSymbol;
        private bool _isLoading;
        private string _statusMessage = "就绪";
        private bool _disposed;
        private ObservableCollection<SymbolStatus> _groupSymbolStatusList = new();

        public StrategyTrackingViewModel(StrategyTrackingService strategyTrackingService, ILogger<StrategyTrackingViewModel> logger)
        {
            _strategyTrackingService = strategyTrackingService ?? throw new ArgumentNullException(nameof(strategyTrackingService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // 初始化命令
            RefreshCommand = new RelayCommand(async () => await RefreshDataAsync(), () => !IsLoading);
            LoadGroupSymbolsCommand = new RelayCommand<ProductGroup>(async (group) => await LoadGroupSymbolsAsync(group));
            LoadSymbolNetValueCommand = new RelayCommand<object>(async (param) => await LoadSymbolNetValueAsync(param));
            AddGroupCommand = new RelayCommand(OnAddGroup);
            EditGroupCommand = new RelayCommand(OnEditGroup, () => SelectedGroup != null);
            DeleteGroupCommand = new RelayCommand(OnDeleteGroup, () => SelectedGroup != null);

            // 初始化定时器 - 每30分钟刷新一次，在每小时的3分和33分执行
            _refreshTimer = new DispatcherTimer();
            _refreshTimer.Tick += async (s, e) => await AutoRefreshAsync();
            SetupAutoRefresh();

            _logger.LogInformation("策略追踪视图模型已初始化");
        }

        #region 属性

        /// <summary>
        /// 产品组合列表
        /// </summary>
        public ObservableCollection<ProductGroup> ProductGroups
        {
            get => _productGroups;
            set => SetProperty(ref _productGroups, value);
        }

        /// <summary>
        /// 当前选中组合的合约列表
        /// </summary>
        public ObservableCollection<string> GroupSymbols
        {
            get => _groupSymbols;
            set => SetProperty(ref _groupSymbols, value);
        }

        /// <summary>
        /// 组合净值曲线数据
        /// </summary>
        public ObservableCollection<NetValuePoint> GroupNetValueData
        {
            get => _groupNetValueData;
            set => SetProperty(ref _groupNetValueData, value);
        }

        /// <summary>
        /// 合约净值曲线数据
        /// </summary>
        public ObservableCollection<NetValuePoint> SymbolNetValueData
        {
            get => _symbolNetValueData;
            set => SetProperty(ref _symbolNetValueData, value);
        }

        /// <summary>
        /// 市场成交额数据
        /// </summary>
        public ObservableCollection<MarketVolumePoint> MarketVolumeData
        {
            get => _marketVolumeData;
            set => SetProperty(ref _marketVolumeData, value);
        }

        /// <summary>
        /// 当前选中的产品组合
        /// </summary>
        public ProductGroup? SelectedGroup
        {
            get => _selectedGroup;
            set
            {
                if (SetProperty(ref _selectedGroup, value) && value != null)
                {
                    _ = Task.Run(async () => await LoadGroupSymbolsAsync(value));
                }
            }
        }

        /// <summary>
        /// 当前选中的合约
        /// </summary>
        public string? SelectedSymbol
        {
            get => _selectedSymbol;
            set
            {
                if (SetProperty(ref _selectedSymbol, value) && !string.IsNullOrEmpty(value))
                {
                    _ = Task.Run(async () => await LoadSymbolNetValueAsync(value));
                }
            }
        }

        /// <summary>
        /// 是否正在加载
        /// </summary>
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        /// <summary>
        /// 状态消息
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>
        /// 统计信息
        /// </summary>
        public string StatisticsInfo
        {
            get
            {
                var groupCount = ProductGroups.Count;
                var symbolCount = GroupSymbols.Count;
                var groupDataCount = GroupNetValueData.Count;
                var symbolDataCount = SymbolNetValueData.Count;
                var marketDataCount = MarketVolumeData.Count;
                
                return $"组合: {groupCount} | 合约: {symbolCount} | 组合数据点: {groupDataCount} | 合约数据点: {symbolDataCount} | 市场数据点: {marketDataCount}";
            }
        }

        /// <summary>
        /// 合约状态列表
        /// </summary>
        public ObservableCollection<SymbolStatus> GroupSymbolStatusList
        {
            get => _groupSymbolStatusList;
            set => SetProperty(ref _groupSymbolStatusList, value);
        }

        #endregion

        #region 命令

        public ICommand RefreshCommand { get; }
        public ICommand LoadGroupSymbolsCommand { get; }
        public ICommand LoadSymbolNetValueCommand { get; }
        public ICommand AddGroupCommand { get; }
        public ICommand EditGroupCommand { get; }
        public ICommand DeleteGroupCommand { get; }

        #endregion

        #region 方法

        /// <summary>
        /// 初始化数据
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                _logger.LogInformation("开始初始化策略追踪数据");
                
                // 并行加载产品组合数据和市场成交额数据
                var refreshTask = RefreshDataAsync();
                var marketVolumeTask = LoadMarketVolumeAsync();
                
                await Task.WhenAll(refreshTask, marketVolumeTask);
                
                _logger.LogInformation("策略追踪数据初始化完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化策略追踪数据时发生错误");
                StatusMessage = $"初始化失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 刷新数据
        /// </summary>
        public async Task RefreshDataAsync()
        {
            if (IsLoading) return;

            try
            {
                IsLoading = true;
                StatusMessage = "正在连接数据库...";

                _logger.LogInformation("开始刷新策略追踪数据");

                // 首先测试数据库连接
                var connected = await _strategyTrackingService.TestConnectionAsync();
                if (!connected)
                {
                    StatusMessage = "数据库连接失败，请检查网络连接";
                    _logger.LogError("策略追踪数据库连接失败");
                    return;
                }

                StatusMessage = "正在加载产品组合数据...";

                // 获取产品组合列表
                List<ProductGroup> groups;
                try
                {
                    groups = await _strategyTrackingService.GetProductGroupsAsync();
                }
                catch (Exception)
                {
                    // 如果数据库连接失败，使用测试数据
                    _logger.LogWarning("数据库连接失败，使用测试数据");
                    StatusMessage = "数据库连接失败，正在加载测试数据...";
                    groups = await _strategyTrackingService.GetTestDataAsync();
                }
                
                // 更新UI - 必须在UI线程上执行
                App.Current.Dispatcher.Invoke(() =>
                {
                    ProductGroups.Clear();
                    foreach (var group in groups)
                    {
                        ProductGroups.Add(group);
                    }
                    OnPropertyChanged(nameof(StatisticsInfo));
                });

                StatusMessage = $"数据加载完成，共加载 {groups.Count} 个产品组合";
                _logger.LogInformation($"策略追踪数据刷新完成，共加载 {groups.Count} 个产品组合");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "刷新策略追踪数据时发生错误");
                StatusMessage = $"加载失败: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// 加载组合的合约列表和净值曲线
        /// </summary>
        private async Task LoadGroupSymbolsAsync(ProductGroup group)
        {
            try
            {
                IsLoading = true;
                StatusMessage = $"正在加载组合 {group.GroupName} 的数据...";

                // 更新合约列表 - 必须在UI线程上执行
                App.Current.Dispatcher.Invoke(() =>
                {
                    GroupSymbols.Clear();
                    foreach (var symbol in group.SymbolList)
                    {
                        GroupSymbols.Add(symbol);
                    }
                });

                // 加载合约状态列表
                List<SymbolStatus> statusList = new();
                try
                {
                    statusList = await _strategyTrackingService.GetSymbolStatusListAsync(group.SymbolList);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"获取合约状态失败，使用空数据");
                }
                App.Current.Dispatcher.Invoke(() =>
                {
                    GroupSymbolStatusList.Clear();
                    int sequenceNumber = 1;
                    foreach (var s in statusList)
                    {
                        s.SequenceNumber = sequenceNumber++;
                        GroupSymbolStatusList.Add(s);
                    }
                });

                // 加载组合净值曲线
                if (group.SymbolList.Count > 0)
                {
                    List<NetValuePoint> netValueData;
                    try
                    {
                        netValueData = await _strategyTrackingService.GetGroupNetValueAsync(group.SymbolList);
                    }
                    catch (Exception)
                    {
                        // 如果数据库连接失败，使用测试数据
                        _logger.LogWarning($"获取组合 {group.GroupName} 净值数据失败，使用测试数据");
                        netValueData = await _strategyTrackingService.GetTestNetValueAsync(group.SymbolList);
                    }
                    
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        GroupNetValueData.Clear();
                        foreach (var point in netValueData)
                        {
                            GroupNetValueData.Add(point);
                        }
                    });
                    
                    // 在UI线程上触发PropertyChanged事件
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        OnPropertyChanged(nameof(GroupNetValueData));
                    });

                    StatusMessage = $"组合 {group.GroupName}: {group.SymbolList.Count} 个合约, {netValueData.Count} 个净值数据点";
                }
                else
                {
                    GroupNetValueData.Clear();
                    StatusMessage = $"组合 {group.GroupName} 没有合约数据";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"加载组合 {group.GroupName} 数据失败");
                StatusMessage = $"加载组合数据失败: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// 加载单个合约的净值曲线
        /// </summary>
        private async Task LoadSymbolNetValueAsync(object? param)
        {
            string? symbol = null;
            if (param is SymbolStatus status)
                symbol = status.Symbol;
            else if (param is string s)
                symbol = s;
            else if (SelectedSymbol is string ss)
                symbol = ss;
            if (string.IsNullOrEmpty(symbol)) return;
            await LoadSymbolNetValueBySymbolAsync(symbol);
        }

        // 真正加载数据的方法
        private async Task LoadSymbolNetValueBySymbolAsync(string symbol)
        {
            try
            {
                IsLoading = true;
                StatusMessage = $"正在加载合约 {symbol} 的净值曲线...";

                List<NetValuePoint> netValueData;
                try
                {
                    netValueData = await _strategyTrackingService.GetSymbolNetValueAsync(symbol);
                }
                catch (Exception)
                {
                    _logger.LogWarning($"获取合约 {symbol} 净值数据失败，使用测试数据");
                    netValueData = await _strategyTrackingService.GetTestSymbolNetValueAsync(symbol);
                }

                App.Current.Dispatcher.Invoke(() =>
                {
                    SymbolNetValueData.Clear();
                    foreach (var point in netValueData)
                    {
                        SymbolNetValueData.Add(point);
                    }
                    OnPropertyChanged(nameof(SymbolNetValueData));
                    if (System.Windows.Application.Current.MainWindow is TCClient.Views.StrategyTrackingWindow win)
                    {
                        win.UpdateSymbolChart();
                    }
                });

                StatusMessage = $"合约 {symbol}: {netValueData.Count} 个净值数据点";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"加载合约 {symbol} 净值曲线失败");
                StatusMessage = $"加载合约净值曲线失败: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// 设置自动刷新
        /// </summary>
        private void SetupAutoRefresh()
        {
            // 计算下次刷新时间（每小时的3分或33分）
            var now = DateTime.Now;
            var nextRefresh = GetNextRefreshTime(now);
            
            var interval = nextRefresh - now;
            if (interval.TotalMilliseconds > 0)
            {
                _refreshTimer.Interval = interval;
                _refreshTimer.Start();
                
                _logger.LogInformation($"设置自动刷新，下次刷新时间: {nextRefresh:HH:mm:ss}");
            }
        }

        /// <summary>
        /// 获取下次刷新时间
        /// </summary>
        private DateTime GetNextRefreshTime(DateTime current)
        {
            var minute = current.Minute;
            var targetMinute = minute < 3 ? 3 : (minute < 33 ? 33 : 3);
            
            var nextRefresh = new DateTime(current.Year, current.Month, current.Day, current.Hour, targetMinute, 0);
            
            // 如果目标时间已过，则设置为下一个时间点
            if (nextRefresh <= current)
            {
                if (targetMinute == 3)
                {
                    nextRefresh = nextRefresh.AddMinutes(30); // 到33分
                }
                else
                {
                    nextRefresh = nextRefresh.AddHours(1).AddMinutes(-30); // 到下一小时的3分
                }
            }
            
            return nextRefresh;
        }

        /// <summary>
        /// 自动刷新
        /// </summary>
        private async Task AutoRefreshAsync()
        {
            try
            {
                // 检查是否是刷新时间点（每小时3分和33分）
                var now = DateTime.Now;
                if (now.Minute != 3 && now.Minute != 33)
                {
                    return; // 不是刷新时间点，直接返回
                }

                _logger.LogInformation("执行自动刷新");
                
                // 刷新产品组合数据
                await RefreshDataAsync();
                
                // 刷新当前选中组合的数据
                if (SelectedGroup != null)
                {
                    await LoadGroupSymbolsAsync(SelectedGroup);
                }
                
                // 刷新当前选中合约的数据
                if (!string.IsNullOrEmpty(SelectedSymbol))
                {
                    await LoadSymbolNetValueAsync(SelectedSymbol);
                }
                
                // 设置下次刷新
                SetupAutoRefresh();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "自动刷新失败");
            }
        }

        /// <summary>
        /// 加载市场成交额数据
        /// </summary>
        private async Task LoadMarketVolumeAsync()
        {
            try
            {
                StatusMessage = "正在加载市场成交额数据...";

                List<MarketVolumePoint> volumeData;
                try
                {
                    volumeData = await _strategyTrackingService.GetMarketVolumeAsync();
                }
                catch (Exception)
                {
                    // 如果数据库连接失败，使用测试数据
                    _logger.LogWarning("获取市场成交额数据失败，使用测试数据");
                    volumeData = await _strategyTrackingService.GetTestMarketVolumeAsync();
                }
                
                // 在UI线程上更新数据
                App.Current.Dispatcher.Invoke(() =>
                {
                    MarketVolumeData.Clear();
                    foreach (var point in volumeData)
                    {
                        MarketVolumeData.Add(point);
                    }
                });
                
                // 在UI线程上触发PropertyChanged事件
                App.Current.Dispatcher.Invoke(() =>
                {
                    OnPropertyChanged(nameof(MarketVolumeData));
                });

                StatusMessage = $"市场成交额数据加载完成: {volumeData.Count} 个数据点";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载市场成交额数据失败");
                StatusMessage = $"加载市场成交额数据失败: {ex.Message}";
            }
        }

        private void OnAddGroup()
        {
            try
            {
                // 使用依赖注入获取ViewModel和Window
                var app = Application.Current as App;
                var viewModel = app?.Services.GetService(typeof(AddEditGroupViewModel)) as AddEditGroupViewModel;
                var window = app?.Services.GetService(typeof(AddEditGroupWindow)) as AddEditGroupWindow;
                
                if (viewModel == null || window == null)
                {
                    MessageBox.Show("无法创建组合编辑窗口", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                
                // 初始化为新增模式
                viewModel.InitializeForAdd();
                
                // 设置窗口
                window.DataContext = viewModel;
                window.Owner = Application.Current.MainWindow;
                window.Title = "新增组合";
                
                // 订阅关闭事件
                viewModel.RequestClose += async () =>
                {
                    window.Close();
                    
                    // 如果有结果，说明保存成功，刷新数据
                    if (viewModel.Result != null)
                    {
                        await RefreshDataAsync();
                    }
                };
                
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "打开新增组合窗口失败");
                MessageBox.Show($"打开新增组合窗口失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void OnEditGroup()
        {
            if (SelectedGroup == null)
            {
                MessageBox.Show("请先选择一个组合", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            try
            {
                // 使用依赖注入获取ViewModel和Window
                var app = Application.Current as App;
                var viewModel = app?.Services.GetService(typeof(AddEditGroupViewModel)) as AddEditGroupViewModel;
                var window = app?.Services.GetService(typeof(AddEditGroupWindow)) as AddEditGroupWindow;
                
                if (viewModel == null || window == null)
                {
                    MessageBox.Show("无法创建组合编辑窗口", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                
                // 初始化为编辑模式
                viewModel.InitializeForEdit(SelectedGroup);
                
                // 设置窗口
                window.DataContext = viewModel;
                window.Owner = Application.Current.MainWindow;
                window.Title = $"编辑组合 - {SelectedGroup.GroupName}";
                
                // 订阅关闭事件
                viewModel.RequestClose += async () =>
                {
                    window.Close();
                    
                    // 如果有结果，说明保存成功，刷新数据
                    if (viewModel.Result != null)
                    {
                        await RefreshDataAsync();
                    }
                };
                
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "打开编辑组合窗口失败");
                MessageBox.Show($"打开编辑组合窗口失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private async void OnDeleteGroup()
        {
            if (SelectedGroup == null)
            {
                MessageBox.Show("请先选择一个组合", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var result = MessageBox.Show($"确定要删除组合 '{SelectedGroup.GroupName}' 吗？", 
                "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    _logger.LogInformation($"开始删除组合: ID={SelectedGroup.Id}, 名称={SelectedGroup.GroupName}");
                    
                    var success = await _strategyTrackingService.DeleteProductGroupAsync(SelectedGroup.Id);
                    
                    if (success)
                    {
                        _logger.LogInformation($"组合删除成功: {SelectedGroup.GroupName}");
                        MessageBox.Show($"组合 '{SelectedGroup.GroupName}' 已删除", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                        
                        // 清空当前选择
                        SelectedGroup = null;
                        
                        // 刷新数据
                        await RefreshDataAsync();
                    }
                    else
                    {
                        MessageBox.Show("删除失败，请重试", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"删除组合失败: {SelectedGroup.GroupName}");
                    MessageBox.Show($"删除组合失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_disposed)
            {
                _refreshTimer?.Stop();
                _disposed = true;
                _logger.LogInformation("策略追踪视图模型已释放");
            }
        }

        #endregion
    }
} 