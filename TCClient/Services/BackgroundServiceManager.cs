using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using TCClient.Utils;

namespace TCClient.Services
{
    /// <summary>
    /// 后台服务配置选项
    /// </summary>
    public class BackgroundServiceOptions
    {
        /// <summary>
        /// 是否启用条件单监控服务
        /// </summary>
        public bool EnableConditionalOrderService { get; set; } = true;
        
        /// <summary>
        /// 是否启用止损监控服务
        /// </summary>
        public bool EnableStopLossMonitorService { get; set; } = true;
        
        /// <summary>
        /// 是否启用寻找机会窗口的定时器
        /// </summary>
        public bool EnableFindOpportunityTimer { get; set; } = true;
        
        /// <summary>
        /// 是否启用订单窗口的价格更新器
        /// </summary>
        public bool EnableOrderPriceUpdater { get; set; } = true;
        
        /// <summary>
        /// 是否启用账户信息更新器
        /// </summary>
        public bool EnableAccountInfoUpdater { get; set; } = true;
        
        /// <summary>
        /// 是否启用账户查询定时器
        /// </summary>
        public bool EnableAccountQueryTimer { get; set; } = true;
    }

    /// <summary>
    /// 后台服务管理器 - 统一管理所有后台定时程序
    /// </summary>
    public class BackgroundServiceManager : IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly BackgroundServiceOptions _options;
        private readonly Dictionary<string, IDisposable> _runningServices = new Dictionary<string, IDisposable>();
        private bool _isDisposed = false;

        public BackgroundServiceManager(IServiceProvider serviceProvider, BackgroundServiceOptions options = null)
        {
            _serviceProvider = serviceProvider;
            _options = options ?? LoadOptionsFromConfig();
        }

        /// <summary>
        /// 从配置文件加载服务选项
        /// </summary>
        private BackgroundServiceOptions LoadOptionsFromConfig()
        {
            try
            {
                // 暂时使用默认配置，后续可以扩展为从文件加载
                LogManager.Log("BackgroundServiceManager", "使用默认服务配置");
                return new BackgroundServiceOptions();
            }
            catch (Exception ex)
            {
                LogManager.LogException("BackgroundServiceManager", ex, "加载服务配置失败，使用默认配置");
                return new BackgroundServiceOptions();
            }
        }

        /// <summary>
        /// 保存服务选项到配置文件
        /// </summary>
        public void SaveOptionsToConfig()
        {
            try
            {
                // 暂时跳过配置保存，后续可以扩展为保存到文件
                LogManager.Log("BackgroundServiceManager", "后台服务配置保存已跳过（使用内存配置）");
            }
            catch (Exception ex)
            {
                LogManager.LogException("BackgroundServiceManager", ex, "保存服务配置失败");
            }
        }

        /// <summary>
        /// 启动所有已启用的后台服务
        /// </summary>
        public void StartAllEnabledServices()
        {
            LogManager.Log("BackgroundServiceManager", "开始启动后台服务...");
            LogManager.Log("BackgroundServiceManager", $"服务配置: 条件单={_options.EnableConditionalOrderService}, 止损={_options.EnableStopLossMonitorService}, 寻找机会={_options.EnableFindOpportunityTimer}, 价格更新={_options.EnableOrderPriceUpdater}, 账户信息={_options.EnableAccountInfoUpdater}, 账户查询={_options.EnableAccountQueryTimer}");

            // 启动条件单监控服务
            if (_options.EnableConditionalOrderService)
            {
                StartConditionalOrderService();
            }
            else
            {
                LogManager.Log("BackgroundServiceManager", "条件单监控服务已禁用");
            }

            // 启动止损监控服务
            if (_options.EnableStopLossMonitorService)
            {
                StartStopLossMonitorService();
            }
            else
            {
                LogManager.Log("BackgroundServiceManager", "止损监控服务已禁用");
            }

            LogManager.Log("BackgroundServiceManager", "后台服务启动完成");
        }

        /// <summary>
        /// 仅启动寻找机会相关的服务（用户需求的模式）
        /// </summary>
        public void StartFindOpportunityOnlyMode()
        {
            LogManager.Log("BackgroundServiceManager", "启动寻找机会专用模式 - 只启动寻找机会相关服务");
            
            // 停止所有其他服务
            StopAllServices();
            
            // 更新配置
            _options.EnableConditionalOrderService = false;
            _options.EnableStopLossMonitorService = false;
            _options.EnableFindOpportunityTimer = true;
            _options.EnableOrderPriceUpdater = false;
            _options.EnableAccountInfoUpdater = false;
            _options.EnableAccountQueryTimer = false;
            
            // 保存配置
            SaveOptionsToConfig();
            
            LogManager.Log("BackgroundServiceManager", "寻找机会专用模式启动完成 - 其他后台服务已禁用");
        }

        /// <summary>
        /// 启动条件单监控服务
        /// </summary>
        private void StartConditionalOrderService()
        {
            try
            {
                var service = _serviceProvider.GetService<ConditionalOrderService>();
                if (service != null)
                {
                    service.Start();
                    _runningServices["ConditionalOrder"] = service;
                    LogManager.Log("BackgroundServiceManager", "条件单监控服务已启动");
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("BackgroundServiceManager", ex, "启动条件单监控服务失败");
            }
        }

        /// <summary>
        /// 启动止损监控服务
        /// </summary>
        private void StartStopLossMonitorService()
        {
            try
            {
                var service = _serviceProvider.GetService<StopLossMonitorService>();
                if (service != null)
                {
                    service.Start();
                    _runningServices["StopLossMonitor"] = service;
                    LogManager.Log("BackgroundServiceManager", "止损监控服务已启动");
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("BackgroundServiceManager", ex, "启动止损监控服务失败");
            }
        }

        /// <summary>
        /// 停止指定的服务
        /// </summary>
        public void StopService(string serviceName)
        {
            try
            {
                if (_runningServices.TryGetValue(serviceName, out var service))
                {
                    if (service is ConditionalOrderService conditionalService)
                    {
                        conditionalService.Stop();
                    }
                    else if (service is StopLossMonitorService stopLossService)
                    {
                        stopLossService.Stop();
                    }

                    service.Dispose();
                    _runningServices.Remove(serviceName);
                    LogManager.Log("BackgroundServiceManager", $"服务 {serviceName} 已停止");
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("BackgroundServiceManager", ex, $"停止服务 {serviceName} 失败");
            }
        }

        /// <summary>
        /// 停止所有服务
        /// </summary>
        public void StopAllServices()
        {
            LogManager.Log("BackgroundServiceManager", "开始停止所有后台服务...");
            
            var serviceNames = new List<string>(_runningServices.Keys);
            foreach (var serviceName in serviceNames)
            {
                StopService(serviceName);
            }
            
            LogManager.Log("BackgroundServiceManager", "所有后台服务已停止");
        }

        /// <summary>
        /// 重启所有服务
        /// </summary>
        public void RestartAllServices()
        {
            LogManager.Log("BackgroundServiceManager", "重启所有后台服务");
            StopAllServices();
            StartAllEnabledServices();
        }

        /// <summary>
        /// 获取当前服务配置
        /// </summary>
        public BackgroundServiceOptions GetCurrentOptions()
        {
            return new BackgroundServiceOptions
            {
                EnableConditionalOrderService = _options.EnableConditionalOrderService,
                EnableStopLossMonitorService = _options.EnableStopLossMonitorService,
                EnableFindOpportunityTimer = _options.EnableFindOpportunityTimer,
                EnableOrderPriceUpdater = _options.EnableOrderPriceUpdater,
                EnableAccountInfoUpdater = _options.EnableAccountInfoUpdater,
                EnableAccountQueryTimer = _options.EnableAccountQueryTimer
            };
        }

        /// <summary>
        /// 更新服务配置
        /// </summary>
        public void UpdateOptions(BackgroundServiceOptions newOptions)
        {
            _options.EnableConditionalOrderService = newOptions.EnableConditionalOrderService;
            _options.EnableStopLossMonitorService = newOptions.EnableStopLossMonitorService;
            _options.EnableFindOpportunityTimer = newOptions.EnableFindOpportunityTimer;
            _options.EnableOrderPriceUpdater = newOptions.EnableOrderPriceUpdater;
            _options.EnableAccountInfoUpdater = newOptions.EnableAccountInfoUpdater;
            _options.EnableAccountQueryTimer = newOptions.EnableAccountQueryTimer;
            
            SaveOptionsToConfig();
            LogManager.Log("BackgroundServiceManager", "后台服务配置已更新");
        }

        /// <summary>
        /// 获取服务运行状态
        /// </summary>
        public Dictionary<string, bool> GetServiceStatus()
        {
            return new Dictionary<string, bool>
            {
                ["ConditionalOrder"] = _runningServices.ContainsKey("ConditionalOrder"),
                ["StopLossMonitor"] = _runningServices.ContainsKey("StopLossMonitor")
            };
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                StopAllServices();
                _isDisposed = true;
            }
        }
    }
} 