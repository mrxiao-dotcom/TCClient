using System.Windows;
using System.Windows.Controls;
using TCClient.ViewModels;

namespace TCClient.Views
{
    public partial class RegisterWindow : Window
    {
        private TextBox _passwordTextBox;
        private TextBox _confirmPasswordTextBox;
        private bool _isPasswordVisible;
        private bool _isConfirmPasswordVisible;
        private readonly RegisterViewModel _viewModel;
        private bool _isUpdatingPassword;
        private bool _isUpdatingConfirmPassword;

        public RegisterWindow()
        {
            InitializeComponent();
            InitializePasswordToggles();

            _viewModel = new RegisterViewModel();
            DataContext = _viewModel;

            // 监听密码属性的变化
            _viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.Password))
                {
                    UpdatePasswordBox();
                }
                else if (e.PropertyName == nameof(_viewModel.ConfirmPassword))
                {
                    UpdateConfirmPasswordBox();
                }
            };

            // 订阅注册成功事件
            _viewModel.RegisterSuccess += () =>
            {
                DialogResult = true;
                Close();
            };

            // 订阅取消事件
            _viewModel.CancelRequested += () =>
            {
                DialogResult = false;
                Close();
            };

            // 初始化密码框
            UpdatePasswordBox();
            UpdateConfirmPasswordBox();
        }

        private void InitializePasswordToggles()
        {
            // 创建隐藏的密码文本框
            _passwordTextBox = new TextBox
            {
                Visibility = Visibility.Collapsed,
                Text = PasswordBox.Password
            };
            Grid.SetColumn(_passwordTextBox, 0);
            Grid.SetRow(_passwordTextBox, 3);
            ((Grid)Content).Children.Add(_passwordTextBox);

            _confirmPasswordTextBox = new TextBox
            {
                Visibility = Visibility.Collapsed,
                Text = ConfirmPasswordBox.Password
            };
            Grid.SetColumn(_confirmPasswordTextBox, 0);
            Grid.SetRow(_confirmPasswordTextBox, 5);
            ((Grid)Content).Children.Add(_confirmPasswordTextBox);

            // 绑定密码框和文本框的文本
            PasswordBox.PasswordChanged += (s, e) => 
            {
                if (!_isUpdatingPassword)
                {
                    _viewModel.Password = PasswordBox.Password;
                    if (_isPasswordVisible)
                    {
                        _passwordTextBox.Text = PasswordBox.Password;
                    }
                }
            };

            _passwordTextBox.TextChanged += (s, e) => 
            {
                if (!_isUpdatingPassword && _isPasswordVisible)
                {
                    _isUpdatingPassword = true;
                    try
                    {
                        PasswordBox.Password = _passwordTextBox.Text;
                        _viewModel.Password = _passwordTextBox.Text;
                    }
                    finally
                    {
                        _isUpdatingPassword = false;
                    }
                }
            };

            ConfirmPasswordBox.PasswordChanged += (s, e) => 
            {
                if (!_isUpdatingConfirmPassword)
                {
                    _viewModel.ConfirmPassword = ConfirmPasswordBox.Password;
                    if (_isConfirmPasswordVisible)
                    {
                        _confirmPasswordTextBox.Text = ConfirmPasswordBox.Password;
                    }
                }
            };

            _confirmPasswordTextBox.TextChanged += (s, e) => 
            {
                if (!_isUpdatingConfirmPassword && _isConfirmPasswordVisible)
                {
                    _isUpdatingConfirmPassword = true;
                    try
                    {
                        ConfirmPasswordBox.Password = _confirmPasswordTextBox.Text;
                        _viewModel.ConfirmPassword = _confirmPasswordTextBox.Text;
                    }
                    finally
                    {
                        _isUpdatingConfirmPassword = false;
                    }
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
                _isUpdatingPassword = true;
                try
                {
                    _passwordTextBox.Text = PasswordBox.Password;
                    _passwordTextBox.Visibility = Visibility.Visible;
                    PasswordBox.Visibility = Visibility.Collapsed;
                }
                finally
                {
                    _isUpdatingPassword = false;
                }
            }
            else
            {
                button.Content = "显示";
                _isUpdatingPassword = true;
                try
                {
                    PasswordBox.Password = _passwordTextBox.Text;
                    PasswordBox.Visibility = Visibility.Visible;
                    _passwordTextBox.Visibility = Visibility.Collapsed;
                }
                finally
                {
                    _isUpdatingPassword = false;
                }
            }
        }

        private void ToggleConfirmPassword_Click(object sender, RoutedEventArgs e)
        {
            _isConfirmPasswordVisible = !_isConfirmPasswordVisible;
            var button = (Button)sender;
            
            if (_isConfirmPasswordVisible)
            {
                button.Content = "隐藏";
                _isUpdatingConfirmPassword = true;
                try
                {
                    _confirmPasswordTextBox.Text = ConfirmPasswordBox.Password;
                    _confirmPasswordTextBox.Visibility = Visibility.Visible;
                    ConfirmPasswordBox.Visibility = Visibility.Collapsed;
                }
                finally
                {
                    _isUpdatingConfirmPassword = false;
                }
            }
            else
            {
                button.Content = "显示";
                _isUpdatingConfirmPassword = true;
                try
                {
                    ConfirmPasswordBox.Password = _confirmPasswordTextBox.Text;
                    ConfirmPasswordBox.Visibility = Visibility.Visible;
                    _confirmPasswordTextBox.Visibility = Visibility.Collapsed;
                }
                finally
                {
                    _isUpdatingConfirmPassword = false;
                }
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
                    {
                        _passwordTextBox.Text = _viewModel.Password;
                    }
                }
                finally
                {
                    _isUpdatingPassword = false;
                }
            }
        }

        private void UpdateConfirmPasswordBox()
        {
            if (_viewModel.ConfirmPassword != null)
            {
                _isUpdatingConfirmPassword = true;
                try
                {
                    ConfirmPasswordBox.Password = _viewModel.ConfirmPassword;
                    if (_isConfirmPasswordVisible)
                    {
                        _confirmPasswordTextBox.Text = _viewModel.ConfirmPassword;
                    }
                }
                finally
                {
                    _isUpdatingConfirmPassword = false;
                }
            }
        }

        public RegisterViewModel ViewModel => _viewModel;
    }
} 