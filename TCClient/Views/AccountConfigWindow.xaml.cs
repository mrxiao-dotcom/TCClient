using System.Windows;
using TCClient.ViewModels;

namespace TCClient.Views
{
    public partial class AccountConfigWindow : Window
    {
        private readonly AccountConfigViewModel _viewModel;

        public AccountConfigWindow()
        {
            InitializeComponent();
            _viewModel = new AccountConfigViewModel();
            DataContext = _viewModel;

            _viewModel.CloseWindow += (sender, e) =>
            {
                DialogResult = e.Result;
                Close();
            };
        }

        public AccountConfigViewModel ViewModel => _viewModel;
    }
} 