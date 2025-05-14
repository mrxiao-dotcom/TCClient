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

        public event EventHandler LoginSuccess;

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

        public ICommand LoginCommand { get; }
        public ICommand RegisterCommand { get; }
        public ICommand ConfigureDatabaseCommand { get; }

        public LoginViewModel()
        {
            _databaseService = ServiceLocator.GetService<IDatabaseService>();
            _configService = ServiceLocator.GetService<LocalConfigService>();
            _userService = ServiceLocator.GetService<IUserService>();
            _messageService = ServiceLocator.GetService<IMessageService>();

            _databaseConnections = new ObservableCollection<DatabaseConnection>();

            LoginCommand = new RelayCommand(async () => await LoginAsync(), () => CanLogin());
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

        private async Task LoginAsync()
        {
            if (IsLoggingIn) return;

            try
            {
                IsLoggingIn = true;
                ErrorMessage = string.Empty;

                // 记录登录尝试
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "TCClient_Login.log");
                
                var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] 开始登录尝试\n" +
                                $"用户名: {Username}\n" +
                                $"数据库: {SelectedDatabase?.Database ?? "未选择"}\n" +
                                $"服务器: {SelectedDatabase?.Server ?? "未选择"}\n";
                
                File.AppendAllText(logPath, logMessage);

                // 验证输入
                if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
                {
                    ErrorMessage = "用户名和密码不能为空";
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] 错误: {ErrorMessage}\n");
                    return;
                }

                // 验证用户
                try
                {
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] 开始验证用户...\n");
                    var isValid = await _userService.ValidateUserAsync(Username, Password);
                    
                    if (isValid)
                    {
                        File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] 用户验证成功\n");
                        
                        // 保存凭据
                        if (RememberPassword)
                        {
                            await _configService.SaveUserCredentials(new UserCredentials
                            {
                                Username = Username,
                                Password = Password,
                                RememberPassword = true
                            });
                            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] 已保存用户凭据\n");
                        }

                        // 触发登录成功事件
                        LoginSuccess?.Invoke(this, EventArgs.Empty);
                        File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] 登录流程完成\n");
                    }
                    else
                    {
                        ErrorMessage = "用户名或密码错误";
                        File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] 错误: {ErrorMessage}\n");
                    }
                }
                catch (Exception ex)
                {
                    ErrorMessage = $"登录失败：{ex.Message}";
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] 错误: {ErrorMessage}\n");
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] 错误详情: {ex}\n");
                }
            }
            finally
            {
                IsLoggingIn = false;
            }
        }

        private void ShowRegisterWindow()
        {
            var registerWindow = new RegisterWindow();
            registerWindow.Owner = Application.Current.MainWindow;
            registerWindow.ShowDialog();
        }

        private void ShowDatabaseConfigWindow()
        {
            var configWindow = new DatabaseConfigWindow();
            configWindow.Owner = Application.Current.MainWindow;
            configWindow.ShowDialog();
        }
    }
} 