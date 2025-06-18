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
using System.Linq;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Threading;

namespace TCClient.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly IDatabaseService _databaseService;
        private readonly LocalConfigService _configService;
        private readonly IUserService _userService;
        private readonly IMessageService _messageService;
        private readonly WindowManagerService _windowManager;
        private string _statusMessage;
        private string _currentUser;
        private string _currentAccount;
        private string _connectionStatus;
        private TradingAccount _selectedAccount;
        private ObservableCollection<TradingAccount> _accounts;
        private ObservableCollection<SimulationOrder> _positions;
        private ObservableCollection<Order> _orders;
        private ObservableCollection<Trade> _trades;
        private string _currentDatabase;
        private object _currentView;
        private string _databaseInfo;
        private string _currentAccountIdDisplay;
        private string _openWindowsStatus;
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "TCClient_MainViewModel.log");
        private static readonly string MenuLogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "TCClient_Menu.log");

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

        public TradingAccount SelectedAccount
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

        public ObservableCollection<TradingAccount> Accounts
        {
            get => _accounts;
            set
            {
                _accounts = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<SimulationOrder> Positions
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

        public string OpenWindowsStatus
        {
            get => _openWindowsStatus;
            set
            {
                if (_openWindowsStatus != value)
                {
                    _openWindowsStatus = value;
                    OnPropertyChanged();
                }
            }
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
        public ICommand SwitchAccountCommand { get; }
        public ICommand ShowPushStatisticsCommand { get; }
        public ICommand ShowAccountQueryCommand { get; }
        public ICommand ShowNetworkDiagnosticCommand { get; }
        public ICommand ShowServiceManagerCommand { get; }
        public ICommand ShowMarketOverviewCommand { get; }
        public ICommand CloseAllChildWindowsCommand { get; }
        public ICommand ShowWindowSwitcherCommand { get; }
        public ICommand ShowBinanceApiConfigCommand { get; }
        public ICommand ShowDrawdownAlertCommand { get; }
        public ICommand ShowStrategyTrackingCommand { get; }

        public MainViewModel(
            IDatabaseService databaseService,
            LocalConfigService configService,
            IUserService userService,
            IMessageService messageService,
            WindowManagerService windowManager)
        {
            _databaseService = databaseService;
            _configService = configService;
            _userService = userService;
            _messageService = messageService;
            _windowManager = windowManager;

            _accounts = new ObservableCollection<TradingAccount>();
            _positions = new ObservableCollection<SimulationOrder>();
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
            SwitchAccountCommand = new RelayCommand(ShowSwitchAccountDialog);
            ShowPushStatisticsCommand = new RelayCommand(ShowPushStatistics);
            ShowAccountQueryCommand = new RelayCommand(ShowAccountQuery);
            ShowNetworkDiagnosticCommand = new RelayCommand(ShowNetworkDiagnostic);
            ShowServiceManagerCommand = new RelayCommand(ShowServiceManager);
            ShowMarketOverviewCommand = new RelayCommand(ShowMarketOverview);
            CloseAllChildWindowsCommand = new RelayCommand(CloseAllChildWindows);
            ShowWindowSwitcherCommand = new RelayCommand(ShowWindowSwitcher);
            ShowBinanceApiConfigCommand = new RelayCommand(ShowBinanceApiConfig);
            ShowDrawdownAlertCommand = new RelayCommand(ShowDrawdownAlert);
            ShowStrategyTrackingCommand = new RelayCommand(ShowStrategyTracking);

            // 初始化状态
            StatusMessage = "就绪";
            CurrentUser = "未登录";
            CurrentDatabase = "未连接";
            DatabaseInfo = "未连接";
            OpenWindowsStatus = "无打开的子窗口";

            // 加载初始数据
            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            try
            {
                await LoadInitialDataAsync();
                await UpdateCurrentAccountIdDisplay();
            }
            catch (Exception ex)
            {
                StatusMessage = $"初始化失败：{ex.Message}";
            }
        }

        public async Task LoadInitialDataAsync()
        {
            try
            {
                // 加载交易账户列表
                await LoadAccounts();

                // 如果有默认账户，选择它并设置当前账户ID
                var defaultAccount = Accounts.FirstOrDefault(a => a.IsDefaultAccount);
                if (defaultAccount != null)
                {
                    SelectedAccount = defaultAccount;
                    AppSession.CurrentAccountId = defaultAccount.Id;
                    CurrentAccountIdDisplay = $"当前账户ID：{defaultAccount.Id}";
                    StatusMessage = $"已自动选择默认账户：{defaultAccount.AccountName}";
                }
                else if (Accounts.Any())
                {
                    // 如果没有默认账户但有其他账户，选择第一个
                    SelectedAccount = Accounts.First();
                    AppSession.CurrentAccountId = Accounts.First().Id;
                    CurrentAccountIdDisplay = $"当前账户ID：{Accounts.First().Id}";
                    StatusMessage = "已选择第一个可用账户";
                }
                else
                {
                    CurrentAccountIdDisplay = "请添加交易账户";
                    StatusMessage = "未配置交易账户";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载初始数据失败：{ex.Message}";
                CurrentAccountIdDisplay = "获取账户信息失败";
            }
        }

        private async Task LoadAccounts()
        {
            try
            {
                var accounts = await _userService.GetTradingAccountsAsync();
                Accounts.Clear();
                foreach (var account in accounts)
                {
                    Accounts.Add(account);
                }
            }
            catch (Exception ex)
            {
                LogToFile($"加载账户列表失败: {ex.Message}");
                StatusMessage = "加载账户列表失败";
            }
        }

        private async void LoadAccountData()
        {
            try
            {
                if (SelectedAccount == null)
                {
                    StatusMessage = "未选择账户";
                    CurrentAccount = "未选择账户";
                    CurrentAccountIdDisplay = "请选择交易账户";
                    return;
                }

                // 更新账户基本信息
                CurrentAccount = SelectedAccount.AccountName;
                await UpdateCurrentAccountIdDisplay();

                // 从 simulation_orders 表加载持仓数据
                var positions = await _databaseService.GetSimulationOrdersAsync((int)SelectedAccount.Id);
                Positions.Clear();
                foreach (var position in positions.Where(p => p.Status == "open"))
                {
                    Positions.Add(position);
                }

                // 更新状态栏显示
                if (Positions.Any())
                {
                    var positionInfo = string.Join(", ", Positions.Select(p => 
                        $"{p.Contract} {p.Direction} {p.Quantity}手"));
                    StatusMessage = $"当前持仓: {positionInfo}";
                }
                else
                {
                    StatusMessage = $"当前账户: {SelectedAccount.AccountName}";
                }
                ConnectionStatus = "已连接";
            }
            catch (Exception ex)
            {
                LogToFile($"加载账户数据失败: {ex.Message}");
                StatusMessage = $"加载账户数据失败: {ex.Message}";
                ConnectionStatus = "连接异常";
            }
        }

        private void ShowFindOpportunity()
        {
            try
            {
                var window = new FindOpportunityWindow()
                {
                    Owner = Application.Current.MainWindow
                };
                window.Show(); // 改为非模态窗口，允许与其他窗口切换
            }
            catch (Exception ex)
            {
                LogMenuError(nameof(ShowFindOpportunity), ex);
                _messageService.ShowMessage(
                    $"打开寻找机会窗口失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ShowAccountConfig()
        {
            try
            {
                var services = ((App)Application.Current).Services;
                var window = new AccountConfigWindow
                {
                    Owner = Application.Current.MainWindow,
                    DataContext = services.GetRequiredService<AccountConfigViewModel>()
                };
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                LogMenuError(nameof(ShowAccountConfig), ex);
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
                _windowManager.ShowDatabaseConfigWindow(Application.Current.MainWindow);
            }
            catch (Exception ex)
            {
                LogMenuError(nameof(ShowDatabaseConfig), ex);
                _messageService.ShowMessage(
                    $"打开数据库配置窗口失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ShowAbout()
        {
            try
            {
                _messageService.ShowMessage(
                    "交易客户端 v1.0\n\n" +
                    "本程序用于管理和监控交易账户，提供实时行情和交易功能。\n\n" +
                    "© 2024 All Rights Reserved",
                    "关于",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogMenuError(nameof(ShowAbout), ex);
                _messageService.ShowMessage(
                    $"显示关于信息失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void Exit()
        {
            try
            {
                LogToFile("Exit方法被调用 - 用户尝试退出应用程序");
                
                var result = _messageService.ShowMessage(
                    "确定要退出应用程序吗？",
                    "确认",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                LogToFile($"退出确认对话框结果: {result}");

                if (result == MessageBoxResult.Yes)
                {
                    LogToFile("用户确认退出应用程序，开始执行退出流程");
                    
                    // 清理资源和保存数据
                    try
                    {
                        LogToFile("保存应用程序状态");
                        // 保存任何需要保存的状态...
                    }
                    catch (Exception saveEx)
                    {
                        LogToFile($"保存状态失败: {saveEx.Message}");
                    }
                    
                    // 设置退出标志
                        AppSession.UserRequestedExit = true;
                        LogToFile("设置AppSession.UserRequestedExit = true");
                        
                    // 使用Dispatcher.BeginInvoke安全地关闭主窗口
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (Application.Current.MainWindow is Views.MainWindow mainWindow)
                            {
                                LogToFile("找到主窗口，调用CloseByUser方法");
                        mainWindow.CloseByUser();
                        LogToFile("CloseByUser方法已调用");
                    }
                    else
                    {
                        LogToFile("未找到主窗口，直接调用Application.Current.Shutdown()");
                        Application.Current.Shutdown();
                    }
                        }
                        catch (Exception closeEx)
                        {
                            LogToFile($"关闭窗口时发生异常: {closeEx.Message}");
                            // 如果关闭窗口失败，尝试直接关闭应用程序
                            try
                            {
                                Application.Current.Shutdown();
                            }
                            catch (Exception shutdownEx)
                            {
                                LogToFile($"应用程序关闭失败: {shutdownEx.Message}");
                                // 最后的手段：强制退出进程
                                Environment.Exit(0);
                            }
                        }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                    
                    // 设置一个超时机制，如果5秒内程序还没退出，强制终止
                    Task.Run(async () =>
                    {
                        await Task.Delay(5000);
                        LogToFile("程序退出超时，执行最终强制退出");
                        Environment.Exit(0);
                    });
                }
                else
                {
                    LogToFile("用户取消了退出操作");
                }
            }
            catch (Exception ex)
            {
                LogMenuError(nameof(Exit), ex);
                try
                {
                _messageService.ShowMessage(
                    $"退出应用程序失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                }
                catch (Exception msgEx)
                {
                    LogToFile($"显示错误消息失败: {msgEx.Message}");
                    // 如果连错误消息都无法显示，直接退出
                    Environment.Exit(1);
                }
            }
        }

        private void ShowAccountManagementWindow()
        {
            try
            {
                _windowManager.ShowAccountManagementWindow(Application.Current.MainWindow);
            }
            catch (Exception ex)
            {
                _messageService.ShowMessage(
                    $"打开账户管理窗口失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ShowRiskMonitorWindow()
        {
            try
            {
                _messageService.ShowMessage(
                    "风险监控功能正在开发中...",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _messageService.ShowMessage(
                    $"打开风险监控窗口失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ShowSimulationTradeWindow()
        {
            try
            {
                _messageService.ShowMessage(
                    "模拟交易功能正在开发中...",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _messageService.ShowMessage(
                    $"打开模拟交易窗口失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ShowLiveTradeWindow()
        {
            try
            {
                _messageService.ShowMessage(
                    "实盘交易功能正在开发中...",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _messageService.ShowMessage(
                    $"打开实盘交易窗口失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ShowMarketDataWindow()
        {
            try
            {
                _messageService.ShowMessage(
                    "行情数据功能正在开发中...",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _messageService.ShowMessage(
                    $"打开行情数据窗口失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ShowTradeHistoryWindow()
        {
            try
            {
                _messageService.ShowMessage(
                    "交易记录功能正在开发中...",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _messageService.ShowMessage(
                    $"打开交易历史窗口失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ShowRanking()
        {
            try
            {
                CurrentView = new RankingView();
                StatusMessage = "显示涨跌幅排行榜";
            }
            catch (Exception ex)
            {
                LogMenuError(nameof(ShowRanking), ex);
                _messageService.ShowMessage(
                    $"显示排行榜失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        public async Task UpdateCurrentAccountIdDisplay()
        {
            try
            {
                var accounts = await _userService.GetTradingAccountsAsync();
                if (accounts.Any())
                {
                    // 如果当前已经选择了账户，使用当前选择的账户
                    if (SelectedAccount != null)
                    {
                        CurrentAccountIdDisplay = $"当前账户ID：{SelectedAccount.Id}";
                        // 确保AppSession中的账户ID与选择的账户一致
                        AppSession.CurrentAccountId = SelectedAccount.Id;
                        return;
                    }
                    
                    // 如果没有选择账户，尝试使用默认账户
                    var defaultAccount = accounts.FirstOrDefault(a => a.IsDefaultAccount);
                    if (defaultAccount != null)
                    {
                        // 只有在没有选择账户时才使用默认账户
                        SelectedAccount = defaultAccount;
                        AppSession.CurrentAccountId = defaultAccount.Id;
                        CurrentAccountIdDisplay = $"当前账户ID：{defaultAccount.Id}";
                        StatusMessage = $"已自动选择默认账户：{defaultAccount.AccountName}";
                    }
                    else
                    {
                        // 如果没有默认账户，选择第一个可用账户
                        var firstAccount = accounts.First();
                        SelectedAccount = firstAccount;
                        AppSession.CurrentAccountId = firstAccount.Id;
                        CurrentAccountIdDisplay = $"当前账户ID：{firstAccount.Id}";
                        StatusMessage = "已选择第一个可用账户";
                    }
                }
                else
                {
                    // 如果没有任何账户，显示添加账户提示
                    CurrentAccountIdDisplay = "请添加交易账户";
                    StatusMessage = "未配置交易账户";
                }
            }
            catch (Exception ex)
            {
                LogToFile($"更新账户显示失败: {ex.Message}");
                CurrentAccountIdDisplay = "获取账户信息失败";
                StatusMessage = $"获取账户信息失败：{ex.Message}";
            }
        }

        private async Task UpdateStatusBarInfoAsync(string username, string database, string connectionStatus)
        {
            try
            {
                // 更新基本连接信息
                // 如果数据库已连接，说明用户已登录
                bool isConnected = !string.IsNullOrEmpty(database) && database != "未连接";
                CurrentUser = isConnected ? username : "未登录";
                CurrentDatabase = isConnected ? "已连接" : "未连接";
                ConnectionStatus = isConnected ? "已连接" : "未连接";

                // 更新账户信息
                if (SelectedAccount != null)
                {
                    CurrentAccount = SelectedAccount.AccountName;
                    await UpdateCurrentAccountIdDisplay();
                    
                    // 如果有持仓，显示持仓信息
                    if (Positions.Any())
                    {
                        var positionInfo = string.Join(", ", Positions.Select(p => 
                            $"{p.Contract} {p.Direction} {p.Quantity}手"));
                        StatusMessage = $"当前持仓: {positionInfo}";
                    }
                    else
                    {
                        StatusMessage = $"当前账户: {SelectedAccount.AccountName}";
                    }
                }
                else
                {
                    CurrentAccount = "未选择账户";
                    CurrentAccountIdDisplay = "请选择交易账户";
                    StatusMessage = "请选择一个交易账户";
                }
            }
            catch (Exception ex)
            {
                LogToFile($"更新状态栏信息时发生错误: {ex.Message}");
                StatusMessage = $"更新状态栏信息失败: {ex.Message}";
            }
        }

        public async Task UpdateStatusBarInfo(string username, string database, string connectionStatus)
        {
            await UpdateStatusBarInfoAsync(username, database, connectionStatus);
        }

        private async void ShowSwitchAccountDialog()
        {
            try
            {
                var accounts = await _userService.GetTradingAccountsAsync();
                if (!accounts.Any())
                {
                    _messageService.ShowMessage(
                        "请先在账户管理中配置交易账户",
                        "提示",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var dialog = new AccountSelectionDialog(accounts)
                {
                    Owner = Application.Current.MainWindow
                };

                if (dialog.ShowDialog() == true && dialog.SelectedAccount != null)
                {
                    // 更新当前账户
                    SelectedAccount = dialog.SelectedAccount;
                    
                    // 设置当前账户ID
                    AppSession.CurrentAccountId = dialog.SelectedAccount.Id;
                    
                    // 直接更新状态栏显示，不调用UpdateCurrentAccountIdDisplay
                    CurrentAccount = dialog.SelectedAccount.AccountName;
                    CurrentAccountIdDisplay = $"当前账户ID：{dialog.SelectedAccount.Id}";
                    
                    // 重新加载账户数据
                    LoadAccountData();
                    
                    _messageService.ShowMessage(
                        $"已切换到账户：{dialog.SelectedAccount.AccountName}",
                        "提示",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                LogMenuError(nameof(ShowSwitchAccountDialog), ex);
                _messageService.ShowMessage(
                    $"切换账户失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void ShowPushStatistics()
        {
            try
            {
                if (SelectedAccount == null)
                {
                    _messageService.ShowMessage(
                        "请先选择一个交易账户",
                        "提示",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                _windowManager.ShowPushStatisticsWindow((int)SelectedAccount.Id);
                UpdateOpenWindowsStatus();
            }
            catch (Exception ex)
            {
                LogMenuError(nameof(ShowPushStatistics), ex);
                _messageService.ShowMessage(
                    $"打开推仓统计窗口失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ShowOrderWindow()
        {
            try
            {
                if (SelectedAccount == null)
                {
                    _messageService.ShowMessage(
                        "请先选择一个交易账户",
                        "提示",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var services = ((App)Application.Current).Services;
                var orderViewModel = services.GetRequiredService<OrderViewModel>();
                var exchangeServiceFactory = services.GetRequiredService<IExchangeServiceFactory>();
                var exchangeService = exchangeServiceFactory.CreateExchangeService(SelectedAccount);

                var window = new OrderWindow(orderViewModel, exchangeService, SelectedAccount.Id)
                {
                    Owner = Application.Current.MainWindow
                };
                window.Show(); // 改为非模态窗口，允许与其他窗口切换
            }
            catch (Exception ex)
            {
                LogMenuError(nameof(ShowOrderWindow), ex);
                _messageService.ShowMessage(
                    $"打开下单窗口失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void ShowAccountQuery()
        {
            try
            {
                if (SelectedAccount == null)
                {
                    _messageService.ShowMessage(
                        "请先选择一个交易账户",
                        "提示",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                _windowManager.ShowAccountQueryWindow((int)SelectedAccount.Id);
                UpdateOpenWindowsStatus();
            }
            catch (Exception ex)
            {
                LogMenuError(nameof(ShowAccountQuery), ex);
                _messageService.ShowMessage(
                    $"打开账户查询窗口失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ShowNetworkDiagnostic()
        {
            try
            {
                _messageService.ShowMessage(
                    "网络诊断功能正在开发中...",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _messageService.ShowMessage(
                    $"打开网络诊断窗口失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ShowServiceManager()
        {
            try
            {
                LogMenuError(nameof(ShowServiceManager), null);
                _windowManager.ShowServiceManagerWindow();
                UpdateOpenWindowsStatus();
            }
            catch (Exception ex)
            {
                LogMenuError(nameof(ShowServiceManager), ex);
                _messageService.ShowMessage(
                    $"打开服务管理器失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ShowMarketOverview()
        {
            try
            {
                _windowManager.ShowMarketOverviewWindow();
                UpdateOpenWindowsStatus();
                LogToFile("成功打开市场总览窗口");
            }
            catch (Exception ex)
            {
                LogMenuError(nameof(ShowMarketOverview), ex);
                _messageService.ShowMessage(
                    $"打开市场总览失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static void LogToFile(string message)
        {
            // 日志输出已禁用
            // 如需启用，请取消注释以下代码：
            /*
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var logMessage = $"[{timestamp}] {message}{Environment.NewLine}";
                File.AppendAllText(LogFilePath, logMessage);
            }
            catch
            {
                // 忽略日志写入失败
            }
            */
        }

        private static void LogMenuError(string methodName, Exception ex)
        {
            // 日志输出已禁用
            // 如需启用，请取消注释以下代码：
            /*
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var logMessage = $"[{timestamp}] 菜单方法 {methodName} 执行失败: {ex?.Message}\n" +
                               $"异常类型: {ex?.GetType().FullName}\n" +
                               $"堆栈跟踪: {ex?.StackTrace}\n" +
                               $"内部异常: {ex?.InnerException?.Message}\n" +
                               $"{new string('-', 80)}\n";
                File.AppendAllText(MenuLogFilePath, logMessage);
            }
            catch
            {
                // 忽略日志写入失败
            }
            */
        }

        /// <summary>
        /// 更新打开窗口状态信息
        /// </summary>
        public void UpdateOpenWindowsStatus()
        {
            try
            {
                OpenWindowsStatus = _windowManager.GetWindowStatus();
            }
            catch (Exception ex)
            {
                LogManager.LogException("MainViewModel", ex, "更新窗口状态失败");
                OpenWindowsStatus = "窗口状态获取失败";
            }
        }
        
        /// <summary>
        /// 关闭所有子窗口
        /// </summary>
        public void CloseAllChildWindows()
        {
            try
            {
                _windowManager.CloseAllChildWindows();
                UpdateOpenWindowsStatus();
                LogToFile("已关闭所有子窗口");
            }
            catch (Exception ex)
            {
                LogMenuError(nameof(CloseAllChildWindows), ex);
                _messageService.ShowMessage(
                    $"关闭子窗口失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// 显示窗口切换器
        /// </summary>
        private void ShowWindowSwitcher()
        {
            try
            {
                _windowManager.ToggleWindowSwitcher();
                LogToFile("切换窗口切换器显示状态");
            }
            catch (Exception ex)
            {
                LogMenuError(nameof(ShowWindowSwitcher), ex);
                _messageService.ShowMessage(
                    $"显示窗口切换器失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ShowBinanceApiConfig()
        {
            try
            {
                var configWindow = new TCClient.Views.BinanceApiConfigWindow()
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };
                configWindow.ShowDialog();
                LogToFile("打开币安API配置窗口");
            }
            catch (Exception ex)
            {
                LogMenuError(nameof(ShowBinanceApiConfig), ex);
                _messageService.ShowMessage(
                    $"打开币安API配置窗口失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ShowDrawdownAlert()
        {
            try
            {
                _windowManager.ShowDrawdownAlertWindow();
            }
            catch (Exception ex)
            {
                LogMenuError(nameof(ShowDrawdownAlert), ex);
                _messageService.ShowMessage(
                    $"打开回撤预警窗口失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ShowStrategyTracking()
        {
            try
            {
                var services = ((App)Application.Current).Services;
                var viewModel = services.GetRequiredService<StrategyTrackingViewModel>();
                var window = new StrategyTrackingWindow(viewModel)
                {
                    Owner = Application.Current.MainWindow
                };
                window.Show(); // 使用非模态窗口
                UpdateOpenWindowsStatus();
            }
            catch (Exception ex)
            {
                LogMenuError(nameof(ShowStrategyTracking), ex);
                _messageService.ShowMessage(
                    $"打开策略跟踪窗口失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
} 