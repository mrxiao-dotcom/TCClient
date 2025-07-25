using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TCClient.Models;
using TCClient.Utils;

namespace TCClient.Services
{
    /// <summary>
    /// 推送通知服务 - 虾推啥
    /// </summary>
    public class PushNotificationService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<PushNotificationService> _logger;
        private readonly string _pushConfigFile;
        private PushConfig _config;

        public PushNotificationService(ILogger<PushNotificationService> logger = null)
        {
            _httpClient = new HttpClient();
            _logger = logger;
            _pushConfigFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "push_config.json");
            LoadConfig();
        }

        /// <summary>
        /// 推送配置
        /// </summary>
        public class PushConfig
        {
            /// <summary>
            /// 虾推啥Token列表
            /// </summary>
            public List<string> XtuisTokens { get; set; } = new List<string>();

            /// <summary>
            /// 每日推送次数限制 (虾推啥限制: 每日最多300条)
            /// </summary>
            public int DailyPushLimit { get; set; } = 20; // 调整为更保守的20条

            /// <summary>
            /// 今日已推送次数
            /// </summary>
            public int TodayPushCount { get; set; } = 0;

            /// <summary>
            /// 最后推送日期
            /// </summary>
            public DateTime LastPushDate { get; set; } = DateTime.MinValue;

            /// <summary>
            /// 是否启用推送
            /// </summary>
            public bool IsEnabled { get; set; } = false;

            /// <summary>
            /// 推送时间间隔（分钟）- 虾推啥限制: 每分钟最多10条
            /// </summary>
            public int PushIntervalMinutes { get; set; } = 360; // 调整为6小时，更保守

            /// <summary>
            /// 最后推送时间 - 用于控制推送频率
            /// </summary>
            public DateTime LastPushTime { get; set; } = DateTime.MinValue;

            /// <summary>
            /// 分钟内推送计数 - 防止超过每分钟10条限制
            /// </summary>
            public int MinutePushCount { get; set; } = 0;

            /// <summary>
            /// 分钟计数开始时间
            /// </summary>
            public DateTime MinuteCountStartTime { get; set; } = DateTime.MinValue;
        }

        /// <summary>
        /// 加载配置
        /// </summary>
        private void LoadConfig()
        {
            try
            {
                if (System.IO.File.Exists(_pushConfigFile))
                {
                    var json = System.IO.File.ReadAllText(_pushConfigFile);
                    _config = JsonConvert.DeserializeObject<PushConfig>(json) ?? new PushConfig();
                }
                else
                {
                    _config = new PushConfig();
                }

                // 如果是新的一天，重置推送计数
                if (_config.LastPushDate.Date != DateTime.Today)
                {
                    _config.TodayPushCount = 0;
                    _config.LastPushDate = DateTime.Today;
                    SaveConfig();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "加载推送配置失败");
                _config = new PushConfig();
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
                System.IO.File.WriteAllText(_pushConfigFile, json);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "保存推送配置失败");
            }
        }

        /// <summary>
        /// 获取当前配置
        /// </summary>
        public PushConfig GetConfig()
        {
            return _config;
        }

        /// <summary>
        /// 更新配置
        /// </summary>
        public void UpdateConfig(PushConfig config)
        {
            _config = config;
            SaveConfig();
        }

        /// <summary>
        /// 检查是否可以推送
        /// </summary>
        public bool CanPush()
        {
            if (!_config.IsEnabled)
            {
                _logger?.LogInformation("推送功能未启用");
                return false;
            }

            if (_config.XtuisTokens.Count == 0)
            {
                _logger?.LogInformation("未配置虾推啥Token");
                return false;
            }

            // 检查每日推送限制 (虾推啥限制: 每日最多300条)
            if (_config.TodayPushCount >= _config.DailyPushLimit)
            {
                _logger?.LogInformation($"已达到每日推送限制: {_config.TodayPushCount}/{_config.DailyPushLimit}");
                return false;
            }

            // 检查推送时间间隔
            var timeSinceLastPush = DateTime.Now - _config.LastPushTime;
            if (timeSinceLastPush.TotalMinutes < _config.PushIntervalMinutes)
            {
                var remainingMinutes = _config.PushIntervalMinutes - timeSinceLastPush.TotalMinutes;
                _logger?.LogInformation($"推送间隔未到，还需等待 {remainingMinutes:F1} 分钟");
                return false;
            }

            // 检查每分钟推送限制 (虾推啥限制: 每分钟最多10条)
            var now = DateTime.Now;
            if (_config.MinuteCountStartTime != DateTime.MinValue && 
                (now - _config.MinuteCountStartTime).TotalMinutes < 1)
            {
                if (_config.MinutePushCount >= 8) // 保守设置为8条，留点余量
                {
                    _logger?.LogInformation($"每分钟推送限制: {_config.MinutePushCount}/8条");
                    return false;
                }
            }
            else
            {
                // 重置分钟计数
                _config.MinuteCountStartTime = now;
                _config.MinutePushCount = 0;
            }

            return true;
        }

        /// <summary>
        /// 测试网络连接
        /// </summary>
        public async Task<bool> TestNetworkConnectionAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("https://xtuis.cn/", new System.Threading.CancellationToken());
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 推送自定义消息（用于成交量预警等特殊场景）
        /// </summary>
        /// <param name="title">消息标题</param>
        /// <param name="content">消息内容</param>
        /// <returns>推送是否成功</returns>
        public async Task<bool> PushCustomMessageAsync(string title, string content)
        {
            if (!CanPush())
            {
                _logger?.LogInformation("推送条件不满足，跳过自定义消息推送");
                return false;
            }

            try
            {
                var successCount = 0;
                var totalCount = _config.XtuisTokens.Count;

                foreach (var token in _config.XtuisTokens)
                {
                    try
                    {
                        // 尝试官方GET方法
                        var success = await TryOfficialGetMethod(token, title, content);
                        if (!success)
                        {
                            // 备用：尝试官方POST方法
                            success = await TryOfficialPostMethod(token, title, content);
                        }

                        if (success)
                        {
                            successCount++;
                            _logger?.LogInformation($"自定义消息推送成功到token: {token}");
                        }
                        else
                        {
                            _logger?.LogWarning($"自定义消息推送失败到token: {token}");
                        }
                        
                        // 避免频繁请求，每个token之间间隔1秒
                        await Task.Delay(1000);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, $"推送自定义消息到token {token} 失败");
                    }
                }

                var overallSuccess = successCount > 0;
                if (overallSuccess)
                {
                    // 更新推送计数和时间
                    _config.TodayPushCount++;
                    _config.LastPushDate = DateTime.Today;
                    _config.LastPushTime = DateTime.Now;
                    _config.MinutePushCount++;
                    
                    SaveConfig();
                    _logger?.LogInformation($"自定义消息推送完成: {successCount}/{totalCount} 个token推送成功，今日已推送 {_config.TodayPushCount}/{_config.DailyPushLimit} 次");
                }
                else
                {
                    _logger?.LogWarning("自定义消息推送失败，未更新计数");
                }

                return overallSuccess;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "推送自定义消息失败");
                return false;
            }
        }

        /// <summary>
        /// 推送市场分析消息
        /// </summary>
        public async Task<bool> PushMarketAnalysisAsync(MarketAnalysisResult analysisResult)
        {
            if (!CanPush())
            {
                _logger?.LogInformation("推送条件不满足，跳过推送");
                return false;
            }

            try
            {
                var message = await FormatMarketAnalysisMessageAsync(analysisResult);
                var success = await SendPushMessageAsync(message);
                
                if (success)
                {
                    // 更新推送计数和时间
                    _config.TodayPushCount++;
                    _config.LastPushDate = DateTime.Today;
                    _config.LastPushTime = DateTime.Now;
                    _config.MinutePushCount++;
                    
                    SaveConfig();
                    _logger?.LogInformation($"推送成功，今日已推送 {_config.TodayPushCount}/{_config.DailyPushLimit} 次，本分钟已推送 {_config.MinutePushCount} 次");
                }
                else
                {
                    _logger?.LogWarning("推送失败，未更新计数");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "推送市场分析消息失败");
                return false;
            }
        }

        /// <summary>
        /// 格式化市场分析消息
        /// </summary>
        private async Task<string> FormatMarketAnalysisMessageAsync(MarketAnalysisResult analysisResult)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"📊 市场分析报告 - {DateTime.Now:yyyy-MM-dd HH:mm}");
            
            // 添加24小时成交量信息
            try
            {
                var webScrapingService = new WebScrapingService();
                var volume24h = await webScrapingService.GetCoinStats24hVolumeAsync();
                if (volume24h.HasValue)
                {
                    sb.AppendLine($"🔢 目前24h成交量：{volume24h.Value / 1_000_000_000:F1}亿");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "获取24小时成交量失败");
            }
            
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━");
            
            // 涨幅分析
            sb.AppendLine("📈 涨幅分析:");
            if (analysisResult.RisingAnalysis.BothPeriods.Count > 0)
            {
                sb.AppendLine($"🔥 当天和30天共涨 ({analysisResult.RisingAnalysis.BothPeriods.Count}个):");
                foreach (var item in analysisResult.RisingAnalysis.BothPeriods.Take(5))
                {
                    sb.AppendLine($"  {item.Symbol}: 当天+{item.TodayChange:F2}%, 30天+{item.ThirtyDayChange:F2}%");
                }
            }

            if (analysisResult.RisingAnalysis.OnlyToday.Count > 0)
            {
                sb.AppendLine($"⚡ 仅当天上涨 ({analysisResult.RisingAnalysis.OnlyToday.Count}个):");
                foreach (var item in analysisResult.RisingAnalysis.OnlyToday.Take(3))
                {
                    sb.AppendLine($"  {item.Symbol}: +{item.TodayChange:F2}%");
                }
            }

            if (analysisResult.RisingAnalysis.OnlyThirtyDays.Count > 0)
            {
                sb.AppendLine($"📊 仅30天上涨 ({analysisResult.RisingAnalysis.OnlyThirtyDays.Count}个):");
                foreach (var item in analysisResult.RisingAnalysis.OnlyThirtyDays.Take(3))
                {
                    sb.AppendLine($"  {item.Symbol}: +{item.ThirtyDayChange:F2}%");
                }
            }

            sb.AppendLine();
            
            // 跌幅分析
            sb.AppendLine("📉 跌幅分析:");
            if (analysisResult.FallingAnalysis.BothPeriods.Count > 0)
            {
                sb.AppendLine($"❄️ 当天和30天共跌 ({analysisResult.FallingAnalysis.BothPeriods.Count}个):");
                foreach (var item in analysisResult.FallingAnalysis.BothPeriods.Take(5))
                {
                    sb.AppendLine($"  {item.Symbol}: 当天{item.TodayChange:F2}%, 30天{item.ThirtyDayChange:F2}%");
                }
            }

            if (analysisResult.FallingAnalysis.OnlyToday.Count > 0)
            {
                sb.AppendLine($"⚡ 仅当天下跌 ({analysisResult.FallingAnalysis.OnlyToday.Count}个):");
                foreach (var item in analysisResult.FallingAnalysis.OnlyToday.Take(3))
                {
                    sb.AppendLine($"  {item.Symbol}: {item.TodayChange:F2}%");
                }
            }

            if (analysisResult.FallingAnalysis.OnlyThirtyDays.Count > 0)
            {
                sb.AppendLine($"📊 仅30天下跌 ({analysisResult.FallingAnalysis.OnlyThirtyDays.Count}个):");
                foreach (var item in analysisResult.FallingAnalysis.OnlyThirtyDays.Take(3))
                {
                    sb.AppendLine($"  {item.Symbol}: {item.ThirtyDayChange:F2}%");
                }
            }

            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"📊 总计: {analysisResult.TotalSymbolsAnalyzed} 个合约");

            return sb.ToString();
        }

        /// <summary>
        /// 发送推送消息
        /// </summary>
        private async Task<bool> SendPushMessageAsync(string message)
        {
            var successCount = 0;
            var totalCount = _config.XtuisTokens.Count;

            foreach (var token in _config.XtuisTokens)
            {
                try
                {
                    var success = await SendToXtuisAsync(token, message);
                    if (success)
                    {
                        successCount++;
                    }
                    
                    // 避免频繁请求，每个token之间间隔1秒
                    await Task.Delay(1000);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, $"推送到token {token} 失败");
                }
            }

            _logger?.LogInformation($"推送完成: {successCount}/{totalCount} 个token推送成功");
            return successCount > 0;
        }

        /// <summary>
        /// 推送到虾推啥 - 使用已验证的官方API格式
        /// </summary>
        private async Task<bool> SendToXtuisAsync(string token, string message)
        {
            try
            {
                // 根据测试结果，只使用有效的官方格式
                var title = "📊 市场分析报告";
                var content = message;
                
                // 方式1: 官方GET格式 (已验证有效)
                var success = await TryOfficialGetMethod(token, title, content);
                if (success) 
                {
                    _logger?.LogInformation($"官方GET格式推送成功到token: {token}");
                    return true;
                }

                // 方式2: 官方POST格式 (备用)
                success = await TryOfficialPostMethod(token, title, content);
                if (success)
                {
                    _logger?.LogInformation($"官方POST格式推送成功到token: {token}");
                    return true;
                }

                _logger?.LogWarning($"所有推送格式都失败了, token: {token}");
                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"推送到虾推啥失败, token: {token}");
                return false;
            }
        }

        /// <summary>
        /// 尝试官方GET方法 - 官方推荐格式
        /// </summary>
        private async Task<bool> TryOfficialGetMethod(string token, string title, string content)
        {
            try
            {
                // 官方格式: https://wx.xtuis.cn/TOKEN.send?text=标题&desp=内容
                var encodedTitle = Uri.EscapeDataString(title);
                var encodedContent = Uri.EscapeDataString(content);
                var url = $"https://wx.xtuis.cn/{token}.send?text={encodedTitle}&desp={encodedContent}";
                
                _logger?.LogDebug($"尝试官方GET方法: https://wx.xtuis.cn/{token}.send?text=...");
                var response = await _httpClient.GetAsync(url);
                var responseContent = await response.Content.ReadAsStringAsync();
                
                _logger?.LogDebug($"官方GET响应: 状态码={response.StatusCode}, 内容={responseContent}");
                
                if (response.IsSuccessStatusCode)
                {
                    _logger?.LogInformation($"官方GET推送成功到token: {token}");
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug($"官方GET方法失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 尝试官方POST方法 - 官方推荐格式
        /// </summary>
        private async Task<bool> TryOfficialPostMethod(string token, string title, string content)
        {
            try
            {
                // 官方格式: https://wx.xtuis.cn/TOKEN.send (POST data: text=标题, desp=内容)
                var url = $"https://wx.xtuis.cn/{token}.send";
                var formData = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("text", title),
                    new KeyValuePair<string, string>("desp", content)
                };
                var formContent = new FormUrlEncodedContent(formData);
                
                _logger?.LogDebug($"尝试官方POST方法: https://wx.xtuis.cn/{token}.send");
                var response = await _httpClient.PostAsync(url, formContent);
                var responseContent = await response.Content.ReadAsStringAsync();
                
                _logger?.LogDebug($"官方POST响应: 状态码={response.StatusCode}, 内容={responseContent}");
                
                if (response.IsSuccessStatusCode)
                {
                    _logger?.LogInformation($"官方POST推送成功到token: {token}");
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug($"官方POST方法失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 尝试备用GET方法 - 兼容旧格式
        /// </summary>
        private async Task<bool> TryBackupGetMethod(string token, string message)
        {
            try
            {
                // 备用格式: https://xtuis.cn/TOKEN?text=消息
                var encodedMessage = Uri.EscapeDataString(message);
                var url = $"https://xtuis.cn/{token}?text={encodedMessage}";
                
                _logger?.LogDebug($"尝试备用GET方法: https://xtuis.cn/{token}?text=...");
                var response = await _httpClient.GetAsync(url);
                var responseContent = await response.Content.ReadAsStringAsync();
                
                _logger?.LogDebug($"备用GET响应: 状态码={response.StatusCode}, 内容={responseContent}");
                
                if (response.IsSuccessStatusCode)
                {
                    _logger?.LogInformation($"备用GET推送成功到token: {token}");
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug($"备用GET方法失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    /// <summary>
    /// 市场分析结果
    /// </summary>
    public class MarketAnalysisResult
    {
        /// <summary>
        /// 涨幅分析
        /// </summary>
        public MarketChangeAnalysis RisingAnalysis { get; set; } = new MarketChangeAnalysis();

        /// <summary>
        /// 跌幅分析
        /// </summary>
        public MarketChangeAnalysis FallingAnalysis { get; set; } = new MarketChangeAnalysis();

        /// <summary>
        /// 分析的合约总数
        /// </summary>
        public int TotalSymbolsAnalyzed { get; set; }

        /// <summary>
        /// 分析时间
        /// </summary>
        public DateTime AnalysisTime { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 市场变化分析
    /// </summary>
    public class MarketChangeAnalysis
    {
        /// <summary>
        /// 当天和30天都有变化的合约
        /// </summary>
        public List<SymbolChangeInfo> BothPeriods { get; set; } = new List<SymbolChangeInfo>();

        /// <summary>
        /// 仅当天有变化的合约
        /// </summary>
        public List<SymbolChangeInfo> OnlyToday { get; set; } = new List<SymbolChangeInfo>();

        /// <summary>
        /// 仅30天有变化的合约
        /// </summary>
        public List<SymbolChangeInfo> OnlyThirtyDays { get; set; } = new List<SymbolChangeInfo>();
    }

    /// <summary>
    /// 合约变化信息
    /// </summary>
    public class SymbolChangeInfo
    {
        /// <summary>
        /// 合约名称
        /// </summary>
        public string Symbol { get; set; } = string.Empty;

        /// <summary>
        /// 当天变化幅度
        /// </summary>
        public decimal TodayChange { get; set; }

        /// <summary>
        /// 30天变化幅度
        /// </summary>
        public decimal ThirtyDayChange { get; set; }

        /// <summary>
        /// 24小时成交额
        /// </summary>
        public decimal Volume24h { get; set; }
    }
} 