using System;
using System.Threading.Tasks;
using TCClient.Services;
using TCClient.Utils;

namespace TCClient
{
    /// <summary>
    /// 成交量监控功能测试类
    /// </summary>
    public class TestVolumeMonitor
    {
        /// <summary>
        /// 测试网页抓取功能
        /// </summary>
        public static async Task TestWebScrapingAsync()
        {
            Console.WriteLine("=== 测试成交量网页抓取功能 ===");
            
            var webScrapingService = new WebScrapingService();
            
            try
            {
                Console.WriteLine("1. 测试网络连接...");
                var networkOk = await webScrapingService.TestNetworkConnectionAsync();
                Console.WriteLine($"   网络连接状态: {(networkOk ? "✅ 正常" : "❌ 失败")}");
                
                if (networkOk)
                {
                    Console.WriteLine("\n2. 获取24小时成交量数据...");
                    var volume = await webScrapingService.GetCoinStats24hVolumeAsync();
                    
                    if (volume.HasValue)
                    {
                        Console.WriteLine($"   ✅ 成功获取成交量: ${volume.Value:N0}");
                        Console.WriteLine($"   换算为亿美元: ${volume.Value / 1_000_000_000:F2}B");
                    }
                    else
                    {
                        Console.WriteLine("   ❌ 获取成交量失败");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ 测试失败: {ex.Message}");
            }
            finally
            {
                webScrapingService.Dispose();
            }
        }

        /// <summary>
        /// 测试成交量监控服务
        /// </summary>
        public static async Task TestVolumeMonitorServiceAsync()
        {
            Console.WriteLine("\n=== 测试成交量监控服务 ===");
            
            var volumeMonitorService = new VolumeMonitorService();
            
            try
            {
                Console.WriteLine("1. 获取当前配置...");
                var config = volumeMonitorService.GetConfig();
                Console.WriteLine($"   监控状态: {(config.IsEnabled ? "启用" : "禁用")}");
                Console.WriteLine($"   低成交量阈值: ${config.LowVolumeThreshold / 1_000_000_000:F0}亿美元");
                Console.WriteLine($"   高成交量阈值: ${config.HighVolumeThreshold / 1_000_000_000:F0}亿美元");
                Console.WriteLine($"   监控间隔: {config.MonitorIntervalMinutes}分钟");
                
                Console.WriteLine("\n2. 执行手动检查...");
                var volume = await volumeMonitorService.ManualCheckAsync();
                
                if (volume.HasValue)
                {
                    Console.WriteLine($"   ✅ 手动检查成功: ${volume.Value / 1_000_000_000:F2}B");
                    
                    // 测试阈值检查
                    if (volume.Value < config.LowVolumeThreshold)
                    {
                        Console.WriteLine($"   ⚠️  当前成交量低于低阈值");
                    }
                    else if (volume.Value > config.HighVolumeThreshold)
                    {
                        Console.WriteLine($"   ⚠️  当前成交量高于高阈值");
                    }
                    else
                    {
                        Console.WriteLine($"   ✅ 当前成交量在正常范围内");
                    }
                }
                else
                {
                    Console.WriteLine("   ❌ 手动检查失败");
                }
                
                Console.WriteLine($"\n3. 服务运行状态: {(volumeMonitorService.IsRunning ? "运行中" : "已停止")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ 测试失败: {ex.Message}");
            }
            finally
            {
                volumeMonitorService.Dispose();
            }
        }

        /// <summary>
        /// 测试配置保存和加载
        /// </summary>
        public static void TestConfigurationSaveLoad()
        {
            Console.WriteLine("\n=== 测试配置保存和加载 ===");
            
            try
            {
                var volumeMonitorService = new VolumeMonitorService();
                
                Console.WriteLine("1. 修改配置...");
                var config = volumeMonitorService.GetConfig();
                config.IsEnabled = true;
                config.LowVolumeThreshold = 30_000_000_000; // 300亿美元
                config.HighVolumeThreshold = 120_000_000_000; // 1200亿美元
                config.MonitorIntervalMinutes = 5;
                config.EnableLowVolumeAlert = true;
                config.EnableHighVolumeAlert = true;
                
                volumeMonitorService.UpdateConfig(config);
                Console.WriteLine("   ✅ 配置更新完成");
                
                Console.WriteLine("\n2. 重新创建服务实例验证配置持久化...");
                var newVolumeMonitorService = new VolumeMonitorService();
                var loadedConfig = newVolumeMonitorService.GetConfig();
                
                Console.WriteLine($"   监控状态: {(loadedConfig.IsEnabled ? "✅ 启用" : "❌ 禁用")}");
                Console.WriteLine($"   低成交量阈值: ${loadedConfig.LowVolumeThreshold / 1_000_000_000:F0}亿美元");
                Console.WriteLine($"   高成交量阈值: ${loadedConfig.HighVolumeThreshold / 1_000_000_000:F0}亿美元");
                Console.WriteLine($"   监控间隔: {loadedConfig.MonitorIntervalMinutes}分钟");
                
                bool configCorrect = loadedConfig.IsEnabled &&
                    loadedConfig.LowVolumeThreshold == 30_000_000_000 &&
                    loadedConfig.HighVolumeThreshold == 120_000_000_000 &&
                    loadedConfig.MonitorIntervalMinutes == 5;
                    
                Console.WriteLine($"   配置验证: {(configCorrect ? "✅ 通过" : "❌ 失败")}");
                
                volumeMonitorService.Dispose();
                newVolumeMonitorService.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ 测试失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 运行所有测试
        /// </summary>
        public static async Task RunAllTestsAsync()
        {
            Console.WriteLine("开始运行成交量监控功能测试...\n");
            
            await TestWebScrapingAsync();
            await TestVolumeMonitorServiceAsync();
            TestConfigurationSaveLoad();
            
            Console.WriteLine("\n=== 测试完成 ===");
            Console.WriteLine("请检查测试结果，确认所有功能正常工作。");
        }
    }
} 