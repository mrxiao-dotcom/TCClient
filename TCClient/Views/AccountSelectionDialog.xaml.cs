using System.Collections.Generic;
using System.Windows;
using TCClient.Models;

namespace TCClient.Views
{
    public partial class AccountSelectionDialog : Window
    {
        public TradingAccount SelectedAccount { get; private set; }

        public AccountSelectionDialog(IEnumerable<TradingAccount> accounts)
        {
            InitializeComponent();
            AccountListView.ItemsSource = accounts;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedAccount = AccountListView.SelectedItem as TradingAccount;
            if (SelectedAccount == null)
            {
                MessageBox.Show("请选择一个账户", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
} 