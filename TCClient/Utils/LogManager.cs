using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TCClient.Utils
{
    public static class LogManager
    {
        // 使用内存缓冲区存储日志
        private static readonly ConcurrentQueue<string> _logBuffer = new ConcurrentQueue<string>();
        
        // 日志文件路径
        private static readonly string MainLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "TCClient_Main.log");
            
        // 备份日志文件路径
        private static readonly string BackupLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "TCClient_Main.log.bak");
            
        // 标志位，表示是否启用日志记录
        private static bool _isLoggingEnabled = true;
        
        // 标志位，表示是否正在将日志写入文件
        private static volatile bool _isFlushingLogs = false;
        
        // 日志文件最大大小（10MB）
        private const long MAX_LOG_SIZE = 10 * 1024 * 1024;
        
        // 锁对象
        private static readonly object _logLock = new object();
        
        // 文件大小检查计数器
        private static int _checkCounter = 0;
        
        static LogManager()
        {
            try
            {
                // 检查日志文件是否过大，如果过大则清空
                if (File.Exists(MainLogPath))
                {
                    var fileInfo = new FileInfo(MainLogPath);
                    if (fileInfo.Length > MAX_LOG_SIZE)
                    {
                        // 尝试备份旧文件
                        try
                        {
                            if (File.Exists(BackupLogPath))
                                File.Delete(BackupLogPath);
                            File.Move(MainLogPath, BackupLogPath);
                        }
                        catch
                        {
                            // 如果备份失败，直接删除
                            File.Delete(MainLogPath);
                        }
                    }
                }
                
                // 写入日志头部
                using (var writer = new StreamWriter(MainLogPath, true))
                {
                    writer.WriteLine($"=============== 日志开始: {DateTime.Now} ===============");
                    writer.WriteLine($"操作系统: {Environment.OSVersion.VersionString}");
                    writer.WriteLine($".NET 版本: {Environment.Version}");
                    writer.WriteLine($"进程 ID: {Environment.ProcessId}");
                    writer.WriteLine("======================================================");
                    writer.Flush();
                }
            }
            catch
            {
                // 如果初始化日志失败，禁用日志记录但不影响程序运行
                _isLoggingEnabled = false;
            }
            
            // 启动后台线程，定期将日志写入文件
            Task.Run(async () => await FlushLogsPeriodicAsync());
        }
        
        /// <summary>
        /// 添加日志到内存缓冲区
        /// </summary>
        public static void Log(string source, string message)
        {
            if (!_isLoggingEnabled) return;
            
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var threadId = Thread.CurrentThread.ManagedThreadId;
                var logEntry = $"[{timestamp}][{source}][线程ID:{threadId}] {message}";
                
                // 添加到内存缓冲区
                _logBuffer.Enqueue(logEntry);
                
                // 调试输出，确保即使日志文件出问题也能看到日志
                System.Diagnostics.Debug.WriteLine(logEntry);
            }
            catch
            {
                // 忽略任何日志记录错误
            }
        }
        
        /// <summary>
        /// 强制立即将缓冲区中的日志写入文件
        /// </summary>
        public static void FlushLogs()
        {
            if (!_isLoggingEnabled || _isFlushingLogs) return;
            
            lock (_logLock)
            {
                _isFlushingLogs = true;
                try
                {
                    WriteLogsToFile();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"日志刷新失败: {ex.Message}");
                }
                finally
                {
                    _isFlushingLogs = false;
                }
            }
        }
        
        /// <summary>
        /// 后台任务：定期将日志写入文件
        /// </summary>
        private static async Task FlushLogsPeriodicAsync()
        {
            while (true)
            {
                try
                {
                    await Task.Delay(3000); // 每3秒执行一次
                    
                    if (!_isLoggingEnabled || _isFlushingLogs) continue;
                    
                    bool acquiredLock = false;
                    try
                    {
                        Monitor.TryEnter(_logLock, 100, ref acquiredLock);
                        if (acquiredLock)
                        {
                            _isFlushingLogs = true;
                            WriteLogsToFile();
                        }
                    }
                    finally
                    {
                        if (acquiredLock)
                        {
                            _isFlushingLogs = false;
                            Monitor.Exit(_logLock);
                        }
                    }
                }
                catch
                {
                    // 忽略任何后台日志处理错误
                    await Task.Delay(5000); // 如果发生错误，等待更长时间
                }
            }
        }
        
        /// <summary>
        /// 将内存中的日志写入文件
        /// </summary>
        private static void WriteLogsToFile()
        {
            if (_logBuffer.IsEmpty) return;
            
            try
            {
                // 每20次写入检查一下文件大小
                _checkCounter++;
                if (_checkCounter >= 20)
                {
                    _checkCounter = 0;
                    CheckAndRotateLogFile();
                }
                
                // 创建临时列表存储日志条目
                var logEntries = new System.Collections.Generic.List<string>();
                while (_logBuffer.TryDequeue(out string logEntry))
                {
                    logEntries.Add(logEntry);
                    if (logEntries.Count >= 1000) break; // 每次最多处理1000条日志
                }
                
                if (logEntries.Count > 0)
                {
                    // 使用文件追加模式，写入日志
                    using (var fileStream = new FileStream(MainLogPath, FileMode.Append, FileAccess.Write, FileShare.Read))
                    using (var writer = new StreamWriter(fileStream))
                    {
                        foreach (var entry in logEntries)
                        {
                            writer.WriteLine(entry);
                        }
                        writer.Flush();
                    }
                }
            }
            catch (IOException)
            {
                // 如果发生IO异常（如文件被占用），我们不禁用日志系统
                // 让下一次尝试继续
            }
            catch (Exception)
            {
                // 其他严重错误发生时禁用日志系统
                _isLoggingEnabled = false;
            }
        }
        
        /// <summary>
        /// 检查并轮转日志文件
        /// </summary>
        private static void CheckAndRotateLogFile()
        {
            try
            {
                if (!File.Exists(MainLogPath)) return;
                
                var fileInfo = new FileInfo(MainLogPath);
                if (fileInfo.Length > MAX_LOG_SIZE)
                {
                    // 尝试备份旧文件
                    try
                    {
                        if (File.Exists(BackupLogPath))
                            File.Delete(BackupLogPath);
                        File.Move(MainLogPath, BackupLogPath);
                    }
                    catch
                    {
                        // 如果备份失败，直接清空文件
                        File.WriteAllText(MainLogPath, $"=============== 日志已重置: {DateTime.Now} ===============\r\n");
                    }
                }
            }
            catch
            {
                // 忽略检查文件大小过程中的任何错误
            }
        }
        
        /// <summary>
        /// 记录异常，包含所有可能的细节
        /// </summary>
        public static void LogException(string source, Exception ex, string additionalInfo = null)
        {
            if (!_isLoggingEnabled || ex == null) return;
            
            Log(source, $"=============== 异常详情开始 ===============");
            
            if (!string.IsNullOrEmpty(additionalInfo))
            {
                Log(source, $"上下文信息: {additionalInfo}");
            }
            
            // 记录主异常信息
            Log(source, $"异常类型: {ex.GetType().FullName}");
            Log(source, $"异常消息: {ex.Message}");
            Log(source, $"异常源: {ex.Source}");
            Log(source, $"目标站点: {ex.TargetSite?.Name}");
            
            // 记录堆栈跟踪
            if (!string.IsNullOrEmpty(ex.StackTrace))
            {
                Log(source, "堆栈跟踪:");
                foreach (var line in ex.StackTrace.Split('\n'))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        Log(source, $"  {line.Trim()}");
                    }
                }
            }
            
            // 递归记录内部异常
            Exception innerEx = ex.InnerException;
            int innerLevel = 1;
            
            while (innerEx != null)
            {
                Log(source, $"====== 内部异常 {innerLevel} ======");
                Log(source, $"内部异常类型: {innerEx.GetType().FullName}");
                Log(source, $"内部异常消息: {innerEx.Message}");
                Log(source, $"内部异常源: {innerEx.Source}");
                
                // 记录内部异常堆栈
                if (!string.IsNullOrEmpty(innerEx.StackTrace))
                {
                    Log(source, "内部异常堆栈跟踪:");
                    foreach (var line in innerEx.StackTrace.Split('\n'))
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            Log(source, $"  {line.Trim()}");
                        }
                    }
                }
                
                innerEx = innerEx.InnerException;
                innerLevel++;
            }
            
            // 记录数据属性（处理MySqlException等特殊异常类型）
            if (ex.Data.Count > 0)
            {
                Log(source, "异常数据属性:");
                foreach (System.Collections.DictionaryEntry entry in ex.Data)
                {
                    Log(source, $"  {entry.Key}: {entry.Value}");
                }
            }
            
            // 特殊处理MySQL异常
            if (ex is MySql.Data.MySqlClient.MySqlException mysqlEx)
            {
                Log(source, $"MySQL错误码: {mysqlEx.Number}");
                Log(source, $"SQL状态: {mysqlEx.SqlState}");
                
                string friendlyMessage = mysqlEx.Number switch
                {
                    1042 => "无法连接到MySQL服务器，请检查服务器是否启动或网络连接",
                    1045 => "MySQL访问被拒绝，用户名或密码错误",
                    1049 => "指定的数据库不存在",
                    1146 => "指定的表不存在",
                    1062 => "记录已存在，违反唯一性约束",
                    _ => $"未处理的MySQL错误：{mysqlEx.Number}"
                };
                Log(source, $"友好错误信息: {friendlyMessage}");
            }
            
            Log(source, $"=============== 异常详情结束 ===============");
            
            // 立即刷新，确保错误信息被写入文件
            FlushLogs();
        }
    }
} 