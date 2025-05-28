using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TCClient.Utils
{
    public static class LogManager
    {
        // 标志位，表示是否启用日志记录（只保留调试输出）
        private static bool _isLoggingEnabled = true;
        
        static LogManager()
        {
            // 移除所有文件操作，只保留内存日志
        }
        
        /// <summary>
        /// 添加日志到调试输出
        /// </summary>
        public static void Log(string source, string message)
        {
            if (!_isLoggingEnabled) return;
            
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var threadId = Thread.CurrentThread.ManagedThreadId;
                var logEntry = $"[{timestamp}][{source}][线程ID:{threadId}] {message}";
                
                // 只保留调试输出
                System.Diagnostics.Debug.WriteLine(logEntry);
            }
            catch
            {
                // 忽略任何日志记录错误
            }
        }
        
        /// <summary>
        /// 强制立即刷新日志（现在只是空操作）
        /// </summary>
        public static void FlushLogs()
        {
            // 不再需要文件刷新操作
        }
        
        /// <summary>
        /// 记录异常信息
        /// </summary>
        public static void LogException(string source, Exception ex, string additionalInfo = "")
        {
            if (!_isLoggingEnabled) return;
            
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var threadId = Thread.CurrentThread.ManagedThreadId;
                
                var logEntry = $"[{timestamp}][{source}][线程ID:{threadId}] 异常: {ex.GetType().Name}: {ex.Message}";
                if (!string.IsNullOrEmpty(additionalInfo))
                {
                    logEntry += $" | 附加信息: {additionalInfo}";
                }
                
                // 只保留调试输出
                System.Diagnostics.Debug.WriteLine(logEntry);
                System.Diagnostics.Debug.WriteLine($"[{timestamp}][{source}][线程ID:{threadId}] 堆栈跟踪: {ex.StackTrace}");
                
                // 如果有内部异常，也记录
                if (ex.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[{timestamp}][{source}][线程ID:{threadId}] 内部异常: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                }
            }
            catch
            {
                // 忽略任何日志记录错误
            }
        }
        
        /// <summary>
        /// 禁用日志记录
        /// </summary>
        public static void DisableLogging()
        {
            _isLoggingEnabled = false;
        }
        
        /// <summary>
        /// 启用日志记录
        /// </summary>
        public static void EnableLogging()
        {
            _isLoggingEnabled = true;
        }
    }
} 