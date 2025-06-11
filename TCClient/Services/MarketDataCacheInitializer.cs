using System;
using System.Threading.Tasks;
using TCClient.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TCClient.Services
{
    /// <summary>
    /// 市场数据缓存初始化器
    /// 在程序启动时预先计算并缓存价格统计数据，避免运行时重复计算
    /// </summary>
    public class MarketDataCacheInitializer
    {
        private readonly MarketOverviewService _marketOverviewService;
        private readonly ILogger<MarketDataCacheInitializer> _logger;

        public MarketDataCacheInitializer(
            MarketOverviewService marketOverviewService,
            ILogger<MarketDataCacheInitializer> logger = null)
        {
            _marketOverviewService = marketOverviewService;
            _logger = logger;
        }

        /// <summary>
        /// 初始化所有市场数据缓存
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                LogManager.Log("MarketDataCacheInitializer", "开始初始化市场数据缓存...");

                // 检查是否已有完整缓存
                if (_marketOverviewService.HasTodayCompleteCache())
                {
                    LogManager.Log("MarketDataCacheInitializer", "检测到完整的当日缓存，跳过初始化");
                    return;
                }

                // 批量初始化所有周期的价格统计缓存
                await _marketOverviewService.InitializeAllPriceStatsCacheAsync();

                LogManager.Log("MarketDataCacheInitializer", "市场数据缓存初始化完成");
            }
            catch (Exception ex)
            {
                LogManager.LogException("MarketDataCacheInitializer", ex, "市场数据缓存初始化失败");
                _logger?.LogError(ex, "市场数据缓存初始化失败");
            }
        }

        /// <summary>
        /// 静态方法：在程序启动时调用
        /// </summary>
        public static async Task InitializeOnStartupAsync(IServiceProvider serviceProvider)
        {
            try
            {
                var initializer = serviceProvider.GetService<MarketDataCacheInitializer>();
                if (initializer != null)
                {
                    await initializer.InitializeAsync();
                }
                else
                {
                    LogManager.Log("MarketDataCacheInitializer", "缓存初始化器未注册，跳过初始化");
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("MarketDataCacheInitializer", ex, "启动时缓存初始化失败");
            }
        }
    }
} 