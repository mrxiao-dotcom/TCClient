using System;
using System.IO;

namespace TCClient.Utils
{
    public static class AppSession
    {
        public static long CurrentAccountId { get; set; }
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
    }
} 