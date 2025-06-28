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
        private readonly VolumeMonitorService _volumeMonitorService;
        private ObservableCollection<string> _tokens;
        private PushNotificationService.PushConfig _config;
        private VolumeMonitorService.VolumeMonitorConfig _volumeConfig;
        private const string TokenPlaceholder = "请输入虾推啥Token...";

        public PushConfigWindow(PushNotificationService pushService, VolumeMonitorService volumeMonitorService = null)
        {
            InitializeComponent();
            _pushService = pushService;
            _volumeMonitorService = volumeMonitorService ?? new VolumeMonitorService();
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
                _volumeConfig = _volumeMonitorService.GetConfig();
                
                // 更新推送配置界面控件
                EnablePushCheckBox.IsChecked = _config.IsEnabled;
                DailyLimitTextBox.Text = _config.DailyPushLimit.ToString();
                IntervalTextBox.Text = _config.PushIntervalMinutes.ToString();
                
                // 更新成交量监控配置界面控件
                EnableVolumeMonitorCheckBox.IsChecked = _volumeConfig.IsEnabled;
                LowVolumeThresholdTextBox.Text = (_volumeConfig.LowVolumeThreshold / 1_000_000_000).ToString(); // 转换为亿美元
                HighVolumeThresholdTextBox.Text = (_volumeConfig.HighVolumeThreshold / 1_000_000_000).ToString(); // 转换为亿美元
                VolumeMonitorIntervalTextBox.Text = _volumeConfig.MonitorIntervalMinutes.ToString();
                EnableLowVolumeAlertCheckBox.IsChecked = _volumeConfig.EnableLowVolumeAlert;
                EnableHighVolumeAlertCheckBox.IsChecked = _volumeConfig.EnableHighVolumeAlert;
                
                // 加载Token列表
                _tokens.Clear();
                foreach (var token in _config.XtuisTokens)
                {
                    _tokens.Add(token);
                }
                
                RefreshStatusDisplay();
                RefreshVolumeDisplay();
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
                var volumeConfig = GetCurrentVolumeConfig();
                
                // 验证推送配置
                if (config.IsEnabled && config.XtuisTokens.Count == 0)
                {
                    var result = MessageBox.Show("推送已启用但未配置Token，是否继续保存？", "确认保存", 
                        MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result == MessageBoxResult.No)
                        return;
                }
                
                _pushService.UpdateConfig(config);
                _volumeMonitorService.UpdateConfig(volumeConfig);
                
                MessageBox.Show("配置保存成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                
                AppSession.Log("PushConfigWindow: 推送配置和成交量监控配置保存成功");
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

        /// <summary>
        /// 获取当前成交量监控配置
        /// </summary>
        private VolumeMonitorService.VolumeMonitorConfig GetCurrentVolumeConfig()
        {
            var config = new VolumeMonitorService.VolumeMonitorConfig
            {
                IsEnabled = EnableVolumeMonitorCheckBox.IsChecked ?? false,
                EnableLowVolumeAlert = EnableLowVolumeAlertCheckBox.IsChecked ?? true,
                EnableHighVolumeAlert = EnableHighVolumeAlertCheckBox.IsChecked ?? true,
                MonitorIntervalMinutes = 10, // 默认值
                LastAlertTime = _volumeConfig?.LastAlertTime ?? DateTime.MinValue,
                AlertCooldownMinutes = _volumeConfig?.AlertCooldownMinutes ?? 60
            };

            // 解析低成交量阈值（从亿美元转换为美元）
            if (decimal.TryParse(LowVolumeThresholdTextBox.Text, out decimal lowThreshold) && lowThreshold > 0)
            {
                config.LowVolumeThreshold = lowThreshold * 1_000_000_000;
            }
            else
            {
                config.LowVolumeThreshold = 50_000_000_000; // 默认500亿美元
            }

            // 解析高成交量阈值（从亿美元转换为美元）
            if (decimal.TryParse(HighVolumeThresholdTextBox.Text, out decimal highThreshold) && highThreshold > 0)
            {
                config.HighVolumeThreshold = highThreshold * 1_000_000_000;
            }
            else
            {
                config.HighVolumeThreshold = 100_000_000_000; // 默认1000亿美元
            }

            // 验证阈值关系
            if (config.HighVolumeThreshold <= config.LowVolumeThreshold)
            {
                throw new ArgumentException("高成交量阈值必须大于低成交量阈值");
            }

            // 解析监控间隔
            if (int.TryParse(VolumeMonitorIntervalTextBox.Text, out int interval) && interval > 0)
            {
                config.MonitorIntervalMinutes = interval;
            }

            return config;
        }

        /// <summary>
        /// 刷新成交量显示
        /// </summary>
        private void RefreshVolumeDisplay()
        {
            try
            {
                var lastVolume = _volumeMonitorService.GetLastVolume();
                if (lastVolume.HasValue)
                {
                    CurrentVolumeText.Text = $"${lastVolume.Value / 1_000_000_000:F1}B";
                    CurrentVolumeText.Foreground = System.Windows.Media.Brushes.Green;
                }
                else
                {
                    CurrentVolumeText.Text = "未获取";
                    CurrentVolumeText.Foreground = System.Windows.Media.Brushes.Gray;
                }
            }
            catch (Exception ex)
            {
                AppSession.Log($"PushConfigWindow: 刷新成交量显示失败 - {ex.Message}");
                CurrentVolumeText.Text = "获取失败";
                CurrentVolumeText.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        /// <summary>
        /// 刷新成交量按钮点击
        /// </summary>
        private async void RefreshVolumeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RefreshVolumeButton.IsEnabled = false;
                RefreshVolumeButton.Content = "获取中...";
                CurrentVolumeText.Text = "获取中...";
                CurrentVolumeText.Foreground = System.Windows.Media.Brushes.Blue;

                var volume = await _volumeMonitorService.ManualCheckAsync();
                RefreshVolumeDisplay();

                if (volume.HasValue)
                {
                    AppSession.Log($"PushConfigWindow: 手动获取成交量成功 - ${volume.Value:N0}");
                }
                else
                {
                    MessageBox.Show("获取成交量数据失败，请检查网络连接", "获取失败", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                AppSession.Log($"PushConfigWindow: 手动获取成交量失败 - {ex.Message}");
                MessageBox.Show($"获取成交量数据失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                CurrentVolumeText.Text = "获取失败";
                CurrentVolumeText.Foreground = System.Windows.Media.Brushes.Red;
            }
            finally
            {
                RefreshVolumeButton.IsEnabled = true;
                RefreshVolumeButton.Content = "刷新";
            }
        }

        /// <summary>
        /// 测试成交量预警按钮点击
        /// </summary>
        private async void TestVolumeAlertButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TestVolumeAlertButton.IsEnabled = false;
                TestVolumeAlertButton.Content = "测试中...";

                AppSession.Log("PushConfigWindow: 开始测试成交量预警推送");

                // 先保存当前配置
                try
                {
                    var config = GetCurrentConfig();
                    var volumeConfig = GetCurrentVolumeConfig();
                    
                    _pushService.UpdateConfig(config);
                    _volumeMonitorService.UpdateConfig(volumeConfig);
                }
                catch (Exception configEx)
                {
                    MessageBox.Show($"保存配置失败: {configEx.Message}", "配置错误", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 执行测试
                var success = await _volumeMonitorService.TestVolumeAlertAsync();

                if (success)
                {
                    MessageBox.Show("✅ 成交量预警推送测试成功！\n\n请检查你的推送接收端确认是否收到测试消息。", 
                        "测试成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    AppSession.Log("PushConfigWindow: 成交量预警推送测试成功");
                }
                else
                {
                    var pushConfig = _pushService.GetConfig();
                    var errorMsg = "❌ 成交量预警推送测试失败\n\n可能的原因：\n";
                    
                    if (!pushConfig.IsEnabled)
                    {
                        errorMsg += "• 推送功能未启用\n";
                    }
                    if (pushConfig.XtuisTokens.Count == 0)
                    {
                        errorMsg += "• 未配置虾推啥Token\n";
                    }
                    if (pushConfig.TodayPushCount >= pushConfig.DailyPushLimit)
                    {
                        errorMsg += $"• 已达到每日推送限制 ({pushConfig.TodayPushCount}/{pushConfig.DailyPushLimit})\n";
                    }
                    
                    var timeSinceLastPush = DateTime.Now - pushConfig.LastPushTime;
                    if (timeSinceLastPush.TotalMinutes < pushConfig.PushIntervalMinutes)
                    {
                        var remainingMinutes = pushConfig.PushIntervalMinutes - timeSinceLastPush.TotalMinutes;
                        errorMsg += $"• 推送间隔未到，还需等待 {remainingMinutes:F1} 分钟\n";
                    }
                    
                    errorMsg += "\n请检查推送配置后重试。";
                    
                    MessageBox.Show(errorMsg, "测试失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    AppSession.Log("PushConfigWindow: 成交量预警推送测试失败");
                }
            }
            catch (Exception ex)
            {
                AppSession.Log($"PushConfigWindow: 测试成交量预警推送失败 - {ex.Message}");
                MessageBox.Show($"测试成交量预警推送失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                TestVolumeAlertButton.IsEnabled = true;
                TestVolumeAlertButton.Content = "测试预警";
            }
        }
    }
} 