using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TCClient.Commands;
using TCClient.Models;
using TCClient.Services;
using TCClient.Views;
using TCClient.Utils;

namespace TCClient.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly IDatabaseService _databaseService;
        private readonly LocalConfigService _configService;
        private readonly IUserService _userService;
        private readonly IMessageService _messageService;
        private string _statusMessage;
        private string _currentUser;
        private string _currentAccount;
        private string _connectionStatus;
        private Account _selectedAccount;
        private ObservableCollection<Account> _accounts;
        private ObservableCollection<Position> _positions;
        private ObservableCollection<Order> _orders;
        private ObservableCollection<Trade> _trades;
        private string _currentDatabase;
        private object _currentView;
        private string _databaseInfo;
        private string _currentAccountIdDisplay;

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        public string CurrentUser
        {
            get => _currentUser;
            set
            {
                if (_currentUser != value)
                {
                    _currentUser = value;
                    OnPropertyChanged();
                }
            }
        }

        public string CurrentAccount
        {
            get => _currentAccount;
            set
            {
                if (_currentAccount != value)
                {
                    _currentAccount = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ConnectionStatus
        {
            get => _connectionStatus;
            set
            {
                if (_connectionStatus != value)
                {
                    _connectionStatus = value;
                    OnPropertyChanged();
                }
            }
        }

        public Account SelectedAccount
        {
            get => _selectedAccount;
            set
            {
                if (_selectedAccount != value)
                {
                    _selectedAccount = value;
                    OnPropertyChanged();
                    if (value != null)
                    {
                        CurrentAccount = value.AccountName;
                        LoadAccountData();
                    }
                }
            }
        }

        public ObservableCollection<Account> Accounts
        {
            get => _accounts;
            set
            {
                _accounts = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<Position> Positions
        {
            get => _positions;
            set
            {
                _positions = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<Order> Orders
        {
            get => _orders;
            set
            {
                _orders = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<Trade> Trades
        {
            get => _trades;
            set
            {
                _trades = value;
                OnPropertyChanged();
            }
        }

        public string CurrentDatabase
        {
            get => _currentDatabase;
            set
            {
                if (_currentDatabase != value)
                {
                    _currentDatabase = value;
                    OnPropertyChanged();
                }
            }
        }

        public object CurrentView
        {
            get => _currentView;
            set
            {
                if (_currentView != value)
                {
                    _currentView = value;
                    OnPropertyChanged();
                }
            }
        }

        public string DatabaseInfo
        {
            get => _databaseInfo;
            set
            {
                if (_databaseInfo != value)
                {
                    _databaseInfo = value;
                    OnPropertyChanged();
                }
            }
        }

        public string CurrentAccountIdDisplay
        {
            get => _currentAccountIdDisplay;
            set { _currentAccountIdDisplay = value; OnPropertyChanged(); }
        }

        public ICommand FindOpportunityCommand { get; }
        public ICommand ConfigureAccountCommand { get; }
        public ICommand ConfigureDatabaseCommand { get; }
        public ICommand AboutCommand { get; }
        public ICommand ExitCommand { get; }
        public ICommand ManageAccountsCommand { get; }
        public ICommand RiskMonitorCommand { get; }
        public ICommand SimulationTradeCommand { get; }
        public ICommand LiveTradeCommand { get; }
        public ICommand MarketDataCommand { get; }
        public ICommand TradeHistoryCommand { get; }
        public ICommand ShowFindOpportunityCommand { get; }
        public ICommand ShowAccountConfigCommand { get; }
        public ICommand ShowDatabaseConfigCommand { get; }
        public ICommand ShowAboutCommand { get; }
        public ICommand ShowRankingCommand { get; }
        public ICommand ShowOrderWindowCommand { get; }

        public MainViewModel(
            IDatabaseService databaseService,
            LocalConfigService configService,
            IUserService userService,
            IMessageService messageService)
        {
            _databaseService = databaseService;
            _configService = configService;
            _userService = userService;
            _messageService = messageService;

            _accounts = new ObservableCollection<Account>();
            _positions = new ObservableCollection<Position>();
            _orders = new ObservableCollection<Order>();
            _trades = new ObservableCollection<Trade>();

            // 初始化命令
            ShowFindOpportunityCommand = new RelayCommand(ShowFindOpportunity);
            ShowAccountConfigCommand = new RelayCommand(ShowAccountConfig);
            ShowDatabaseConfigCommand = new RelayCommand(ShowDatabaseConfig);
            ShowAboutCommand = new RelayCommand(ShowAbout);
            ShowRankingCommand = new RelayCommand(ShowRanking);
            ExitCommand = new RelayCommand(Exit);
            ManageAccountsCommand = new RelayCommand(() => ShowAccountManagementWindow());
            RiskMonitorCommand = new RelayCommand(() => ShowRiskMonitorWindow());
            SimulationTradeCommand = new RelayCommand(() => ShowSimulationTradeWindow());
            LiveTradeCommand = new RelayCommand(() => ShowLiveTradeWindow());
            MarketDataCommand = new RelayCommand(() => ShowMarketDataWindow());
            TradeHistoryCommand = new RelayCommand(() => ShowTradeHistoryWindow());
            ShowOrderWindowCommand = new RelayCommand(ShowOrderWindow);

            // 初始化状态
            StatusMessage = "就绪";
            CurrentUser = "未登录";
            CurrentDatabase = "未连接";
            DatabaseInfo = "未连接";

            // 加载初始数据
            LoadInitialDataAsync().ConfigureAwait(false);
            UpdateCurrentAccountIdDisplay();
        }

        private async Task LoadInitialDataAsync()
        {
            try
            {
                var isConnected = await _databaseService.TestConnectionAsync();
                DatabaseInfo = isConnected ? "已连接" : "未连接";
            }
            catch (Exception ex)
            {
                _messageService.ShowMessage(
                    $"连接数据库失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                DatabaseInfo = "连接失败";
            }
        }

        private async Task LoadAccounts()
        {
            try
            {
                var accounts = await _databaseService.GetUserAccountsAsync(CurrentUser);
                Accounts.Clear();
                foreach (var account in accounts)
                {
                    Accounts.Add(account);
                }

                if (Accounts.Count > 0)
                {
                    SelectedAccount = Accounts[0];
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载账户列表失败：{ex.Message}";
            }
        }

        private async void LoadAccountData()
        {
            if (SelectedAccount == null) return;

            try
            {
                StatusMessage = "正在加载账户数据...";

                // 加载持仓数据
                var positions = await _databaseService.GetPositionsAsync(SelectedAccount.Id);
                Positions.Clear();
                foreach (var position in positions)
                {
                    Positions.Add(position);
                }

                // 加载委托数据
                var orders = await _databaseService.GetOrdersAsync(SelectedAccount.Id);
                Orders.Clear();
                foreach (var order in orders)
                {
                    Orders.Add(order);
                }

                // 加载成交数据
                var trades = await _databaseService.GetTradesAsync(SelectedAccount.Id);
                Trades.Clear();
                foreach (var trade in trades)
                {
                    Trades.Add(trade);
                }

                StatusMessage = "账户数据加载完成";
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载账户数据失败：{ex.Message}";
            }
        }

        private void ShowFindOpportunity()
        {
            try
            {
                var window = new RankingWindow
                {
                    Owner = Application.Current.MainWindow
                };
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                _messageService.ShowMessage(
                    $"打开排行榜窗口失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ShowAccountConfig()
        {
            try
            {
                var window = new AccountConfigWindow
                {
                    Owner = Application.Current.MainWindow
                };
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                _messageService.ShowMessage(
                    $"打开账户配置窗口失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ShowDatabaseConfig()
        {
            try
            {
                var window = new DatabaseConfigWindow
                {
                    Owner = Application.Current.MainWindow
                };
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                _messageService.ShowMessage(
                    $"打开数据库配置窗口失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ShowAbout()
        {
            _messageService.ShowMessage(
                "交易客户端 v1.0\n\n" +
                "本程序用于管理和监控交易账户，提供实时行情和交易功能。\n\n" +
                "© 2024 All Rights Reserved",
                "关于",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void Exit()
        {
            var result = _messageService.ShowMessage(
                "确定要退出程序吗？",
                "确认退出",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                Application.Current.Shutdown();
            }
        }

        private void ShowAccountManagementWindow()
        {
            // TODO: 实现账户管理窗口
            MessageBox.Show("账户管理功能正在开发中...", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ShowRiskMonitorWindow()
        {
            // TODO: 实现风险监控窗口
            MessageBox.Show("风险监控功能正在开发中...", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ShowSimulationTradeWindow()
        {
            // TODO: 实现模拟交易窗口
            MessageBox.Show("模拟交易功能正在开发中...", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ShowLiveTradeWindow()
        {
            // TODO: 实现实盘交易窗口
            MessageBox.Show("实盘交易功能正在开发中...", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ShowMarketDataWindow()
        {
            // TODO: 实现行情数据窗口
            MessageBox.Show("行情数据功能正在开发中...", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ShowTradeHistoryWindow()
        {
            // TODO: 实现交易记录窗口
            MessageBox.Show("交易记录功能正在开发中...", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ShowOrderWindow()
        {
            var window = new TCClient.Views.OrderWindow { Owner = System.Windows.Application.Current.MainWindow };
            window.ShowDialog();
        }

        private void ShowRanking()
        {
            CurrentView = new RankingView();
            StatusMessage = "显示涨跌幅排行榜";
        }

        public void UpdateCurrentAccountIdDisplay()
        {
            var id = TCClient.Utils.AppSession.CurrentAccountId;
            if (id > 0)
                CurrentAccountIdDisplay = $"当前账户ID：{id}";
            else
                CurrentAccountIdDisplay = "未绑定账户，请在菜单-设置中进行账户绑定";
        }
    }
} 