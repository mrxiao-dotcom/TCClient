using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TCClient.Commands;
using TCClient.Services;
using TCClient.Utils;

namespace TCClient.ViewModels
{
    public class RegisterViewModel : ViewModelBase
    {
        private readonly IUserService _userService;
        private readonly IMessageService _messageService;
        private string _username;
        private string _password;
        private string _confirmPassword;
        private string _errorMessage;
        private bool _isRegistering;

        public event EventHandler RegisterSuccess;
        public event EventHandler CancelRequested;

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

        public string ConfirmPassword
        {
            get => _confirmPassword;
            set
            {
                if (_confirmPassword != value)
                {
                    _confirmPassword = value;
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

        public bool IsRegistering
        {
            get => _isRegistering;
            set
            {
                if (_isRegistering != value)
                {
                    _isRegistering = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand RegisterCommand { get; }
        public ICommand CancelCommand { get; }

        public RegisterViewModel(
            IUserService userService,
            IMessageService messageService)
        {
            _userService = userService;
            _messageService = messageService;

            RegisterCommand = new RelayCommand(async () => await RegisterAsync(), () => CanRegister());
            CancelCommand = new RelayCommand(() => OnCancelRequested());
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

            if (Password.Length < 6)
            {
                ErrorMessage = "密码长度不能少于6个字符";
                return;
            }

            if (Password != ConfirmPassword)
            {
                ErrorMessage = "两次输入的密码不一致";
                return;
            }

            ErrorMessage = string.Empty;
        }

        private bool CanRegister()
        {
            return !IsRegistering && 
                   !string.IsNullOrWhiteSpace(Username) && 
                   !string.IsNullOrWhiteSpace(Password) &&
                   !string.IsNullOrWhiteSpace(ConfirmPassword) &&
                   string.IsNullOrEmpty(ErrorMessage);
        }

        private async Task RegisterAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
                {
                    _messageService.ShowMessage("用户名和密码不能为空", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (Password != ConfirmPassword)
                {
                    _messageService.ShowMessage("两次输入的密码不一致", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                IsRegistering = true;
                ErrorMessage = string.Empty;

                // 验证用户是否已存在
                var isValid = await _userService.ValidateUserAsync(Username, Password);
                if (isValid)
                {
                    ErrorMessage = "用户名已存在";
                    return;
                }

                // 创建新用户
                var success = await _userService.CreateUserAsync(Username, Password);
                if (success)
                {
                    _messageService.ShowMessage("注册成功", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    RegisterSuccess?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    ErrorMessage = "注册失败，请稍后重试";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"注册失败：{ex.Message}";
                _messageService.ShowMessage($"注册失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsRegistering = false;
            }
        }

        private void OnCancelRequested()
        {
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }
    }
} 