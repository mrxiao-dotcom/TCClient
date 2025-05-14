using System.Windows;
using System.Windows.Controls;
using TCClient.ViewModels;

namespace TCClient.Views
{
    public partial class LoginWindow : Window
    {
        private readonly LoginViewModel _viewModel;
        private bool _isUpdatingPassword;
        private TextBox _passwordTextBox;
        private bool _isPasswordVisible;
        private bool _isInitialized;

        public LoginWindow()
        {
            InitializeComponent();
            InitializePasswordToggle();

            _viewModel = new LoginViewModel();
            DataContext = _viewModel;

            // 监听密码属性的变化
            _viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.Password))
                {
                    UpdatePasswordBox();
                }
            };

            // 监听登录成功事件
            _viewModel.LoginSuccess += (s, e) =>
            {
                if (_isInitialized)
                {
                    Dispatcher.Invoke(() =>
                    {
                        DialogResult = true;
                        Close();
                    });
                }
            };

            // 初始化密码框
            UpdatePasswordBox();

            // 设置窗口为对话框模式
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;

            // 在窗口加载完成后标记为已初始化
            Loaded += (s, e) => _isInitialized = true;
        }

        private void InitializePasswordToggle()
        {
            // 创建隐藏的密码文本框
            _passwordTextBox = new TextBox
            {
                Visibility = Visibility.Collapsed,
                Text = PasswordBox.Password
            };
            Grid.SetColumn(_passwordTextBox, 1);
            Grid.SetRow(_passwordTextBox, 2);
            ((Grid)Content).Children.Add(_passwordTextBox);

            // 绑定密码框和文本框的文本
            PasswordBox.PasswordChanged += (s, e) => 
            {
                if (_isPasswordVisible)
                    _passwordTextBox.Text = PasswordBox.Password;
                if (!_isUpdatingPassword)
                    _viewModel.Password = PasswordBox.Password;
            };

            _passwordTextBox.TextChanged += (s, e) => 
            {
                if (_isPasswordVisible)
                {
                    PasswordBox.Password = _passwordTextBox.Text;
                    if (!_isUpdatingPassword)
                        _viewModel.Password = _passwordTextBox.Text;
                }
            };
        }

        private void TogglePasswordVisibility_Click(object sender, RoutedEventArgs e)
        {
            _isPasswordVisible = !_isPasswordVisible;
            var button = (Button)sender;
            
            if (_isPasswordVisible)
            {
                button.Content = "隐藏";
                _passwordTextBox.Text = PasswordBox.Password;
                _passwordTextBox.Visibility = Visibility.Visible;
                PasswordBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                button.Content = "显示";
                PasswordBox.Password = _passwordTextBox.Text;
                PasswordBox.Visibility = Visibility.Visible;
                _passwordTextBox.Visibility = Visibility.Collapsed;
            }
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (!_isUpdatingPassword)
            {
                _viewModel.Password = PasswordBox.Password;
            }
        }

        private void UpdatePasswordBox()
        {
            if (_viewModel.Password != null)
            {
                _isUpdatingPassword = true;
                try
                {
                    PasswordBox.Password = _viewModel.Password;
                    if (_isPasswordVisible)
                        _passwordTextBox.Text = _viewModel.Password;
                }
                finally
                {
                    _isUpdatingPassword = false;
                }
            }
        }

        public LoginViewModel ViewModel => _viewModel;
    }
} 