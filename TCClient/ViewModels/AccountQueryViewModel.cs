using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using TCClient.Models;
using TCClient.Services;
using TCClient.Utils;
using Microsoft.Extensions.Logging;

namespace TCClient.ViewModels
{
    /// <summary>
    /// 账户查询窗口ViewModel
    /// </summary>
    public class AccountQueryViewModel : INotifyPropertyChanged
    {
        private readonly IDatabaseService _databaseService;
        private readonly ILogger<AccountQueryViewModel> _logger;
        private readonly DispatcherTimer _refreshTimer;
        
        private bool _isLoading;
        private int _refreshInterval = 30; // 默认30秒
        private int _countdown;
        private double _progressValue;
        
        // 账户基本信息
        private AccountBalance _accountBalance;
        
        // 持仓信息
        private ObservableCollection<AccountPosition> _positions;

        public AccountQueryViewModel(IDatabaseService databaseService, ILogger<AccountQueryViewModel> logger)
        {
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // 初始化集合
            Positions = new ObservableCollection<AccountPosition>();
            
            // 初始化命令
            RefreshCommand = new RelayCommand(async () => await RefreshDataAsync());
            
            // 初始化定时器
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _refreshTimer.Tick += RefreshTimer_Tick;
            
            // 启动定时器
            StartRefreshTimer();
        }

        #region 属性

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public int RefreshInterval
        {
            get => _refreshInterval;
            set
            {
                if (value >= 5 && value <= 3000)
                {
                    SetProperty(ref _refreshInterval, value);
                    RestartRefreshTimer();
                }
            }
        }

        public int Countdown
        {
            get => _countdown;
            set => SetProperty(ref _countdown, value);
        }

        public double ProgressValue
        {
            get => _progressValue;
            set => SetProperty(ref _progressValue, value);
        }

        public AccountBalance AccountBalance
        {
            get => _accountBalance;
            set => SetProperty(ref _accountBalance, value);
        }

        public ObservableCollection<AccountPosition> Positions
        {
            get => _positions;
            set => SetProperty(ref _positions, value);
        }

        #endregion

        #region 命令

        public ICommand RefreshCommand { get; }

        #endregion

        #region 方法

        /// <summary>
        /// 刷新数据
        /// </summary>
        public async Task RefreshDataAsync()
        {
            try
            {
                IsLoading = true;
                _logger?.LogInformation("开始刷新账户查询数据");

                var accountId = AppSession.CurrentAccountId;
                _logger?.LogInformation("当前账户ID: {accountId}", accountId);

                if (accountId <= 0)
                {
                    _logger?.LogWarning("当前账户ID无效，无法加载账户数据");
                    return;
                }

                // 获取账户余额信息
                var balance = await _databaseService.GetAccountBalanceAsync(accountId);
                if (balance != null)
                {
                    AccountBalance = balance;
                    _logger?.LogInformation("获取账户余额成功");
                }
                else
                {
                    _logger?.LogWarning("未获取到账户余额信息");
                }

                // 获取账户持仓信息
                var positions = await _databaseService.GetAccountPositionsAsync(accountId);
                Positions.Clear();
                foreach (var position in positions)
                {
                    Positions.Add(position);
                }
                _logger?.LogInformation("获取到持仓信息数量: {count}", positions.Count);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "刷新账户查询数据失败");
                throw;
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// 启动刷新定时器
        /// </summary>
        private void StartRefreshTimer()
        {
            Countdown = RefreshInterval;
            ProgressValue = 100;
            _refreshTimer.Start();
        }

        /// <summary>
        /// 重启刷新定时器
        /// </summary>
        private void RestartRefreshTimer()
        {
            _refreshTimer.Stop();
            StartRefreshTimer();
        }

        /// <summary>
        /// 定时器Tick事件
        /// </summary>
        private async void RefreshTimer_Tick(object sender, EventArgs e)
        {
            Countdown--;
            ProgressValue = (double)Countdown / RefreshInterval * 100;

            if (Countdown <= 0)
            {
                // 时间到，刷新数据
                try
                {
                    await RefreshDataAsync();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "自动刷新数据失败");
                }
                finally
                {
                    // 重启定时器
                    Countdown = RefreshInterval;
                    ProgressValue = 100;
                }
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _refreshTimer?.Stop();
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion
    }
} 