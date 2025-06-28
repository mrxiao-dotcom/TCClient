using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TCClient.Utils;

namespace TCClient.Services
{
    /// <summary>
    /// 网页抓取服务 - 用于抓取各种网站的数据
    /// </summary>
    public class WebScrapingService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<WebScrapingService> _logger;

        public WebScrapingService(ILogger<WebScrapingService> logger = null)
        {
            _httpClient = new HttpClient();
            _logger = logger;
            
            // 设置用户代理，模拟浏览器访问
            _httpClient.DefaultRequestHeaders.Add("User-Agent", 
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            
            // 设置超时时间
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        /// <summary>
        /// 从 CoinStats 网站获取24小时成交量数据
        /// </summary>
        /// <returns>24小时成交量数据（单位：美元），如果获取失败返回null</returns>
        public async Task<decimal?> GetCoinStats24hVolumeAsync()
        {
            try
            {
                const string url = "https://coinstats.app/zh/exchanges/";
                
                _logger?.LogInformation($"开始抓取CoinStats 24小时成交量数据，URL: {url}");
                
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                
                var htmlContent = await response.Content.ReadAsStringAsync();
                
                // 查找24小时成交量数据
                // 在网页中查找类似：24小时成交量$82,688,403,855 这样的文本
                var volumePattern = @"24小时成交量[\s\S]*?\$([0-9,]+(?:\.[0-9]+)?[KMBTQ]?)";
                var match = Regex.Match(htmlContent, volumePattern, RegexOptions.IgnoreCase);
                
                if (!match.Success)
                {
                    // 尝试另一种模式
                    volumePattern = @"成交量[\s\S]*?\$([0-9,]+(?:\.[0-9]+)?[KMBTQ]?)";
                    match = Regex.Match(htmlContent, volumePattern, RegexOptions.IgnoreCase);
                }
                
                if (match.Success)
                {
                    var volumeStr = match.Groups[1].Value;
                    var volume = ParseVolumeString(volumeStr);
                    
                    if (volume.HasValue)
                    {
                        _logger?.LogInformation($"成功获取24小时成交量: ${volume.Value:N0}");
                        LogManager.Log("WebScrapingService", $"CoinStats 24小时成交量: ${volume.Value:N0}");
                        return volume;
                    }
                }
                
                _logger?.LogWarning("无法从网页中解析出24小时成交量数据");
                LogManager.Log("WebScrapingService", "无法解析CoinStats 24小时成交量数据");
                return null;
            }
            catch (HttpRequestException ex)
            {
                _logger?.LogError(ex, "网络请求失败");
                LogManager.LogException("WebScrapingService", ex, "获取CoinStats数据时网络请求失败");
                return null;
            }
            catch (TaskCanceledException ex)
            {
                _logger?.LogError(ex, "请求超时");
                LogManager.LogException("WebScrapingService", ex, "获取CoinStats数据超时");
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "获取CoinStats 24小时成交量数据失败");
                LogManager.LogException("WebScrapingService", ex, "获取CoinStats 24小时成交量数据失败");
                return null;
            }
        }

        /// <summary>
        /// 解析成交量字符串，支持K、M、B、T、Q等单位
        /// </summary>
        /// <param name="volumeStr">成交量字符串，如 "82.69B" 或 "1,234,567"</param>
        /// <returns>成交量数值（美元）</returns>
        private decimal? ParseVolumeString(string volumeStr)
        {
            if (string.IsNullOrWhiteSpace(volumeStr))
                return null;

            try
            {
                // 去除逗号
                volumeStr = volumeStr.Replace(",", "");
                
                // 检查最后一个字符是否为单位
                var lastChar = volumeStr[volumeStr.Length - 1];
                decimal multiplier = 1;
                string numberPart = volumeStr;
                
                switch (char.ToUpper(lastChar))
                {
                    case 'K':
                        multiplier = 1_000;
                        numberPart = volumeStr.Substring(0, volumeStr.Length - 1);
                        break;
                    case 'M':
                        multiplier = 1_000_000;
                        numberPart = volumeStr.Substring(0, volumeStr.Length - 1);
                        break;
                    case 'B':
                        multiplier = 1_000_000_000;
                        numberPart = volumeStr.Substring(0, volumeStr.Length - 1);
                        break;
                    case 'T':
                        multiplier = 1_000_000_000_000;
                        numberPart = volumeStr.Substring(0, volumeStr.Length - 1);
                        break;
                    case 'Q':
                        multiplier = 1_000_000_000_000_000;
                        numberPart = volumeStr.Substring(0, volumeStr.Length - 1);
                        break;
                }
                
                if (decimal.TryParse(numberPart, out decimal number))
                {
                    return number * multiplier;
                }
                
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"解析成交量字符串失败: {volumeStr}");
                return null;
            }
        }

        /// <summary>
        /// 测试网络连接
        /// </summary>
        public async Task<bool> TestNetworkConnectionAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("https://coinstats.app/");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
} 