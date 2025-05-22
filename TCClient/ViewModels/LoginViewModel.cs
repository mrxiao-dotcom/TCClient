using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TCClient.Commands;
using TCClient.Services;
using TCClient.Views;
using TCClient.Utils;
using System.IO;
using MySql.Data.MySqlClient;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;

namespace TCClient.ViewModels
{
    public class LoginViewModel : ViewModelBase
    {
        private readonly IDatabaseService _databaseService;
        private readonly LocalConfigService _configService;
        private readonly IUserService _userService;
        private readonly IMessageService _messageService;
        private string _username;
        private string _password;
        private string _errorMessage;
        private bool _rememberPassword;
        private bool _isLoggingIn;
        private DatabaseConnection _selectedDatabase;
        private ObservableCollection<DatabaseConnection> _databaseConnections;
        private CancellationTokenSource _loginCancellationTokenSource;
        private bool? _dialogResult;

        public class LoginSuccessEventArgs : EventArgs
        {
            public string Username { get; }
            public string Database { get; }

            public LoginSuccessEventArgs(string username, string database)
            {
                Username = username;
                Database = database;
            }
        }

        public event EventHandler<LoginSuccessEventArgs> LoginSuccess;

        public string Username
        {
            get => _username;
            set
            {
                if (_username != value)
                {
                    _username = value;
                    OnPropertyChanged();
                    ValidateInput();
                }
            }
        }

        public string Password
        {
            get => _password;
            set
            {
                if (_password != value)
                {
                    _password = value;
                    OnPropertyChanged();
                    ValidateInput();
                }
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                if (_errorMessage != value)
                {
                    _errorMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool RememberPassword
        {
            get => _rememberPassword;
            set
            {
                if (_rememberPassword != value)
                {
                    _rememberPassword = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsLoggingIn
        {
            get => _isLoggingIn;
            set
            {
                if (_isLoggingIn != value)
                {
                    _isLoggingIn = value;
                    OnPropertyChanged();
                }
            }
        }

        public DatabaseConnection SelectedDatabase
        {
            get => _selectedDatabase;
            set
            {
                if (_selectedDatabase != value)
                {
                    _selectedDatabase = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<DatabaseConnection> DatabaseConnections
        {
            get => _databaseConnections;
            set
            {
                _databaseConnections = value;
                OnPropertyChanged();
            }
        }

        public bool? DialogResult
        {
            get => _dialogResult;
            set
            {
                if (_dialogResult != value)
                {
                    _dialogResult = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand LoginCommand { get; }
        public ICommand RegisterCommand { get; }
        public ICommand ConfigureDatabaseCommand { get; }

        public LoginViewModel(
            IDatabaseService databaseService,
            LocalConfigService configService,
            IUserService userService,
            IMessageService messageService)
        {
            _databaseService = databaseService;
            _configService = configService;
            _userService = userService;
            _messageService = messageService;

            _databaseConnections = new ObservableCollection<DatabaseConnection>();
            _loginCancellationTokenSource = new CancellationTokenSource();

            LoginCommand = new RelayCommand(async () => await TryLoginAsync(), () => CanLogin());
            RegisterCommand = new RelayCommand(() => ShowRegisterWindow());
            ConfigureDatabaseCommand = new RelayCommand(() => ShowDatabaseConfigWindow());

            LoadSavedCredentials();
        }

        private async void LoadSavedCredentials()
        {
            // 加载数据库连接列表
            var connections = await _configService.LoadDatabaseConnections();
            DatabaseConnections.Clear();
            foreach (var conn in connections)
            {
                DatabaseConnections.Add(conn);
            }

            // 如果有数据库连接，默认选择第一个
            if (DatabaseConnections.Count > 0)
            {
                SelectedDatabase = DatabaseConnections[0];
            }

            // 加载保存的用户凭据
            var credentials = await _configService.LoadUserCredentials();
            if (credentials != null)
            {
                Username = credentials.Username;
                Password = credentials.Password;
                RememberPassword = credentials.RememberPassword;
            }
        }

        private void ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(Username))
            {
                ErrorMessage = "用户名不能为空";
                return;
            }

            if (string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "密码不能为空";
                return;
            }

            ErrorMessage = string.Empty;
        }

        private bool CanLogin()
        {
            return !IsLoggingIn && 
                   !string.IsNullOrWhiteSpace(Username) && 
                   !string.IsNullOrWhiteSpace(Password) &&
                   string.IsNullOrEmpty(ErrorMessage);
        }

        private async Task TryLoginAsync()
        {
            if (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password))
            {
                ErrorMessage = "请输入用户名和密码";
                return;
            }

            IsLoggingIn = true;
            ErrorMessage = string.Empty;

            // 重置CancellationTokenSource
            if (_loginCancellationTokenSource != null)
            {
                _loginCancellationTokenSource.Dispose();
            }
            _loginCancellationTokenSource = new CancellationTokenSource();

            try
            {
                Utils.LogManager.Log("Login", $"尝试登录，用户名: {Username}");
                
                // 测试数据库连接
                bool isConnected = await _databaseService.TestConnectionAsync(_loginCancellationTokenSource.Token);
                if (!isConnected)
                {
                    Utils.LogManager.Log("Login", "数据库连接失败");
                    ErrorMessage = "无法连接到数据库，请检查网络或数据库配置";
                    IsLoggingIn = false;
                    return;
                }
                
                Utils.LogManager.Log("Login", "数据库连接成功，开始验证用户");

                // 验证用户
                bool isValid = await _userService.ValidateUserAsync(Username, Password, _loginCancellationTokenSource.Token);
                if (!isValid)
                {
                    Utils.LogManager.Log("Login", "用户验证失败");
                    ErrorMessage = "用户名或密码错误";
                    IsLoggingIn = false;
                    return;
                }

                Utils.LogManager.Log("Login", "用户验证成功，获取用户ID");

                // 获取用户ID
                var user = await _userService.GetUserAsync(Username, _loginCancellationTokenSource.Token);
                if (user != null)
                {
                    Utils.LogManager.Log("Login", $"用户ID获取成功: {user.Id}");
                    AppSession.CurrentUserId = user.Id;
                }
                else
                {
                    Utils.LogManager.Log("Login", "无法获取用户ID");
                }

                Utils.LogManager.Log("Login", "获取用户账户信息");

                // 获取当前用户的交易账户
                var accounts = await _userService.GetUserAccountsAsync(Username, _loginCancellationTokenSource.Token);
                if (accounts != null && accounts.Count > 0)
                {
                    Utils.LogManager.Log("Login", $"获取到 {accounts.Count} 个交易账户");
                    // 设置当前账户ID
                    var firstAccount = accounts.FirstOrDefault();
                    if (firstAccount != null)
                    {
                        AppSession.CurrentAccountId = firstAccount.Id;
                        Utils.LogManager.Log("Login", $"当前账户ID设置为: {AppSession.CurrentAccountId}");
                    }
                }
                else
                {
                    Utils.LogManager.Log("Login", "用户没有任何交易账户");
                }

                // 保存登录凭据（如果选择记住密码）
                if (RememberPassword)
                {
                    Utils.LogManager.Log("Login", "保存用户凭据");
                    await _configService.SaveUserCredentials(new UserCredentials
                    {
                        Username = Username,
                        Password = Password,
                        RememberPassword = true
                    });
                }

                Utils.LogManager.Log("Login", "登录成功，设置会话状态");
                
                // 设置登录状态
                AppSession.IsLoggedIn = true;
                var currentDB = _selectedDatabase?.Name ?? "未设置";
                
                Utils.LogManager.Log("Login", $"触发登录成功事件，用户: {Username}, 数据库: {currentDB}");
                LoginSuccess?.Invoke(this, new LoginSuccessEventArgs(Username, currentDB));

                // 关闭登录窗口
                DialogResult = true;
                Utils.LogManager.Log("Login", "登录流程完成，DialogResult设置为true");
            }
            catch (OperationCanceledException)
            {
                Utils.LogManager.Log("Login", "登录操作被取消");
                ErrorMessage = "登录操作被取消";
            }
            catch (MySql.Data.MySqlClient.MySqlException ex)
            {
                Utils.LogManager.LogException("Login", ex, "MySQL数据库异常");
                
                // 根据MySQL错误码提供更具体的错误消息
                switch (ex.Number)
                {
                    case 1042: // 无法连接到MySQL服务器
                        ErrorMessage = "无法连接到数据库服务器，请检查网络连接";
                        break;
                    case 1045: // 访问被拒绝（用户名或密码错误）
                        ErrorMessage = "数据库访问被拒绝，用户名或密码错误";
                        break;
                    case 1049: // 未知数据库
                        ErrorMessage = "指定的数据库不存在";
                        break;
                    case 0: // 通用错误
                        ErrorMessage = $"数据库连接错误: {ex.Message}";
                        break;
                    default:
                        ErrorMessage = $"数据库错误 (代码:{ex.Number}): {ex.Message}";
                        break;
                }
            }
            catch (Exception ex)
            {
                Utils.LogManager.LogException("Login", ex, "登录过程中发生异常");
                ErrorMessage = $"登录失败: {ex.Message}";
            }
            finally
            {
                IsLoggingIn = false;
                Utils.LogManager.Log("Login", "登录过程结束，IsLoggingIn设置为false");
            }
        }

        private void ShowRegisterWindow()
        {
            try
            {
                var app = (App)Application.Current;
                var window = app.Services.GetRequiredService<RegisterWindow>();
                window.Owner = Application.Current.MainWindow;
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                _messageService.ShowMessage(
                    $"打开注册窗口失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ShowDatabaseConfigWindow()
        {
            var configWindow = new DatabaseConfigWindow();
            configWindow.Owner = Application.Current.MainWindow;
            configWindow.ShowDialog();
        }

        public void CancelLogin()
        {
            _loginCancellationTokenSource?.Cancel();
        }
    }
} 