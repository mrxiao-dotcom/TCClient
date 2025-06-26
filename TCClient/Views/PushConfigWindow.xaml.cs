using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TCClient.Services;
using TCClient.Utils;

namespace TCClient.Views
{
    /// <summary>
    /// 推送配置窗口
    /// </summary>
    public partial class PushConfigWindow : Window
    {
        private readonly PushNotificationService _pushService;
        private ObservableCollection<string> _tokens;
        private PushNotificationService.PushConfig _config;
        private const string TokenPlaceholder = "请输入虾推啥Token...";

        public PushConfigWindow(PushNotificationService pushService)
        {
            InitializeComponent();
            _pushService = pushService;
            _tokens = new ObservableCollection<string>();
            TokenListBox.ItemsSource = _tokens;
            
            LoadCurrentConfig();
            Loaded += PushConfigWindow_Loaded;
        }

        private void PushConfigWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 窗口加载完成后刷新状态显示
            RefreshStatusDisplay();
        }

        /// <summary>
        /// 加载当前配置
        /// </summary>
        private void LoadCurrentConfig()
        {
            try
            {
                _config = _pushService.GetConfig();
                
                // 更新界面控件
                EnablePushCheckBox.IsChecked = _config.IsEnabled;
                DailyLimitTextBox.Text = _config.DailyPushLimit.ToString();
                IntervalTextBox.Text = _config.PushIntervalMinutes.ToString();
                
                // 加载Token列表
                _tokens.Clear();
                foreach (var token in _config.XtuisTokens)
                {
                    _tokens.Add(token);
                }
                
                RefreshStatusDisplay();
            }
            catch (Exception ex)
            {
                AppSession.Log($"PushConfigWindow: 加载配置失败 - {ex.Message}");
                MessageBox.Show($"加载配置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 刷新状态显示
        /// </summary>
        private void RefreshStatusDisplay()
        {
            try
            {
                var config = _pushService.GetConfig();
                
                TodayPushCountText.Text = $"{config.TodayPushCount} 次";
                PushLimitText.Text = $"{config.DailyPushLimit} 次/天";
                
                if (config.LastPushDate == DateTime.MinValue)
                {
                    LastPushTimeText.Text = "从未推送";
                }
                else
                {
                    LastPushTimeText.Text = config.LastPushDate.ToString("yyyy-MM-dd");
                }
                
                // 更新推送状态
                if (!config.IsEnabled)
                {
                    PushStatusText.Text = "未启用";
                    PushStatusText.Foreground = System.Windows.Media.Brushes.Gray;
                }
                else if (config.XtuisTokens.Count == 0)
                {
                    PushStatusText.Text = "未配置Token";
                    PushStatusText.Foreground = System.Windows.Media.Brushes.Orange;
                }
                else if (config.TodayPushCount >= config.DailyPushLimit)
                {
                    PushStatusText.Text = "今日已达上限";
                    PushStatusText.Foreground = System.Windows.Media.Brushes.Red;
                }
                else
                {
                    PushStatusText.Text = "正常";
                    PushStatusText.Foreground = System.Windows.Media.Brushes.Green;
                }
            }
            catch (Exception ex)
            {
                AppSession.Log($"PushConfigWindow: 刷新状态显示失败 - {ex.Message}");
            }
        }

        /// <summary>
        /// 添加Token
        /// </summary>
        private void AddTokenButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var token = NewTokenTextBox.Text.Trim();
                if (string.IsNullOrEmpty(token))
                {
                    MessageBox.Show("请输入Token", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (_tokens.Contains(token))
                {
                    MessageBox.Show("Token已存在", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _tokens.Add(token);
                NewTokenTextBox.Text = string.Empty;
                
                AppSession.Log($"PushConfigWindow: 添加Token - {token}");
            }
            catch (Exception ex)
            {
                AppSession.Log($"PushConfigWindow: 添加Token失败 - {ex.Message}");
                MessageBox.Show($"添加Token失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 删除Token
        /// </summary>
        private void RemoveTokenButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.Tag is string token)
                {
                    var result = MessageBox.Show($"确定要删除Token: {token} ?", "确认删除", 
                        MessageBoxButton.YesNo, MessageBoxImage.Question);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        _tokens.Remove(token);
                        AppSession.Log($"PushConfigWindow: 删除Token - {token}");
                    }
                }
            }
            catch (Exception ex)
            {
                AppSession.Log($"PushConfigWindow: 删除Token失败 - {ex.Message}");
                MessageBox.Show($"删除Token失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 测试推送
        /// </summary>
        private async void TestPushButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TestPushButton.IsEnabled = false;
                TestPushButton.Content = "推送中...";
                
                // 临时保存当前配置
                var tempConfig = GetCurrentConfig();
                _pushService.UpdateConfig(tempConfig);
                
                if (!_pushService.CanPush())
                {
                    MessageBox.Show("推送条件不满足：\n- 请检查是否启用推送\n- 是否配置了Token\n- 是否超过每日推送限制", 
                        "无法推送", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 测试网络连接
                TestPushButton.Content = "检测网络...";
                var networkOk = await _pushService.TestNetworkConnectionAsync();
                if (!networkOk)
                {
                    MessageBox.Show("网络连接测试失败！\n\n请检查：\n• 网络连接是否正常\n• 防火墙是否阻止了连接\n• 是否可以访问 https://xtuis.cn/", 
                        "网络连接失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                TestPushButton.Content = "发送测试消息...";
                
                // 创建测试消息
                var testResult = new MarketAnalysisResult
                {
                    RisingAnalysis = new MarketChangeAnalysis
                    {
                        BothPeriods = new System.Collections.Generic.List<SymbolChangeInfo>
                        {
                            new SymbolChangeInfo { Symbol = "BTCUSDT", TodayChange = 5.2m, ThirtyDayChange = 12.8m },
                            new SymbolChangeInfo { Symbol = "ETHUSDT", TodayChange = 3.1m, ThirtyDayChange = 8.4m }
                        },
                        OnlyToday = new System.Collections.Generic.List<SymbolChangeInfo>
                        {
                            new SymbolChangeInfo { Symbol = "ADAUSDT", TodayChange = 2.5m }
                        }
                    },
                    FallingAnalysis = new MarketChangeAnalysis
                    {
                        BothPeriods = new System.Collections.Generic.List<SymbolChangeInfo>
                        {
                            new SymbolChangeInfo { Symbol = "XRPUSDT", TodayChange = -2.1m, ThirtyDayChange = -5.6m }
                        }
                    },
                    TotalSymbolsAnalyzed = 100
                };
                
                // 使用新的测试工具进行详细测试
                var tokens = tempConfig.XtuisTokens;
                if (tokens.Count == 0)
                {
                    MessageBox.Show("请先添加虾推啥Token", "无Token", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var allSuccess = true;
                var reportBuilder = new System.Text.StringBuilder();
                reportBuilder.AppendLine("=== 推送测试详细报告 ===\n");

                foreach (var token in tokens)
                {
                    TestPushButton.Content = $"测试Token: {token.Substring(0, Math.Min(6, token.Length))}...";
                    
                    var apiTestResult = await Utils.XtuisApiTester.TestTokenAsync(token, "测试推送消息");
                    var report = Utils.XtuisApiTester.GenerateTestReport(apiTestResult);
                    reportBuilder.AppendLine(report);
                    reportBuilder.AppendLine();

                    if (!apiTestResult.IsSuccess)
                    {
                        allSuccess = false;
                    }
                }

                if (allSuccess)
                {
                    MessageBox.Show("✅ 所有Token测试成功！\n\n请查看你的推送接收端确认是否收到测试消息。", 
                        "推送测试成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    RefreshStatusDisplay();
                }
                else
                {
                    // 显示详细的测试报告
                    var detailWindow = new Window
                    {
                        Title = "推送测试详细报告",
                        Width = 700,
                        Height = 500,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Owner = this
                    };
                    
                    var scrollViewer = new ScrollViewer
                    {
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Margin = new Thickness(10)
                    };
                    
                    var textBlock = new TextBlock
                    {
                        Text = reportBuilder.ToString(),
                        FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap
                    };
                    
                    scrollViewer.Content = textBlock;
                    detailWindow.Content = scrollViewer;
                    detailWindow.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                AppSession.Log($"PushConfigWindow: 测试推送失败 - {ex.Message}");
                MessageBox.Show($"测试推送失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                TestPushButton.IsEnabled = true;
                TestPushButton.Content = "发送测试消息";
            }
        }

        /// <summary>
        /// 保存配置
        /// </summary>
        private void SaveConfigButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var config = GetCurrentConfig();
                
                // 验证配置
                if (config.IsEnabled && config.XtuisTokens.Count == 0)
                {
                    var result = MessageBox.Show("推送已启用但未配置Token，是否继续保存？", "确认保存", 
                        MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result == MessageBoxResult.No)
                        return;
                }
                
                _pushService.UpdateConfig(config);
                
                MessageBox.Show("配置保存成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                
                AppSession.Log("PushConfigWindow: 推送配置保存成功");
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                AppSession.Log($"PushConfigWindow: 保存配置失败 - {ex.Message}");
                MessageBox.Show($"保存配置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 取消配置
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// 获取当前配置
        /// </summary>
        private PushNotificationService.PushConfig GetCurrentConfig()
        {
            var config = new PushNotificationService.PushConfig
            {
                IsEnabled = EnablePushCheckBox.IsChecked ?? false,
                XtuisTokens = _tokens.ToList(),
                TodayPushCount = _config.TodayPushCount,
                LastPushDate = _config.LastPushDate
            };
            
            // 解析数值
            if (int.TryParse(DailyLimitTextBox.Text, out int dailyLimit) && dailyLimit > 0)
            {
                config.DailyPushLimit = dailyLimit;
            }
            else
            {
                config.DailyPushLimit = 5; // 默认值
            }
            
            if (int.TryParse(IntervalTextBox.Text, out int interval) && interval > 0)
            {
                config.PushIntervalMinutes = interval;
            }
            else
            {
                config.PushIntervalMinutes = 240; // 默认值
            }
            
            return config;
        }

        /// <summary>
        /// 文本框获得焦点时清除占位符
        /// </summary>
        private void NewTokenTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (NewTokenTextBox.Text == TokenPlaceholder)
            {
                NewTokenTextBox.Text = string.Empty;
                NewTokenTextBox.Foreground = System.Windows.Media.Brushes.Black;
            }
        }

        /// <summary>
        /// 文本框失去焦点时显示占位符
        /// </summary>
        private void NewTokenTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NewTokenTextBox.Text))
            {
                NewTokenTextBox.Text = TokenPlaceholder;
                NewTokenTextBox.Foreground = System.Windows.Media.Brushes.Gray;
            }
        }
    }
} 