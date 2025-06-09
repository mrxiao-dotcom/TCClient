using System;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TCClient.Services;
using Microsoft.Extensions.DependencyInjection;

namespace TCClient.Views
{
    public partial class NetworkDiagnosticWindow : Window
    {
        private readonly IExchangeService _exchangeService;
        private bool _isDiagnosing = false;

        public NetworkDiagnosticWindow()
        {
            InitializeComponent();
            
            // 从依赖注入容器获取交易所服务
            var app = Application.Current as App;
            _exchangeService = app?.Services?.GetService<IExchangeService>();
        }

        private async void StartDiagnosticButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isDiagnosing) return;
            
            _isDiagnosing = true;
            StartDiagnosticButton.IsEnabled = false;
            RetryConnectionButton.IsEnabled = false;
            
            try
            {
                await RunNetworkDiagnosticAsync();
            }
            finally
            {
                _isDiagnosing = false;
                StartDiagnosticButton.IsEnabled = true;
                RetryConnectionButton.IsEnabled = true;
            }
        }

        private async Task RunNetworkDiagnosticAsync()
        {
            // 清空之前的结果
            DiagnosticResultsPanel.Children.Clear();
            DiagnosticProgressBar.Value = 0;
            ProgressTextBlock.Text = "开始网络诊断...";

            await AddDiagnosticResult("🔍 开始网络连接诊断", "正在检测网络连接状况...", null);

            // 1. 检查本地网络连接
            await UpdateProgress(10, "检查本地网络连接...");
            await CheckLocalNetworkConnection();

            // 2. DNS解析测试
            await UpdateProgress(25, "测试DNS解析...");
            await CheckDNSResolution();

            // 3. Ping测试
            await UpdateProgress(40, "测试网络延迟...");
            await CheckNetworkLatency();

            // 4. 测试Binance API连接
            await UpdateProgress(60, "测试Binance API连接...");
            await CheckBinanceAPIConnection();

            // 5. 测试获取价格数据
            await UpdateProgress(80, "测试获取价格数据...");
            await CheckPriceDataRetrieval();

            // 诊断完成
            await UpdateProgress(100, "诊断完成");
            await AddDiagnosticResult("✅ 诊断完成", "网络诊断已完成，请查看上述结果", "success");
        }

        private async Task CheckLocalNetworkConnection()
        {
            try
            {
                bool hasConnection = NetworkInterface.GetIsNetworkAvailable();
                
                if (hasConnection)
                {
                    await AddDiagnosticResult("✅ 本地网络连接", "本地网络连接正常", "success");
                }
                else
                {
                    await AddDiagnosticResult("❌ 本地网络连接", "本地网络连接异常，请检查网线或WiFi连接", "error");
                }
            }
            catch (Exception ex)
            {
                await AddDiagnosticResult("❌ 本地网络连接", $"检测本地网络连接时出错: {ex.Message}", "error");
            }
        }

        private async Task CheckDNSResolution()
        {
            try
            {
                var hostNames = new[] { "api.binance.com", "fapi.binance.com", "www.google.com" };
                
                foreach (var hostName in hostNames)
                {
                    try
                    {
                        var hostEntry = await System.Net.Dns.GetHostEntryAsync(hostName);
                        if (hostEntry.AddressList.Length > 0)
                        {
                            await AddDiagnosticResult($"✅ DNS解析 ({hostName})", 
                                $"成功解析到 {hostEntry.AddressList[0]}", "success");
                        }
                        else
                        {
                            await AddDiagnosticResult($"❌ DNS解析 ({hostName})", 
                                "DNS解析失败，未返回IP地址", "error");
                        }
                    }
                    catch (Exception ex)
                    {
                        await AddDiagnosticResult($"❌ DNS解析 ({hostName})", 
                            $"DNS解析失败: {ex.Message}", "error");
                    }
                    
                    await Task.Delay(100); // 避免过快的请求
                }
            }
            catch (Exception ex)
            {
                await AddDiagnosticResult("❌ DNS解析测试", $"DNS解析测试出错: {ex.Message}", "error");
            }
        }

        private async Task CheckNetworkLatency()
        {
            var hosts = new[] { "api.binance.com", "fapi.binance.com", "8.8.8.8" };
            
            foreach (var host in hosts)
            {
                try
                {
                    using (var ping = new Ping())
                    {
                        var reply = await ping.SendPingAsync(host, 5000);
                        
                        if (reply.Status == IPStatus.Success)
                        {
                            string latencyInfo = $"延迟: {reply.RoundtripTime}ms";
                            if (reply.RoundtripTime < 100)
                            {
                                await AddDiagnosticResult($"✅ Ping测试 ({host})", latencyInfo + " (良好)", "success");
                            }
                            else if (reply.RoundtripTime < 300)
                            {
                                await AddDiagnosticResult($"⚠️ Ping测试 ({host})", latencyInfo + " (一般)", "warning");
                            }
                            else
                            {
                                await AddDiagnosticResult($"❌ Ping测试 ({host})", latencyInfo + " (较慢)", "error");
                            }
                        }
                        else
                        {
                            await AddDiagnosticResult($"❌ Ping测试 ({host})", 
                                $"Ping失败: {reply.Status}", "error");
                        }
                    }
                }
                catch (Exception ex)
                {
                    await AddDiagnosticResult($"❌ Ping测试 ({host})", 
                        $"Ping测试出错: {ex.Message}", "error");
                }
                
                await Task.Delay(100);
            }
        }

        private async Task CheckBinanceAPIConnection()
        {
            try
            {
                if (_exchangeService == null)
                {
                    await AddDiagnosticResult("❌ Binance API连接", 
                        "交易所服务未初始化", "error");
                    return;
                }

                if (_exchangeService is BinanceExchangeService binanceService)
                {
                    bool isConnected = await binanceService.TestConnectionAsync();
                    
                    if (isConnected)
                    {
                        await AddDiagnosticResult("✅ Binance API连接", 
                            "Binance API连接测试成功", "success");
                    }
                    else
                    {
                        await AddDiagnosticResult("❌ Binance API连接", 
                            "Binance API连接测试失败，可能需要代理或网络有限制", "error");
                    }
                }
                else
                {
                    await AddDiagnosticResult("⚠️ Binance API连接", 
                        "当前使用模拟交易服务，无法测试真实API连接", "warning");
                }
            }
            catch (Exception ex)
            {
                await AddDiagnosticResult("❌ Binance API连接", 
                    $"API连接测试出错: {ex.Message}", "error");
            }
        }

        private async Task CheckPriceDataRetrieval()
        {
            try
            {
                if (_exchangeService == null)
                {
                    await AddDiagnosticResult("❌ 价格数据获取", 
                        "交易所服务未初始化", "error");
                    return;
                }

                // 尝试获取BTC的价格数据
                var ticker = await _exchangeService.GetTickerAsync("BTCUSDT");
                
                if (ticker != null && ticker.LastPrice > 0)
                {
                    await AddDiagnosticResult("✅ 价格数据获取", 
                        $"成功获取BTCUSDT价格: {ticker.LastPrice:F2}", "success");
                }
                else
                {
                    await AddDiagnosticResult("❌ 价格数据获取", 
                        "无法获取价格数据，ticker返回null或价格为0", "error");
                }

                // 尝试获取所有合约列表
                var tickers = await _exchangeService.GetAllTickersAsync();
                if (tickers != null && tickers.Count > 0)
                {
                    await AddDiagnosticResult("✅ 合约列表获取", 
                        $"成功获取 {tickers.Count} 个合约的行情数据", "success");
                }
                else
                {
                    await AddDiagnosticResult("❌ 合约列表获取", 
                        "无法获取合约列表数据", "error");
                }
            }
            catch (Exception ex)
            {
                await AddDiagnosticResult("❌ 价格数据获取", 
                    $"获取价格数据时出错: {ex.Message}", "error");
            }
        }

        private async Task UpdateProgress(int value, string message)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                DiagnosticProgressBar.Value = value;
                ProgressTextBlock.Text = message;
            });
            
            await Task.Delay(200); // 让用户看到进度更新
        }

        private async Task AddDiagnosticResult(string title, string message, string type)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                var panel = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Margin = new Thickness(0, 5, 0, 5)
                };

                var titleBlock = new TextBlock
                {
                    Text = title,
                    FontWeight = FontWeights.Bold,
                    FontSize = 13
                };

                var messageBlock = new TextBlock
                {
                    Text = message,
                    FontSize = 12,
                    Margin = new Thickness(20, 2, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                };

                // 根据类型设置颜色
                switch (type)
                {
                    case "success":
                        titleBlock.Foreground = Brushes.Green;
                        break;
                    case "error":
                        titleBlock.Foreground = Brushes.Red;
                        break;
                    case "warning":
                        titleBlock.Foreground = Brushes.Orange;
                        break;
                    default:
                        titleBlock.Foreground = Brushes.Black;
                        break;
                }

                panel.Children.Add(titleBlock);
                panel.Children.Add(messageBlock);

                DiagnosticResultsPanel.Children.Add(panel);
            });
        }

        private async void RetryConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            RetryConnectionButton.IsEnabled = false;
            
            try
            {
                ProgressTextBlock.Text = "正在重试连接...";
                
                // 清空诊断结果
                DiagnosticResultsPanel.Children.Clear();
                
                await AddDiagnosticResult("🔄 重试连接", "正在尝试重新连接到Binance API...", null);

                if (_exchangeService != null)
                {
                    // 尝试获取价格数据
                    var ticker = await _exchangeService.GetTickerAsync("BTCUSDT");
                    
                    if (ticker != null && ticker.LastPrice > 0)
                    {
                        await AddDiagnosticResult("✅ 重试成功", 
                            $"成功获取BTCUSDT价格: {ticker.LastPrice:F2}，网络连接已恢复", "success");
                        ProgressTextBlock.Text = "连接成功";
                        
                        MessageBox.Show("网络连接已恢复！您现在可以正常使用程序了。", 
                                      "连接成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        await AddDiagnosticResult("❌ 重试失败", 
                            "仍然无法获取价格数据，请检查网络连接或稍后再试", "error");
                        ProgressTextBlock.Text = "连接失败";
                    }
                }
                else
                {
                    await AddDiagnosticResult("❌ 重试失败", 
                        "交易所服务未初始化", "error");
                }
            }
            catch (Exception ex)
            {
                await AddDiagnosticResult("❌ 重试失败", 
                    $"重试连接时出错: {ex.Message}", "error");
                ProgressTextBlock.Text = "重试失败";
            }
            finally
            {
                RetryConnectionButton.IsEnabled = true;
            }
        }

        private void OpenNetworkSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 打开Windows网络设置
                Process.Start(new ProcessStartInfo
                {
                    FileName = "ms-settings:network",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开网络设置: {ex.Message}\n\n请手动打开Windows设置 > 网络和Internet", 
                              "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
} 