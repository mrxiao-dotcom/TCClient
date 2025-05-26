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
        private TradingAccount _selectedAccount;
        private ObservableCollection<TradingAccount> _accounts;
        private ObservableCollection<SimulationOrder> _positions;
        private ObservableCollection<Order> _orders;
        private ObservableCollection<Trade> _trades;
        private string _currentDatabase;
        private object _currentView;
        private string _databaseInfo;
        private string _currentAccountIdDisplay;
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

            // 初始化状态
            StatusMessage = "就绪";
            CurrentUser = "未登录";
            CurrentDatabase = "未连接";
            DatabaseInfo = "未连接";

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
                var services = ((App)Application.Current).Services;
                var window = new RankingWindow(
                    services.GetRequiredService<IRankingService>(),
                    services.GetRequiredService<IMessageService>())
                {
                    Owner = Application.Current.MainWindow
                };
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                LogMenuError(nameof(ShowFindOpportunity), ex);
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
                // 获取当前活动窗口作为Owner
                Window ownerWindow = null;
                foreach (Window win in Application.Current.Windows)
                {
                    if (win.IsActive)
                    {
                        ownerWindow = win;
                        break;
                    }
                }
                
                // 如果没有活动窗口，使用第一个可见窗口
                if (ownerWindow == null)
                {
                    foreach (Window win in Application.Current.Windows)
                    {
                        if (win.IsVisible)
                        {
                            ownerWindow = win;
                            break;
                        }
                    }
                }

                // 首先尝试显示数据库设置向导
                var setupWizard = new DatabaseSetupWizard();
                if (ownerWindow != null)
                {
                    setupWizard.Owner = ownerWindow;
                }
                setupWizard.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                var result = setupWizard.ShowDialog();
                
                // 如果用户取消了向导，则显示高级配置窗口
                if (result != true)
                {
                    var configWindow = new DatabaseConfigWindow();
                    if (ownerWindow != null)
                    {
                        configWindow.Owner = ownerWindow;
                    }
                    configWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    configWindow.ShowDialog();
                }
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
                    
                    // 使用CloseByUser方法关闭主窗口
                    if (Application.Current.MainWindow is Views.MainWindow mainWindow)
                    {
                        // 记录日志
                        LogToFile("找到主窗口，调用CloseByUser方法");
                        
                        // 设置一个标志让应用程序知道这是用户请求的关闭
                        AppSession.UserRequestedExit = true;
                        LogToFile("设置AppSession.UserRequestedExit = true");
                        
                        // 调用CloseByUser方法
                        mainWindow.CloseByUser();
                        LogToFile("CloseByUser方法已调用");
                    }
                    else
                    {
                        // 如果找不到主窗口，直接关闭应用程序
                        LogToFile("未找到主窗口，直接调用Application.Current.Shutdown()");
                        Application.Current.Shutdown();
                    }
                }
                else
                {
                    LogToFile("用户取消了退出操作");
                }
            }
            catch (Exception ex)
            {
                LogMenuError(nameof(Exit), ex);
                _messageService.ShowMessage(
                    $"退出应用程序失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ShowAccountManagementWindow()
        {
            try
            {
                // 获取当前活动窗口作为Owner
                Window ownerWindow = null;
                foreach (Window win in Application.Current.Windows)
                {
                    if (win.IsActive)
                    {
                        ownerWindow = win;
                        break;
                    }
                }
                
                // 如果没有活动窗口，使用第一个可见窗口
                if (ownerWindow == null)
                {
                    foreach (Window win in Application.Current.Windows)
                    {
                        if (win.IsVisible)
                        {
                            ownerWindow = win;
                            break;
                        }
                    }
                }

                var window = new AccountConfigWindow();
                if (ownerWindow != null)
                {
                    window.Owner = ownerWindow;
                }
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                window.ShowDialog();
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
                    var defaultAccount = accounts.FirstOrDefault(a => a.IsDefaultAccount);
                    if (defaultAccount != null)
                    {
                        // 如果有默认账户，显示默认账户信息
                        CurrentAccountIdDisplay = $"当前账户ID：{defaultAccount.Id}";
                        if (SelectedAccount == null || SelectedAccount.Id != defaultAccount.Id)
                        {
                            // 如果当前选择的不是默认账户，自动切换到默认账户
                            SelectedAccount = defaultAccount;
                            AppSession.CurrentAccountId = defaultAccount.Id;
                            StatusMessage = $"已自动选择默认账户：{defaultAccount.AccountName}";
                        }
                    }
                    else
                    {
                        // 如果没有默认账户但有其他账户
                        var currentId = AppSession.CurrentAccountId;
                        if (currentId > 0)
                        {
                            CurrentAccountIdDisplay = $"当前账户ID：{currentId}";
                        }
                        else
                        {
                            // 如果没有选择任何账户，选择第一个
                            var firstAccount = accounts.First();
                            SelectedAccount = firstAccount;
                            AppSession.CurrentAccountId = firstAccount.Id;
                            CurrentAccountIdDisplay = $"当前账户ID：{firstAccount.Id}";
                            StatusMessage = "已选择第一个可用账户";
                        }
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
                    
                    // 更新状态栏显示
                    CurrentAccount = dialog.SelectedAccount.AccountName;
                    await UpdateCurrentAccountIdDisplay();
                    
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
                window.ShowDialog();
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

        private static void LogToFile(string message)
        {
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
        }

        private static void LogMenuError(string methodName, Exception ex)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var logMessage = $"[{timestamp}] 菜单方法 {methodName} 执行失败: {ex.Message}\n" +
                               $"异常类型: {ex.GetType().FullName}\n" +
                               $"堆栈跟踪: {ex.StackTrace}\n" +
                               $"内部异常: {ex.InnerException?.Message}\n" +
                               $"{new string('-', 80)}\n";
                File.AppendAllText(MenuLogFilePath, logMessage);
            }
            catch
            {
                // 忽略日志写入失败
            }
        }
    }
} 