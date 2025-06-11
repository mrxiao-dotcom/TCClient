using System;
using System.Threading.Tasks;
using System.Data;
using MySql.Data.MySqlClient;
using TCClient.Models;
using System.IO;

namespace TCClient.Services
{
    public class TradingAccountService
    {
        private readonly string _connectionString;
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "TCClient_TradingAccountService.log");

        private static void LogToFile(string message)
        {
            // 日志输出已禁用
            // 如需启用，请取消注释以下代码：
            /*
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var logMessage = $"[{timestamp}] {message}{Environment.NewLine}";
                File.AppendAllText(LogFilePath, logMessage);
            }
            catch
            {
                // 忽略日志写入失败
            }
            */
        }

        public TradingAccountService(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentException("连接字符串不能为空", nameof(connectionString));
            }
            _connectionString = connectionString;
        }

        public async Task<AccountInfo> GetAccountInfoAsync(long accountId)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    SELECT 
                        equity as TotalEquity,
                        opportunity_count as OpportunityCount,
                        initial_equity as InitialEquity
                    FROM trading_accounts 
                    WHERE id = @AccountId";

                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@AccountId", accountId);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new AccountInfo
                    {
                        TotalEquity = reader.GetDecimal("TotalEquity"),
                        AvailableBalance = reader.GetDecimal("TotalEquity"), // 暂时用总权益代替
                        UnrealizedPnL = 0m, // 暂时设为0
                        MarginBalance = reader.GetDecimal("TotalEquity"), // 暂时用总权益代替
                        MaintenanceMargin = 0m, // 暂时设为0
                        InitialMargin = 0m, // 暂时设为0
                        PositionMargin = 0m, // 暂时设为0
                        WalletBalance = reader.GetDecimal("InitialEquity"), // 使用初始资金
                        UpdateTime = DateTime.Now,
                        OpportunityCount = reader.GetInt32("OpportunityCount")
                    };
                }

                throw new Exception($"未找到账户ID: {accountId}");
            }
            catch (Exception ex)
            {
                throw new Exception($"获取账户信息失败: {ex.Message}", ex);
            }
        }
    }
} 