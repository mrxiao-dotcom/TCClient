using System.Windows;
using TCClient.ViewModels;
using TCClient.Services;
using Microsoft.Extensions.DependencyInjection;

namespace TCClient.Views
{
    public partial class AddAccountWindow : Window
    {
        public AddAccountViewModel ViewModel { get; }

        public AddAccountWindow()
        {
            InitializeComponent();

            var services = ((App)Application.Current).Services;
            ViewModel = services.GetRequiredService<AddAccountViewModel>();
            DataContext = ViewModel;

            // 监听ViewModel的属性变化
            ViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ViewModel.IsEditMode))
                {
                    // 如果是编辑模式，设置密码框的值
                    if (ViewModel.IsEditMode)
                    {
                        ApiSecretBox.Password = ViewModel.ApiSecret ?? string.Empty;
                        ApiPassphraseBox.Password = ViewModel.ApiPassphrase ?? string.Empty;
                    }
                }
            };

            // 监听密码框的值变化
            ApiSecretBox.PasswordChanged += (s, e) =>
            {
                if (ViewModel != null)
                {
                    ViewModel.ApiSecret = ApiSecretBox.Password;
                }
            };

            ApiPassphraseBox.PasswordChanged += (s, e) =>
            {
                if (ViewModel != null)
                {
                    ViewModel.ApiPassphrase = ApiPassphraseBox.Password;
                }
            };

            // 监听保存命令
            ViewModel.SaveCommand.CanExecuteChanged += (s, e) =>
            {
                // 在保存命令执行前，确保更新密码框的值
                if (ViewModel != null)
                {
                    ViewModel.ApiSecret = ApiSecretBox.Password;
                    ViewModel.ApiPassphrase = ApiPassphraseBox.Password;
                }
            };

            // 监听窗口关闭事件
            ViewModel.CloseWindow += OnViewModelCloseWindow;
        }

        private void OnViewModelCloseWindow(object sender, DialogResultEventArgs e)
        {
            // 确保在窗口显示为对话框后再设置 DialogResult
            if (IsLoaded && IsVisible)
            {
                DialogResult = e.Result;
            }
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            // 清理事件订阅
            if (ViewModel != null)
            {
                ViewModel.CloseWindow -= OnViewModelCloseWindow;
            }
        }
    }
} 