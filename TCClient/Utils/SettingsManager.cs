using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace TCClient.Utils
{
    /// <summary>
    /// 设置管理器，用于保存和加载应用程序配置
    /// </summary>
    public static class SettingsManager
    {
        private static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
            "TCClient", "Settings");
        
        private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");
        private static readonly string RecentSymbolsFilePath = Path.Combine(SettingsDirectory, "recent_symbols.json");

        static SettingsManager()
        {
            // 确保设置目录存在
            if (!Directory.Exists(SettingsDirectory))
            {
                Directory.CreateDirectory(SettingsDirectory);
            }
        }

        /// <summary>
        /// 应用程序设置
        /// </summary>
        public class AppSettings
        {
            public FindOpportunitySettings FindOpportunity { get; set; } = new FindOpportunitySettings();
            public KLineSettings KLine { get; set; } = new KLineSettings();
        }

        /// <summary>
        /// 寻找机会窗口设置
        /// </summary>
        public class FindOpportunitySettings
        {
            public int VolumeDays { get; set; } = 7;
            public double VolumeMultiplier { get; set; } = 2.0;
            public int UpdateIntervalSeconds { get; set; } = 30;
        }

        /// <summary>
        /// K线图设置
        /// </summary>
        public class KLineSettings
        {
            public int MAPeriod { get; set; } = 20;
            public bool ShowVolume { get; set; } = true;
            public bool ShowMA { get; set; } = true;
        }

        /// <summary>
        /// 最近访问的合约项目
        /// </summary>
        public class RecentSymbolItem
        {
            public string Symbol { get; set; }
            public decimal CurrentPrice { get; set; }
            public decimal ChangePercent { get; set; }
            public bool IsPositive { get; set; }
            public DateTime LastViewTime { get; set; }
        }

        /// <summary>
        /// 保存应用程序设置
        /// </summary>
        public static async Task SaveSettingsAsync(AppSettings settings)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                await File.WriteAllTextAsync(SettingsFilePath, json);
                LogManager.Log("SettingsManager", $"设置已保存到: {SettingsFilePath}");
            }
            catch (Exception ex)
            {
                LogManager.Log("SettingsManager", $"保存设置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 加载应用程序设置
        /// </summary>
        public static async Task<AppSettings> LoadSettingsAsync()
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                {
                    LogManager.Log("SettingsManager", "设置文件不存在，使用默认设置");
                    return new AppSettings();
                }

                var json = await File.ReadAllTextAsync(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                LogManager.Log("SettingsManager", $"设置已从文件加载: {SettingsFilePath}");
                return settings ?? new AppSettings();
            }
            catch (Exception ex)
            {
                LogManager.Log("SettingsManager", $"加载设置失败: {ex.Message}，使用默认设置");
                return new AppSettings();
            }
        }

        /// <summary>
        /// 保存最近访问的合约列表
        /// </summary>
        public static async Task SaveRecentSymbolsAsync(List<RecentSymbolItem> recentSymbols)
        {
            try
            {
                var json = JsonSerializer.Serialize(recentSymbols, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                await File.WriteAllTextAsync(RecentSymbolsFilePath, json);
                LogManager.Log("SettingsManager", $"最近访问合约已保存到: {RecentSymbolsFilePath}，共 {recentSymbols.Count} 个");
            }
            catch (Exception ex)
            {
                LogManager.Log("SettingsManager", $"保存最近访问合约失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 加载最近访问的合约列表
        /// </summary>
        public static async Task<List<RecentSymbolItem>> LoadRecentSymbolsAsync()
        {
            try
            {
                if (!File.Exists(RecentSymbolsFilePath))
                {
                    LogManager.Log("SettingsManager", "最近访问合约文件不存在，返回空列表");
                    return new List<RecentSymbolItem>();
                }

                var json = await File.ReadAllTextAsync(RecentSymbolsFilePath);
                var recentSymbols = JsonSerializer.Deserialize<List<RecentSymbolItem>>(json);
                LogManager.Log("SettingsManager", $"最近访问合约已从文件加载: {RecentSymbolsFilePath}，共 {recentSymbols?.Count ?? 0} 个");
                return recentSymbols ?? new List<RecentSymbolItem>();
            }
            catch (Exception ex)
            {
                LogManager.Log("SettingsManager", $"加载最近访问合约失败: {ex.Message}，返回空列表");
                return new List<RecentSymbolItem>();
            }
        }

        /// <summary>
        /// 清空所有设置文件
        /// </summary>
        public static void ClearAllSettings()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    File.Delete(SettingsFilePath);
                    LogManager.Log("SettingsManager", "设置文件已删除");
                }

                if (File.Exists(RecentSymbolsFilePath))
                {
                    File.Delete(RecentSymbolsFilePath);
                    LogManager.Log("SettingsManager", "最近访问合约文件已删除");
                }
            }
            catch (Exception ex)
            {
                LogManager.Log("SettingsManager", $"清空设置文件失败: {ex.Message}");
            }
        }
    }
} 