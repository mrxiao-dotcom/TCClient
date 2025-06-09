using System;
using System.IO;

namespace TCClient.Utils
{
    public static class AppSession
    {
        private static long _currentUserId;
        private static long _currentAccountId;
        private static bool _isLoggedIn;
        private static bool _userRequestedExit;
        private static bool _windowDisplayed;
        private static bool _suspendEvents;

        public static long CurrentUserId
        {
            get => _currentUserId;
            set
            {
                if (_currentUserId != value)
                {
                    _currentUserId = value;
                    if (!_suspendEvents) OnCurrentUserIdChanged?.Invoke(value);
                }
            }
        }

        public static long CurrentAccountId
        {
            get => _currentAccountId;
            set
            {
                if (_currentAccountId != value)
                {
                    _currentAccountId = value;
                    if (!_suspendEvents) OnCurrentAccountIdChanged?.Invoke(value);
                }
            }
        }
        
        /// <summary>
        /// 标记用户是否成功登录，用于在无法设置DialogResult时传递登录状态
        /// </summary>
        public static bool IsLoggedIn
        {
            get => _isLoggedIn;
            set
            {
                if (_isLoggedIn != value)
                {
                    _isLoggedIn = value;
                    if (!_suspendEvents) OnLoginStatusChanged?.Invoke(value);
                }
            }
        }
        
        /// <summary>
        /// 标记用户是否主动请求退出应用程序，用于确保只有用户触发的退出才会真正关闭应用
        /// </summary>
        public static bool UserRequestedExit
        {
            get => _userRequestedExit;
            set
            {
                if (_userRequestedExit != value)
                {
                    _userRequestedExit = value;
                    if (!_suspendEvents) OnExitRequestChanged?.Invoke(value);
                }
            }
        }
        
        /// <summary>
        /// 标记主窗口是否已被显示，用于防止窗口被自动关闭
        /// </summary>
        public static bool WindowDisplayed
        {
            get => _windowDisplayed;
            set
            {
                if (_windowDisplayed != value)
                {
                    _windowDisplayed = value;
                    if (!_suspendEvents) OnWindowDisplayStatusChanged?.Invoke(value);
                }
            }
        }
        
        /// <summary>
        /// 是否暂停所有事件触发，用于避免在特定操作过程中触发事件导致UI冲突
        /// </summary>
        public static bool SuspendEvents
        {
            get => _suspendEvents;
            set => _suspendEvents = value;
        }

        public static event Action<long> OnCurrentUserIdChanged;
        public static event Action<long> OnCurrentAccountIdChanged;
        public static event Action<bool> OnLoginStatusChanged;
        public static event Action<bool> OnExitRequestChanged;
        public static event Action<bool> OnWindowDisplayStatusChanged;

        public static void ClearSession()
        {
            CurrentUserId = 0;
            CurrentAccountId = 0;
            IsLoggedIn = false;
            UserRequestedExit = false;
            WindowDisplayed = false;
        }

        // 可扩展：public static string CurrentUserName { get; set; }

        private static readonly string LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TCClient_App.log");

        public static void Log(string message)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var logMessage = $"[{timestamp}] {message}{Environment.NewLine}";
                File.AppendAllText(LogFilePath, logMessage);
            }
            catch { }
        }

        /// <summary>
        /// 强制清理所有资源并退出应用程序
        /// </summary>
        public static void ForceCleanupAndExit()
        {
            try
            {
                Log("开始强制清理资源并退出应用程序");
                
                // 停止所有后台任务
                try
                {
                    // 取消所有可能的CancellationToken
                    System.Threading.CancellationTokenSource.CreateLinkedTokenSource().Cancel();
                    Log("已取消所有后台任务");
                }
                catch (Exception ex)
                {
                    Log($"取消后台任务失败: {ex.Message}");
                }
                
                // 清理定时器
                try
                {
                    // 清理所有可能的DispatcherTimer
                    if (System.Windows.Application.Current != null)
                    {
                        foreach (System.Windows.Window window in System.Windows.Application.Current.Windows)
                        {
                            try
                            {
                                if (window is Views.FindOpportunityWindow)
                                {
                                    window.Close();
                                }
                            }
                            catch (Exception windowEx)
                            {
                                Log($"关闭窗口失败: {windowEx.Message}");
                            }
                        }
                    }
                    Log("已清理定时器");
                }
                catch (Exception ex)
                {
                    Log($"清理定时器失败: {ex.Message}");
                }
                
                // 强制垃圾回收
                try
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    Log("已执行垃圾回收");
                }
                catch (Exception ex)
                {
                    Log($"垃圾回收失败: {ex.Message}");
                }
                
                Log("资源清理完成，强制退出进程");
                
                // 强制退出进程
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Log($"强制清理资源时发生异常: {ex.Message}");
                // 无论如何都要退出
                Environment.Exit(1);
            }
        }
    }
} 