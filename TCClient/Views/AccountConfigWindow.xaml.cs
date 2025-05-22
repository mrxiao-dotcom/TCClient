using System.Windows;
using TCClient.ViewModels;
using TCClient.Services;
using Microsoft.Extensions.DependencyInjection;

namespace TCClient.Views
{
    public partial class AccountConfigWindow : Window
    {
        public AccountConfigViewModel ViewModel { get; }

        public AccountConfigWindow()
        {
            InitializeComponent();

            var services = ((App)Application.Current).Services;
            ViewModel = services.GetRequiredService<AccountConfigViewModel>();
            DataContext = ViewModel;

            ViewModel.CloseWindow += (s, e) =>
            {
                DialogResult = e.Result;
                Close();
            };
        }
    }
} 