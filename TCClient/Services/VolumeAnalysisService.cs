using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TCClient.Utils;

namespace TCClient.Services
{
    /// <summary>
    /// 成交额分析服务，提供平均成交额计算和缓存功能
    /// </summary>
    public class VolumeAnalysisService
    {
        private readonly IDatabaseService _databaseService;
        private static readonly string CacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
            "TCClient", "VolumeCache");
        
        private const string CACHE_FILE_FORMAT = "avg_volume_{0}days_{1:yyyyMMdd}.json";
        
        public VolumeAnalysisService(IDatabaseService databaseService)
        {
            _databaseService = databaseService;
            
            // 确保缓存目录存在
            if (!Directory.Exists(CacheDirectory))
            {
                Directory.CreateDirectory(CacheDirectory);
            }
        }

        /// <summary>
        /// 获取指定合约过去N天的平均成交额（带缓存）
        /// </summary>
        public async Task<decimal> GetAverageVolumeAsync(string symbol, int days, CancellationToken cancellationToken = default)
        {
            try
            {
                // 检查缓存
                var cachedResult = await LoadFromCacheAsync(symbol, days);
                if (cachedResult.HasValue)
                {
                    LogManager.Log("VolumeAnalysis", $"从缓存获取 {symbol} 过去 {days} 天平均成交额: {cachedResult.Value:F2}");
                    return cachedResult.Value;
                }

                // 缓存不存在或过期，从数据库获取
                LogManager.Log("VolumeAnalysis", $"缓存不存在，从数据库获取 {symbol} 过去 {days} 天平均成交额");
                var avgVolume = await _databaseService.GetAverageQuoteVolumeAsync(symbol, days, cancellationToken);

                // 保存到缓存
                if (avgVolume > 0)
                {
                    await SaveToCacheAsync(symbol, days, avgVolume);
                }

                return avgVolume;
            }
            catch (Exception ex)
            {
                LogManager.LogException("VolumeAnalysis", ex, $"获取 {symbol} 平均成交额失败");
                return 0;
            }
        }

        /// <summary>
        /// 从缓存加载平均成交额
        /// </summary>
        private async Task<decimal?> LoadFromCacheAsync(string symbol, int days)
        {
            try
            {
                var cacheFileName = string.Format(CACHE_FILE_FORMAT, days, DateTime.Today);
                var cacheFilePath = Path.Combine(CacheDirectory, cacheFileName);
                
                if (!File.Exists(cacheFilePath))
                {
                    return null;
                }
                
                // 检查文件是否是今天创建的
                var fileInfo = new FileInfo(cacheFilePath);
                if (fileInfo.CreationTime.Date != DateTime.Today)
                {
                    LogManager.Log("VolumeAnalysis", $"缓存文件已过期，删除: {cacheFileName}");
                    File.Delete(cacheFilePath);
                    return null;
                }

                var json = await File.ReadAllTextAsync(cacheFilePath);
                var cacheData = JsonSerializer.Deserialize<Dictionary<string, decimal>>(json);
                
                if (cacheData != null && cacheData.TryGetValue(symbol, out var avgVolume))
                {
                    return avgVolume;
                }
                
                return null;
            }
            catch (Exception ex)
            {
                LogManager.LogException("VolumeAnalysis", ex, "加载缓存失败");
                return null;
            }
        }

        /// <summary>
        /// 保存平均成交额到缓存
        /// </summary>
        private async Task SaveToCacheAsync(string symbol, int days, decimal avgVolume)
        {
            try
            {
                var cacheFileName = string.Format(CACHE_FILE_FORMAT, days, DateTime.Today);
                var cacheFilePath = Path.Combine(CacheDirectory, cacheFileName);
                
                Dictionary<string, decimal> cacheData;
                
                // 如果文件已存在，加载现有数据
                if (File.Exists(cacheFilePath))
                {
                    try
                    {
                        var existingJson = await File.ReadAllTextAsync(cacheFilePath);
                        cacheData = JsonSerializer.Deserialize<Dictionary<string, decimal>>(existingJson) 
                                   ?? new Dictionary<string, decimal>();
                    }
                    catch
                    {
                        cacheData = new Dictionary<string, decimal>();
                    }
                }
                else
                {
                    cacheData = new Dictionary<string, decimal>();
                }
                
                // 更新数据
                cacheData[symbol] = avgVolume;
                
                // 保存到文件
                var json = JsonSerializer.Serialize(cacheData, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                await File.WriteAllTextAsync(cacheFilePath, json);
                
                LogManager.Log("VolumeAnalysis", $"成功缓存 {symbol} 平均成交额: {avgVolume:F2}");
            }
            catch (Exception ex)
            {
                LogManager.LogException("VolumeAnalysis", ex, "保存缓存失败");
            }
        }

        /// <summary>
        /// 清理过期的缓存文件
        /// </summary>
        public void CleanupExpiredCache()
        {
            try
            {
                if (!Directory.Exists(CacheDirectory))
                {
                    return;
                }

                var files = Directory.GetFiles(CacheDirectory, "avg_volume_*.json");
                var today = DateTime.Today;
                
                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.CreationTime.Date < today)
                    {
                        try
                        {
                            File.Delete(file);
                            LogManager.Log("VolumeAnalysis", $"删除过期缓存文件: {Path.GetFileName(file)}");
                        }
                        catch (Exception ex)
                        {
                            LogManager.LogException("VolumeAnalysis", ex, $"删除缓存文件失败: {Path.GetFileName(file)}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("VolumeAnalysis", ex, "清理缓存失败");
            }
        }

        /// <summary>
        /// 预加载常用合约的平均成交额到缓存
        /// </summary>
        public async Task PreloadCommonSymbolsAsync(IEnumerable<string> symbols, int days, CancellationToken cancellationToken = default)
        {
            var tasks = new List<Task>();
            
            foreach (var symbol in symbols)
            {
                tasks.Add(GetAverageVolumeAsync(symbol, days, cancellationToken));
                
                // 避免同时发起太多数据库查询
                if (tasks.Count >= 10)
                {
                    await Task.WhenAll(tasks);
                    tasks.Clear();
                    
                    // 添加短暂延迟
                    await Task.Delay(100, cancellationToken);
                }
            }
            
            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks);
            }
            
            LogManager.Log("VolumeAnalysis", "常用合约平均成交额预加载完成");
        }
    }
} 