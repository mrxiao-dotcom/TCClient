using System;
using System.Windows;
using TCClient.Services;

namespace TCClient.Views
{
    public partial class BinanceApiConfigWindow : Window
    {
        public BinanceApiConfigWindow()
        {
            InitializeComponent();
            LoadCurrentConfig();
        }

        private async void LoadCurrentConfig()
        {
            try
            {
                var config = await BinanceApiConfigService.GetConfigAsync();
                
                ApiKeyTextBox.Text = config.ApiKey;
                SecretKeyPasswordBox.Password = config.SecretKey;
                IsEnabledCheckBox.IsChecked = config.IsEnabled;
                
                if (BinanceApiConfigService.IsValidConfig(config))
                {
                    StatusTextBlock.Text = $"当前配置有效，最后更新时间: {config.LastUpdated:yyyy-MM-dd HH:mm:ss}";
                    StatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
                }
                else
                {
                    StatusTextBlock.Text = "当前配置无效或未配置";
                    StatusTextBlock.Foreground = System.Windows.Media.Brushes.Orange;
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"加载配置失败: {ex.Message}";
                StatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            var apiKey = ApiKeyTextBox.Text.Trim();
            var secretKey = SecretKeyPasswordBox.Password.Trim();

            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(secretKey))
            {
                StatusTextBlock.Text = "请输入API Key和Secret Key";
                StatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            TestConnectionButton.IsEnabled = false;
            StatusTextBlock.Text = "正在测试连接...";
            StatusTextBlock.Foreground = System.Windows.Media.Brushes.Blue;

            try
            {
                var (success, message) = await BinanceApiConfigService.TestConnectionAsync(apiKey, secretKey);
                
                StatusTextBlock.Text = message;
                StatusTextBlock.Foreground = success ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Red;
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"测试连接失败: {ex.Message}";
                StatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
            }
            finally
            {
                TestConnectionButton.IsEnabled = true;
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var apiKey = ApiKeyTextBox.Text.Trim();
            var secretKey = SecretKeyPasswordBox.Password.Trim();
            var isEnabled = IsEnabledCheckBox.IsChecked == true;

            // 如果启用了API但没有输入密钥，提示用户
            if (isEnabled && (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(secretKey)))
            {
                var result = MessageBox.Show(
                    "您启用了币安API但未输入完整的密钥信息。\n\n是否继续保存？（系统将使用公共API）",
                    "确认保存",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            SaveButton.IsEnabled = false;
            StatusTextBlock.Text = "正在保存配置...";
            StatusTextBlock.Foreground = System.Windows.Media.Brushes.Blue;

            try
            {
                var config = new BinanceApiConfigService.BinanceApiConfig
                {
                    ApiKey = apiKey,
                    SecretKey = secretKey,
                    IsEnabled = isEnabled
                };

                var success = await BinanceApiConfigService.SaveConfigAsync(config);
                
                if (success)
                {
                    StatusTextBlock.Text = "配置保存成功";
                    StatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
                    
                    // 延迟关闭窗口
                    await System.Threading.Tasks.Task.Delay(1000);
                    DialogResult = true;
                    Close();
                }
                else
                {
                    StatusTextBlock.Text = "配置保存失败";
                    StatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"保存配置失败: {ex.Message}";
                StatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
            }
            finally
            {
                SaveButton.IsEnabled = true;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
} 