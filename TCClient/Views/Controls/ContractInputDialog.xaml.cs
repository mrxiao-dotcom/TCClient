using System.Windows;

namespace TCClient.Views.Controls
{
    public partial class ContractInputDialog : Window
    {
        public string ContractSymbol { get; private set; }

        public ContractInputDialog()
        {
            InitializeComponent();
            ContractSymbolTextBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var symbol = ContractSymbolTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(symbol))
            {
                MessageBox.Show("请输入合约名称", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 移除USDT后缀（如果有）
            if (symbol.EndsWith("USDT", System.StringComparison.OrdinalIgnoreCase))
            {
                symbol = symbol.Substring(0, symbol.Length - 4);
            }

            ContractSymbol = symbol.ToUpper();
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