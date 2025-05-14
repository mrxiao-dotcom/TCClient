using System.Windows;
using TCClient.ViewModels;
using TCClient.Utils;
using TCClient.Services;

namespace TCClient.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();

            var databaseService = ServiceLocator.GetService<IDatabaseService>();
            var configService = ServiceLocator.GetService<LocalConfigService>();
            var userService = ServiceLocator.GetService<IUserService>();
            var messageService = ServiceLocator.GetService<IMessageService>();

            _viewModel = new MainViewModel(databaseService, configService, userService, messageService);
            DataContext = _viewModel;

            // 窗口关闭前确认
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var result = MessageBox.Show(
                "确定要退出程序吗？",
                "确认",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.No)
            {
                e.Cancel = true;
            }
        }
    }
} 