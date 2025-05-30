using System.Windows;
using System.Windows.Controls;
using TCClient.ViewModels;
using TCClient.Services;
using Microsoft.Extensions.DependencyInjection;

namespace TCClient.Views
{
    public partial class DatabaseConfigWindow : Window
    {
        private readonly DatabaseConfigViewModel _viewModel;
        private bool _isUpdatingPassword;

        public DatabaseConfigWindow()
        {
            InitializeComponent();
            
            // 从应用程序的服务容器获取服务
            var app = (App)Application.Current;
            var databaseService = app.Services.GetRequiredService<IDatabaseService>();
            var configService = app.Services.GetRequiredService<LocalConfigService>();
            
            _viewModel = new DatabaseConfigViewModel(databaseService, configService);
            DataContext = _viewModel;

            // 监听当前连接的变化
            _viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.CurrentConnection))
                {
                    UpdatePasswordBox();
                }
            };

            // 订阅关闭窗口事件
            _viewModel.CloseWindow += (result) =>
            {
                DialogResult = result;
                Close();
            };

            // 在窗口关闭时处理密码
            Closing += (s, e) =>
            {
                if (DialogResult == true)
                {
                    _viewModel.CurrentConnection.Password = PasswordBox.Password;
                }
            };

            // 初始化密码框
            UpdatePasswordBox();
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (!_isUpdatingPassword && _viewModel.CurrentConnection != null)
            {
                _viewModel.CurrentConnection.Password = PasswordBox.Password;
            }
        }

        private void UpdatePasswordBox()
        {
            if (_viewModel.CurrentConnection != null)
            {
                _isUpdatingPassword = true;
                try
                {
                    PasswordBox.Password = _viewModel.CurrentConnection.Password ?? string.Empty;
                }
                finally
                {
                    _isUpdatingPassword = false;
                }
            }
        }

        public DatabaseConfigViewModel ViewModel => _viewModel;
    }
} 