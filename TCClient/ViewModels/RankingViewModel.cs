using System;
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
        private DateTime _selectedDate;
        private bool _isLoading;
        private string _statusMessage;

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
                    LoadRankingDataAsync().ConfigureAwait(false);
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

        public RankingViewModel(IDatabaseService databaseService, IMessageService messageService)
        {
            _databaseService = databaseService;
            _messageService = messageService;
            _selectedDate = DateTime.Today;
            RefreshCommand = new RelayCommand(async () => await LoadRankingDataAsync());
            LoadRankingDataAsync().ConfigureAwait(false);
        }

        private async Task LoadRankingDataAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "正在加载排行榜数据...";
                var startDate = SelectedDate.AddDays(-29);
                var endDate = SelectedDate;
                var rankingData = await _databaseService.GetRankingDataAsync(startDate, endDate);
                TopGainerRows.Clear();
                TopLoserRows.Clear();
                // 按日期分组
                var grouped = rankingData.GroupBy(r => r.RecordTime.Date).OrderByDescending(g => g.Key);
                foreach (var group in grouped)
                {
                    var date = group.Key;
                    // 涨幅榜：按 ChangeRate 降序取前10
                    var gainers = group.OrderByDescending(r => r.ChangeRate).Take(10).ToList();
                    var gRow = new RankingRow { Date = date };
                    for (int i = 0; i < gainers.Count; i++)
                    {
                        var item = gainers[i];
                        var text = $"{item.Symbol}\n{item.ChangePercent:N2}%";
                        typeof(RankingRow).GetProperty($"Rank{i + 1}").SetValue(gRow, text);
                    }
                    TopGainerRows.Add(gRow);
                    // 跌幅榜：按 ChangeRate 升序取前10
                    var losers = group.OrderBy(r => r.ChangeRate).Take(10).ToList();
                    var lRow = new RankingRow { Date = date };
                    for (int i = 0; i < losers.Count; i++)
                    {
                        var item = losers[i];
                        var text = $"{item.Symbol}\n{item.ChangePercent:N2}%";
                        typeof(RankingRow).GetProperty($"Rank{i + 1}").SetValue(lRow, text);
                    }
                    TopLoserRows.Add(lRow);
                }
                StatusMessage = $"数据加载完成，显示 {startDate:yyyy-MM-dd} 至 {endDate:yyyy-MM-dd} 的排行榜数据";
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载数据失败：{ex.Message}";
                _messageService.ShowMessage(
                    $"加载排行榜数据失败：{ex.Message}",
                    "错误",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
} 