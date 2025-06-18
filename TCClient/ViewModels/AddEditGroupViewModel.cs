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
            
            // 加载候选池数据
            _ = LoadCandidateSymbolsAsync();
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
                // 传递空列表获取所有合约状态
                var symbolStatuses = await _strategyService.GetSymbolStatusListAsync(new List<string>());
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
                        Volume24h = 0 // 可以后续从其他表获取
                    });
                }

                CandidateSymbols.Clear();
                foreach (var candidate in candidates)
                {
                    CandidateSymbols.Add(candidate);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载候选池失败: {ex.Message}");
            }
        }

        private async Task FilterCandidateSymbolsAsync()
        {
            try
            {
                // 传递空列表获取所有合约状态
                var symbolStatuses = await _strategyService.GetSymbolStatusListAsync(new List<string>());
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
                        Volume24h = 0 // 可以后续从其他表获取
                    });
                }

                // 应用排序
                candidates = SelectedSortOption switch
                {
                    "累计盈利" => candidates.OrderByDescending(c => c.TotalProfit).ToList(),
                    _ => candidates.OrderByDescending(c => c.Volume24h).ToList() // "成交额"
                };

                CandidateSymbols.Clear();
                foreach (var candidate in candidates)
                {
                    CandidateSymbols.Add(candidate);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"筛选候选池失败: {ex.Message}");
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
            // 这里需要获取DataGrid的选中项，暂时先不实现
            // 在实际使用中，可以通过View传递选中的项目
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