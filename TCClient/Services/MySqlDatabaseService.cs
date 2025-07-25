using System;
using System.Collections.Generic;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;
using TCClient.Models;
using TCClient.ViewModels;
using System.IO;
using TCClient.Utils;
using System.Threading;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace TCClient.Services
{
    public class MySqlDatabaseService : IDatabaseService, IUserService
    {
        private readonly LocalConfigService _configService;
        private readonly ILogger<MySqlDatabaseService> _logger;
        private string _connectionString;

        public MySqlDatabaseService(ILogger<MySqlDatabaseService> logger)
        {
            LogManager.Log("Database", "MySqlDatabaseService 构造函数开始执行...");
            _configService = new LocalConfigService();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            LogManager.Log("Database", "MySqlDatabaseService 构造函数执行完成");
            _logger.LogInformation("MySqlDatabaseService 已初始化，ILogger 注入成功");
        }

        private async Task LoadConnectionStringAsync()
            {
                try
                {
                LogManager.Log("Database", "加载数据库连接字符串...");
                    var connections = await _configService.LoadDatabaseConnections();
                
                if (connections.Count == 0)
                {
                    throw new InvalidOperationException("没有可用的数据库连接配置");
                }
                
                var connection = connections[0];
                string server = connection.Server;
                string port = connection.Port.ToString();
                string database = connection.Database;
                string username = connection.Username;
                string password = connection.Password;
                
                // 构建连接字符串，添加连接超时和其他参数
                _connectionString = $"Server={server};Port={port};Database={database};User ID={username};Password={password};Connection Timeout=30;Command Timeout=60;SSL Mode=None;";
                LogManager.Log("Database", $"数据库连接字符串已加载: Server={server};Port={port};Database={database}");
                }
                catch (Exception ex)
                {
                LogManager.LogException("Database", ex, "加载数据库连接字符串失败");
                    throw;
                }
            }

        private async Task EnsureConnectionStringLoadedAsync()
        {
            LogManager.Log("Database", "确保连接字符串已加载...");
            if (string.IsNullOrEmpty(_connectionString))
            {
                await LoadConnectionStringAsync();
            }
            LogManager.Log("Database", "连接字符串检查完成");
        }

        // 辅助方法：记录数据库操作开始
        private void LogDatabaseOperationStart(string operation, string sql = null, Dictionary<string, object> parameters = null)
        {
            LogManager.Log("Database", $"=== 开始数据库操作: {operation} ===");
            if (!string.IsNullOrEmpty(sql))
            {
                LogManager.Log("Database", $"SQL: {sql}");
            }
            
            if (parameters != null && parameters.Count > 0)
            {
                LogManager.Log("Database", "参数:");
                foreach (var param in parameters)
                {
                    string paramValue = param.Value?.ToString() ?? "null";
                    LogManager.Log("Database", $"  @{param.Key} = {paramValue}");
                }
            }
        }
        
        // 辅助方法：记录数据库操作结束
        private void LogDatabaseOperationEnd(string operation, bool success)
        {
            LogManager.Log("Database", $"=== 数据库操作结束: {operation} - {(success ? "成功" : "失败")} ===");
        }
        
        // 辅助方法：记录数据库操作异常
        private void LogDatabaseOperationError(string operation, Exception ex)
        {
            LogManager.LogException("Database", ex, $"数据库操作失败: {operation}");
            
            // 特别处理MySQL异常
            if (ex is MySqlException mysqlEx)
            {
                LogManager.Log("Database", $"MySQL错误码: {mysqlEx.Number}");
                LogManager.Log("Database", $"SQL状态: {mysqlEx.SqlState}");
                
                switch (mysqlEx.Number)
                {
                    case 1042: // 无法连接到MySQL服务器
                        LogManager.Log("Database", "无法连接到MySQL服务器，请检查服务器是否启动或网络连接");
                        break;
                    case 1045: // 访问被拒绝（用户名或密码错误）
                        LogManager.Log("Database", "MySQL访问被拒绝，用户名或密码错误");
                        break;
                    case 1049: // 未知数据库
                        LogManager.Log("Database", "指定的数据库不存在");
                        break;
                    case 1146: // 表不存在
                        LogManager.Log("Database", "指定的表不存在");
                        break;
                    case 1062: // 主键或唯一键冲突
                        LogManager.Log("Database", "记录已存在，违反唯一性约束");
                        break;
                    default:
                        LogManager.Log("Database", $"未处理的MySQL错误：{mysqlEx.Number}");
                        break;
                }
            }
        }

        /// <summary>
        /// 执行数据库操作的通用包装方法，包含超时和异常处理
        /// </summary>
        /// <typeparam name="T">返回值类型</typeparam>
        /// <param name="operation">操作名称</param>
        /// <param name="databaseOperation">数据库操作函数</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <param name="timeoutSeconds">超时时间（秒），默认30秒</param>
        /// <returns>操作结果</returns>
        private async Task<T> ExecuteDatabaseOperationAsync<T>(
            string operation,
            Func<MySqlConnection, CancellationToken, Task<T>> databaseOperation,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = 30)
        {
            LogDatabaseOperationStart(operation);
            
            try
            {
                await EnsureConnectionStringLoadedAsync();
                
                if (string.IsNullOrEmpty(_connectionString))
                {
                    throw new InvalidOperationException("数据库连接字符串未设置");
                }

                // 添加命令超时设置到连接字符串
                var connectionStringWithTimeout = _connectionString;
                if (!_connectionString.Contains("Default Command Timeout"))
                {
                    connectionStringWithTimeout += $";Default Command Timeout={timeoutSeconds};";
                }
                if (!_connectionString.Contains("Connection Timeout"))
                {
                    connectionStringWithTimeout += ";Connection Timeout=10;";
                }

                using var connection = new MySqlConnection(connectionStringWithTimeout);
                
                // 使用CancellationTokenSource设置额外的超时控制
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds + 5)); // 给额外5秒缓冲
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                
                await connection.OpenAsync(combinedCts.Token);
                
                var result = await databaseOperation(connection, combinedCts.Token);
                
                LogDatabaseOperationEnd(operation, true);
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                LogManager.Log("Database", $"数据库操作被用户取消: {operation}");
                LogDatabaseOperationEnd(operation, false);
                throw;
            }
            catch (OperationCanceledException)
            {
                LogManager.Log("Database", $"数据库操作超时: {operation} (超时时间: {timeoutSeconds}秒)");
                LogDatabaseOperationEnd(operation, false);
                
                // 使用友好的异常处理
                var timeoutEx = new TimeoutException($"数据库操作超时: {operation}");
                Utils.NetworkExceptionHandler.HandleDatabaseException(timeoutEx, operation, false);
                
                throw timeoutEx;
            }
            catch (MySqlException mysqlEx)
            {
                LogDatabaseOperationError(operation, mysqlEx);
                LogDatabaseOperationEnd(operation, false);
                
                // 使用友好的异常处理
                Utils.NetworkExceptionHandler.HandleDatabaseException(mysqlEx, operation, false);
                
                throw;
            }
            catch (Exception ex)
            {
                LogDatabaseOperationError(operation, ex);
                LogDatabaseOperationEnd(operation, false);
                
                // 检查是否为网络或数据库相关异常
                if (Utils.NetworkExceptionHandler.IsDatabaseException(ex) || 
                    Utils.NetworkExceptionHandler.IsNetworkException(ex))
                {
                    Utils.NetworkExceptionHandler.HandleDatabaseException(ex, operation, false);
                }
                
                throw;
            }
        }

        /// <summary>
        /// 执行无返回值的数据库操作
        /// </summary>
        /// <param name="operation">操作名称</param>
        /// <param name="databaseOperation">数据库操作函数</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <param name="timeoutSeconds">超时时间（秒），默认30秒</param>
        private async Task ExecuteDatabaseOperationAsync(
            string operation,
            Func<MySqlConnection, CancellationToken, Task> databaseOperation,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = 30)
        {
            await ExecuteDatabaseOperationAsync<object>(
                operation,
                async (connection, ct) =>
                {
                    await databaseOperation(connection, ct);
                    return null;
                },
                cancellationToken,
                timeoutSeconds);
        }

        public bool TestConnection(string connectionString)
        {
            var operation = "测试数据库连接";
            LogDatabaseOperationStart(operation, null, new Dictionary<string, object> {
                { "connectionString", $"{connectionString.Substring(0, Math.Min(30, connectionString.Length))}..." }
            });
            
            try
            {
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();
                    LogDatabaseOperationEnd(operation, true);
                    return true;
                    }
                }
            catch (Exception ex)
                {
                LogDatabaseOperationError(operation, ex);
                LogDatabaseOperationEnd(operation, false);
                            return false;
                        }
                    }

        public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            var operation = "异步测试数据库连接";
            LogDatabaseOperationStart(operation);
            
            try
            {
                await EnsureConnectionStringLoadedAsync();
                
                if (string.IsNullOrEmpty(_connectionString))
                {
                    LogManager.Log("Database", "错误：数据库连接字符串未设置");
                    throw new InvalidOperationException("数据库连接字符串未设置");
                }

                // 在连接字符串中添加连接超时设置
                var connectionStringWithTimeout = _connectionString;
                if (!_connectionString.Contains("Connection Timeout"))
                {
                    connectionStringWithTimeout += ";Connection Timeout=10;";
                }
                
                LogManager.Log("Database", $"尝试连接数据库，连接字符串: {connectionStringWithTimeout.Replace("Pwd=", "Pwd=***")}");

                using (var connection = new MySqlConnection(connectionStringWithTimeout))
                {
                    LogManager.Log("Database", "开始打开数据库连接...");
                    
                    // 使用CancellationTokenSource设置额外的超时控制
                    using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                    using (var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
                    {
                        await connection.OpenAsync(combinedCts.Token);
                    }
                    
                    LogManager.Log("Database", $"数据库连接成功！服务器版本: {connection.ServerVersion}");
                    LogManager.Log("Database", $"连接状态: {connection.State}");
                    
                    // 测试一个简单的查询
                    using (var cmd = new MySqlCommand("SELECT 1", connection))
                    {
                        var result = await cmd.ExecuteScalarAsync(cancellationToken);
                        LogManager.Log("Database", $"测试查询结果: {result}");
                    }
                    
                    LogDatabaseOperationEnd(operation, true);
                    return true;
                }
            }
            catch (OperationCanceledException)
            {
                LogManager.Log("Database", "数据库连接测试被取消");
                LogDatabaseOperationEnd(operation, false);
                throw;
            }
            catch (MySqlException mysqlEx)
            {
                LogManager.Log("Database", $"MySQL连接错误 - 错误码: {mysqlEx.Number}");
                LogManager.Log("Database", $"MySQL错误信息: {mysqlEx.Message}");
                
                switch (mysqlEx.Number)
                {
                    case 0: // 通用连接错误
                        LogManager.Log("Database", "无法连接到MySQL服务器，请检查：");
                        LogManager.Log("Database", "1. MySQL服务是否正在运行");
                        LogManager.Log("Database", "2. 服务器地址和端口是否正确");
                        LogManager.Log("Database", "3. 防火墙是否阻止连接");
                        break;
                    case 1042: // 无法连接到MySQL服务器
                        LogManager.Log("Database", "无法连接到MySQL服务器，可能原因：");
                        LogManager.Log("Database", "1. 服务器未启动或不可达");
                        LogManager.Log("Database", "2. 网络连接问题");
                        LogManager.Log("Database", "3. 端口被防火墙阻止");
                        break;
                    case 1045: // 访问被拒绝
                        LogManager.Log("Database", "数据库访问被拒绝，请检查用户名和密码");
                        break;
                    case 1049: // 未知数据库
                        LogManager.Log("Database", "指定的数据库不存在，请检查数据库名称");
                        break;
                    case 1130: // 主机不允许连接
                        LogManager.Log("Database", "主机不允许连接，请检查MySQL用户权限");
                        break;
                    default:
                        LogManager.Log("Database", $"未知的MySQL错误: {mysqlEx.Number} - {mysqlEx.Message}");
                        break;
                }
                
                LogDatabaseOperationError(operation, mysqlEx);
                LogDatabaseOperationEnd(operation, false);
                return false;
            }
            catch (Exception ex)
            {
                LogManager.Log("Database", $"数据库连接发生未知错误: {ex.GetType().Name}");
                LogManager.Log("Database", $"错误信息: {ex.Message}");
                LogDatabaseOperationError(operation, ex);
                LogDatabaseOperationEnd(operation, false);
                return false;
            }
        }

        public async Task<bool> ValidateUserAsync(string username, string password, CancellationToken cancellationToken = default)
        {
            LogManager.Log("Database", $"开始验证用户: {username}");
            
            try
            {
                return await ExecuteDatabaseOperationAsync(
                    $"验证用户 {username}",
                    async (connection, ct) =>
                    {
                using var cmd = new MySqlCommand(
                    "SELECT password_hash FROM users WHERE username = @username",
                    connection);
                cmd.Parameters.AddWithValue("@username", username);
                LogManager.Log("Database", $"执行查询: SELECT password_hash FROM users WHERE username = '{username}'");

                        var result = await cmd.ExecuteScalarAsync(ct);
                if (result == null)
                {
                    LogManager.Log("Database", $"用户 {username} 不存在");
                    return false;
                }

                var storedHash = result.ToString();
                var inputHash = HashPassword(password);
                LogManager.Log("Database", $"密码验证: 存储的哈希值 = {storedHash}, 输入的哈希值 = {inputHash}");
                
                var isValid = storedHash == inputHash;
                LogManager.Log("Database", $"密码验证结果: {(isValid ? "成功" : "失败")}");
                return isValid;
                    },
                    cancellationToken,
                    15 // 用户验证操作设置较短的超时时间
                );
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, "验证用户时发生错误");
                throw;
            }
        }

        public async Task<bool> CreateUserAsync(string username, string password, CancellationToken cancellationToken = default)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);

                // 检查用户名是否已存在
                using var checkCmd = new MySqlCommand(
                    "SELECT COUNT(*) FROM users WHERE username = @username",
                    connection);
                checkCmd.Parameters.AddWithValue("@username", username);

                var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync(cancellationToken));
                if (count > 0)
                {
                    return false;
                }

                // 创建新用户
                using var cmd = new MySqlCommand(
                    "INSERT INTO users (username, password_hash) VALUES (@username, @password_hash)",
                    connection);
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@password_hash", HashPassword(password));

                await cmd.ExecuteNonQueryAsync(cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, "创建用户失败");
                throw;
            }
        }

        // 账户相关方法
        public async Task<List<Account>> GetUserAccountsAsync(string username, CancellationToken cancellationToken = default)
        {
            var accounts = new List<Account>();
            
            try
            {
                LogManager.Log("Database", $"开始获取用户 {username} 的账户信息");
                await EnsureConnectionStringLoadedAsync();
                
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync(cancellationToken);
                using (var command = connection.CreateCommand())
                {
                        // 修改SQL查询，使其匹配实际的数据库表结构
                    command.CommandText = @"
                            SELECT a.id, a.account_name, a.equity, a.initial_equity, 
                                   a.opportunity_count, a.is_active, a.create_time, 
                                   a.update_time, ua.is_default
                            FROM trading_accounts a
                        INNER JOIN user_trading_accounts ua ON a.id = ua.account_id
                        INNER JOIN users u ON ua.user_id = u.id
                        WHERE u.username = @username AND a.is_active = 1";

                    command.Parameters.AddWithValue("@username", username);
                        LogManager.Log("Database", $"执行SQL: {command.CommandText}");

                    using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                    {
                        while (await reader.ReadAsync(cancellationToken))
                        {
                                // 创建Account对象，仅设置存在的字段
                                var account = new Account
                            {
                                Id = reader.GetInt32("id"),
                                AccountName = reader.GetString("account_name"),
                                    // 使用常量值或默认值替代不存在的字段
                                    Type = "交易账户", // 固定值
                                    Balance = reader.GetDecimal("equity"), // 用权益替代余额
                                Equity = reader.GetDecimal("equity"),
                                    Margin = 0, // 默认值
                                    RiskRatio = 0, // 默认值
                                CreateTime = reader.GetDateTime("create_time"),
                                    LastLoginTime = null, // 不存在该字段
                                IsActive = reader.GetBoolean("is_active"),
                                    Description = null // 不存在该字段
                                };
                                
                                accounts.Add(account);
                                LogManager.Log("Database", $"已添加账户: ID={account.Id}, 名称={account.AccountName}");
                            }
                        }
                    }
                }
                
                LogManager.Log("Database", $"成功获取用户账户信息，共 {accounts.Count} 个账户");
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, $"获取用户 {username} 的账户信息失败");
                // 继续抛出异常让调用者处理
                throw;
            }
            
            return accounts;
        }

        public async Task<Account> GetAccountByIdAsync(int accountId, CancellationToken cancellationToken = default)
        {
            try
            {
                LogManager.Log("Database", $"开始获取账户ID {accountId} 的信息");
                await EnsureConnectionStringLoadedAsync();
                
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync(cancellationToken);
                using (var command = connection.CreateCommand())
                {
                        // 修改SQL查询，使其匹配实际的数据库表结构
                        command.CommandText = @"
                            SELECT id, account_name, equity, initial_equity, 
                                   opportunity_count, is_active, create_time, update_time
                            FROM trading_accounts 
                            WHERE id = @id";
                            
                    command.Parameters.AddWithValue("@id", accountId);
                        LogManager.Log("Database", $"执行SQL: {command.CommandText}");

                    using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                    {
                        if (await reader.ReadAsync(cancellationToken))
                        {
                                // 创建Account对象，仅设置存在的字段
                                var account = new Account
                            {
                                Id = reader.GetInt32("id"),
                                AccountName = reader.GetString("account_name"),
                                    // 使用常量值或默认值替代不存在的字段
                                    Type = "交易账户", // 固定值
                                    Balance = reader.GetDecimal("equity"), // 用权益替代余额
                                Equity = reader.GetDecimal("equity"),
                                    Margin = 0, // 默认值
                                    RiskRatio = 0, // 默认值
                                CreateTime = reader.GetDateTime("create_time"),
                                    LastLoginTime = null, // 不存在该字段
                                IsActive = reader.GetBoolean("is_active"),
                                    Description = null // 不存在该字段
                                };
                                
                                LogManager.Log("Database", $"已获取账户: ID={account.Id}, 名称={account.AccountName}");
                                return account;
                            }
                        }
                    }
                }
                
                LogManager.Log("Database", $"未找到ID为 {accountId} 的账户");
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, $"获取账户ID {accountId} 的信息失败");
                // 继续抛出异常让调用者处理
                throw;
            }
            
            return null;
        }

        public async Task<bool> CreateAccountAsync(Account account, CancellationToken cancellationToken = default)
        {
            try
            {
                LogManager.Log("Database", $"开始创建账户: {account.AccountName}");
                await EnsureConnectionStringLoadedAsync();
                
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync(cancellationToken);
                using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
                {
                    try
                    {
                        using (var command = connection.CreateCommand())
                        {
                            command.Transaction = transaction;
                                // 修改SQL插入语句，使其匹配实际的数据库表结构
                            command.CommandText = @"
                                INSERT INTO trading_accounts (
                                        account_name, binance_account_id, api_key, api_secret,
                                        equity, initial_equity, opportunity_count, status, is_active,
                                        create_time, update_time
                                ) VALUES (
                                        @account_name, @binance_account_id, @api_key, @api_secret,
                                        @equity, @initial_equity, @opportunity_count, @status, @is_active,
                                        @create_time, @update_time
                                )";

                            command.Parameters.AddWithValue("@account_name", account.AccountName);
                                command.Parameters.AddWithValue("@binance_account_id", $"default_{Guid.NewGuid().ToString("N").Substring(0, 8)}");
                                command.Parameters.AddWithValue("@api_key", string.Empty);
                                command.Parameters.AddWithValue("@api_secret", string.Empty);
                            command.Parameters.AddWithValue("@equity", account.Equity);
                                command.Parameters.AddWithValue("@initial_equity", account.Equity); // 初始权益等于当前权益
                                command.Parameters.AddWithValue("@opportunity_count", 10); // 默认值
                                command.Parameters.AddWithValue("@status", 1);
                                command.Parameters.AddWithValue("@is_active", account.IsActive ? 1 : 0);
                            command.Parameters.AddWithValue("@create_time", DateTime.Now);
                                command.Parameters.AddWithValue("@update_time", DateTime.Now);

                                LogManager.Log("Database", $"执行SQL: {command.CommandText}");
                            await command.ExecuteNonQueryAsync(cancellationToken);
                        }

                        await transaction.CommitAsync(cancellationToken);
                            LogManager.Log("Database", $"账户 {account.AccountName} 创建成功");
                        return true;
                    }
                        catch (Exception ex)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                            LogManager.LogException("Database", ex, $"创建账户 {account.AccountName} 时发生错误，事务已回滚");
                        throw;
                    }
                }
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, $"创建账户 {account.AccountName} 失败");
                throw;
            }
        }

        public async Task<bool> UpdateAccountAsync(Account account, CancellationToken cancellationToken = default)
        {
            try
            {
                LogManager.Log("Database", $"开始更新账户ID {account.Id}: {account.AccountName}");
                await EnsureConnectionStringLoadedAsync();
                
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync(cancellationToken);
                using (var command = connection.CreateCommand())
                {
                        // 修改SQL更新语句，使其匹配实际的数据库表结构
                    command.CommandText = @"
                        UPDATE trading_accounts SET
                            account_name = @account_name,
                            equity = @equity,
                            is_active = @is_active,
                                update_time = @update_time
                        WHERE id = @id";

                    command.Parameters.AddWithValue("@id", account.Id);
                    command.Parameters.AddWithValue("@account_name", account.AccountName);
                    command.Parameters.AddWithValue("@equity", account.Equity);
                        command.Parameters.AddWithValue("@is_active", account.IsActive ? 1 : 0);
                        command.Parameters.AddWithValue("@update_time", DateTime.Now);

                        LogManager.Log("Database", $"执行SQL: {command.CommandText}");
                    var result = await command.ExecuteNonQueryAsync(cancellationToken);
                        
                        LogManager.Log("Database", $"账户更新结果: 影响行数={result}");
                    return result > 0;
                }
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, $"更新账户ID {account.Id} 失败");
                throw;
            }
        }

        public async Task<bool> DeleteAccountAsync(int accountId, CancellationToken cancellationToken = default)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync(cancellationToken);
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "UPDATE trading_accounts SET is_active = 0 WHERE id = @id";
                    command.Parameters.AddWithValue("@id", accountId);

                    var result = await command.ExecuteNonQueryAsync(cancellationToken);
                    return result > 0;
                }
            }
        }

        // 持仓相关方法
        public async Task<List<Position>> GetPositionsAsync(int accountId, CancellationToken cancellationToken = default)
        {
            var positions = new List<Position>();
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync(cancellationToken);
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM positions WHERE account_id = @account_id AND status = 'active'";
                    command.Parameters.AddWithValue("@account_id", accountId);

                    using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                    {
                        while (await reader.ReadAsync(cancellationToken))
                        {
                            positions.Add(new Position
                            {
                                Id = reader.GetInt32("id"),
                                AccountId = reader.GetInt32("account_id"),
                                Contract = reader.GetString("contract"),
                                Direction = reader.GetString("direction"),
                                Quantity = reader.GetInt32("quantity"),
                                EntryPrice = reader.GetDecimal("entry_price"),
                                CurrentPrice = reader.GetDecimal("current_price"),
                                FloatingPnL = reader.GetDecimal("floating_pnl"),
                                StopLoss = reader.GetDecimal("stop_loss"),
                                TakeProfit = reader.GetDecimal("take_profit"),
                                OpenTime = reader.GetDateTime("open_time"),
                                CloseTime = reader.IsDBNull("close_time") ? null : (DateTime?)reader.GetDateTime("close_time"),
                                Status = reader.GetString("status")
                            });
                        }
                    }
                }
            }
            return positions;
        }

        public async Task<Position> GetPositionByIdAsync(int positionId, CancellationToken cancellationToken = default)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync(cancellationToken);
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM positions WHERE id = @id";
                    command.Parameters.AddWithValue("@id", positionId);

                    using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                    {
                        if (await reader.ReadAsync(cancellationToken))
                        {
                            return new Position
                            {
                                Id = reader.GetInt32("id"),
                                AccountId = reader.GetInt32("account_id"),
                                Contract = reader.GetString("contract"),
                                Direction = reader.GetString("direction"),
                                Quantity = reader.GetInt32("quantity"),
                                EntryPrice = reader.GetDecimal("entry_price"),
                                CurrentPrice = reader.GetDecimal("current_price"),
                                FloatingPnL = reader.GetDecimal("floating_pnl"),
                                StopLoss = reader.GetDecimal("stop_loss"),
                                TakeProfit = reader.GetDecimal("take_profit"),
                                OpenTime = reader.GetDateTime("open_time"),
                                CloseTime = reader.IsDBNull("close_time") ? null : (DateTime?)reader.GetDateTime("close_time"),
                                Status = reader.GetString("status")
                            };
                        }
                    }
                }
            }
            return null;
        }

        public async Task<bool> CreatePositionAsync(Position position, CancellationToken cancellationToken = default)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync(cancellationToken);
                using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
                {
                    try
                    {
                        using (var command = connection.CreateCommand())
                        {
                            command.Transaction = transaction;
                            command.CommandText = @"
                                INSERT INTO positions (
                                    account_id, contract, direction, quantity,
                                    entry_price, current_price, floating_pnl,
                                    stop_loss, take_profit, open_time, status
                                ) VALUES (
                                    @account_id, @contract, @direction, @quantity,
                                    @entry_price, @current_price, @floating_pnl,
                                    @stop_loss, @take_profit, @open_time, @status
                                )";

                            command.Parameters.AddWithValue("@account_id", position.AccountId);
                            command.Parameters.AddWithValue("@contract", position.Contract);
                            command.Parameters.AddWithValue("@direction", position.Direction);
                            command.Parameters.AddWithValue("@quantity", position.Quantity);
                            command.Parameters.AddWithValue("@entry_price", position.EntryPrice);
                            command.Parameters.AddWithValue("@current_price", position.CurrentPrice);
                            command.Parameters.AddWithValue("@floating_pnl", position.FloatingPnL);
                            command.Parameters.AddWithValue("@stop_loss", position.StopLoss);
                            command.Parameters.AddWithValue("@take_profit", position.TakeProfit);
                            command.Parameters.AddWithValue("@open_time", DateTime.Now);
                            command.Parameters.AddWithValue("@status", "active");

                            await command.ExecuteNonQueryAsync(cancellationToken);
                        }

                        await transaction.CommitAsync(cancellationToken);
                        return true;
                    }
                    catch
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        throw;
                    }
                }
            }
        }

        public async Task<bool> UpdatePositionAsync(Position position, CancellationToken cancellationToken = default)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync(cancellationToken);
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        UPDATE positions SET
                            current_price = @current_price,
                            floating_pnl = @floating_pnl,
                            stop_loss = @stop_loss,
                            take_profit = @take_profit,
                            status = @status
                        WHERE id = @id";

                    command.Parameters.AddWithValue("@id", position.Id);
                    command.Parameters.AddWithValue("@current_price", position.CurrentPrice);
                    command.Parameters.AddWithValue("@floating_pnl", position.FloatingPnL);
                    command.Parameters.AddWithValue("@stop_loss", position.StopLoss);
                    command.Parameters.AddWithValue("@take_profit", position.TakeProfit);
                    command.Parameters.AddWithValue("@status", position.Status);

                    var result = await command.ExecuteNonQueryAsync(cancellationToken);
                    return result > 0;
                }
            }
        }

        public async Task<bool> ClosePositionAsync(int positionId, CancellationToken cancellationToken = default)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync(cancellationToken);
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        UPDATE positions SET
                            status = 'closed',
                            close_time = @close_time
                        WHERE id = @id";

                    command.Parameters.AddWithValue("@id", positionId);
                    command.Parameters.AddWithValue("@close_time", DateTime.Now);

                    var result = await command.ExecuteNonQueryAsync(cancellationToken);
                    return result > 0;
                }
            }
        }

        // 委托相关方法
        public async Task<List<Order>> GetOrdersAsync(int accountId)
        {
            var orders = new List<Order>();
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM orders WHERE account_id = @account_id ORDER BY create_time DESC";
                    command.Parameters.AddWithValue("@account_id", accountId);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            orders.Add(new Order
                            {
                                Id = reader.GetInt32("id"),
                                AccountId = reader.GetInt32("account_id"),
                                OrderId = reader.GetString("order_id"),
                                Contract = reader.GetString("contract"),
                                Direction = reader.GetString("direction"),
                                OffsetFlag = reader.GetString("offset_flag"),
                                Quantity = reader.GetInt32("quantity"),
                                Price = reader.GetDecimal("price"),
                                OrderType = reader.GetString("order_type"),
                                Status = reader.GetString("status"),
                                CreateTime = reader.GetDateTime("create_time"),
                                UpdateTime = reader.IsDBNull("update_time") ? null : (DateTime?)reader.GetDateTime("update_time"),
                                Message = reader.IsDBNull("message") ? null : reader.GetString("message")
                            });
                        }
                    }
                }
            }
            return orders;
        }

        public async Task<Order> GetOrderByIdAsync(int orderId)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM orders WHERE id = @id";
                    command.Parameters.AddWithValue("@id", orderId);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return new Order
                            {
                                Id = reader.GetInt32("id"),
                                AccountId = reader.GetInt32("account_id"),
                                OrderId = reader.GetString("order_id"),
                                Contract = reader.GetString("contract"),
                                Direction = reader.GetString("direction"),
                                OffsetFlag = reader.GetString("offset_flag"),
                                Quantity = reader.GetInt32("quantity"),
                                Price = reader.GetDecimal("price"),
                                OrderType = reader.GetString("order_type"),
                                Status = reader.GetString("status"),
                                CreateTime = reader.GetDateTime("create_time"),
                                UpdateTime = reader.IsDBNull("update_time") ? null : (DateTime?)reader.GetDateTime("update_time"),
                                Message = reader.IsDBNull("message") ? null : reader.GetString("message")
                            };
                        }
                    }
                }
            }
            return null;
        }

        public async Task<bool> CreateOrderAsync(Order order)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        using (var command = connection.CreateCommand())
                        {
                            command.Transaction = transaction;
                            command.CommandText = @"
                                INSERT INTO orders (
                                    account_id, order_id, contract, direction,
                                    offset_flag, quantity, price, order_type,
                                    status, create_time, message
                                ) VALUES (
                                    @account_id, @order_id, @contract, @direction,
                                    @offset_flag, @quantity, @price, @order_type,
                                    @status, @create_time, @message
                                )";

                            command.Parameters.AddWithValue("@account_id", order.AccountId);
                            command.Parameters.AddWithValue("@order_id", order.OrderId);
                            command.Parameters.AddWithValue("@contract", order.Contract);
                            command.Parameters.AddWithValue("@direction", order.Direction);
                            command.Parameters.AddWithValue("@offset_flag", order.OffsetFlag);
                            command.Parameters.AddWithValue("@quantity", order.Quantity);
                            command.Parameters.AddWithValue("@price", order.Price);
                            command.Parameters.AddWithValue("@order_type", order.OrderType);
                            command.Parameters.AddWithValue("@status", "submitted");
                            command.Parameters.AddWithValue("@create_time", DateTime.Now);
                            command.Parameters.AddWithValue("@message", (object)order.Message ?? DBNull.Value);

                            await command.ExecuteNonQueryAsync();
                        }

                        await transaction.CommitAsync();
                        return true;
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                }
            }
        }

        public async Task<bool> UpdateOrderAsync(Order order)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        UPDATE orders SET
                            status = @status,
                            update_time = @update_time,
                            message = @message
                        WHERE id = @id";

                    command.Parameters.AddWithValue("@id", order.Id);
                    command.Parameters.AddWithValue("@status", order.Status);
                    command.Parameters.AddWithValue("@update_time", DateTime.Now);
                    command.Parameters.AddWithValue("@message", (object)order.Message ?? DBNull.Value);

                    var result = await command.ExecuteNonQueryAsync();
                    return result > 0;
                }
            }
        }

        public async Task<bool> CancelOrderAsync(int orderId)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        UPDATE orders SET
                            status = 'cancelled',
                            update_time = @update_time,
                            message = '订单已取消'
                        WHERE id = @id";

                    command.Parameters.AddWithValue("@id", orderId);
                    command.Parameters.AddWithValue("@update_time", DateTime.Now);

                    var result = await command.ExecuteNonQueryAsync();
                    return result > 0;
                }
            }
        }

        // 成交相关方法
        public async Task<List<Trade>> GetTradesAsync(int accountId)
        {
            var trades = new List<Trade>();
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM trades WHERE account_id = @account_id ORDER BY trade_time DESC";
                    command.Parameters.AddWithValue("@account_id", accountId);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            trades.Add(new Trade
                            {
                                Id = reader.GetInt32("id"),
                                AccountId = reader.GetInt32("account_id"),
                                TradeId = reader.GetString("trade_id"),
                                OrderId = reader.GetString("order_id"),
                                Contract = reader.GetString("contract"),
                                Direction = reader.GetString("direction"),
                                OffsetFlag = reader.GetString("offset_flag"),
                                Quantity = reader.GetInt32("quantity"),
                                Price = reader.GetDecimal("price"),
                                Commission = reader.GetDecimal("commission"),
                                Tax = reader.GetDecimal("tax"),
                                TradeTime = reader.GetDateTime("trade_time"),
                                TradeType = reader.GetString("trade_type"),
                                Message = reader.IsDBNull("message") ? null : reader.GetString("message")
                            });
                        }
                    }
                }
            }
            return trades;
        }

        public async Task<Trade> GetTradeByIdAsync(int tradeId)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM trades WHERE id = @id";
                    command.Parameters.AddWithValue("@id", tradeId);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return new Trade
                            {
                                Id = reader.GetInt32("id"),
                                AccountId = reader.GetInt32("account_id"),
                                TradeId = reader.GetString("trade_id"),
                                OrderId = reader.GetString("order_id"),
                                Contract = reader.GetString("contract"),
                                Direction = reader.GetString("direction"),
                                OffsetFlag = reader.GetString("offset_flag"),
                                Quantity = reader.GetInt32("quantity"),
                                Price = reader.GetDecimal("price"),
                                Commission = reader.GetDecimal("commission"),
                                Tax = reader.GetDecimal("tax"),
                                TradeTime = reader.GetDateTime("trade_time"),
                                TradeType = reader.GetString("trade_type"),
                                Message = reader.IsDBNull("message") ? null : reader.GetString("message")
                            };
                        }
                    }
                }
            }
            return null;
        }

        public async Task<bool> CreateTradeAsync(Trade trade)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        using (var command = connection.CreateCommand())
                        {
                            command.Transaction = transaction;
                            command.CommandText = @"
                                INSERT INTO trades (
                                    account_id, trade_id, order_id, contract,
                                    direction, offset_flag, quantity, price,
                                    commission, tax, trade_time, trade_type,
                                    message
                                ) VALUES (
                                    @account_id, @trade_id, @order_id, @contract,
                                    @direction, @offset_flag, @quantity, @price,
                                    @commission, @tax, @trade_time, @trade_type,
                                    @message
                                )";

                            command.Parameters.AddWithValue("@account_id", trade.AccountId);
                            command.Parameters.AddWithValue("@trade_id", trade.TradeId);
                            command.Parameters.AddWithValue("@order_id", trade.OrderId);
                            command.Parameters.AddWithValue("@contract", trade.Contract);
                            command.Parameters.AddWithValue("@direction", trade.Direction);
                            command.Parameters.AddWithValue("@offset_flag", trade.OffsetFlag);
                            command.Parameters.AddWithValue("@quantity", trade.Quantity);
                            command.Parameters.AddWithValue("@price", trade.Price);
                            command.Parameters.AddWithValue("@commission", trade.Commission);
                            command.Parameters.AddWithValue("@tax", trade.Tax);
                            command.Parameters.AddWithValue("@trade_time", DateTime.Now);
                            command.Parameters.AddWithValue("@trade_type", trade.TradeType);
                            command.Parameters.AddWithValue("@message", (object)trade.Message ?? DBNull.Value);

                            await command.ExecuteNonQueryAsync();
                        }

                        await transaction.CommitAsync();
                        return true;
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                }
            }
        }

        public async Task<List<Trade>> GetTradesByOrderIdAsync(string orderId)
        {
            var trades = new List<Trade>();
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM trades WHERE order_id = @order_id ORDER BY trade_time";
                    command.Parameters.AddWithValue("@order_id", orderId);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            trades.Add(new Trade
                            {
                                Id = reader.GetInt32("id"),
                                AccountId = reader.GetInt32("account_id"),
                                TradeId = reader.GetString("trade_id"),
                                OrderId = reader.GetString("order_id"),
                                Contract = reader.GetString("contract"),
                                Direction = reader.GetString("direction"),
                                OffsetFlag = reader.GetString("offset_flag"),
                                Quantity = reader.GetInt32("quantity"),
                                Price = reader.GetDecimal("price"),
                                Commission = reader.GetDecimal("commission"),
                                Tax = reader.GetDecimal("tax"),
                                TradeTime = reader.GetDateTime("trade_time"),
                                TradeType = reader.GetString("trade_type"),
                                Message = reader.IsDBNull("message") ? null : reader.GetString("message")
                            });
                        }
                    }
                }
            }
            return trades;
        }

        // 辅助方法
        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(hashedBytes);
        }

        // IUserService 接口实现
        public async Task<IEnumerable<TradingAccount>> GetTradingAccountsAsync(CancellationToken cancellationToken = default)
        {
            var accounts = new List<TradingAccount>();
            try
            {
                await EnsureConnectionStringLoadedAsync();
            using (var connection = new MySqlConnection(_connectionString))
            {
                    await connection.OpenAsync(cancellationToken);
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            SELECT t.*, uta.is_default
                            FROM trading_accounts t
                            INNER JOIN user_trading_accounts uta ON t.id = uta.account_id
                            WHERE uta.user_id = @user_id AND t.is_active = 1
                            ORDER BY t.create_time DESC";

                        command.Parameters.AddWithValue("@user_id", AppSession.CurrentUserId);

                        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                    {
                            while (await reader.ReadAsync(cancellationToken))
                        {
                            accounts.Add(new TradingAccount
                            {
                                Id = reader.GetInt64("id"),
                                    AccountName = reader.GetString("account_name"),
                                BinanceAccountId = reader.GetString("binance_account_id"),
                                ApiKey = reader.GetString("api_key"),
                                ApiSecret = reader.GetString("api_secret"),
                                ApiPassphrase = reader.IsDBNull(reader.GetOrdinal("api_passphrase")) ? null : reader.GetString("api_passphrase"),
                                Equity = reader.GetDecimal("equity"),
                                InitialEquity = reader.GetDecimal("initial_equity"),
                                OpportunityCount = reader.GetInt32("opportunity_count"),
                                Status = reader.GetInt32("status"),
                                IsActive = reader.GetInt32("is_active"),
                                CreateTime = reader.GetDateTime("create_time"),
                                UpdateTime = reader.GetDateTime("update_time"),
                                    IsDefault = reader.GetInt32("is_default"),
                                    IsDefaultAccount = reader.GetInt32("is_default") == 1
                            });
                        }
                    }
                }
            }
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, "获取交易账户列表失败");
                throw;
            }
            return accounts;
        }

        public async Task<TradingAccount> GetTradingAccountByIdAsync(long accountId, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            SELECT t.*, uta.is_default
                            FROM trading_accounts t
                            INNER JOIN user_trading_accounts uta ON t.id = uta.account_id
                            WHERE uta.user_id = @user_id AND t.id = @account_id AND t.is_active = 1
                            LIMIT 1";

                        command.Parameters.AddWithValue("@user_id", AppSession.CurrentUserId);
                        command.Parameters.AddWithValue("@account_id", accountId);

                        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                        {
                            if (await reader.ReadAsync(cancellationToken))
                            {
                                return new TradingAccount
                                {
                                    Id = reader.GetInt64("id"),
                                    AccountName = reader.GetString("account_name"),
                                    BinanceAccountId = reader.GetString("binance_account_id"),
                                    ApiKey = reader.GetString("api_key"),
                                    ApiSecret = reader.GetString("api_secret"),
                                    ApiPassphrase = reader.IsDBNull(reader.GetOrdinal("api_passphrase")) ? null : reader.GetString("api_passphrase"),
                                    Equity = reader.GetDecimal("equity"),
                                    InitialEquity = reader.GetDecimal("initial_equity"),
                                    OpportunityCount = reader.GetInt32("opportunity_count"),
                                    Status = reader.GetInt32("status"),
                                    IsActive = reader.GetInt32("is_active"),
                                    CreateTime = reader.GetDateTime("create_time"),
                                    UpdateTime = reader.GetDateTime("update_time"),
                                    IsDefault = reader.GetInt32("is_default"),
                                    IsDefaultAccount = reader.GetInt32("is_default") == 1
                                };
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, "获取交易账户失败");
                throw;
            }
            return null;
        }

        public async Task<bool> CreateTradingAccountAsync(TradingAccount account, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
                    {
                        try
                        {
                            // 插入交易账户
                            var query = @"
                                INSERT INTO trading_accounts 
                                        (account_name, binance_account_id, api_key, api_secret, api_passphrase,
                                         equity, initial_equity, opportunity_count, status, is_active,
                                         create_time, update_time)
                                VALUES 
                                        (@account_name, @binance_account_id, @api_key, @api_secret, @api_passphrase,
                                         @equity, @initial_equity, @opportunity_count, @status, @is_active,
                                         @create_time, @update_time);
                                        SELECT LAST_INSERT_ID();";

                            long accountId;
                            using (var command = new MySqlCommand(query, connection))
                            {
                                command.Transaction = transaction;
                                command.Parameters.AddWithValue("@account_name", account.AccountName);
                                command.Parameters.AddWithValue("@binance_account_id", (object)account.BinanceAccountId ?? DBNull.Value);
                                command.Parameters.AddWithValue("@api_key", (object)account.ApiKey ?? DBNull.Value);
                                command.Parameters.AddWithValue("@api_secret", (object)account.ApiSecret ?? DBNull.Value);
                                command.Parameters.AddWithValue("@api_passphrase", (object)account.ApiPassphrase ?? DBNull.Value);
                                command.Parameters.AddWithValue("@equity", account.Equity);
                                command.Parameters.AddWithValue("@initial_equity", account.InitialEquity);
                                command.Parameters.AddWithValue("@opportunity_count", account.OpportunityCount);
                                command.Parameters.AddWithValue("@status", account.Status);
                                command.Parameters.AddWithValue("@is_active", account.IsActive);
                                command.Parameters.AddWithValue("@create_time", DateTime.Now);
                                command.Parameters.AddWithValue("@update_time", DateTime.Now);

                                accountId = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
                            }

                            // 添加用户账户关联
                            if (accountId > 0 && AppSession.CurrentUserId > 0)
                            {
                                if (account.IsDefault == 1)
                                {
                                    // 先将该用户其它账户的is_default全部置为0
                                    using var clearCmd = connection.CreateCommand();
                                    clearCmd.Transaction = transaction;
                                    clearCmd.CommandText = "UPDATE user_trading_accounts SET is_default=0 WHERE user_id=@userId";
                                    clearCmd.Parameters.AddWithValue("@userId", AppSession.CurrentUserId);
                                    await clearCmd.ExecuteNonQueryAsync(cancellationToken);
                                }

                                // 插入新关联
                                using var cmd = connection.CreateCommand();
                                cmd.Transaction = transaction;
                                cmd.CommandText = @"INSERT INTO user_trading_accounts (user_id, account_id, is_default, create_time, update_time) 
                                                  VALUES (@userId, @accountId, @isDefault, NOW(), NOW())";
                                cmd.Parameters.AddWithValue("@userId", AppSession.CurrentUserId);
                                cmd.Parameters.AddWithValue("@accountId", accountId);
                                cmd.Parameters.AddWithValue("@isDefault", account.IsDefault);
                                await cmd.ExecuteNonQueryAsync(cancellationToken);
                            }

                            await transaction.CommitAsync(cancellationToken);
                            return true;
                        }
                        catch (Exception ex)
                        {
                            LogManager.LogException("Database", ex, $"创建账户 {account.AccountName} 时发生错误，事务已回滚");
                            await transaction.RollbackAsync(cancellationToken);
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, $"创建账户 {account.AccountName} 失败");
                throw;
            }
        }

        public async Task<bool> UpdateTradingAccountAsync(TradingAccount account, CancellationToken cancellationToken = default)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync(cancellationToken);
                using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
                {
                    try
                    {
                        // 更新交易账户
                var query = @"
                    UPDATE trading_accounts 
                    SET account_name = @account_name,
                        binance_account_id = @binance_account_id,
                        api_key = @api_key,
                        api_secret = @api_secret,
                        api_passphrase = @api_passphrase,
                        equity = @equity,
                        initial_equity = @initial_equity,
                        opportunity_count = @opportunity_count,
                        status = @status,
                        is_active = @is_active,
                        update_time = @update_time
                    WHERE id = @id;";

                using (var command = new MySqlCommand(query, connection))
                {
                            command.Transaction = transaction;
                    command.Parameters.AddWithValue("@id", account.Id);
                    command.Parameters.AddWithValue("@account_name", account.AccountName);
                            command.Parameters.AddWithValue("@binance_account_id", (object)account.BinanceAccountId ?? DBNull.Value);
                            command.Parameters.AddWithValue("@api_key", (object)account.ApiKey ?? DBNull.Value);
                            command.Parameters.AddWithValue("@api_secret", (object)account.ApiSecret ?? DBNull.Value);
                            command.Parameters.AddWithValue("@api_passphrase", (object)account.ApiPassphrase ?? DBNull.Value);
                    command.Parameters.AddWithValue("@equity", account.Equity);
                    command.Parameters.AddWithValue("@initial_equity", account.InitialEquity);
                    command.Parameters.AddWithValue("@opportunity_count", account.OpportunityCount);
                    command.Parameters.AddWithValue("@status", account.Status);
                    command.Parameters.AddWithValue("@is_active", account.IsActive);
                    command.Parameters.AddWithValue("@update_time", DateTime.Now);

                            var result = await command.ExecuteNonQueryAsync(cancellationToken);
                            if (result <= 0)
                            {
                                await transaction.RollbackAsync(cancellationToken);
                                return false;
            }
        }

                        // 更新用户账户关联
                        if (AppSession.CurrentUserId > 0)
                        {
                            // 检查是否已存在关联
                            using (var checkCmd = connection.CreateCommand())
                            {
                                checkCmd.Transaction = transaction;
                                checkCmd.CommandText = "SELECT COUNT(*) FROM user_trading_accounts WHERE user_id = @userId AND account_id = @accountId";
                                checkCmd.Parameters.AddWithValue("@userId", AppSession.CurrentUserId);
                                checkCmd.Parameters.AddWithValue("@accountId", account.Id);
                                var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync(cancellationToken)) > 0;

                                if (exists)
                                {
                                    // 如果设置为默认账户，先清除其他默认账户
                                    if (account.IsDefault == 1)
        {
                                        using var clearCmd = connection.CreateCommand();
                                        clearCmd.Transaction = transaction;
                                        clearCmd.CommandText = "UPDATE user_trading_accounts SET is_default=0 WHERE user_id=@userId";
                                        clearCmd.Parameters.AddWithValue("@userId", AppSession.CurrentUserId);
                                        await clearCmd.ExecuteNonQueryAsync(cancellationToken);
                                    }

                                    // 更新现有关联
                                    using var updateCmd = connection.CreateCommand();
                                    updateCmd.Transaction = transaction;
                                    updateCmd.CommandText = "UPDATE user_trading_accounts SET is_default=@isDefault, update_time=NOW() WHERE user_id=@userId AND account_id=@accountId";
                                    updateCmd.Parameters.AddWithValue("@userId", AppSession.CurrentUserId);
                                    updateCmd.Parameters.AddWithValue("@accountId", account.Id);
                                    updateCmd.Parameters.AddWithValue("@isDefault", account.IsDefault);
                                    await updateCmd.ExecuteNonQueryAsync(cancellationToken);
                                }
                                else
                                {
                                    // 如果设置为默认账户，先清除其他默认账户
                                    if (account.IsDefault == 1)
                                    {
                                        using var clearCmd = connection.CreateCommand();
                                        clearCmd.Transaction = transaction;
                                        clearCmd.CommandText = "UPDATE user_trading_accounts SET is_default=0 WHERE user_id=@userId";
                                        clearCmd.Parameters.AddWithValue("@userId", AppSession.CurrentUserId);
                                        await clearCmd.ExecuteNonQueryAsync(cancellationToken);
                                    }

                                    // 插入新关联
                                    using var insertCmd = connection.CreateCommand();
                                    insertCmd.Transaction = transaction;
                                    insertCmd.CommandText = @"INSERT INTO user_trading_accounts (user_id, account_id, is_default, create_time, update_time) 
                                                             VALUES (@userId, @accountId, @isDefault, NOW(), NOW())";
                                    insertCmd.Parameters.AddWithValue("@userId", AppSession.CurrentUserId);
                                    insertCmd.Parameters.AddWithValue("@accountId", account.Id);
                                    insertCmd.Parameters.AddWithValue("@isDefault", account.IsDefault);
                                    await insertCmd.ExecuteNonQueryAsync(cancellationToken);
                                }
                            }
                        }

                        await transaction.CommitAsync(cancellationToken);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        LogManager.LogException("Database", ex, "更新交易账户失败");
                        await transaction.RollbackAsync(cancellationToken);
                        throw;
                    }
                }
            }
        }

        public async Task<bool> DeleteTradingAccountAsync(int accountId, CancellationToken cancellationToken = default)
        {
            return await DeleteTradingAccountAsync((long)accountId, cancellationToken);
        }

        public async Task<bool> DeleteTradingAccountAsync(long accountId, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "DELETE FROM trading_accounts WHERE id = @id";
                    command.Parameters.AddWithValue("@id", accountId);
                        var result = await command.ExecuteNonQueryAsync(cancellationToken);
                    return result > 0;
                }
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, "删除交易账户失败");
                throw;
            }
        }

        public async Task<List<RankingData>> GetRankingDataAsync(DateTime startDate, DateTime endDate)
        {
            var result = new List<RankingData>();
            var connectionString = _configService.GetCurrentConnectionString();

            try
            {
                using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();

                var sql = @"
                    SELECT 
                        id,
                        symbol,
                        last_price,
                        change_rate,
                        amount_24h,
                        ranking,
                        timestamp,
                        record_time,
                        created_at,
                        volume
                    FROM realtime_ranking_history
                    WHERE DATE(record_time) BETWEEN @StartDate AND @EndDate
                    AND ranking <= 30  -- 只获取前30名
                    ORDER BY record_time DESC, ranking ASC";

                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@StartDate", startDate.Date);
                command.Parameters.AddWithValue("@EndDate", endDate.Date);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    result.Add(new RankingData
                    {
                        Id = reader.GetInt64("id"),
                        Symbol = reader.GetString("symbol"),
                        LastPrice = reader.GetDecimal("last_price"),
                        ChangeRate = reader.GetDecimal("change_rate"),
                        Amount24h = reader.GetDecimal("amount_24h"),
                        Ranking = reader.GetInt32("ranking"),
                        Timestamp = reader.GetInt64("timestamp"),
                        RecordTime = reader.GetDateTime("record_time"),
                        CreatedAt = reader.GetDateTime("created_at"),
                        Volume = reader.GetDecimal("volume")
                    });
                }

                LogManager.Log("Database", $"成功获取排行榜数据，共 {result.Count} 条记录");
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, "获取排行榜数据失败");
                throw;
            }

            return result;
        }

        public async Task<List<DailyRanking>> GetDailyRankingDataAsync(DateTime startDate, DateTime endDate)
        {
            var result = new List<DailyRanking>();
            var connectionString = _configService.GetCurrentConnectionString();

            try
            {
                LogManager.Log("Database", $"开始查询每日排行榜数据，日期范围：{startDate.Date:yyyy-MM-dd} 至 {endDate.Date:yyyy-MM-dd}");
                
                using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();
                
                LogManager.Log("Database", "数据库连接已建立");

                // 首先检查表是否存在
                var checkTableSql = @"
                    SELECT COUNT(*) 
                    FROM information_schema.tables 
                    WHERE table_schema = DATABASE() 
                    AND table_name = 'daily_ranking'";
                    
                using var checkCommand = new MySqlCommand(checkTableSql, connection);
                var tableExists = Convert.ToInt32(await checkCommand.ExecuteScalarAsync()) > 0;
                
                LogManager.Log("Database", $"daily_ranking 表存在: {tableExists}");
                
                if (!tableExists)
                {
                    LogManager.Log("Database", "daily_ranking 表不存在，返回空结果");
                    return result;
                }

                // 检查表中的总记录数
                var countSql = "SELECT COUNT(*) FROM daily_ranking";
                using var countCommand = new MySqlCommand(countSql, connection);
                var totalRecords = Convert.ToInt32(await countCommand.ExecuteScalarAsync());
                LogManager.Log("Database", $"daily_ranking 表中总共有 {totalRecords} 条记录");

                var sql = @"
                    SELECT 
                        id,
                        date,
                        top_gainers,
                        top_losers,
                        created_at,
                        updated_at
                    FROM daily_ranking
                    WHERE date BETWEEN @StartDate AND @EndDate
                    ORDER BY date DESC";

                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@StartDate", startDate.Date);
                command.Parameters.AddWithValue("@EndDate", endDate.Date);
                
                LogManager.Log("Database", $"执行SQL查询: {sql}");
                LogManager.Log("Database", $"参数: StartDate={startDate.Date:yyyy-MM-dd}, EndDate={endDate.Date:yyyy-MM-dd}");

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var dailyRanking = new DailyRanking
                    {
                        Id = reader.GetInt32("id"),
                        Date = reader.GetDateTime("date"),
                        TopGainers = reader.GetString("top_gainers"),
                        TopLosers = reader.GetString("top_losers"),
                        CreatedAt = reader.GetDateTime("created_at"),
                        UpdatedAt = reader.GetDateTime("updated_at")
                    };
                    
                    LogManager.Log("Database", $"读取记录: ID={dailyRanking.Id}, Date={dailyRanking.Date:yyyy-MM-dd}");
                    LogManager.Log("Database", $"TopGainers长度: {dailyRanking.TopGainers?.Length ?? 0}");
                    LogManager.Log("Database", $"TopLosers长度: {dailyRanking.TopLosers?.Length ?? 0}");
                    
                    result.Add(dailyRanking);
                }

                LogManager.Log("Database", $"成功获取每日排行榜数据，共 {result.Count} 条记录");
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, "获取每日排行榜数据失败");
                throw;
            }

            return result;
        }

        public async Task<PositionPushInfo> GetOpenPushInfoAsync(long accountId, string contract)
        {
            await EnsureConnectionStringLoadedAsync();
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM position_push_info WHERE account_id=@accountId AND contract=@contract AND status='open' LIMIT 1";
            command.Parameters.AddWithValue("@accountId", accountId);
            command.Parameters.AddWithValue("@contract", contract);
            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new PositionPushInfo
                {
                    Id = reader.GetInt64("id"),
                    Contract = reader.GetString("contract"),
                    AccountId = reader.GetInt64("account_id"),
                    Status = reader.GetString("status"),
                    CreateTime = reader.GetDateTime("create_time"),
                    CloseTime = reader.IsDBNull("close_time") ? (DateTime?)null : reader.GetDateTime("close_time")
                };
            }
            return null;
        }

        public async Task<PositionPushInfo> CreatePushInfoAsync(long accountId, string contract)
        {
            await EnsureConnectionStringLoadedAsync();
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO position_push_info (account_id, contract, status, create_time) VALUES (@accountId, @contract, 'open', NOW()); SELECT LAST_INSERT_ID();";
            command.Parameters.AddWithValue("@accountId", accountId);
            command.Parameters.AddWithValue("@contract", contract);
            var id = Convert.ToInt64(await command.ExecuteScalarAsync());
            return new PositionPushInfo { Id = id, AccountId = accountId, Contract = contract, Status = "open", CreateTime = DateTime.Now };
        }

        public async Task<long> InsertSimulationOrderAsync(SimulationOrder order)
        {
            await EnsureConnectionStringLoadedAsync();
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            
            using var transaction = await connection.BeginTransactionAsync();
            try
            {
                // 1. 计算初始止损金额（real_pnl）
                decimal realPnL = 0m;
                if (order.Direction.ToLower() == "buy")
                {
                    // 做多：实际盈亏 = (止损价 - 开仓价) * 数量 * 合约面值（止损时的亏损金额，通常为负值）
                    realPnL = (order.InitialStopLoss - order.EntryPrice) * (decimal)order.Quantity * order.ContractSize;
                }
                else if (order.Direction.ToLower() == "sell")
                {
                    // 做空：实际盈亏 = (开仓价 - 止损价) * 数量 * 合约面值（止损时的亏损金额，通常为负值）
                    realPnL = (order.EntryPrice - order.InitialStopLoss) * (decimal)order.Quantity * order.ContractSize;
                }
                
                // 2. 插入订单
                long orderId;
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
                        INSERT INTO simulation_orders (
                            order_id, account_id, contract, contract_size,
                            direction, quantity, entry_price, initial_stop_loss,
                            current_stop_loss, leverage, margin, total_value,
                            status, open_time, real_profit, floating_pnl, current_price, last_update_time
                        ) VALUES (
                            @order_id, @account_id, @contract, @contract_size,
                            @direction, @quantity, @entry_price, @initial_stop_loss,
                            @current_stop_loss, @leverage, @margin, @total_value,
                            @status, @open_time, @real_profit, 0, @current_price, NOW()
                        );
                SELECT LAST_INSERT_ID();";

                    command.Parameters.AddWithValue("@order_id", order.OrderId);
                    command.Parameters.AddWithValue("@account_id", order.AccountId);
                    command.Parameters.AddWithValue("@contract", order.Contract);
                    command.Parameters.AddWithValue("@contract_size", order.ContractSize);
                    command.Parameters.AddWithValue("@direction", order.Direction);
                    command.Parameters.AddWithValue("@quantity", order.Quantity);
                    command.Parameters.AddWithValue("@entry_price", order.EntryPrice);
                    command.Parameters.AddWithValue("@initial_stop_loss", order.InitialStopLoss);
                    command.Parameters.AddWithValue("@current_stop_loss", order.CurrentStopLoss);
                    command.Parameters.AddWithValue("@leverage", order.Leverage);
                    command.Parameters.AddWithValue("@margin", order.Margin);
                    command.Parameters.AddWithValue("@total_value", order.TotalValue);
                    command.Parameters.AddWithValue("@status", order.Status);
                    command.Parameters.AddWithValue("@open_time", order.OpenTime);
                    command.Parameters.AddWithValue("@real_profit", realPnL);
                    command.Parameters.AddWithValue("@current_price", order.EntryPrice);

                    orderId = Convert.ToInt64(await command.ExecuteScalarAsync());
                }

                // 3. 创建止损委托单（如果设置了止损价格）
                if (order.InitialStopLoss > 0)
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = @"
                            INSERT INTO stop_take_orders 
                            (account_id, simulation_order_id, symbol, order_type, direction, quantity, trigger_price, 
                             working_type, status, create_time, update_time)
                            VALUES 
                            (@account_id, @simulation_order_id, @symbol, @order_type, @direction, @quantity, @trigger_price, 
                             @working_type, @status, NOW(), NOW())";
                        
                        // 确定止损单的方向：与开仓方向相反
                        string stopLossDirection = order.Direction.ToLower() == "buy" ? "SELL" : "BUY";
                        
                        command.Parameters.AddWithValue("@account_id", order.AccountId);
                        command.Parameters.AddWithValue("@simulation_order_id", orderId);
                        command.Parameters.AddWithValue("@symbol", order.Contract);
                        command.Parameters.AddWithValue("@order_type", "STOP_LOSS");
                        command.Parameters.AddWithValue("@direction", stopLossDirection);
                        command.Parameters.AddWithValue("@quantity", (decimal)order.Quantity);
                        command.Parameters.AddWithValue("@trigger_price", order.InitialStopLoss);
                        command.Parameters.AddWithValue("@working_type", "MARK_PRICE");
                        command.Parameters.AddWithValue("@status", "WAITING");
                        
                        await command.ExecuteNonQueryAsync();
                        
                        LogManager.Log("Database", $"创建止损委托单成功 - 订单ID: {orderId}, 合约: {order.Contract}, 方向: {stopLossDirection}, 触发价: {order.InitialStopLoss}");
                    }
                }

                // 4. 检查是否已存在推仓信息
                long pushId;
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
                        SELECT id FROM position_push_info 
                        WHERE account_id = @account_id 
                        AND contract = @contract 
                        AND status = 'open' 
                        LIMIT 1";
                    
                    command.Parameters.AddWithValue("@account_id", order.AccountId);
                    command.Parameters.AddWithValue("@contract", order.Contract);
                    
                    var existingPushId = await command.ExecuteScalarAsync();
                    if (existingPushId != null)
                    {
                        pushId = Convert.ToInt64(existingPushId);
                    }
                    else
                    {
                        // 5. 创建新的推仓信息
                        command.CommandText = @"
                            INSERT INTO position_push_info 
                            (account_id, contract, status, create_time) 
                            VALUES 
                            (@account_id, @contract, 'open', NOW());
                            SELECT LAST_INSERT_ID();";
                        
                        pushId = Convert.ToInt64(await command.ExecuteScalarAsync());
                    }
                }

                // 6. 创建推仓与订单关联
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
                        INSERT INTO position_push_order_rel 
                        (push_id, order_id, create_time) 
                        VALUES 
                        (@push_id, @order_id, NOW())";
                    
                    command.Parameters.AddWithValue("@push_id", pushId);
                    command.Parameters.AddWithValue("@order_id", orderId);
                    
                    await command.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
                LogManager.Log("Database", $"订单创建完成 - 订单ID: {orderId}, 推仓ID: {pushId}");
                return orderId;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                LogManager.LogException("Database", ex, "创建订单和推仓信息失败");
                throw;
            }
        }

        public async Task InsertPushOrderRelAsync(long pushId, long orderId)
        {
            await EnsureConnectionStringLoadedAsync();
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO position_push_order_rel (push_id, order_id, create_time) VALUES (@pushId, @orderId, NOW())";
            command.Parameters.AddWithValue("@pushId", pushId);
            command.Parameters.AddWithValue("@orderId", orderId);
            await command.ExecuteNonQueryAsync();
        }

        public async Task AddUserTradingAccountAsync(long userId, long accountId, bool isDefault, CancellationToken cancellationToken = default)
        {
            await EnsureConnectionStringLoadedAsync();
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync(cancellationToken);
                using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
                {
            try
            {
                if (isDefault)
                {
                    // 先将该用户其它账户的is_default全部置为0
                            using (var clearCmd = connection.CreateCommand())
                            {
                    clearCmd.Transaction = transaction;
                    clearCmd.CommandText = "UPDATE user_trading_accounts SET is_default=0 WHERE user_id=@userId";
                    clearCmd.Parameters.AddWithValue("@userId", userId);
                                await clearCmd.ExecuteNonQueryAsync(cancellationToken);
                }
                        }

                // 插入新关联
                        using (var cmd = connection.CreateCommand())
                        {
                cmd.Transaction = transaction;
                            cmd.CommandText = @"INSERT INTO user_trading_accounts (user_id, account_id, is_default, create_time, update_time) 
                                              VALUES (@userId, @accountId, @isDefault, NOW(), NOW())";
                cmd.Parameters.AddWithValue("@userId", userId);
                cmd.Parameters.AddWithValue("@accountId", accountId);
                cmd.Parameters.AddWithValue("@isDefault", isDefault ? 1 : 0);
                            await cmd.ExecuteNonQueryAsync(cancellationToken);
                        }

                        await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                        await transaction.RollbackAsync(cancellationToken);
                throw;
                    }
                }
            }
        }

        public async Task SetUserDefaultAccountAsync(long userId, long accountId, CancellationToken cancellationToken = default)
        {
            await EnsureConnectionStringLoadedAsync();
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync(cancellationToken);
                using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
                {
            try
            {
                // 先全部清零
                        using (var clearCmd = connection.CreateCommand())
                        {
                clearCmd.Transaction = transaction;
                clearCmd.CommandText = "UPDATE user_trading_accounts SET is_default=0 WHERE user_id=@userId";
                clearCmd.Parameters.AddWithValue("@userId", userId);
                            await clearCmd.ExecuteNonQueryAsync(cancellationToken);
                        }

                // 再设置目标为1
                        using (var setCmd = connection.CreateCommand())
                        {
                setCmd.Transaction = transaction;
                setCmd.CommandText = "UPDATE user_trading_accounts SET is_default=1 WHERE user_id=@userId AND account_id=@accountId";
                setCmd.Parameters.AddWithValue("@userId", userId);
                setCmd.Parameters.AddWithValue("@accountId", accountId);
                            await setCmd.ExecuteNonQueryAsync(cancellationToken);
                        }

                        await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                        await transaction.RollbackAsync(cancellationToken);
                throw;
            }
                }
            }
        }

        public async Task<List<SimulationOrder>> GetSimulationOrdersAsync(int accountId, CancellationToken cancellationToken = default)
        {
            var orders = new List<SimulationOrder>();
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync(cancellationToken);
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT * FROM simulation_orders 
                        WHERE account_id = @account_id 
                        ORDER BY open_time DESC";

                    command.Parameters.AddWithValue("@account_id", accountId);

                    using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                    {
                        while (await reader.ReadAsync(cancellationToken))
                        {
                            orders.Add(new SimulationOrder
                            {
                                Id = reader.GetInt64("id"),
                                OrderId = reader.GetString("order_id"),
                                AccountId = reader.GetInt64("account_id"),
                                Contract = reader.GetString("contract"),
                                ContractSize = reader.GetDecimal("contract_size"),
                                Direction = reader.GetString("direction"),
                                Quantity = reader.GetInt32("quantity"),
                                EntryPrice = reader.GetDecimal("entry_price"),
                                InitialStopLoss = reader.GetDecimal("initial_stop_loss"),
                                CurrentStopLoss = reader.GetDecimal("current_stop_loss"),
                                HighestPrice = reader.IsDBNull(reader.GetOrdinal("highest_price")) ? null : (decimal?)reader.GetDecimal("highest_price"),
                                MaxFloatingProfit = reader.IsDBNull(reader.GetOrdinal("max_floating_profit")) ? null : (decimal?)reader.GetDecimal("max_floating_profit"),
                                Leverage = reader.GetInt32("leverage"),
                                Margin = reader.GetDecimal("margin"),
                                TotalValue = reader.GetDecimal("total_value"),
                                Status = reader.GetString("status"),
                                OpenTime = reader.GetDateTime("open_time"),
                                CloseTime = reader.IsDBNull(reader.GetOrdinal("close_time")) ? null : (DateTime?)reader.GetDateTime("close_time"),
                                ClosePrice = reader.IsDBNull(reader.GetOrdinal("close_price")) ? null : (decimal?)reader.GetDecimal("close_price"),
                                RealizedProfit = reader.IsDBNull(reader.GetOrdinal("realized_profit")) ? null : (decimal?)reader.GetDecimal("realized_profit"),
                                CloseType = reader.IsDBNull(reader.GetOrdinal("close_type")) ? null : reader.GetString("close_type"),
                                RealProfit = reader.IsDBNull(reader.GetOrdinal("real_profit")) ? null : (decimal?)reader.GetDecimal("real_profit"),
                                FloatingPnL = reader.IsDBNull(reader.GetOrdinal("floating_pnl")) ? null : (decimal?)reader.GetDecimal("floating_pnl"),
                                CurrentPrice = reader.IsDBNull(reader.GetOrdinal("current_price")) ? null : (decimal?)reader.GetDecimal("current_price"),
                                LastUpdateTime = reader.IsDBNull(reader.GetOrdinal("last_update_time")) ? null : (DateTime?)reader.GetDateTime("last_update_time")
                            });
                        }
                    }
                }
            }
            return orders;
        }

        public async Task<bool> ConnectAsync(string connectionString, CancellationToken cancellationToken = default)
        {
            try
            {
                _connectionString = connectionString;
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    LogManager.Log("Database", "数据库连接成功");
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, "数据库连接失败");
                return false;
            }
        }

        public async Task<bool> DisconnectAsync()
        {
            try
            {
                _connectionString = null;
                LogManager.Log("Database", "数据库连接已断开");
                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, "断开数据库连接时发生错误");
                return await Task.FromResult(false);
            }
        }

        public async Task<List<DatabaseInfo>> GetDatabasesAsync(CancellationToken cancellationToken = default)
        {
            var databases = new List<DatabaseInfo>();
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SHOW DATABASES";
                        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                        {
                            while (await reader.ReadAsync(cancellationToken))
                            {
                                var dbName = reader.GetString(0);
                                // 排除系统数据库
                                if (!dbName.Equals("information_schema", StringComparison.OrdinalIgnoreCase) &&
                                    !dbName.Equals("mysql", StringComparison.OrdinalIgnoreCase) &&
                                    !dbName.Equals("performance_schema", StringComparison.OrdinalIgnoreCase) &&
                                    !dbName.Equals("sys", StringComparison.OrdinalIgnoreCase))
                                {
                                    databases.Add(new DatabaseInfo
                                    {
                                        Name = dbName,
                                        Description = $"数据库 {dbName}",
                                        IsSelected = false
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, "获取数据库列表失败");
                throw;
            }
            return databases;
        }

        public async Task<bool> AddTradingAccountAsync(TradingAccount account, CancellationToken cancellationToken = default)
        {
            return await CreateTradingAccountAsync(account, cancellationToken);
        }

        public async Task<bool> DeleteSimulationOrderAsync(int orderId, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "DELETE FROM simulation_orders WHERE id = @id";
                        command.Parameters.AddWithValue("@id", orderId);
                        var result = await command.ExecuteNonQueryAsync(cancellationToken);
                        return result > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, "删除模拟订单失败");
                throw;
            }
        }

        public async Task<bool> UpdateSimulationOrderAsync(SimulationOrder order, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            UPDATE simulation_orders SET
                                current_stop_loss = @current_stop_loss,
                                highest_price = @highest_price,
                                max_floating_profit = @max_floating_profit,
                                status = @status,
                                close_time = @close_time,
                                close_price = @close_price,
                                realized_profit = @realized_profit,
                                close_type = @close_type,
                                real_profit = @real_profit,
                                floating_pnl = @floating_pnl,
                                current_price = @current_price,
                                last_update_time = @last_update_time
                            WHERE id = @id";

                        command.Parameters.AddWithValue("@id", order.Id);
                        command.Parameters.AddWithValue("@current_stop_loss", order.CurrentStopLoss);
                        command.Parameters.AddWithValue("@highest_price", (object)order.HighestPrice ?? DBNull.Value);
                        command.Parameters.AddWithValue("@max_floating_profit", (object)order.MaxFloatingProfit ?? DBNull.Value);
                        command.Parameters.AddWithValue("@status", order.Status);
                        command.Parameters.AddWithValue("@close_time", (object)order.CloseTime ?? DBNull.Value);
                        command.Parameters.AddWithValue("@close_price", (object)order.ClosePrice ?? DBNull.Value);
                        command.Parameters.AddWithValue("@realized_profit", (object)order.RealizedProfit ?? DBNull.Value);
                        command.Parameters.AddWithValue("@close_type", (object)order.CloseType ?? DBNull.Value);
                        command.Parameters.AddWithValue("@real_profit", (object)order.RealProfit ?? DBNull.Value);
                        command.Parameters.AddWithValue("@floating_pnl", (object)order.FloatingPnL ?? DBNull.Value);
                        command.Parameters.AddWithValue("@current_price", (object)order.CurrentPrice ?? DBNull.Value);
                        command.Parameters.AddWithValue("@last_update_time", DateTime.Now);

                        var result = await command.ExecuteNonQueryAsync(cancellationToken);
                        return result > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, "更新模拟订单失败");
                throw;
            }
        }

        public async Task<bool> AddSimulationOrderAsync(SimulationOrder order, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
                    {
                        try
                        {
                            using (var command = connection.CreateCommand())
                            {
                                command.Transaction = transaction;
                                command.CommandText = @"
                                    INSERT INTO simulation_orders (
                                        order_id, account_id, contract, contract_size,
                                        direction, quantity, entry_price, initial_stop_loss,
                                        current_stop_loss, leverage, margin, total_value,
                                        status, open_time, real_profit
                                    ) VALUES (
                                        @order_id, @account_id, @contract, @contract_size,
                                        @direction, @quantity, @entry_price, @initial_stop_loss,
                                        @current_stop_loss, @leverage, @margin, @total_value,
                                        @status, @open_time, @real_profit
                                    )";

                                    // 添加参数
                                    command.Parameters.AddWithValue("@order_id", order.OrderId);
                                    command.Parameters.AddWithValue("@account_id", order.AccountId);
                                    command.Parameters.AddWithValue("@contract", order.Contract);
                                    command.Parameters.AddWithValue("@contract_size", order.ContractSize);
                                    command.Parameters.AddWithValue("@direction", order.Direction);
                                    command.Parameters.AddWithValue("@quantity", order.Quantity);
                                    command.Parameters.AddWithValue("@entry_price", order.EntryPrice);
                                    command.Parameters.AddWithValue("@initial_stop_loss", order.InitialStopLoss);
                                    command.Parameters.AddWithValue("@current_stop_loss", order.CurrentStopLoss);
                                    command.Parameters.AddWithValue("@leverage", order.Leverage);
                                    command.Parameters.AddWithValue("@margin", order.Margin);
                                    command.Parameters.AddWithValue("@total_value", order.TotalValue);
                                    command.Parameters.AddWithValue("@status", order.Status);
                                    command.Parameters.AddWithValue("@open_time", order.OpenTime);
                                    command.Parameters.AddWithValue("@real_profit", DBNull.Value); // 添加real_profit字段，初始化为NULL

                                    await command.ExecuteNonQueryAsync(cancellationToken);
                            }

                            await transaction.CommitAsync(cancellationToken);
                            return true;
                        }
                        catch
                        {
                            await transaction.RollbackAsync(cancellationToken);
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, "添加模拟订单失败");
                throw;
            }
        }

        public async Task<PushSummaryInfo> GetPushSummaryInfoAsync(long accountId, string contract)
        {
            try
            {
                LogManager.Log("Database", $"开始获取推仓信息 - 账户ID: {accountId}, 合约: {contract}");
                _logger?.LogInformation("=== 数据库服务：开始获取推仓信息 ===");
                _logger?.LogInformation("账户ID: {accountId}", accountId);
                _logger?.LogInformation("查询合约: '{contract}' (长度: {length})", contract, contract?.Length ?? 0);
                
                // 添加调试：先查询数据库中所有的推仓记录
                await EnsureConnectionStringLoadedAsync();
                using (var debugConnection = new MySqlConnection(_connectionString))
                {
                    await debugConnection.OpenAsync();
                    using (var debugCommand = debugConnection.CreateCommand())
                    {
                        debugCommand.CommandText = "SELECT DISTINCT contract FROM position_push_info WHERE account_id = @accountId ORDER BY contract";
                        debugCommand.Parameters.AddWithValue("@accountId", accountId);
                        
                        LogManager.Log("Database", $"调试：查询账户 {accountId} 的所有推仓合约");
                        _logger?.LogInformation("查询账户 {accountId} 的所有推仓合约", accountId);
                        
                        using (var debugReader = await debugCommand.ExecuteReaderAsync())
                        {
                            var contracts = new List<string>();
                            while (await debugReader.ReadAsync())
                            {
                                contracts.Add(debugReader.GetString("contract"));
                            }
                            LogManager.Log("Database", $"调试：找到的推仓合约: [{string.Join(", ", contracts)}]");
                            LogManager.Log("Database", $"调试：查询的合约名称: '{contract}' (长度: {contract?.Length ?? 0})");
                            
                            _logger?.LogInformation("数据库中找到的推仓合约: [{contracts}]", string.Join(", ", contracts));
                            _logger?.LogInformation("当前查询的合约名称: '{contract}' (长度: {length})", contract, contract?.Length ?? 0);
                        }
                    }
                }
                
                PushSummaryInfo summary = null;
                
                // 第一个连接：获取推仓基本信息
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    SELECT p.*, 
                           COUNT(o.id) as total_orders,
                           SUM(CASE WHEN o.status = 'open' THEN 1 ELSE 0 END) as open_orders,
                           SUM(CASE WHEN o.status = 'closed' THEN 1 ELSE 0 END) as closed_orders,
                                   SUM(COALESCE(o.floating_pnl, 0)) as total_floating_pnl,
                                   SUM(COALESCE(o.real_profit, 0)) as total_real_pnl
                    FROM position_push_info p
                    LEFT JOIN position_push_order_rel r ON p.id = r.push_id
                    LEFT JOIN simulation_orders o ON r.order_id = o.id
                    WHERE p.account_id = @accountId 
                    AND p.contract = @contract 
                    AND p.status = 'open'
                            GROUP BY p.id, p.contract, p.account_id, p.create_time, p.status, p.close_time";

                command.Parameters.AddWithValue("@accountId", accountId);
                command.Parameters.AddWithValue("@contract", contract);

                        LogManager.Log("Database", $"执行推仓信息查询: {command.CommandText}");
                        LogManager.Log("Database", $"查询参数: accountId={accountId}, contract='{contract}'");
                        
                        _logger?.LogInformation("执行推仓信息查询");
                        _logger?.LogInformation("SQL: {sql}", command.CommandText);
                        _logger?.LogInformation("查询参数: accountId={accountId}, contract='{contract}'", accountId, contract);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (!await reader.ReadAsync())
                            {
                                LogManager.Log("Database", $"未找到推仓信息 - 账户ID: {accountId}, 合约: {contract}");
                                _logger?.LogInformation("未找到推仓信息 - 账户ID: {accountId}, 合约: {contract}", accountId, contract);
                                
                                // 尝试模糊查询，看看是否有类似的合约名称
                                using (var fuzzyConnection = new MySqlConnection(_connectionString))
                                {
                                    await fuzzyConnection.OpenAsync();
                                    using (var fuzzyCommand = fuzzyConnection.CreateCommand())
                                    {
                                        fuzzyCommand.CommandText = @"
                                            SELECT contract 
                                            FROM position_push_info 
                                            WHERE account_id = @accountId 
                                            AND (contract LIKE @contractPattern1 OR contract LIKE @contractPattern2)
                                            ORDER BY contract";
                                        
                                        fuzzyCommand.Parameters.AddWithValue("@accountId", accountId);
                                        fuzzyCommand.Parameters.AddWithValue("@contractPattern1", $"%{contract}%");
                                        fuzzyCommand.Parameters.AddWithValue("@contractPattern2", $"{contract}%");
                                        
                                        using (var fuzzyReader = await fuzzyCommand.ExecuteReaderAsync())
                                        {
                                            var similarContracts = new List<string>();
                                            while (await fuzzyReader.ReadAsync())
                                            {
                                                similarContracts.Add(fuzzyReader.GetString("contract"));
                                            }
                                            if (similarContracts.Any())
                                            {
                                                LogManager.Log("Database", $"找到相似的合约名称: [{string.Join(", ", similarContracts)}]");
                                                _logger?.LogInformation("找到相似的合约名称: [{contracts}]", string.Join(", ", similarContracts));
                                            }
                                            else
                                            {
                                                LogManager.Log("Database", "未找到任何相似的合约名称");
                                                _logger?.LogInformation("未找到任何相似的合约名称");
                                            }
                                        }
                                    }
                                }
                                
                                // 没有找到开放的推仓记录，返回null
                                return null;
                            }

                            LogManager.Log("Database", "成功读取推仓基本信息");
                            _logger?.LogInformation("成功读取推仓基本信息");
                            
                            var pushId = reader.GetInt64("id");
                            var totalOrders = reader.GetInt32("total_orders");
                            var openOrders = reader.GetInt32("open_orders");
                            
                            _logger?.LogInformation("推仓ID: {pushId}, 总订单数: {totalOrders}, 开仓订单数: {openOrders}", 
                                pushId, totalOrders, openOrders);
                            
                            summary = new PushSummaryInfo
                            {
                                PushId = pushId,
                                Contract = reader.GetString("contract"),
                                CreateTime = reader.GetDateTime("create_time"),
                                Status = reader.GetString("status"),
                                TotalOrderCount = totalOrders,
                                OpenOrderCount = openOrders,
                                ClosedOrderCount = reader.GetInt32("closed_orders"),
                                TotalFloatingPnL = reader.IsDBNull(reader.GetOrdinal("total_floating_pnl")) ? 0m : reader.GetDecimal("total_floating_pnl"),
                                TotalRealPnL = reader.IsDBNull(reader.GetOrdinal("total_real_pnl")) ? 0m : reader.GetDecimal("total_real_pnl"),
                                Orders = new List<SimulationOrder>()
                            };
                        }
                    }
                }

                if (summary == null)
                {
                    throw new Exception("获取推仓信息失败：summary 对象为空");
                }

                // 第二个连接：获取订单详情
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    SELECT o.* 
                    FROM simulation_orders o
                    INNER JOIN position_push_order_rel r ON o.id = r.order_id
                    WHERE r.push_id = @pushId
                    ORDER BY o.open_time DESC";

                command.Parameters.AddWithValue("@pushId", summary.PushId);
                        LogManager.Log("Database", $"执行订单查询: pushId={summary.PushId}");
                        _logger?.LogInformation("执行订单查询: pushId={pushId}", summary.PushId);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                while (await reader.ReadAsync())
                {
                    var order = new SimulationOrder
                    {
                        Id = reader.GetInt64("id"),
                        OrderId = reader.GetString("order_id"),
                        AccountId = reader.GetInt64("account_id"),
                        Contract = reader.GetString("contract"),
                        ContractSize = reader.GetDecimal("contract_size"),
                        Direction = reader.GetString("direction"),
                                    Quantity = reader.GetFloat("quantity"), // 使用 GetFloat 读取数量
                        EntryPrice = reader.GetDecimal("entry_price"),
                        InitialStopLoss = reader.GetDecimal("initial_stop_loss"),
                        CurrentStopLoss = reader.GetDecimal("current_stop_loss"),
                        HighestPrice = reader.IsDBNull(reader.GetOrdinal("highest_price")) ? null : (decimal?)reader.GetDecimal("highest_price"),
                        MaxFloatingProfit = reader.IsDBNull(reader.GetOrdinal("max_floating_profit")) ? null : (decimal?)reader.GetDecimal("max_floating_profit"),
                        Leverage = reader.GetInt32("leverage"),
                        Margin = reader.GetDecimal("margin"),
                        TotalValue = reader.GetDecimal("total_value"),
                        Status = reader.GetString("status"),
                        OpenTime = reader.GetDateTime("open_time"),
                        CloseTime = reader.IsDBNull(reader.GetOrdinal("close_time")) ? null : (DateTime?)reader.GetDateTime("close_time"),
                        ClosePrice = reader.IsDBNull(reader.GetOrdinal("close_price")) ? null : (decimal?)reader.GetDecimal("close_price"),
                                    RealizedProfit = reader.IsDBNull(reader.GetOrdinal("realized_profit")) ? 0m : reader.GetDecimal("realized_profit"),
                        CloseType = reader.IsDBNull(reader.GetOrdinal("close_type")) ? null : reader.GetString("close_type"),
                                    RealProfit = reader.IsDBNull(reader.GetOrdinal("real_profit")) ? 0m : reader.GetDecimal("real_profit"),
                                    FloatingPnL = reader.IsDBNull(reader.GetOrdinal("floating_pnl")) ? 0m : reader.GetDecimal("floating_pnl"),
                        CurrentPrice = reader.IsDBNull(reader.GetOrdinal("current_price")) ? null : (decimal?)reader.GetDecimal("current_price"),
                        LastUpdateTime = reader.IsDBNull(reader.GetOrdinal("last_update_time")) ? null : (DateTime?)reader.GetDateTime("last_update_time")
                    };
                    summary.Orders.Add(order);
                                LogManager.Log("Database", $"成功读取订单: ID={order.Id}, 状态={order.Status}, 数量={order.Quantity}");
                            }
                        }
                    }
                }

                // 第三个连接：计算可用风险金（新的计算规则）
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // 第一步：获取账户基本信息
                    decimal equity = 0m;
                    int opportunityCount = 0;
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            SELECT equity, opportunity_count 
                            FROM trading_accounts 
                            WHERE id = @accountId";

                        command.Parameters.AddWithValue("@accountId", accountId);
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                equity = reader.GetDecimal("equity");
                                opportunityCount = reader.GetInt32("opportunity_count");
                                LogManager.Log("Database", $"获取账户信息 - 权益: {equity}, 机会次数: {opportunityCount}");
                            }
                        }
                    }
                    
                    // 第二步：计算单笔可用风险金
                    decimal singleRiskAmount = opportunityCount > 0 ? equity / opportunityCount : 0;
                    LogManager.Log("Database", $"单笔可用风险金: {singleRiskAmount}");
                    
                    // 第三步：获取该合约所有推仓关联订单的累加实际盈亏
                    decimal totalRealProfit = 0m;
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            SELECT SUM(COALESCE(o.real_profit, 0)) as total_real_profit
                            FROM simulation_orders o
                            INNER JOIN position_push_order_rel r ON o.id = r.order_id
                            INNER JOIN position_push_info p ON r.push_id = p.id
                            WHERE p.account_id = @accountId 
                            AND p.contract = @contract";

                        command.Parameters.AddWithValue("@accountId", accountId);
                        command.Parameters.AddWithValue("@contract", contract);
                        
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync() && !reader.IsDBNull(0))
                            {
                                totalRealProfit = reader.GetDecimal("total_real_profit");
                            }
                        }
                    }
                    
                    LogManager.Log("Database", $"该合约累加实际盈亏: {totalRealProfit}");
                    
                    // 第四步：计算当前可用风险金 = 单笔可用风险金 + 累加实际盈亏
                    summary.AvailableRiskAmount = singleRiskAmount + totalRealProfit;
                    
                    // 设置计算详情属性
                    summary.SingleRiskAmount = singleRiskAmount;
                    summary.AccumulatedRealProfit = totalRealProfit;
                    
                    LogManager.Log("Database", $"当前可用风险金计算: {singleRiskAmount} + {totalRealProfit} = {summary.AvailableRiskAmount}");
                }

                // 计算占用风险金
            summary.RiskAmount = summary.Orders.Where(o => o.Status == "open").Sum(o => o.Margin);
                LogManager.Log("Database", $"计算占用风险金: {summary.RiskAmount}");

                LogManager.Log("Database", "成功获取推仓信息");
                _logger?.LogInformation("=== 数据库服务：推仓信息获取完成 ===");
                _logger?.LogInformation("最终结果 - 推仓ID: {pushId}, 订单数: {orderCount}", 
                    summary.PushId, summary.Orders?.Count ?? 0);
            return summary;
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, "获取推仓信息失败");
                _logger?.LogError(ex, "获取推仓信息失败 - 账户ID: {accountId}, 合约: {contract}", accountId, contract);
                throw;
            }
        }

        public async Task<decimal> GetAccountAvailableRiskAmountAsync(long accountId)
        {
            await EnsureConnectionStringLoadedAsync();
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT equity, opportunity_count 
                FROM trading_accounts 
                WHERE id = @accountId";

            command.Parameters.AddWithValue("@accountId", accountId);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var equity = reader.GetDecimal("equity");
                var opportunityCount = reader.GetInt32("opportunity_count");
                return opportunityCount > 0 ? equity / opportunityCount : 0;
            }

            return 0;
        }

        /// <summary>
        /// 获取指定合约的可用风险金（新的计算规则）
        /// 计算规则：当前可用风险金 = 账户单笔可用风险金 + 该合约累加实际盈亏
        /// </summary>
        public async Task<decimal> GetContractAvailableRiskAmountAsync(long accountId, string contract)
        {
            await EnsureConnectionStringLoadedAsync();
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            // 第一步：获取账户基本信息，计算单笔可用风险金
            decimal singleRiskAmount = 0m;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    SELECT equity, opportunity_count 
                    FROM trading_accounts 
                    WHERE id = @accountId";

                command.Parameters.AddWithValue("@accountId", accountId);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        var equity = reader.GetDecimal("equity");
                        var opportunityCount = reader.GetInt32("opportunity_count");
                        singleRiskAmount = opportunityCount > 0 ? equity / opportunityCount : 0;
                    }
                }
            }

            // 第二步：获取该合约所有推仓关联订单的累加实际盈亏
            decimal totalRealProfit = 0m;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    SELECT SUM(COALESCE(o.real_profit, 0)) as total_real_profit
                    FROM simulation_orders o
                    INNER JOIN position_push_order_rel r ON o.id = r.order_id
                    INNER JOIN position_push_info p ON r.push_id = p.id
                    WHERE p.account_id = @accountId 
                    AND p.contract = @contract";

                command.Parameters.AddWithValue("@accountId", accountId);
                command.Parameters.AddWithValue("@contract", contract);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync() && !reader.IsDBNull(0))
                    {
                        totalRealProfit = reader.GetDecimal("total_real_profit");
                    }
                }
            }

            // 第三步：计算当前可用风险金 = 单笔可用风险金 + 累加实际盈亏
            return singleRiskAmount + totalRealProfit;
        }

        // 添加GetUserAsync方法实现
        public async Task<User> GetUserAsync(string username, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT id, username, password_hash, create_time FROM users WHERE username = @username";
                        command.Parameters.AddWithValue("@username", username);

                        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                        {
                            if (await reader.ReadAsync())
                            {
                                return new User
                                {
                                    Id = reader.GetInt64("id"),
                                    Username = reader.GetString("username"),
                                    PasswordHash = reader.GetString("password_hash"),
                                    CreateTime = reader.GetDateTime("create_time")
                                };
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, "获取用户信息失败");
                throw;
            }
            
            return null;
        }

        #region 条件单相关方法

        /// <summary>
        /// 插入条件单到数据库
        /// </summary>
        public async Task<long> InsertConditionalOrderAsync(ConditionalOrder order, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            INSERT INTO conditional_orders (
                                account_id, symbol, direction, condition_type,
                                trigger_price, quantity, leverage, stop_loss_price,
                                status, create_time
                            ) VALUES (
                                @account_id, @symbol, @direction, @condition_type,
                                @trigger_price, @quantity, @leverage, @stop_loss_price,
                                @status, @create_time
                            );
                            SELECT LAST_INSERT_ID();";

                        command.Parameters.AddWithValue("@account_id", order.AccountId);
                        command.Parameters.AddWithValue("@symbol", order.Symbol);
                        command.Parameters.AddWithValue("@direction", order.Direction);
                        command.Parameters.AddWithValue("@condition_type", order.ConditionType.ToString());
                        command.Parameters.AddWithValue("@trigger_price", order.TriggerPrice);
                        command.Parameters.AddWithValue("@quantity", order.Quantity);
                        command.Parameters.AddWithValue("@leverage", order.Leverage);
                        command.Parameters.AddWithValue("@stop_loss_price", (object)order.StopLossPrice ?? DBNull.Value);
                        command.Parameters.AddWithValue("@status", order.Status.ToString());
                        command.Parameters.AddWithValue("@create_time", order.CreateTime);

                        var result = await command.ExecuteScalarAsync(cancellationToken);
                        return Convert.ToInt64(result);
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, "插入条件单失败");
                throw;
            }
        }

        /// <summary>
        /// 获取指定账户的条件单列表
        /// </summary>
        public async Task<List<ConditionalOrder>> GetConditionalOrdersAsync(long accountId, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureConnectionStringLoadedAsync();
                var orders = new List<ConditionalOrder>();
                
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            SELECT * FROM conditional_orders 
                            WHERE account_id = @account_id 
                            ORDER BY create_time DESC";
                        command.Parameters.AddWithValue("@account_id", accountId);

                        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                        {
                            while (await reader.ReadAsync())
                            {
                                var order = new ConditionalOrder
                                {
                                    Id = reader.GetInt64("id"),
                                    AccountId = reader.GetInt64("account_id"),
                                    Symbol = reader.GetString("symbol"),
                                    Direction = reader.GetString("direction"),
                                    ConditionType = Enum.Parse<ConditionalOrderType>(reader.GetString("condition_type")),
                                    TriggerPrice = reader.GetDecimal("trigger_price"),
                                    Quantity = reader.GetDecimal("quantity"),
                                    Leverage = reader.GetInt32("leverage"),
                                    StopLossPrice = reader.IsDBNull(reader.GetOrdinal("stop_loss_price")) ? null : (decimal?)reader.GetDecimal("stop_loss_price"),
                                    Status = Enum.Parse<ConditionalOrderStatus>(reader.GetString("status")),
                                    ExecutionOrderId = reader.IsDBNull(reader.GetOrdinal("execution_order_id")) ? null : reader.GetString("execution_order_id"),
                                    ErrorMessage = reader.IsDBNull(reader.GetOrdinal("error_message")) ? null : reader.GetString("error_message"),
                                    CreateTime = reader.GetDateTime("create_time"),
                                    TriggerTime = reader.IsDBNull(reader.GetOrdinal("trigger_time")) ? null : (DateTime?)reader.GetDateTime("trigger_time"),
                                    ExecutionTime = reader.IsDBNull(reader.GetOrdinal("execution_time")) ? null : (DateTime?)reader.GetDateTime("execution_time"),
                                    UpdateTime = reader.GetDateTime("update_time")
                                };
                                orders.Add(order);
                            }
                        }
                    }
                }
                
                return orders;
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, "获取条件单列表失败");
                throw;
            }
        }

        /// <summary>
        /// 获取所有等待中的条件单
        /// </summary>
        public async Task<List<ConditionalOrder>> GetWaitingConditionalOrdersAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureConnectionStringLoadedAsync();
                var orders = new List<ConditionalOrder>();
                
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            SELECT * FROM conditional_orders 
                            WHERE status = 'WAITING' 
                            ORDER BY create_time ASC";

                        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                        {
                            while (await reader.ReadAsync(cancellationToken))
                            {
                                var order = new ConditionalOrder
                                {
                                    Id = reader.GetInt64("id"),
                                    AccountId = reader.GetInt64("account_id"),
                                    Symbol = reader.GetString("symbol"),
                                    Direction = reader.GetString("direction"),
                                    ConditionType = Enum.Parse<ConditionalOrderType>(reader.GetString("condition_type")),
                                    TriggerPrice = reader.GetDecimal("trigger_price"),
                                    Quantity = reader.GetDecimal("quantity"),
                                    Leverage = reader.GetInt32("leverage"),
                                    StopLossPrice = reader.IsDBNull(reader.GetOrdinal("stop_loss_price")) ? null : (decimal?)reader.GetDecimal("stop_loss_price"),
                                    Status = Enum.Parse<ConditionalOrderStatus>(reader.GetString("status")),
                                    ExecutionOrderId = reader.IsDBNull(reader.GetOrdinal("execution_order_id")) ? null : reader.GetString("execution_order_id"),
                                    ErrorMessage = reader.IsDBNull(reader.GetOrdinal("error_message")) ? null : reader.GetString("error_message"),
                                    CreateTime = reader.GetDateTime("create_time"),
                                    TriggerTime = reader.IsDBNull(reader.GetOrdinal("trigger_time")) ? null : (DateTime?)reader.GetDateTime("trigger_time"),
                                    ExecutionTime = reader.IsDBNull(reader.GetOrdinal("execution_time")) ? null : (DateTime?)reader.GetDateTime("execution_time"),
                                    UpdateTime = reader.GetDateTime("update_time")
                                };
                                orders.Add(order);
                            }
                        }
                    }
                }
                
                return orders;
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, "获取等待中的条件单失败");
                throw;
            }
        }

        /// <summary>
        /// 更新条件单状态
        /// </summary>
        public async Task<bool> UpdateConditionalOrderStatusAsync(long orderId, ConditionalOrderStatus status, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            UPDATE conditional_orders 
                            SET status = @status, 
                                trigger_time = CASE WHEN @status = 'TRIGGERED' THEN NOW() ELSE trigger_time END,
                                update_time = NOW()
                            WHERE id = @id";

                        command.Parameters.AddWithValue("@id", orderId);
                        command.Parameters.AddWithValue("@status", status.ToString());

                        var result = await command.ExecuteNonQueryAsync(cancellationToken);
                        return result > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, "更新条件单状态失败");
                throw;
            }
        }

        /// <summary>
        /// 更新条件单为已执行状态
        /// </summary>
        public async Task<bool> UpdateConditionalOrderToExecutedAsync(long orderId, string executionOrderId, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            UPDATE conditional_orders 
                            SET status = 'EXECUTED', 
                                execution_order_id = @execution_order_id,
                                execution_time = NOW(),
                                update_time = NOW()
                            WHERE id = @id";

                        command.Parameters.AddWithValue("@id", orderId);
                        command.Parameters.AddWithValue("@execution_order_id", executionOrderId);

                        var result = await command.ExecuteNonQueryAsync(cancellationToken);
                        return result > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, "更新条件单为已执行状态失败");
                throw;
            }
        }

        /// <summary>
        /// 更新条件单为失败状态
        /// </summary>
        public async Task<bool> UpdateConditionalOrderToFailedAsync(long orderId, string errorMessage, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            UPDATE conditional_orders 
                            SET status = 'FAILED', 
                                error_message = @error_message,
                                update_time = NOW()
                            WHERE id = @id";

                        command.Parameters.AddWithValue("@id", orderId);
                        command.Parameters.AddWithValue("@error_message", errorMessage);

                        var result = await command.ExecuteNonQueryAsync(cancellationToken);
                        return result > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, "更新条件单为失败状态失败");
                throw;
            }
        }

        /// <summary>
        /// 取消条件单
        /// </summary>
        public async Task<bool> CancelConditionalOrderAsync(long orderId, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            UPDATE conditional_orders 
                            SET status = 'CANCELLED', 
                                update_time = NOW()
                            WHERE id = @id AND status = 'WAITING'";

                        command.Parameters.AddWithValue("@id", orderId);

                        var result = await command.ExecuteNonQueryAsync(cancellationToken);
                        return result > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, "取消条件单失败");
                throw;
            }
        }

        /// <summary>
        /// 更新推仓信息状态
        /// </summary>
        public async Task<bool> UpdatePushInfoStatusAsync(long pushId, string status, DateTime? closeTime = null)
        {
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using (var command = connection.CreateCommand())
                    {
                        if (closeTime.HasValue)
                        {
                            command.CommandText = @"
                                UPDATE position_push_info 
                                SET status = @status, close_time = @close_time 
                                WHERE id = @id";
                            command.Parameters.AddWithValue("@close_time", closeTime.Value);
                        }
                        else
                        {
                            command.CommandText = @"
                                UPDATE position_push_info 
                                SET status = @status 
                                WHERE id = @id";
                        }

                        command.Parameters.AddWithValue("@id", pushId);
                        command.Parameters.AddWithValue("@status", status);

                        var result = await command.ExecuteNonQueryAsync();
                        
                        _logger?.LogInformation("更新推仓状态成功 - 推仓ID: {pushId}, 状态: {status}, 完结时间: {closeTime}", 
                            pushId, status, closeTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "无");
                        LogManager.Log("Database", $"更新推仓状态成功 - 推仓ID: {pushId}, 状态: {status}");
                        
                        return result > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "更新推仓状态失败 - 推仓ID: {pushId}", pushId);
                LogManager.LogException("Database", ex, $"更新推仓状态失败 - 推仓ID: {pushId}");
                throw;
            }
        }

        #endregion

        /// <summary>
        /// 获取当前用户信息
        /// </summary>
        public async Task<User> GetCurrentUserAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // 从AppSession获取当前用户ID
                if (AppSession.CurrentUserId <= 0)
                {
                    LogManager.Log("Database", "当前用户ID无效");
                    return null;
                }

                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT * FROM users WHERE id = @id";
                        command.Parameters.AddWithValue("@id", AppSession.CurrentUserId);
                        LogManager.Log("Database", $"执行SQL: {command.CommandText}");

                        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                        {
                            if (await reader.ReadAsync(cancellationToken))
                            {
                                var user = new User
                                {
                                    Id = reader.GetInt64("id"),
                                    Username = reader.GetString("username"),
                                    Email = reader.IsDBNull(reader.GetOrdinal("email")) ? null : reader.GetString("email"),
                                    LastLoginTime = reader.IsDBNull(reader.GetOrdinal("last_login_time")) ? null : (DateTime?)reader.GetDateTime("last_login_time"),
                                    Status = reader.GetInt32("status"),
                                    CreateTime = reader.GetDateTime("create_time"),
                                    UpdateTime = reader.GetDateTime("update_time")
                                };
                                
                                LogManager.Log("Database", $"成功获取当前用户信息: ID={user.Id}, 用户名={user.Username}");
                                return user;
                            }
                        }
                    }
                }
                
                LogManager.Log("Database", $"未找到当前用户: ID={AppSession.CurrentUserId}");
                return null;
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, $"获取当前用户信息失败");
                throw;
            }
        }

        /// <summary>
        /// 获取账户持仓信息
        /// </summary>
        public async Task<List<AccountPosition>> GetAccountPositionsAsync(long accountId, CancellationToken cancellationToken = default)
        {
            var positions = new List<AccountPosition>();
            
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            SELECT id, account_id, symbol, position_side, entry_price, mark_price, 
                                   position_amt, leverage, margin_type, isolated_margin, 
                                   unrealized_pnl, liquidation_price, timestamp, created_at, updated_at
                            FROM account_positions 
                            WHERE account_id = @accountId 
                            AND ABS(position_amt) > 0
                            ORDER BY symbol ASC";
                        
                        command.Parameters.AddWithValue("@accountId", accountId);
                        
                        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                        {
                            while (await reader.ReadAsync(cancellationToken))
                            {
                                var position = new AccountPosition
                                {
                                    Id = reader.GetInt64("id"),
                                    AccountId = reader.GetInt64("account_id"),
                                    Symbol = reader.GetString("symbol"),
                                    PositionSide = reader.GetString("position_side"),
                                    EntryPrice = reader.GetDecimal("entry_price"),
                                    MarkPrice = reader.GetDecimal("mark_price"),
                                    PositionAmt = reader.GetDecimal("position_amt"),
                                    Leverage = reader.GetInt32("leverage"),
                                    MarginType = reader.GetString("margin_type"),
                                    IsolatedMargin = reader.GetDecimal("isolated_margin"),
                                    UnrealizedPnl = reader.GetDecimal("unrealized_pnl"),
                                    LiquidationPrice = reader.GetDecimal("liquidation_price"),
                                    Timestamp = reader.IsDBNull("timestamp") ? DateTime.Now : reader.GetDateTime("timestamp"),
                                    CreatedAt = reader.GetDateTime("created_at"),
                                    UpdatedAt = reader.GetDateTime("updated_at")
                                };
                                
                                positions.Add(position);
                            }
                        }
                    }
                }
                
                LogManager.Log("Database", $"获取账户 {accountId} 持仓信息成功，共 {positions.Count} 条记录");
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, $"获取账户 {accountId} 持仓信息失败");
                throw;
            }
            
            return positions;
        }

        /// <summary>
        /// 获取账户余额信息
        /// </summary>
        public async Task<AccountBalance> GetAccountBalanceAsync(long accountId, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    
                    // 优先从account_balances表获取
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            SELECT account_id, total_equity, available_balance, 
                                   margin_balance, unrealized_pnl, timestamp
                            FROM account_balances 
                            WHERE account_id = @accountId 
                            ORDER BY timestamp DESC 
                            LIMIT 1";
                        
                        command.Parameters.AddWithValue("@accountId", accountId);
                        
                        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                        {
                            if (await reader.ReadAsync(cancellationToken))
                            {
                                var balance = new AccountBalance
                                {
                                    AccountId = reader.GetInt64("account_id"),
                                    TotalEquity = reader.GetDecimal("total_equity"),
                                    AvailableBalance = reader.GetDecimal("available_balance"),
                                    MarginBalance = reader.GetDecimal("margin_balance"),
                                    UnrealizedPnL = reader.GetDecimal("unrealized_pnl"),
                                    Timestamp = reader.GetDateTime("timestamp"),
                                    Source = "account_balances"
                                };
                                
                                LogManager.Log("Database", $"从account_balances表获取账户 {accountId} 余额信息成功");
                                return balance;
                            }
                        }
                    }
                    
                    // 如果account_balances表没有数据，从trading_accounts表获取基本信息
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            SELECT id, equity, opportunity_count 
                            FROM trading_accounts 
                            WHERE id = @accountId";
                        
                        command.Parameters.AddWithValue("@accountId", accountId);
                        
                        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                        {
                            if (await reader.ReadAsync(cancellationToken))
                            {
                                var balance = new AccountBalance
                                {
                                    AccountId = reader.GetInt64("id"),
                                    TotalEquity = reader.GetDecimal("equity"),
                                    AvailableBalance = reader.GetDecimal("equity"),
                                    MarginBalance = reader.GetDecimal("equity"),
                                    UnrealizedPnL = 0m,
                                    Timestamp = DateTime.Now,
                                    Source = "trading_accounts",
                                    OpportunityCount = reader.GetInt32("opportunity_count")
                                };
                                
                                LogManager.Log("Database", $"从trading_accounts表获取账户 {accountId} 余额信息成功");
                                return balance;
                            }
                        }
                    }
                }
                
                LogManager.Log("Database", $"未找到账户 {accountId} 的余额信息");
                return null;
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, $"获取账户 {accountId} 余额信息失败");
                throw;
            }
        }

        /// <summary>
        /// 获取所有开放订单
        /// </summary>
        public async Task<List<SimulationOrder>> GetAllOpenOrdersAsync()
        {
            var orders = new List<SimulationOrder>();
            
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            SELECT id, order_id, account_id, contract, contract_size, direction, 
                                   quantity, entry_price, initial_stop_loss, current_stop_loss,
                                   highest_price, max_floating_profit, leverage, margin, total_value,
                                   status, open_time, close_time, close_price, realized_profit,
                                   close_type, real_profit, floating_pnl, current_price, last_update_time
                            FROM simulation_orders 
                            WHERE status = 'open'
                            ORDER BY open_time DESC";
                        
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var order = new SimulationOrder
                                {
                                    Id = reader.GetInt64("id"),
                                    OrderId = reader.GetString("order_id"),
                                    AccountId = reader.GetInt64("account_id"),
                                    Contract = reader.GetString("contract"),
                                    ContractSize = reader.GetDecimal("contract_size"),
                                    Direction = reader.GetString("direction"),
                                    Quantity = reader.GetFloat("quantity"),
                                    EntryPrice = reader.GetDecimal("entry_price"),
                                    InitialStopLoss = reader.GetDecimal("initial_stop_loss"),
                                    CurrentStopLoss = reader.GetDecimal("current_stop_loss"),
                                    HighestPrice = reader.IsDBNull("highest_price") ? null : reader.GetDecimal("highest_price"),
                                    MaxFloatingProfit = reader.IsDBNull("max_floating_profit") ? null : reader.GetDecimal("max_floating_profit"),
                                    Leverage = reader.GetInt32("leverage"),
                                    Margin = reader.GetDecimal("margin"),
                                    TotalValue = reader.GetDecimal("total_value"),
                                    Status = reader.GetString("status"),
                                    OpenTime = reader.GetDateTime("open_time"),
                                    CloseTime = reader.IsDBNull("close_time") ? null : reader.GetDateTime("close_time"),
                                    ClosePrice = reader.IsDBNull("close_price") ? null : reader.GetDecimal("close_price"),
                                    RealizedProfit = reader.IsDBNull("realized_profit") ? null : reader.GetDecimal("realized_profit"),
                                    CloseType = reader.IsDBNull("close_type") ? null : reader.GetString("close_type"),
                                    RealProfit = reader.IsDBNull("real_profit") ? null : reader.GetDecimal("real_profit"),
                                    FloatingPnL = reader.IsDBNull("floating_pnl") ? null : reader.GetDecimal("floating_pnl"),
                                    CurrentPrice = reader.IsDBNull("current_price") ? null : reader.GetDecimal("current_price"),
                                    LastUpdateTime = reader.IsDBNull("last_update_time") ? null : reader.GetDateTime("last_update_time")
                                };
                                
                                orders.Add(order);
                            }
                        }
                    }
                }
                
                LogManager.Log("Database", $"获取所有开放订单成功，共 {orders.Count} 条记录");
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, "获取所有开放订单失败");
                throw;
            }
            
            return orders;
        }

        /// <summary>
        /// 获取所有推仓信息
        /// </summary>
        public async Task<List<PushSummaryInfo>> GetAllPushInfosAsync(long accountId)
        {
            var pushInfos = new List<PushSummaryInfo>();
            
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            SELECT id, contract, status, create_time, close_time
                            FROM position_push_info 
                            WHERE account_id = @accountId
                            ORDER BY create_time DESC";
                        
                        command.Parameters.AddWithValue("@accountId", accountId);
                        
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var pushInfo = new PushSummaryInfo
                                {
                                    PushId = reader.GetInt64("id"),
                                    Contract = reader.GetString("contract"),
                                    Status = reader.GetString("status"),
                                    CreateTime = reader.GetDateTime("create_time"),
                                    CloseTime = reader.IsDBNull("close_time") ? null : reader.GetDateTime("close_time")
                                };
                                
                                pushInfos.Add(pushInfo);
                            }
                        }
                    }
                }
                
                LogManager.Log("Database", $"获取账户 {accountId} 所有推仓信息成功，共 {pushInfos.Count} 条记录");
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, $"获取账户 {accountId} 所有推仓信息失败");
                throw;
            }
            
            return pushInfos;
        }

        /// <summary>
        /// 获取推仓订单
        /// </summary>
        public async Task<List<SimulationOrder>> GetPushOrdersAsync(long pushId)
        {
            var orders = new List<SimulationOrder>();
            
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            SELECT so.id, so.order_id, so.account_id, so.contract, so.contract_size, 
                                   so.direction, so.quantity, so.entry_price, so.initial_stop_loss,
                                   so.current_stop_loss, so.highest_price, so.max_floating_profit,
                                   so.leverage, so.margin, so.total_value, so.status, so.open_time,
                                   so.close_time, so.close_price, so.realized_profit, so.close_type,
                                   so.real_profit, so.floating_pnl, so.current_price, so.last_update_time
                            FROM simulation_orders so
                            INNER JOIN position_push_order_rel por ON so.id = por.order_id
                            WHERE por.push_id = @pushId
                            ORDER BY so.open_time ASC";
                        
                        command.Parameters.AddWithValue("@pushId", pushId);
                        
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var order = new SimulationOrder
                                {
                                    Id = reader.GetInt64("id"),
                                    OrderId = reader.GetString("order_id"),
                                    AccountId = reader.GetInt64("account_id"),
                                    Contract = reader.GetString("contract"),
                                    ContractSize = reader.GetDecimal("contract_size"),
                                    Direction = reader.GetString("direction"),
                                    Quantity = reader.GetFloat("quantity"),
                                    EntryPrice = reader.GetDecimal("entry_price"),
                                    InitialStopLoss = reader.GetDecimal("initial_stop_loss"),
                                    CurrentStopLoss = reader.GetDecimal("current_stop_loss"),
                                    HighestPrice = reader.IsDBNull("highest_price") ? null : reader.GetDecimal("highest_price"),
                                    MaxFloatingProfit = reader.IsDBNull("max_floating_profit") ? null : reader.GetDecimal("max_floating_profit"),
                                    Leverage = reader.GetInt32("leverage"),
                                    Margin = reader.GetDecimal("margin"),
                                    TotalValue = reader.GetDecimal("total_value"),
                                    Status = reader.GetString("status"),
                                    OpenTime = reader.GetDateTime("open_time"),
                                    CloseTime = reader.IsDBNull("close_time") ? null : reader.GetDateTime("close_time"),
                                    ClosePrice = reader.IsDBNull("close_price") ? null : reader.GetDecimal("close_price"),
                                    RealizedProfit = reader.IsDBNull("realized_profit") ? null : reader.GetDecimal("realized_profit"),
                                    CloseType = reader.IsDBNull("close_type") ? null : reader.GetString("close_type"),
                                    RealProfit = reader.IsDBNull("real_profit") ? null : reader.GetDecimal("real_profit"),
                                    FloatingPnL = reader.IsDBNull("floating_pnl") ? null : reader.GetDecimal("floating_pnl"),
                                    CurrentPrice = reader.IsDBNull("current_price") ? null : reader.GetDecimal("current_price"),
                                    LastUpdateTime = reader.IsDBNull("last_update_time") ? null : reader.GetDateTime("last_update_time")
                                };
                                
                                orders.Add(order);
                            }
                        }
                    }
                }
                
                LogManager.Log("Database", $"获取推仓 {pushId} 订单成功，共 {orders.Count} 条记录");
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, $"获取推仓 {pushId} 订单失败");
                throw;
            }
            
            return orders;
        }

        /// <summary>
        /// 插入止损止盈单
        /// </summary>
        public async Task<long> InsertStopTakeOrderAsync(StopTakeOrder order, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            INSERT INTO stop_take_orders 
                            (account_id, simulation_order_id, symbol, order_type, direction, quantity, trigger_price, 
                             working_type, status, create_time, update_time)
                            VALUES 
                            (@accountId, @simulationOrderId, @symbol, @orderType, @direction, @quantity, @triggerPrice, 
                             @workingType, @status, NOW(), NOW());
                            SELECT LAST_INSERT_ID();";
                        
                        command.Parameters.AddWithValue("@accountId", order.AccountId);
                        command.Parameters.AddWithValue("@simulationOrderId", order.SimulationOrderId);
                        command.Parameters.AddWithValue("@symbol", order.Symbol);
                        command.Parameters.AddWithValue("@orderType", order.OrderType);
                        command.Parameters.AddWithValue("@direction", order.Direction);
                        command.Parameters.AddWithValue("@quantity", order.Quantity);
                        command.Parameters.AddWithValue("@triggerPrice", order.TriggerPrice);
                        command.Parameters.AddWithValue("@workingType", order.WorkingType);
                        command.Parameters.AddWithValue("@status", order.Status);
                        
                        var result = await command.ExecuteScalarAsync(cancellationToken);
                        var orderId = Convert.ToInt64(result);
                        
                        LogManager.Log("Database", $"插入止损止盈单成功，ID: {orderId}");
                        return orderId;
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, "插入止损止盈单失败");
                throw;
            }
        }

        /// <summary>
        /// 获取止损止盈单列表
        /// </summary>
        public async Task<List<StopTakeOrder>> GetStopTakeOrdersAsync(long accountId, CancellationToken cancellationToken = default)
        {
            var orders = new List<StopTakeOrder>();
            
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            SELECT id, account_id, simulation_order_id, symbol, order_type, direction, quantity, trigger_price, 
                                   working_type, status, binance_order_id, execution_price, error_message, 
                                   create_time, update_time
                            FROM stop_take_orders 
                            WHERE account_id = @accountId
                            ORDER BY create_time DESC";
                        
                        command.Parameters.AddWithValue("@accountId", accountId);
                        
                        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                        {
                            while (await reader.ReadAsync(cancellationToken))
                            {
                                var order = new StopTakeOrder
                                {
                                    Id = reader.GetInt64("id"),
                                    AccountId = reader.GetInt64("account_id"),
                                    SimulationOrderId = reader.GetInt64("simulation_order_id"),
                                    Symbol = reader.GetString("symbol"),
                                    OrderType = reader.GetString("order_type"),
                                    Direction = reader.GetString("direction"),
                                    Quantity = reader.GetDecimal("quantity"),
                                    TriggerPrice = reader.GetDecimal("trigger_price"),
                                    WorkingType = reader.GetString("working_type"),
                                    Status = reader.GetString("status"),
                                    BinanceOrderId = reader.IsDBNull("binance_order_id") ? null : reader.GetString("binance_order_id"),
                                    ExecutionPrice = reader.IsDBNull("execution_price") ? null : reader.GetDecimal("execution_price"),
                                    ErrorMessage = reader.IsDBNull("error_message") ? null : reader.GetString("error_message"),
                                    CreateTime = reader.GetDateTime("create_time"),
                                    UpdateTime = reader.GetDateTime("update_time")
                                };
                                
                                orders.Add(order);
                            }
                        }
                    }
                }
                
                LogManager.Log("Database", $"获取账户 {accountId} 止损止盈单成功，共 {orders.Count} 条记录");
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, $"获取账户 {accountId} 止损止盈单失败");
                throw;
            }
            
            return orders;
        }

        /// <summary>
        /// 获取等待中的止损止盈单
        /// </summary>
        public async Task<List<StopTakeOrder>> GetWaitingStopTakeOrdersAsync(CancellationToken cancellationToken = default)
        {
            var orders = new List<StopTakeOrder>();
            
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            SELECT id, account_id, simulation_order_id, symbol, order_type, direction, quantity, trigger_price, 
                                   working_type, status, binance_order_id, execution_price, error_message, 
                                   create_time, update_time
                            FROM stop_take_orders 
                            WHERE status = 'WAITING'
                            ORDER BY create_time ASC";
                        
                        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                        {
                            while (await reader.ReadAsync(cancellationToken))
                            {
                                var order = new StopTakeOrder
                                {
                                    Id = reader.GetInt64("id"),
                                    AccountId = reader.GetInt64("account_id"),
                                    SimulationOrderId = reader.GetInt64("simulation_order_id"),
                                    Symbol = reader.GetString("symbol"),
                                    OrderType = reader.GetString("order_type"),
                                    Direction = reader.GetString("direction"),
                                    Quantity = reader.GetDecimal("quantity"),
                                    TriggerPrice = reader.GetDecimal("trigger_price"),
                                    WorkingType = reader.GetString("working_type"),
                                    Status = reader.GetString("status"),
                                    BinanceOrderId = reader.IsDBNull("binance_order_id") ? null : reader.GetString("binance_order_id"),
                                    ExecutionPrice = reader.IsDBNull("execution_price") ? null : reader.GetDecimal("execution_price"),
                                    ErrorMessage = reader.IsDBNull("error_message") ? null : reader.GetString("error_message"),
                                    CreateTime = reader.GetDateTime("create_time"),
                                    UpdateTime = reader.GetDateTime("update_time")
                                };
                                
                                orders.Add(order);
                            }
                        }
                    }
                }
                
                LogManager.Log("Database", $"获取等待中的止损止盈单成功，共 {orders.Count} 条记录");
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, "获取等待中的止损止盈单失败");
                throw;
            }
            
            return orders;
        }

        /// <summary>
        /// 更新止损止盈单状态
        /// </summary>
        public async Task<bool> UpdateStopTakeOrderStatusAsync(long orderId, string status, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            UPDATE stop_take_orders 
                            SET status = @status, updated_at = NOW()
                            WHERE id = @orderId";
                        
                        command.Parameters.AddWithValue("@orderId", orderId);
                        command.Parameters.AddWithValue("@status", status);
                        
                        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
                        var success = rowsAffected > 0;
                        
                        LogManager.Log("Database", $"更新止损止盈单状态 - OrderId: {orderId}, Status: {status}, Success: {success}");
                        return success;
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, $"更新止损止盈单状态失败 - OrderId: {orderId}");
                return false;
            }
        }

        /// <summary>
        /// 更新止损止盈单为已执行状态
        /// </summary>
        public async Task<bool> UpdateStopTakeOrderToExecutedAsync(long orderId, string binanceOrderId, decimal executionPrice, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            UPDATE stop_take_orders 
                            SET status = 'executed', binance_order_id = @binanceOrderId, 
                                execution_price = @executionPrice, updated_at = NOW()
                            WHERE id = @orderId";
                        
                        command.Parameters.AddWithValue("@orderId", orderId);
                        command.Parameters.AddWithValue("@binanceOrderId", binanceOrderId);
                        command.Parameters.AddWithValue("@executionPrice", executionPrice);
                        
                        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
                        var success = rowsAffected > 0;
                        
                        LogManager.Log("Database", $"更新止损止盈单为已执行 - OrderId: {orderId}, BinanceOrderId: {binanceOrderId}, ExecutionPrice: {executionPrice}, Success: {success}");
                        return success;
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, $"更新止损止盈单为已执行失败 - OrderId: {orderId}");
                return false;
            }
        }

        /// <summary>
        /// 更新止损止盈单为失败状态
        /// </summary>
        public async Task<bool> UpdateStopTakeOrderToFailedAsync(long orderId, string errorMessage, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            UPDATE stop_take_orders 
                            SET status = 'failed', error_message = @errorMessage, updated_at = NOW()
                            WHERE id = @orderId";
                        
                        command.Parameters.AddWithValue("@orderId", orderId);
                        command.Parameters.AddWithValue("@errorMessage", errorMessage);
                        
                        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
                        var success = rowsAffected > 0;
                        
                        LogManager.Log("Database", $"更新止损止盈单为失败 - OrderId: {orderId}, ErrorMessage: {errorMessage}, Success: {success}");
                        return success;
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, $"更新止损止盈单为失败状态失败 - OrderId: {orderId}");
                return false;
            }
        }

        /// <summary>
        /// 取消止损止盈单
        /// </summary>
        public async Task<bool> CancelStopTakeOrderAsync(long orderId, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            UPDATE stop_take_orders 
                            SET status = 'cancelled', updated_at = NOW()
                            WHERE id = @orderId";
                        
                        command.Parameters.AddWithValue("@orderId", orderId);
                        
                        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
                        var success = rowsAffected > 0;
                        
                        LogManager.Log("Database", $"取消止损止盈单 - OrderId: {orderId}, Success: {success}");
                        return success;
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, $"取消止损止盈单失败 - OrderId: {orderId}");
                return false;
            }
        }

        /// <summary>
        /// 获取所有交易对符号
        /// </summary>
        public async Task<List<string>> GetAllSymbolsAsync(CancellationToken cancellationToken = default)
        {
            var symbols = new List<string>();
            
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            SELECT DISTINCT symbol 
                            FROM kline_data 
                            ORDER BY symbol ASC";
                        
                        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                        {
                            while (await reader.ReadAsync(cancellationToken))
                            {
                                symbols.Add(reader.GetString("symbol"));
                            }
                        }
                    }
                }
                
                LogManager.Log("Database", $"获取所有交易对符号成功，共 {symbols.Count} 个");
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, "获取所有交易对符号失败");
                throw;
            }
            
            return symbols;
        }

        /// <summary>
        /// 获取K线数据
        /// </summary>
        public async Task<List<KLineData>> GetKlineDataAsync(string symbol, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
        {
            var klineData = new List<KLineData>();
            
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            SELECT symbol, open_time, close_time, open_price, high_price, 
                                   low_price, close_price, volume, quote_volume, trades, 
                                   taker_buy_volume, taker_buy_quote_volume
                            FROM kline_data 
                            WHERE symbol = @symbol 
                            AND open_time BETWEEN @startDate AND @endDate
                            ORDER BY open_time ASC";
                        
                        command.Parameters.AddWithValue("@symbol", symbol);
                        command.Parameters.AddWithValue("@startDate", startDate);
                        command.Parameters.AddWithValue("@endDate", endDate);
                        
                        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                        {
                            while (await reader.ReadAsync(cancellationToken))
                            {
                                var kline = new KLineData
                                {
                                    Symbol = reader.GetString("symbol"),
                                    OpenTime = reader.GetDateTime("open_time"),
                                    CloseTime = reader.GetDateTime("close_time"),
                                    OpenPrice = reader.GetDecimal("open_price"),
                                    HighPrice = reader.GetDecimal("high_price"),
                                    LowPrice = reader.GetDecimal("low_price"),
                                    ClosePrice = reader.GetDecimal("close_price"),
                                    Volume = reader.GetDecimal("volume"),
                                    QuoteVolume = reader.GetDecimal("quote_volume"),
                                    Trades = reader.GetInt32("trades"),
                                    TakerBuyVolume = reader.GetDecimal("taker_buy_volume"),
                                    TakerBuyQuoteVolume = reader.GetDecimal("taker_buy_quote_volume")
                                };
                                
                                klineData.Add(kline);
                            }
                        }
                    }
                }
                
                LogManager.Log("Database", $"获取 {symbol} K线数据成功，共 {klineData.Count} 条记录");
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, $"获取 {symbol} K线数据失败");
                throw;
            }
            
            return klineData;
        }

        /// <summary>
        /// 计算指定合约过去N天的平均成交额（从数据库kline_data表）
        /// </summary>
        public async Task<decimal> GetAverageQuoteVolumeAsync(string symbol, int days, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    using (var command = connection.CreateCommand())
                    {
                        // 修正查询：移除不存在的interval_type字段，使用日期条件筛选日线数据
                        // 通过时间间隔判断是否为日线数据（24小时间隔）
                        command.CommandText = @"
                            SELECT AVG(quote_volume) as avg_quote_volume, COUNT(*) as data_count
                            FROM kline_data 
                            WHERE symbol = @symbol 
                            AND quote_volume > 0
                            AND DATE(open_time) >= DATE_SUB(CURDATE(), INTERVAL @days DAY)
                            AND DATE(open_time) < CURDATE()
                            AND TIME_TO_SEC(TIMEDIFF(close_time, open_time)) >= 86000";
                        
                        command.Parameters.AddWithValue("@symbol", symbol);
                        command.Parameters.AddWithValue("@days", days);
                        
                        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                        {
                            if (await reader.ReadAsync(cancellationToken))
                            {
                                var avgQuoteVolume = reader.IsDBNull("avg_quote_volume") ? 0m : reader.GetDecimal("avg_quote_volume");
                                var dataCount = reader.GetInt32("data_count");
                                
                                LogManager.Log("Database", $"获取 {symbol} 过去 {days} 天平均成交额: {avgQuoteVolume:F2}，基于 {dataCount} 条有效数据");
                                return avgQuoteVolume;
                            }
                        }
                    }
                }
                
                LogManager.Log("Database", $"获取 {symbol} 平均成交额失败：没有找到数据");
                return 0;
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, $"获取 {symbol} 平均成交额失败");
                return 0;
            }
        }

        /// <summary>
        /// 获取所有状态为open的推仓信息
        /// </summary>
        public async Task<List<PushSummaryInfo>> GetAllPushSummaryInfosAsync()
        {
            var result = new List<PushSummaryInfo>();
            
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // 查询所有状态为open的推仓信息
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            SELECT DISTINCT pp.id as push_id, pp.contract, pp.status, 
                                   pp.create_time, pp.close_time, pp.account_id
                            FROM position_push_info pp
                            WHERE pp.status = 'open'
                            ORDER BY pp.create_time DESC";
                        
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var pushInfo = new PushSummaryInfo
                                {
                                    PushId = reader.GetInt64("push_id"),
                                    Contract = reader.GetString("contract"),
                                    Status = reader.GetString("status"),
                                    CreateTime = reader.GetDateTime("create_time"),
                                    CloseTime = reader.IsDBNull("close_time") ? null : reader.GetDateTime("close_time"),
                                    SingleRiskAmount = 0, // 默认值，后续可以从账户信息计算
                                    AvailableRiskAmount = 0, // 默认值，后续可以从账户信息计算
                                    Orders = new List<SimulationOrder>()
                                };
                                
                                result.Add(pushInfo);
                            }
                        }
                    }
                    
                    // 为每个推仓信息获取关联的订单
                    foreach (var pushInfo in result)
                    {
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = @"
                                SELECT so.id, so.account_id, so.order_id, so.contract, so.direction, so.quantity,
                                       so.entry_price, so.current_price, so.initial_stop_loss, so.current_stop_loss,
                                       so.status, so.open_time, so.close_time, so.close_price,
                                       so.real_profit, so.floating_pnl, so.contract_size,
                                       so.highest_price, so.last_update_time
                                FROM simulation_orders so
                                INNER JOIN position_push_order_rel por ON so.id = por.order_id
                                WHERE por.push_id = @pushId
                                ORDER BY so.open_time DESC";
                            
                            command.Parameters.AddWithValue("@pushId", pushInfo.PushId);
                            
                            using (var reader = await command.ExecuteReaderAsync())
                            {
                                while (await reader.ReadAsync())
                                {
                                    var order = new SimulationOrder
                                    {
                                        Id = reader.GetInt64("id"),
                                        AccountId = reader.GetInt64("account_id"),
                                        OrderId = reader.GetString("order_id"),
                                        Contract = reader.GetString("contract"),
                                        Direction = reader.GetString("direction"),
                                        Quantity = (float)reader.GetDouble("quantity"),
                                        EntryPrice = reader.GetDecimal("entry_price"),
                                        CurrentPrice = reader.IsDBNull("current_price") ? null : reader.GetDecimal("current_price"),
                                        InitialStopLoss = reader.GetDecimal("initial_stop_loss"),
                                        CurrentStopLoss = reader.GetDecimal("current_stop_loss"),
                                        Status = reader.GetString("status"),
                                        OpenTime = reader.GetDateTime("open_time"),
                                        CloseTime = reader.IsDBNull("close_time") ? null : reader.GetDateTime("close_time"),
                                        ClosePrice = reader.IsDBNull("close_price") ? null : reader.GetDecimal("close_price"),
                                        RealProfit = reader.IsDBNull("real_profit") ? null : reader.GetDecimal("real_profit"),
                                        FloatingPnL = reader.IsDBNull("floating_pnl") ? null : reader.GetDecimal("floating_pnl"),
                                        ContractSize = reader.GetDecimal("contract_size"),
                                        HighestPrice = reader.IsDBNull("highest_price") ? null : reader.GetDecimal("highest_price"),
                                        LastUpdateTime = reader.IsDBNull("last_update_time") ? null : reader.GetDateTime("last_update_time")
                                    };
                                    
                                    pushInfo.Orders.Add(order);
                                }
                            }
                        }
                    }
                }
                
                LogManager.Log("Database", $"获取所有推仓信息成功，共 {result.Count} 个推仓");
                return result;
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, "获取所有推仓信息失败");
                throw;
            }
        }

        /// <summary>
        /// 直接从数据库计算价格统计数据（避免获取完整K线数据）
        /// </summary>
        public async Task<Dictionary<string, (decimal HighPrice, decimal LowPrice, decimal OpenPrice)>> GetPriceStatsDirectAsync(int days, CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, (decimal, decimal, decimal)>();
            
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    using (var command = connection.CreateCommand())
                    {
                        // 直接在数据库中计算统计数据，避免传输大量K线数据
                        command.CommandText = @"
                            SELECT 
                                symbol,
                                MAX(high_price) as max_high,
                                MIN(low_price) as min_low,
                                (SELECT open_price FROM kline_data k2 
                                 WHERE k2.symbol = kline_data.symbol 
                                 AND DATE(k2.open_time) >= DATE_SUB(CURDATE(), INTERVAL @days DAY)
                                 AND DATE(k2.open_time) < CURDATE()
                                 AND TIME_TO_SEC(TIMEDIFF(k2.close_time, k2.open_time)) >= 86000
                                 ORDER BY k2.open_time ASC LIMIT 1) as first_open
                            FROM kline_data 
                            WHERE DATE(open_time) >= DATE_SUB(CURDATE(), INTERVAL @days DAY)
                            AND DATE(open_time) < CURDATE()
                            AND TIME_TO_SEC(TIMEDIFF(close_time, open_time)) >= 86000
                            GROUP BY symbol";
                        
                        command.Parameters.AddWithValue("@days", days);
                        
                        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                        {
                            while (await reader.ReadAsync(cancellationToken))
                            {
                                var symbol = reader.GetString("symbol");
                                var highPrice = reader.GetDecimal("max_high");
                                var lowPrice = reader.GetDecimal("min_low");
                                var openPrice = reader.GetDecimal("first_open");
                                
                                result[symbol] = (highPrice, lowPrice, openPrice);
                            }
                        }
                    }
                }
                
                LogManager.Log("Database", $"直接计算了{result.Count}个合约过去{days}天的价格统计数据");
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, $"直接计算价格统计数据失败");
                throw;
            }
            
            return result;
        }

        /// <summary>
        /// 批量获取多个周期的价格统计数据
        /// </summary>
        public async Task<Dictionary<int, Dictionary<string, (decimal HighPrice, decimal LowPrice, decimal OpenPrice)>>> GetBatchPriceStatsAsync(int[] daysPeriods, CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<int, Dictionary<string, (decimal, decimal, decimal)>>();
            
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    
                    foreach (var days in daysPeriods)
                    {
                        var periodStats = new Dictionary<string, (decimal, decimal, decimal)>();
                        
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = @"
                                SELECT 
                                    symbol,
                                    MAX(high_price) as max_high,
                                    MIN(low_price) as min_low,
                                    (SELECT open_price FROM kline_data k2 
                                     WHERE k2.symbol = kline_data.symbol 
                                     AND DATE(k2.open_time) >= DATE_SUB(CURDATE(), INTERVAL @days DAY)
                                     AND DATE(k2.open_time) < CURDATE()
                                     AND TIME_TO_SEC(TIMEDIFF(k2.close_time, k2.open_time)) >= 86000
                                     ORDER BY k2.open_time ASC LIMIT 1) as first_open
                                FROM kline_data 
                                WHERE DATE(open_time) >= DATE_SUB(CURDATE(), INTERVAL @days DAY)
                                AND DATE(open_time) < CURDATE()
                                AND TIME_TO_SEC(TIMEDIFF(close_time, open_time)) >= 86000
                                GROUP BY symbol";
                            
                            command.Parameters.Clear();
                            command.Parameters.AddWithValue("@days", days);
                            
                            using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                            {
                                while (await reader.ReadAsync(cancellationToken))
                                {
                                    var symbol = reader.GetString("symbol");
                                    var highPrice = reader.GetDecimal("max_high");
                                    var lowPrice = reader.GetDecimal("min_low");
                                    var openPrice = reader.GetDecimal("first_open");
                                    
                                    periodStats[symbol] = (highPrice, lowPrice, openPrice);
                                }
                            }
                        }
                        
                        result[days] = periodStats;
                    }
                }
                
                LogManager.Log("Database", $"批量计算了{daysPeriods.Length}个周期的价格统计数据");
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, "批量计算价格统计数据失败");
                throw;
            }
            
            return result;
        }

        /// <summary>
        /// 直接从数据库计算单日统计数据
        /// </summary>
        public async Task<DailyMarketStats> GetDailyStatsDirectAsync(DateTime date, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureConnectionStringLoadedAsync();
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    using (var command = connection.CreateCommand())
                    {
                        // 直接在数据库中计算单日统计
                        command.CommandText = @"
                            SELECT 
                                SUM(CASE 
                                    WHEN ((close_price - open_price) / open_price * 100) > 0.1 THEN 1 
                                    ELSE 0 
                                END) as rising_count,
                                SUM(CASE 
                                    WHEN ((close_price - open_price) / open_price * 100) < -0.1 THEN 1 
                                    ELSE 0 
                                END) as falling_count,
                                SUM(CASE 
                                    WHEN ((close_price - open_price) / open_price * 100) >= -0.1 
                                        AND ((close_price - open_price) / open_price * 100) <= 0.1 THEN 1 
                                    ELSE 0 
                                END) as flat_count,
                                SUM(quote_volume) as daily_volume
                            FROM kline_data 
                            WHERE DATE(open_time) = DATE(@date)
                            AND TIME_TO_SEC(TIMEDIFF(close_time, open_time)) >= 86000
                            AND open_price > 0";
                        
                        command.Parameters.AddWithValue("@date", date.Date);
                        
                        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                        {
                            if (await reader.ReadAsync(cancellationToken))
                            {
                                var stats = new DailyMarketStats
                                {
                                    Date = date.Date,
                                    RisingCount = reader.IsDBNull("rising_count") ? 0 : reader.GetInt32("rising_count"),
                                    FallingCount = reader.IsDBNull("falling_count") ? 0 : reader.GetInt32("falling_count"),
                                    FlatCount = reader.IsDBNull("flat_count") ? 0 : reader.GetInt32("flat_count"),
                                    DailyVolume = reader.IsDBNull("daily_volume") ? 0m : reader.GetDecimal("daily_volume")
                                };
                                
                                LogManager.Log("Database", $"直接计算{date:yyyy-MM-dd}统计数据: 上涨{stats.RisingCount}, 下跌{stats.FallingCount}, 平盘{stats.FlatCount}");
                                return stats;
                            }
                        }
                    }
                }
                
                LogManager.Log("Database", $"计算{date:yyyy-MM-dd}统计数据失败：没有找到数据");
                return null;
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, $"计算{date:yyyy-MM-dd}统计数据失败");
                return null;
            }
        }
    }
}