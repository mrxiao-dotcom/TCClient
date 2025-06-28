using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TCClient.Utils;
using Newtonsoft.Json;
using System.IO;

namespace TCClient.Services
{
    /// <summary>
    /// 成交量监控服务 - 定时监控24小时成交量并发送预警
    /// </summary>
    public class VolumeMonitorService : IDisposable
    {
        private readonly WebScrapingService _webScrapingService;
        private readonly PushNotificationService _pushNotificationService;
        private readonly ILogger<VolumeMonitorService> _logger;
        private readonly string _configFile;
        
        private Timer _monitorTimer;
        private VolumeMonitorConfig _config;
        private decimal? _lastVolume;
        private bool _isRunning = false;

        public VolumeMonitorService(
            WebScrapingService webScrapingService = null,
            PushNotificationService pushNotificationService = null,
            ILogger<VolumeMonitorService> logger = null)
        {
            _webScrapingService = webScrapingService ?? new WebScrapingService();
            _pushNotificationService = pushNotificationService ?? new PushNotificationService();
            _logger = logger;
            _configFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "volume_monitor_config.json");
            
            LoadConfig();
        }

        /// <summary>
        /// 成交量监控配置
        /// </summary>
        public class VolumeMonitorConfig
        {
            /// <summary>
            /// 是否启用成交量监控
            /// </summary>
            public bool IsEnabled { get; set; } = false;

            /// <summary>
            /// 低成交量预警阈值（美元）
            /// </summary>
            public decimal LowVolumeThreshold { get; set; } = 50_000_000_000; // 500亿

            /// <summary>
            /// 高成交量预警阈值（美元）
            /// </summary>
            public decimal HighVolumeThreshold { get; set; } = 100_000_000_000; // 1000亿

            /// <summary>
            /// 监控间隔（分钟）
            /// </summary>
            public int MonitorIntervalMinutes { get; set; } = 10;

            /// <summary>
            /// 是否启用低成交量预警
            /// </summary>
            public bool EnableLowVolumeAlert { get; set; } = true;

            /// <summary>
            /// 是否启用高成交量预警
            /// </summary>
            public bool EnableHighVolumeAlert { get; set; } = true;

            /// <summary>
            /// 最后预警时间
            /// </summary>
            public DateTime LastAlertTime { get; set; } = DateTime.MinValue;

            /// <summary>
            /// 预警冷却时间（分钟）- 防止频繁预警
            /// </summary>
            public int AlertCooldownMinutes { get; set; } = 60; // 1小时冷却
        }

        /// <summary>
        /// 加载配置
        /// </summary>
        private void LoadConfig()
        {
            try
            {
                if (File.Exists(_configFile))
                {
                    var json = File.ReadAllText(_configFile);
                    _config = JsonConvert.DeserializeObject<VolumeMonitorConfig>(json) ?? new VolumeMonitorConfig();
                }
                else
                {
                    _config = new VolumeMonitorConfig();
                    SaveConfig();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "加载成交量监控配置失败");
                LogManager.LogException("VolumeMonitorService", ex, "加载配置失败");
                _config = new VolumeMonitorConfig();
            }
        }

        /// <summary>
        /// 保存配置
        /// </summary>
        private void SaveConfig()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_config, Formatting.Indented);
                File.WriteAllText(_configFile, json);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "保存成交量监控配置失败");
                LogManager.LogException("VolumeMonitorService", ex, "保存配置失败");
            }
        }

        /// <summary>
        /// 获取当前配置
        /// </summary>
        public VolumeMonitorConfig GetConfig()
        {
            return _config;
        }

        /// <summary>
        /// 更新配置
        /// </summary>
        public void UpdateConfig(VolumeMonitorConfig config)
        {
            // 验证配置
            if (config.HighVolumeThreshold <= config.LowVolumeThreshold)
            {
                throw new ArgumentException("高成交量阈值必须大于低成交量阈值");
            }

            if (config.MonitorIntervalMinutes < 1)
            {
                throw new ArgumentException("监控间隔不能小于1分钟");
            }

            _config = config;
            SaveConfig();

            // 如果服务正在运行且配置已更改，重启定时器
            if (_isRunning)
            {
                StopMonitoring();
                StartMonitoring();
            }

            LogManager.Log("VolumeMonitorService", $"配置已更新: 启用={_config.IsEnabled}, 低阈值=${_config.LowVolumeThreshold:N0}, 高阈值=${_config.HighVolumeThreshold:N0}, 间隔={_config.MonitorIntervalMinutes}分钟");
        }

        /// <summary>
        /// 开始监控
        /// </summary>
        public void StartMonitoring()
        {
            if (_isRunning)
            {
                LogManager.Log("VolumeMonitorService", "成交量监控服务已在运行");
                return;
            }

            if (!_config.IsEnabled)
            {
                LogManager.Log("VolumeMonitorService", "成交量监控功能未启用");
                return;
            }

            try
            {
                var interval = TimeSpan.FromMinutes(_config.MonitorIntervalMinutes);
                _monitorTimer = new Timer(async _ => await CheckVolumeAsync(), null, TimeSpan.Zero, interval);
                _isRunning = true;

                LogManager.Log("VolumeMonitorService", $"成交量监控服务已启动，监控间隔: {_config.MonitorIntervalMinutes}分钟");
                _logger?.LogInformation($"成交量监控服务已启动，监控间隔: {_config.MonitorIntervalMinutes}分钟");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "启动成交量监控服务失败");
                LogManager.LogException("VolumeMonitorService", ex, "启动监控服务失败");
            }
        }

        /// <summary>
        /// 停止监控
        /// </summary>
        public void StopMonitoring()
        {
            if (!_isRunning)
                return;

            try
            {
                _monitorTimer?.Dispose();
                _monitorTimer = null;
                _isRunning = false;

                LogManager.Log("VolumeMonitorService", "成交量监控服务已停止");
                _logger?.LogInformation("成交量监控服务已停止");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "停止成交量监控服务失败");
                LogManager.LogException("VolumeMonitorService", ex, "停止监控服务失败");
            }
        }

        /// <summary>
        /// 检查成交量并发送预警
        /// </summary>
        private async Task CheckVolumeAsync()
        {
            try
            {
                LogManager.Log("VolumeMonitorService", "开始检查24小时成交量数据");

                var currentVolume = await _webScrapingService.GetCoinStats24hVolumeAsync();
                if (!currentVolume.HasValue)
                {
                    LogManager.Log("VolumeMonitorService", "获取成交量数据失败，跳过本次检查");
                    return;
                }

                _lastVolume = currentVolume.Value;
                LogManager.Log("VolumeMonitorService", $"当前24小时成交量: ${currentVolume.Value:N0}");

                // 检查是否需要发送预警
                await CheckAndSendAlertAsync(currentVolume.Value);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "检查成交量时发生错误");
                LogManager.LogException("VolumeMonitorService", ex, "检查成交量时发生错误");
            }
        }

        /// <summary>
        /// 检查并发送预警
        /// </summary>
        private async Task CheckAndSendAlertAsync(decimal volume)
        {
            try
            {
                // 检查预警冷却时间
                var timeSinceLastAlert = DateTime.Now - _config.LastAlertTime;
                if (timeSinceLastAlert.TotalMinutes < _config.AlertCooldownMinutes)
                {
                    LogManager.Log("VolumeMonitorService", $"预警冷却中，距离下次可预警还需 {_config.AlertCooldownMinutes - timeSinceLastAlert.TotalMinutes:F1} 分钟");
                    return;
                }

                string alertTitle = null;
                string alertMessage = null;

                // 检查低成交量预警
                if (_config.EnableLowVolumeAlert && volume < _config.LowVolumeThreshold)
                {
                    alertTitle = "成交量跌破预警";
                    alertMessage = $"24小时成交量跌破设定阈值\n\n" +
                                  $"当前成交量: ${volume:N0}\n" +
                                  $"预警阈值: ${_config.LowVolumeThreshold:N0}\n" +
                                  $"检查时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                }
                // 检查高成交量预警
                else if (_config.EnableHighVolumeAlert && volume > _config.HighVolumeThreshold)
                {
                    alertTitle = "成交量突破预警";
                    alertMessage = $"24小时成交量突破设定阈值\n\n" +
                                  $"当前成交量: ${volume:N0}\n" +
                                  $"预警阈值: ${_config.HighVolumeThreshold:N0}\n" +
                                  $"检查时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                }

                // 发送预警
                if (!string.IsNullOrEmpty(alertTitle) && !string.IsNullOrEmpty(alertMessage))
                {
                    LogManager.Log("VolumeMonitorService", $"触发成交量预警: {alertTitle}");
                    
                    // 发送成交量预警推送
                    var success = await SendVolumeAlertAsync(alertTitle, alertMessage);
                    
                    if (success)
                    {
                        _config.LastAlertTime = DateTime.Now;
                        SaveConfig();
                        LogManager.Log("VolumeMonitorService", "成交量预警推送成功");
                    }
                    else
                    {
                        LogManager.Log("VolumeMonitorService", "成交量预警推送失败");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "发送成交量预警时发生错误");
                LogManager.LogException("VolumeMonitorService", ex, "发送成交量预警时发生错误");
            }
        }

        /// <summary>
        /// 发送成交量预警推送
        /// </summary>
        private async Task<bool> SendVolumeAlertAsync(string title, string message)
        {
            try
            {
                // 检查推送服务是否可用
                if (!_pushNotificationService.CanPush())
                {
                    LogManager.Log("VolumeMonitorService", "推送服务不可用，跳过成交量预警推送");
                    return false;
                }

                // 使用自定义消息推送方法发送成交量预警
                var success = await _pushNotificationService.PushCustomMessageAsync(title, message);
                
                if (success)
                {
                    LogManager.Log("VolumeMonitorService", $"成交量预警推送成功: {title}");
                }
                else
                {
                    LogManager.Log("VolumeMonitorService", $"成交量预警推送失败: {title}");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "发送成交量预警推送时发生错误");
                LogManager.LogException("VolumeMonitorService", ex, "发送成交量预警推送时发生错误");
                return false;
            }
        }

        /// <summary>
        /// 获取最后的成交量数据
        /// </summary>
        public decimal? GetLastVolume()
        {
            return _lastVolume;
        }

        /// <summary>
        /// 获取服务运行状态
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// 手动执行一次检查（用于测试）
        /// </summary>
        public async Task<decimal?> ManualCheckAsync()
        {
            try
            {
                LogManager.Log("VolumeMonitorService", "执行手动成交量检查");
                var volume = await _webScrapingService.GetCoinStats24hVolumeAsync();
                if (volume.HasValue)
                {
                    _lastVolume = volume.Value;
                    LogManager.Log("VolumeMonitorService", $"手动检查结果 - 24小时成交量: ${volume.Value:N0}");
                }
                return volume;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "手动检查成交量时发生错误");
                LogManager.LogException("VolumeMonitorService", ex, "手动检查成交量时发生错误");
                return null;
            }
        }

        /// <summary>
        /// 测试成交量预警推送功能
        /// </summary>
        public async Task<bool> TestVolumeAlertAsync()
        {
            try
            {
                LogManager.Log("VolumeMonitorService", "开始测试成交量预警推送功能");

                // 检查推送服务状态
                var canPush = _pushNotificationService.CanPush();
                LogManager.Log("VolumeMonitorService", $"推送服务可用性: {canPush}");

                if (!canPush)
                {
                    var config = _pushNotificationService.GetConfig();
                    LogManager.Log("VolumeMonitorService", $"推送服务配置: 启用={config.IsEnabled}, Token数量={config.XtuisTokens.Count}, 今日推送={config.TodayPushCount}/{config.DailyPushLimit}");
                    return false;
                }

                // 获取当前成交量
                var currentVolume = await _webScrapingService.GetCoinStats24hVolumeAsync();
                if (!currentVolume.HasValue)
                {
                    LogManager.Log("VolumeMonitorService", "无法获取当前成交量，测试失败");
                    return false;
                }

                // 发送测试预警消息
                var testTitle = "成交量监控测试";
                var testMessage = $"这是一条测试消息\n\n" +
                                 $"当前24小时成交量: ${currentVolume.Value:N0}\n" +
                                 $"换算为亿美元: ${currentVolume.Value / 1_000_000_000:F2}B\n" +
                                 $"测试时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

                LogManager.Log("VolumeMonitorService", "发送测试预警消息...");
                var success = await SendVolumeAlertAsync(testTitle, testMessage);

                if (success)
                {
                    LogManager.Log("VolumeMonitorService", "✅ 成交量预警推送测试成功");
                }
                else
                {
                    LogManager.Log("VolumeMonitorService", "❌ 成交量预警推送测试失败");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "测试成交量预警推送时发生错误");
                LogManager.LogException("VolumeMonitorService", ex, "测试成交量预警推送时发生错误");
                return false;
            }
        }

        public void Dispose()
        {
            StopMonitoring();
            _webScrapingService?.Dispose();
            _pushNotificationService?.Dispose();
        }
    }
} 