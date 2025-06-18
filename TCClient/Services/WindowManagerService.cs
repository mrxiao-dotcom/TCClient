using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using TCClient.ViewModels;
using TCClient.Views;
using TCClient.Utils;

namespace TCClient.Services
{
    /// <summary>
    /// 窗口管理服务 - 支持同时打开多个子窗口
    /// </summary>
    public class WindowManagerService : IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly Dictionary<string, List<Window>> _openWindows = new Dictionary<string, List<Window>>();
        private readonly object _windowsLock = new object();
        private bool _disposed = false;
        
        // 窗口切换器相关
        private WindowSwitcherViewModel _windowSwitcher;
        private System.Windows.Controls.Primitives.Popup _switcherPopup;
        
        // 事件
        public event Action<Window, string> WindowOpened;
        public event Action<Window> WindowClosed;

        public WindowManagerService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            InitializeWindowSwitcher();
            LogManager.Log("WindowManagerService", "窗口管理服务已初始化");
        }

        /// <summary>
        /// 获取所有打开的窗口数量
        /// </summary>
        public int TotalOpenWindows
        {
            get
            {
                lock (_windowsLock)
                {
                    return _openWindows.Values.SelectMany(list => list).Count();
                }
            }
        }

        /// <summary>
        /// 获取指定类型的打开窗口数量
        /// </summary>
        /// <param name="windowType">窗口类型名称</param>
        /// <returns>打开的窗口数量</returns>
        public int GetWindowCount(string windowType)
        {
            lock (_windowsLock)
            {
                return _openWindows.ContainsKey(windowType) ? _openWindows[windowType].Count : 0;
            }
        }

        /// <summary>
        /// 显示市场总览窗口（非模态）
        /// </summary>
        public void ShowMarketOverviewWindow()
        {
            try
            {
                var window = new MarketOverviewWindow();
                ConfigureWindow(window, "MarketOverview", "市场总览");
                window.Show();
                
                RegisterWindow("MarketOverview", window);
                LogManager.Log("WindowManagerService", "市场总览窗口已打开");
            }
            catch (Exception ex)
            {
                LogManager.LogException("WindowManagerService", ex, "打开市场总览窗口失败");
                throw;
            }
        }

        /// <summary>
        /// 显示服务管理器窗口（非模态）
        /// </summary>
        public void ShowServiceManagerWindow()
        {
            try
            {
                var window = new ServiceManagerWindow();
                ConfigureWindow(window, "ServiceManager", "后台服务管理器");
                window.Show();
                
                RegisterWindow("ServiceManager", window);
                LogManager.Log("WindowManagerService", "服务管理器窗口已打开");
            }
            catch (Exception ex)
            {
                LogManager.LogException("WindowManagerService", ex, "打开服务管理器窗口失败");
                throw;
            }
        }

        /// <summary>
        /// 显示推仓统计窗口（非模态）
        /// </summary>
        /// <param name="accountId">账户ID</param>
        public void ShowPushStatisticsWindow(int accountId)
        {
            try
            {
                if (accountId <= 0)
                {
                    throw new ArgumentException("请先选择一个有效的交易账户");
                }

                var viewModel = _serviceProvider.GetRequiredService<PushStatisticsViewModel>();
                var window = new PushStatisticsWindow(viewModel);
                
                ConfigureWindow(window, "PushStatistics", $"推仓统计 - 账户{accountId}");
                
                // 设置当前账户ID
                AppSession.CurrentAccountId = accountId;
                
                // 窗口显示后立即刷新数据
                window.Loaded += async (s, e) => await viewModel.RefreshDataAsync();
                
                window.Show();
                RegisterWindow("PushStatistics", window);
                LogManager.Log("WindowManagerService", $"推仓统计窗口已打开 - 账户{accountId}");
            }
            catch (Exception ex)
            {
                LogManager.LogException("WindowManagerService", ex, "打开推仓统计窗口失败");
                throw;
            }
        }

        /// <summary>
        /// 显示账户查询窗口（非模态）
        /// </summary>
        /// <param name="accountId">账户ID</param>
        public void ShowAccountQueryWindow(int accountId)
        {
            try
            {
                if (accountId <= 0)
                {
                    throw new ArgumentException("请先选择一个有效的交易账户");
                }

                var viewModel = _serviceProvider.GetRequiredService<AccountQueryViewModel>();
                var window = new AccountQueryWindow(viewModel);
                
                ConfigureWindow(window, "AccountQuery", $"账户查询 - 账户{accountId}");
                
                // 设置当前账户ID
                AppSession.CurrentAccountId = accountId;
                
                window.Show();
                RegisterWindow("AccountQuery", window);
                LogManager.Log("WindowManagerService", $"账户查询窗口已打开 - 账户{accountId}");
            }
            catch (Exception ex)
            {
                LogManager.LogException("WindowManagerService", ex, "打开账户查询窗口失败");
                throw;
            }
        }

        /// <summary>
        /// 显示下单窗口（模态，但支持多个）
        /// </summary>
        /// <param name="accountId">账户ID</param>
        /// <param name="symbol">交易对（可选）</param>
        /// <param name="owner">父窗口</param>
        public void ShowOrderWindow(int accountId, string symbol = null, Window owner = null)
        {
            try
            {
                if (accountId <= 0)
                {
                    throw new ArgumentException("请先选择一个有效的交易账户");
                }

                var window = _serviceProvider.GetRequiredService<OrderWindow>();
                
                // 配置窗口
                var title = string.IsNullOrEmpty(symbol) ? 
                    $"下单窗口 - 账户{accountId}" : 
                    $"下单窗口 - 账户{accountId} - {symbol}";
                
                ConfigureWindow(window, "Order", title, owner);
                
                // 如果指定了交易对，可以在这里设置
                if (!string.IsNullOrEmpty(symbol))
                {
                    // 这里可以设置窗口的默认交易对
                    // window.SetSymbol(symbol);
                }
                
                window.ShowDialog();
                RegisterWindow("Order", window);
                LogManager.Log("WindowManagerService", $"下单窗口已打开 - 账户{accountId}, 交易对{symbol}");
            }
            catch (Exception ex)
            {
                LogManager.LogException("WindowManagerService", ex, "打开下单窗口失败");
                throw;
            }
        }

        /// <summary>
        /// 显示数据库配置窗口（模态）
        /// </summary>
        /// <param name="owner">父窗口</param>
        public bool? ShowDatabaseConfigWindow(Window owner = null)
        {
            try
            {
                // 首先尝试显示数据库设置向导
                var setupWizard = new DatabaseSetupWizard();
                ConfigureWindow(setupWizard, "DatabaseSetup", "数据库设置向导", owner);
                var result = setupWizard.ShowDialog();
                
                // 如果用户取消了向导，则显示高级配置窗口
                if (result != true)
                {
                    var configWindow = new DatabaseConfigWindow();
                    ConfigureWindow(configWindow, "DatabaseConfig", "数据库配置", owner);
                    result = configWindow.ShowDialog();
                }
                
                LogManager.Log("WindowManagerService", $"数据库配置窗口已关闭，结果: {result}");
                return result;
            }
            catch (Exception ex)
            {
                LogManager.LogException("WindowManagerService", ex, "打开数据库配置窗口失败");
                throw;
            }
        }

        /// <summary>
        /// 显示账户管理窗口（模态）
        /// </summary>
        /// <param name="owner">父窗口</param>
        public void ShowAccountManagementWindow(Window owner = null)
        {
            try
            {
                var window = new AccountConfigWindow();
                ConfigureWindow(window, "AccountConfig", "账户管理", owner);
                window.ShowDialog();
                LogManager.Log("WindowManagerService", "账户管理窗口已关闭");
            }
            catch (Exception ex)
            {
                LogManager.LogException("WindowManagerService", ex, "打开账户管理窗口失败");
                throw;
            }
        }

        /// <summary>
        /// 显示回撤预警窗口（非模态）
        /// </summary>
        public void ShowDrawdownAlertWindow()
        {
            try
            {
                var window = new DrawdownAlertWindow();
                ConfigureWindow(window, "DrawdownAlert", "回撤预警监控");
                window.Show();
                
                RegisterWindow("DrawdownAlert", window);
                LogManager.Log("WindowManagerService", "回撤预警窗口已打开");
            }
            catch (Exception ex)
            {
                LogManager.LogException("WindowManagerService", ex, "打开回撤预警窗口失败");
                throw;
            }
        }

        /// <summary>
        /// 关闭指定类型的所有窗口
        /// </summary>
        /// <param name="windowType">窗口类型</param>
        public void CloseAllWindows(string windowType)
        {
            lock (_windowsLock)
            {
                if (_openWindows.ContainsKey(windowType))
                {
                    var windows = _openWindows[windowType].ToList();
                    foreach (var window in windows)
                    {
                        try
                        {
                            window.Close();
                        }
                        catch (Exception ex)
                        {
                            LogManager.LogException("WindowManagerService", ex, $"关闭窗口失败: {window.Title}");
                        }
                    }
                    _openWindows[windowType].Clear();
                    LogManager.Log("WindowManagerService", $"已关闭所有 {windowType} 类型的窗口");
                }
            }
        }

        /// <summary>
        /// 关闭所有非主窗口
        /// </summary>
        public void CloseAllChildWindows()
        {
            lock (_windowsLock)
            {
                var allWindows = _openWindows.Values.SelectMany(list => list).ToList();
                foreach (var window in allWindows)
                {
                    try
                    {
                        if (window != Application.Current.MainWindow)
                        {
                            window.Close();
                        }
                    }
                    catch (Exception ex)
                    {
                        LogManager.LogException("WindowManagerService", ex, $"关闭子窗口失败: {window.Title}");
                    }
                }
                _openWindows.Clear();
                LogManager.Log("WindowManagerService", "已关闭所有子窗口");
            }
        }

        /// <summary>
        /// 获取窗口状态信息
        /// </summary>
        /// <returns>窗口状态字符串</returns>
        public string GetWindowStatus()
        {
            lock (_windowsLock)
            {
                var status = new List<string>();
                foreach (var kvp in _openWindows)
                {
                    status.Add($"{kvp.Key}: {kvp.Value.Count}个");
                }
                return status.Count > 0 ? string.Join(", ", status) : "无打开的子窗口";
            }
        }

        /// <summary>
        /// 配置窗口的通用属性
        /// </summary>
        private void ConfigureWindow(Window window, string windowType, string title, Window owner = null)
        {
            window.Title = title;
            window.Owner = owner ?? Application.Current.MainWindow;
            window.WindowStartupLocation = owner != null ? 
                WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            
            // 为非模态窗口添加任务栏图标
            if (windowType != "Order" && windowType != "DatabaseConfig" && 
                windowType != "DatabaseSetup" && windowType != "AccountConfig")
            {
                window.ShowInTaskbar = true;
            }
        }

        /// <summary>
        /// 注册窗口到管理列表
        /// </summary>
        private void RegisterWindow(string windowType, Window window)
        {
            lock (_windowsLock)
            {
                if (!_openWindows.ContainsKey(windowType))
                {
                    _openWindows[windowType] = new List<Window>();
                }
                
                _openWindows[windowType].Add(window);
                
                // 监听窗口关闭事件
                window.Closed += (s, e) => UnregisterWindow(windowType, window);
                
                // 触发窗口打开事件
                WindowOpened?.Invoke(window, windowType);
                
                LogManager.Log("WindowManagerService", 
                    $"窗口已注册: {windowType}, 当前同类型窗口数量: {_openWindows[windowType].Count}");
            }
        }

        /// <summary>
        /// 从管理列表中移除窗口
        /// </summary>
        private void UnregisterWindow(string windowType, Window window)
        {
            lock (_windowsLock)
            {
                if (_openWindows.ContainsKey(windowType))
                {
                    _openWindows[windowType].Remove(window);
                    if (_openWindows[windowType].Count == 0)
                    {
                        _openWindows.Remove(windowType);
                    }
                    
                    // 触发窗口关闭事件
                    WindowClosed?.Invoke(window);
                    
                    LogManager.Log("WindowManagerService", 
                        $"窗口已移除: {windowType}, 剩余同类型窗口数量: " +
                        $"{(_openWindows.ContainsKey(windowType) ? _openWindows[windowType].Count : 0)}");
                }
            }
        }

        /// <summary>
        /// 初始化窗口切换器
        /// </summary>
        private void InitializeWindowSwitcher()
        {
            try
            {
                _windowSwitcher = new WindowSwitcherViewModel(this);
                
                // 创建弹出窗口
                _switcherPopup = new System.Windows.Controls.Primitives.Popup
                {
                    Child = new Views.WindowSwitcherPanel(_windowSwitcher),
                    Placement = System.Windows.Controls.Primitives.PlacementMode.Center,
                    AllowsTransparency = true,
                    StaysOpen = false,
                    Width = 400,
                    Height = 300
                };
                
                LogManager.Log("WindowManagerService", "窗口切换器已初始化");
            }
            catch (Exception ex)
            {
                LogManager.LogException("WindowManagerService", ex, "初始化窗口切换器失败");
            }
        }
        
        /// <summary>
        /// 显示窗口切换器
        /// </summary>
        public void ShowWindowSwitcher()
        {
            try
            {
                if (_windowSwitcher != null && _switcherPopup != null)
                {
                    _windowSwitcher.RefreshWindowList();
                    _switcherPopup.IsOpen = true;
                    LogManager.Log("WindowManagerService", "显示窗口切换器");
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("WindowManagerService", ex, "显示窗口切换器失败");
            }
        }
        
        /// <summary>
        /// 隐藏窗口切换器
        /// </summary>
        public void HideWindowSwitcher()
        {
            try
            {
                if (_switcherPopup != null)
                {
                    _switcherPopup.IsOpen = false;
                    LogManager.Log("WindowManagerService", "隐藏窗口切换器");
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("WindowManagerService", ex, "隐藏窗口切换器失败");
            }
        }
        
        /// <summary>
        /// 切换窗口切换器显示状态
        /// </summary>
        public void ToggleWindowSwitcher()
        {
            try
            {
                if (_switcherPopup?.IsOpen == true)
                {
                    HideWindowSwitcher();
                }
                else
                {
                    ShowWindowSwitcher();
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("WindowManagerService", ex, "切换窗口切换器状态失败");
            }
        }
        
        /// <summary>
        /// 激活指定窗口
        /// </summary>
        /// <param name="window">要激活的窗口</param>
        public void ActivateWindow(Window window)
        {
            try
            {
                if (window != null)
                {
                    if (window.WindowState == WindowState.Minimized)
                    {
                        window.WindowState = WindowState.Normal;
                    }
                    window.Activate();
                    window.Focus();
                    LogManager.Log("WindowManagerService", $"激活窗口: {window.Title}");
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("WindowManagerService", ex, "激活窗口失败");
            }
        }
        
        /// <summary>
        /// 最小化所有窗口
        /// </summary>
        public void MinimizeAllWindows()
        {
            lock (_windowsLock)
            {
                try
                {
                    var allWindows = _openWindows.Values.SelectMany(list => list).ToList();
                    foreach (var window in allWindows)
                    {
                        window.WindowState = WindowState.Minimized;
                    }
                    LogManager.Log("WindowManagerService", "已最小化所有窗口");
                }
                catch (Exception ex)
                {
                    LogManager.LogException("WindowManagerService", ex, "最小化所有窗口失败");
                }
            }
        }
        
        /// <summary>
        /// 恢复所有窗口
        /// </summary>
        public void RestoreAllWindows()
        {
            lock (_windowsLock)
            {
                try
                {
                    var allWindows = _openWindows.Values.SelectMany(list => list).ToList();
                    foreach (var window in allWindows)
                    {
                        if (window.WindowState == WindowState.Minimized)
                        {
                            window.WindowState = WindowState.Normal;
                        }
                        window.Activate();
                    }
                    LogManager.Log("WindowManagerService", "已恢复所有窗口");
                }
                catch (Exception ex)
                {
                    LogManager.LogException("WindowManagerService", ex, "恢复所有窗口失败");
                }
            }
        }
        
        /// <summary>
        /// 获取所有打开的窗口
        /// </summary>
        /// <returns>所有打开的窗口列表</returns>
        public List<Window> GetAllOpenWindows()
        {
            lock (_windowsLock)
            {
                return _openWindows.Values.SelectMany(list => list).ToList();
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                CloseAllChildWindows();
                
                // 清理窗口切换器
                _windowSwitcher?.Dispose();
                _switcherPopup = null;
                
                _disposed = true;
                LogManager.Log("WindowManagerService", "窗口管理服务已释放");
            }
        }
    }
} 