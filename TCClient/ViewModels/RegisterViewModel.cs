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
        private string _username = string.Empty;
        private string _password = string.Empty;
        private string _confirmPassword = string.Empty;
        private string _errorMessage = string.Empty;
        private bool _isRegistering;

        public event Action? RegisterSuccess;
        public event Action? CancelRequested;

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

        public RegisterViewModel()
        {
            _userService = ServiceLocator.GetService<IUserService>();
            _messageService = ServiceLocator.GetService<IMessageService>();
            RegisterCommand = new RelayCommand(async () => await RegisterAsync(), () => CanRegister());
            CancelCommand = new RelayCommand(() => CancelRequested?.Invoke());
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
                   Password == ConfirmPassword &&
                   string.IsNullOrEmpty(ErrorMessage);
        }

        private async Task RegisterAsync()
        {
            if (!CanRegister() || _isRegistering) return;

            try
            {
                _isRegistering = true;
                ErrorMessage = string.Empty;

                if (string.IsNullOrWhiteSpace(Username))
                {
                    ErrorMessage = "请输入用户名";
                    Console.WriteLine("注册失败：用户名为空");
                    return;
                }

                if (string.IsNullOrWhiteSpace(Password))
                {
                    ErrorMessage = "请输入密码";
                    Console.WriteLine("注册失败：密码为空");
                    return;
                }

                if (Password != ConfirmPassword)
                {
                    ErrorMessage = "两次输入的密码不一致";
                    Console.WriteLine("注册失败：密码不一致");
                    return;
                }

                var result = await _userService.CreateUserAsync(Username, Password);
                if (result)
                {
                    Console.WriteLine("注册成功！");
                    RegisterSuccess?.Invoke();
                }
                else
                {
                    ErrorMessage = "用户名已存在";
                    Console.WriteLine("注册失败：用户名已存在");
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"注册失败：{ex.Message}";
                Console.WriteLine($"注册失败：{ex}");
                Console.WriteLine($"异常堆栈：{ex.StackTrace}");
            }
            finally
            {
                _isRegistering = false;
            }
        }

        private void Cancel()
        {
            // 关闭窗口的逻辑应该在View中处理
            if (Application.Current.Windows.Count > 0)
            {
                var window = Application.Current.Windows[Application.Current.Windows.Count - 1];
                if (window is Window w)
                {
                    w.DialogResult = false;
                    w.Close();
                }
            }
        }
    }
} 