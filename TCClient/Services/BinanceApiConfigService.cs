using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace TCClient.Services
{
    public class BinanceApiConfigService
    {
        private const string ConfigFileName = "binance_api_config.json";
        private static readonly string ConfigFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);

        public class BinanceApiConfig
        {
            public string ApiKey { get; set; } = string.Empty;
            public string SecretKey { get; set; } = string.Empty;
            public bool IsEnabled { get; set; } = false;
            public DateTime LastUpdated { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// 获取币安API配置
        /// </summary>
        /// <returns>API配置</returns>
        public static async Task<BinanceApiConfig> GetConfigAsync()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    var json = await File.ReadAllTextAsync(ConfigFilePath);
                    var config = JsonSerializer.Deserialize<BinanceApiConfig>(json);
                    return config ?? new BinanceApiConfig();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"读取币安API配置失败: {ex.Message}");
            }

            return new BinanceApiConfig();
        }

        /// <summary>
        /// 保存币安API配置
        /// </summary>
        /// <param name="config">API配置</param>
        /// <returns>是否保存成功</returns>
        public static async Task<bool> SaveConfigAsync(BinanceApiConfig config)
        {
            try
            {
                config.LastUpdated = DateTime.Now;
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                
                await File.WriteAllTextAsync(ConfigFilePath, json);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存币安API配置失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 验证API配置是否有效
        /// </summary>
        /// <param name="config">API配置</param>
        /// <returns>是否有效</returns>
        public static bool IsValidConfig(BinanceApiConfig config)
        {
            return !string.IsNullOrWhiteSpace(config.ApiKey) && 
                   !string.IsNullOrWhiteSpace(config.SecretKey) && 
                   config.IsEnabled;
        }

        /// <summary>
        /// 测试API连接
        /// </summary>
        /// <param name="apiKey">API密钥</param>
        /// <param name="secretKey">密钥</param>
        /// <returns>连接测试结果</returns>
        public static async Task<(bool Success, string Message)> TestConnectionAsync(string apiKey, string secretKey)
        {
            try
            {
                using var binanceApi = new BinanceApiService(apiKey, secretKey);
                var isConnected = await binanceApi.TestConnectivityAsync();
                
                if (isConnected)
                {
                    return (true, "API连接测试成功");
                }
                else
                {
                    return (false, "API连接测试失败，请检查网络连接");
                }
            }
            catch (Exception ex)
            {
                return (false, $"API连接测试失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 创建默认配置文件
        /// </summary>
        /// <returns>是否创建成功</returns>
        public static async Task<bool> CreateDefaultConfigAsync()
        {
            var defaultConfig = new BinanceApiConfig
            {
                ApiKey = "",
                SecretKey = "",
                IsEnabled = false
            };

            return await SaveConfigAsync(defaultConfig);
        }

        /// <summary>
        /// 检查配置文件是否存在
        /// </summary>
        /// <returns>是否存在</returns>
        public static bool ConfigExists()
        {
            return File.Exists(ConfigFilePath);
        }

        /// <summary>
        /// 删除配置文件
        /// </summary>
        /// <returns>是否删除成功</returns>
        public static bool DeleteConfig()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    File.Delete(ConfigFilePath);
                    return true;
                }
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"删除币安API配置失败: {ex.Message}");
                return false;
            }
        }
    }
} 