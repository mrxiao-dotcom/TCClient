using System.Windows;

namespace TCClient.Views
{
    public partial class ContractInputDialog : Window
    {
        public string ContractCode { get; set; } = string.Empty;

        public ContractInputDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ContractCode))
            {
                MessageBox.Show("请输入合约代码", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 处理合约代码
            ContractCode = ProcessContractCode(ContractCode.Trim());
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// 处理合约代码，自动补充USDT后缀
        /// </summary>
        /// <param name="input">用户输入的合约代码</param>
        /// <returns>处理后的合约代码</returns>
        private string ProcessContractCode(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            // 转换为大写
            input = input.ToUpper();

            // 如果已经包含USDT，直接返回
            if (input.EndsWith("USDT"))
                return input;

            // 如果包含其他稳定币后缀，也直接返回
            string[] stableCoins = { "BUSD", "USDC", "TUSD", "FDUSD" };
            foreach (var stableCoin in stableCoins)
            {
                if (input.EndsWith(stableCoin))
                    return input;
            }

            // 如果包含其他常见交易对后缀，也直接返回
            string[] otherPairs = { "BTC", "ETH", "BNB" };
            foreach (var pair in otherPairs)
            {
                if (input.EndsWith(pair) && input != pair) // 避免BTC变成BTCUSDT
                    return input;
            }

            // 自动添加USDT后缀
            return input + "USDT";
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
} 