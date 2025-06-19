using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using TCClient.Models;
using TCClient.Services;
using TCClient.Commands;

namespace TCClient.ViewModels
{
    public class AddEditGroupViewModel : INotifyPropertyChanged
    {
        private readonly StrategyTrackingService _strategyService;
        private string _groupName = string.Empty;
        private string? _selectedGroupSymbol;
        private string _selectedStgFilter = "全部";
        private string _selectedSortOption = "成交额";
        private string _statusMessage = "准备就绪";

        public AddEditGroupViewModel(StrategyTrackingService strategyService)
        {
            _strategyService = strategyService;
            
            GroupSymbols = new ObservableCollection<string>();
            CandidateSymbols = new ObservableCollection<CandidateSymbol>();
            SelectedCandidates = new ObservableCollection<CandidateSymbol>();
            
            StgFilterOptions = new List<string> { "全部", "多头", "空头", "空仓" };
            SortOptions = new List<string> { "成交额", "累计盈利" };
            
            // 初始化命令
            RemoveSymbolCommand = new RelayCommand<string>(RemoveSymbol);
            AddSelectedCandidatesCommand = new RelayCommand(async () => await AddSelectedCandidatesAsync());
            RemoveSelectedGroupSymbolsCommand = new RelayCommand(RemoveSelectedGroupSymbols);
            SaveCommand = new RelayCommand(async () => await SaveAsync());
            CancelCommand = new RelayCommand(Cancel);
            RefreshCandidatesCommand = new RelayCommand(async () => await LoadCandidateSymbolsAsync());
            
            // 加载候选池数据
            StatusMessage = "正在初始化候选池...";
            _ = Task.Run(async () => await LoadCandidateSymbolsAsync());
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action? RequestClose;

        public string GroupName
        {
            get => _groupName;
            set => SetProperty(ref _groupName, value);
        }

        public string? SelectedGroupSymbol
        {
            get => _selectedGroupSymbol;
            set => SetProperty(ref _selectedGroupSymbol, value);
        }

        public string SelectedStgFilter
        {
            get => _selectedStgFilter;
            set
            {
                if (SetProperty(ref _selectedStgFilter, value))
                {
                    _ = FilterCandidateSymbolsAsync();
                }
            }
        }

        public string SelectedSortOption
        {
            get => _selectedSortOption;
            set
            {
                if (SetProperty(ref _selectedSortOption, value))
                {
                    _ = FilterCandidateSymbolsAsync();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ObservableCollection<string> GroupSymbols { get; }
        public ObservableCollection<CandidateSymbol> CandidateSymbols { get; }
        public ObservableCollection<CandidateSymbol> SelectedCandidates { get; }
        public List<string> StgFilterOptions { get; }
        public List<string> SortOptions { get; }

        public ICommand RemoveSymbolCommand { get; }
        public ICommand AddSelectedCandidatesCommand { get; }
        public ICommand RemoveSelectedGroupSymbolsCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand RefreshCandidatesCommand { get; }

        // 用于编辑模式
        public ProductGroup? EditingGroup { get; set; }
        
        // 用于保存结果
        public ProductGroup? Result { get; private set; }

        public void InitializeForAdd()
        {
            GroupName = string.Empty;
            GroupSymbols.Clear();
            EditingGroup = null;
        }

        public void InitializeForEdit(ProductGroup group)
        {
            EditingGroup = group;
            GroupName = group.GroupName;
            GroupSymbols.Clear();
            
            // 加载组合的合约列表
            if (group.SymbolList != null && group.SymbolList.Count > 0)
            {
                foreach (var symbol in group.SymbolList)
                {
                    GroupSymbols.Add(symbol);
                }
            }
        }

        private async Task LoadCandidateSymbolsAsync()
        {
            try
            {
                StatusMessage = "正在连接数据库，加载候选池数据（过滤24小时内更新的合约）...";
                
                // 获取所有合约状态
                var symbolStatuses = await _strategyService.GetAllSymbolStatusAsync();
                
                StatusMessage = $"数据库连接成功，获取到 {symbolStatuses.Count} 个24小时内更新的合约状态，正在处理...";
                
                var candidates = new List<CandidateSymbol>();
                
                foreach (var status in symbolStatuses)
                {
                    // 获取成交额数据（这里简化处理，实际可能需要单独查询）
                    candidates.Add(new CandidateSymbol
                    {
                        Symbol = status.Symbol,
                        Stg = status.Stg,
                        StgDesc = status.StgDesc,
                        TotalProfit = status.TotalProfit,
                        Volume24h = (long)status.Volume24h // 使用从数据库获取的成交额
                    });
                }

                // 在UI线程上更新集合
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    CandidateSymbols.Clear();
                    foreach (var candidate in candidates)
                    {
                        CandidateSymbols.Add(candidate);
                    }
                    StatusMessage = $"候选池加载完成，共 {CandidateSymbols.Count} 个候选合约（仅显示24小时内更新的合约）";
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"数据库连接失败或24小时内无更新合约: {ex.Message}，正在使用测试数据...";
                
                // 尝试使用测试数据
                try
                {
                    var testCandidates = GetTestCandidates();
                    
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        CandidateSymbols.Clear();
                        foreach (var candidate in testCandidates)
                        {
                            CandidateSymbols.Add(candidate);
                        }
                        StatusMessage = $"使用测试数据，共 {CandidateSymbols.Count} 个候选合约";
                    });
                }
                catch (Exception testEx)
                {
                    StatusMessage = $"加载失败: 数据库连接失败且测试数据也无法加载 - {testEx.Message}";
                }
            }
        }

        private async Task FilterCandidateSymbolsAsync()
        {
            try
            {
                StatusMessage = $"正在应用筛选条件: {SelectedStgFilter} / {SelectedSortOption}...";
                
                // 获取所有合约状态
                var symbolStatuses = await _strategyService.GetAllSymbolStatusAsync();
                var candidates = new List<CandidateSymbol>();
                
                foreach (var status in symbolStatuses)
                {
                    // 应用多空筛选
                    bool passStgFilter = SelectedStgFilter switch
                    {
                        "多头" => status.Stg > 0,
                        "空头" => status.Stg < 0,
                        "空仓" => status.Stg == 0,
                        _ => true // "全部"
                    };

                    if (!passStgFilter) continue;

                    candidates.Add(new CandidateSymbol
                    {
                        Symbol = status.Symbol,
                        Stg = status.Stg,
                        StgDesc = status.StgDesc,
                        TotalProfit = status.TotalProfit,
                        Volume24h = (long)status.Volume24h // 使用从数据库获取的成交额
                    });
                }

                // 应用排序
                candidates = SelectedSortOption switch
                {
                    "累计盈利" => candidates.OrderByDescending(c => c.TotalProfit).ToList(),
                    _ => candidates.OrderByDescending(c => c.Volume24h).ToList() // "成交额"
                };

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    CandidateSymbols.Clear();
                    foreach (var candidate in candidates)
                    {
                        CandidateSymbols.Add(candidate);
                    }
                    StatusMessage = $"筛选完成，找到 {candidates.Count} 个符合条件的合约（仅24小时内更新）";
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"筛选失败: {ex.Message}";
            }
        }

        private void RemoveSymbol(string? symbol)
        {
            if (!string.IsNullOrEmpty(symbol) && GroupSymbols.Contains(symbol))
            {
                GroupSymbols.Remove(symbol);
            }
        }

        private async Task AddSelectedCandidatesAsync()
        {
            try
            {
                if (SelectedCandidates.Count == 0)
                {
                    StatusMessage = "请先选择要添加的合约";
                    return;
                }

                var addedCount = 0;
                foreach (var candidate in SelectedCandidates)
                {
                    if (!GroupSymbols.Contains(candidate.Symbol))
                    {
                        GroupSymbols.Add(candidate.Symbol);
                        addedCount++;
                    }
                }

                StatusMessage = addedCount > 0 ? 
                    $"已添加 {addedCount} 个合约到组合中，组合当前共有 {GroupSymbols.Count} 个合约" : 
                    "所选合约已存在于组合中";
            }
            catch (Exception ex)
            {
                StatusMessage = $"添加合约失败: {ex.Message}";
            }
            
            await Task.CompletedTask;
        }

        public void AddSelectedCandidates(IEnumerable<CandidateSymbol> selectedCandidates)
        {
            foreach (var candidate in selectedCandidates)
            {
                if (!GroupSymbols.Contains(candidate.Symbol))
                {
                    GroupSymbols.Add(candidate.Symbol);
                }
            }
        }

        private void RemoveSelectedGroupSymbols()
        {
            if (!string.IsNullOrEmpty(SelectedGroupSymbol) && GroupSymbols.Contains(SelectedGroupSymbol))
            {
                GroupSymbols.Remove(SelectedGroupSymbol);
            }
        }

        private async Task SaveAsync()
        {
            if (string.IsNullOrWhiteSpace(GroupName))
            {
                System.Windows.MessageBox.Show("请输入组合名称", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (EditingGroup != null)
                {
                    // 编辑模式
                    var symbolList = GroupSymbols.ToList();
                    var success = await _strategyService.UpdateProductGroupAsync(EditingGroup.Id, GroupName, symbolList);
                    
                    if (success)
                    {
                        // 创建更新后的ProductGroup用于返回
                        Result = new ProductGroup
                        {
                            Id = EditingGroup.Id,
                            GroupName = GroupName,
                            Symbols = string.Join("#", GroupSymbols),
                            CreatedAt = EditingGroup.CreatedAt,
                            UpdatedAt = DateTime.Now
                        };
                    }
                    else
                    {
                        throw new Exception("更新组合失败");
                    }
                }
                else
                {
                    // 新增模式
                    var symbols = GroupSymbols.ToList();
                    var success = await _strategyService.SaveProductGroupAsync(GroupName, symbols);
                    
                    if (success)
                    {
                        // 创建新的ProductGroup用于返回（ID由数据库分配，这里设为0）
                        Result = new ProductGroup
                        {
                            Id = 0, // 实际ID由数据库分配
                            GroupName = GroupName,
                            Symbols = string.Join("#", GroupSymbols),
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now
                        };
                    }
                    else
                    {
                        throw new Exception("保存组合失败");
                    }
                }

                RequestClose?.Invoke();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"保存失败: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void Cancel()
        {
            Result = null;
            RequestClose?.Invoke();
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        /// <summary>
        /// 获取测试候选数据
        /// </summary>
        private List<CandidateSymbol> GetTestCandidates()
        {
            return new List<CandidateSymbol>
            {
                new CandidateSymbol { Symbol = "BTCUSDT", Stg = 1, StgDesc = "多头", TotalProfit = 1250.5m, Volume24h = 100000000 },
                new CandidateSymbol { Symbol = "ETHUSDT", Stg = -1, StgDesc = "空头", TotalProfit = -850.2m, Volume24h = 80000000 },
                new CandidateSymbol { Symbol = "ADAUSDT", Stg = 0, StgDesc = "空仓", TotalProfit = 0, Volume24h = 50000000 },
                new CandidateSymbol { Symbol = "DOTUSDT", Stg = 2, StgDesc = "多头", TotalProfit = 650.8m, Volume24h = 30000000 },
                new CandidateSymbol { Symbol = "LINKUSDT", Stg = -2, StgDesc = "空头", TotalProfit = -450.3m, Volume24h = 25000000 },
                new CandidateSymbol { Symbol = "UNIUSDT", Stg = 1, StgDesc = "多头", TotalProfit = 320.1m, Volume24h = 20000000 },
                new CandidateSymbol { Symbol = "MATICUSDT", Stg = 0, StgDesc = "空仓", TotalProfit = 0, Volume24h = 15000000 },
                new CandidateSymbol { Symbol = "AVAXUSDT", Stg = 1, StgDesc = "多头", TotalProfit = 180.5m, Volume24h = 12000000 }
            };
        }
    }

    public class CandidateSymbol
    {
        public string Symbol { get; set; } = string.Empty;
        public int Stg { get; set; }
        public string StgDesc { get; set; } = string.Empty;
        public decimal TotalProfit { get; set; }
        public long Volume24h { get; set; }
    }
}