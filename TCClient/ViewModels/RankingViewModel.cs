using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using TCClient.Commands;
using TCClient.Models;
using TCClient.Services;
using TCClient.Utils;

namespace TCClient.ViewModels
{
    public class RankingViewModel : ViewModelBase
    {
        private readonly IDatabaseService _databaseService;
        private readonly IMessageService _messageService;
        private readonly IExchangeService _exchangeService;
        private DateTime _selectedDate;
        private bool _isLoading;
        private string _statusMessage;
        private HashSet<string> _tradableSymbols;

        public ObservableCollection<RankingRow> TopGainerRows { get; set; } = new();
        public ObservableCollection<RankingRow> TopLoserRows { get; set; } = new();

        public DateTime SelectedDate
        {
            get => _selectedDate;
            set
            {
                if (_selectedDate != value)
                {
                    _selectedDate = value;
                    OnPropertyChanged();
                    // 使用 Task.Run 避免阻塞UI线程
                    _ = Task.Run(async () => await LoadRankingDataAsync());
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public ICommand RefreshCommand { get; }

        public RankingViewModel(IDatabaseService databaseService, IMessageService messageService, IExchangeService exchangeService)
        {
            _databaseService = databaseService;
            _messageService = messageService;
            _exchangeService = exchangeService;
            _selectedDate = DateTime.Today;
            RefreshCommand = new RelayCommand(async () => await LoadRankingDataAsync());
            
            // 延迟初始化数据加载，避免构造函数中的异步操作
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () =>
            {
                await LoadRankingDataAsync();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private async Task LoadRankingDataAsync()
        {
            try
            {
                // 确保UI更新在UI线程上执行
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IsLoading = true;
                    StatusMessage = "正在加载排行榜数据...";
                });
                
                // 获取可交易合约列表
                await LoadTradableSymbolsAsync();
                
                var startDate = SelectedDate.AddDays(-29);
                var endDate = SelectedDate;
                
                LogManager.Log("RankingViewModel", $"开始加载排行榜数据，日期范围：{startDate:yyyy-MM-dd} 至 {endDate:yyyy-MM-dd}");
                
                // 使用新的 daily_ranking 表数据
                var dailyRankingData = await _databaseService.GetDailyRankingDataAsync(startDate, endDate);
                
                LogManager.Log("RankingViewModel", $"从数据库获取到 {dailyRankingData?.Count ?? 0} 条记录");
                
                // 在UI线程上更新集合
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    TopGainerRows.Clear();
                    TopLoserRows.Clear();
                    
                    if (dailyRankingData != null && dailyRankingData.Count > 0)
                    {
                        foreach (var dailyRanking in dailyRankingData)
                        {
                            LogManager.Log("RankingViewModel", $"处理日期 {dailyRanking.Date:yyyy-MM-dd} 的数据");
                            LogManager.Log("RankingViewModel", $"TopGainers: {dailyRanking.TopGainers}");
                            LogManager.Log("RankingViewModel", $"TopLosers: {dailyRanking.TopLosers}");
                            
                            // 处理涨幅榜数据（过滤不可交易合约）
                            var gainers = _tradableSymbols != null ? 
                                dailyRanking.GetTopGainersList(_tradableSymbols) : 
                                dailyRanking.GetTopGainersList();
                            LogManager.Log("RankingViewModel", $"解析到 {gainers.Count} 个可交易涨幅项目");
                            
                            var gRow = new RankingRow { Date = dailyRanking.Date };
                            for (int i = 0; i < Math.Min(gainers.Count, 10); i++)
                            {
                                var item = gainers[i];
                                var text = item.DisplayText;
                                LogManager.Log("RankingViewModel", $"涨幅榜第{i+1}位: {text}");
                                typeof(RankingRow).GetProperty($"Rank{i + 1}").SetValue(gRow, text);
                            }
                            TopGainerRows.Add(gRow);
                            
                            // 处理跌幅榜数据（过滤不可交易合约）
                            var losers = _tradableSymbols != null ? 
                                dailyRanking.GetTopLosersList(_tradableSymbols) : 
                                dailyRanking.GetTopLosersList();
                            LogManager.Log("RankingViewModel", $"解析到 {losers.Count} 个可交易跌幅项目");
                            
                            var lRow = new RankingRow { Date = dailyRanking.Date };
                            for (int i = 0; i < Math.Min(losers.Count, 10); i++)
                            {
                                var item = losers[i];
                                var text = item.DisplayText;
                                LogManager.Log("RankingViewModel", $"跌幅榜第{i+1}位: {text}");
                                typeof(RankingRow).GetProperty($"Rank{i + 1}").SetValue(lRow, text);
                            }
                            TopLoserRows.Add(lRow);
                        }
                        
                        var tradableCount = _tradableSymbols?.Count ?? 0;
                        StatusMessage = $"数据加载完成，显示 {startDate:yyyy-MM-dd} 至 {endDate:yyyy-MM-dd} 的排行榜数据，共 {dailyRankingData.Count} 天（已过滤 {tradableCount} 个可交易合约）";
                        LogManager.Log("RankingViewModel", $"UI更新完成，涨幅榜 {TopGainerRows.Count} 行，跌幅榜 {TopLoserRows.Count} 行");
                    }
                    else
                    {
                        StatusMessage = $"未找到 {startDate:yyyy-MM-dd} 至 {endDate:yyyy-MM-dd} 期间的排行榜数据";
                        LogManager.Log("RankingViewModel", "没有找到任何数据");
                    }
                });
            }
            catch (Exception ex)
            {
                LogManager.LogException("RankingViewModel", ex, "加载排行榜数据时发生异常");
                
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    StatusMessage = $"加载数据失败：{ex.Message}";
                });
                
                _messageService.ShowMessage(
                    $"加载排行榜数据失败：{ex.Message}",
                    "错误",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IsLoading = false;
                });
            }
        }

        /// <summary>
        /// 加载可交易合约列表
        /// </summary>
        private async Task LoadTradableSymbolsAsync()
        {
            try
            {
                if (_exchangeService != null)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        StatusMessage = "正在获取可交易合约列表...";
                    });

                    var tradableSymbolsList = await _exchangeService.GetTradableSymbolsAsync();
                    if (tradableSymbolsList != null && tradableSymbolsList.Any())
                    {
                        _tradableSymbols = new HashSet<string>(tradableSymbolsList, StringComparer.OrdinalIgnoreCase);
                        LogManager.Log("RankingViewModel", $"成功获取 {_tradableSymbols.Count} 个可交易合约");
                    }
                    else
                    {
                        LogManager.Log("RankingViewModel", "未获取到可交易合约列表，将显示所有合约");
                        _tradableSymbols = null;
                    }
                }
                else
                {
                    LogManager.Log("RankingViewModel", "交易所服务未可用，将显示所有合约");
                    _tradableSymbols = null;
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("RankingViewModel", ex, "获取可交易合约列表失败");
                _tradableSymbols = null;
            }
        }
    }
} 