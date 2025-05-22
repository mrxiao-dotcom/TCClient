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

namespace TCClient.Services
{
    public class MySqlDatabaseService : IDatabaseService, IUserService
    {
        private readonly LocalConfigService _configService;
        private string _connectionString;

        public MySqlDatabaseService()
        {
            LogManager.Log("Database", "MySqlDatabaseService 构造函数开始执行...");
            _configService = new LocalConfigService();
            LogManager.Log("Database", "MySqlDatabaseService 构造函数执行完成");
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
                
                // 构建连接字符串
                _connectionString = $"Server={server};Port={port};Database={database};User ID={username};Password={password};";
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

                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
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
                catch (Exception ex)
                {
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
                await EnsureConnectionStringLoadedAsync();
                LogManager.Log("Database", "连接字符串已加载，准备连接数据库");

                using var connection = new MySqlConnection(_connectionString);
                LogManager.Log("Database", "正在打开数据库连接...");
                await connection.OpenAsync(cancellationToken);
                LogManager.Log("Database", "数据库连接已打开");

                using var cmd = new MySqlCommand(
                    "SELECT password_hash FROM users WHERE username = @username",
                    connection);
                cmd.Parameters.AddWithValue("@username", username);
                LogManager.Log("Database", $"执行查询: SELECT password_hash FROM users WHERE username = '{username}'");

                var result = await cmd.ExecuteScalarAsync(cancellationToken);
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
            }
            catch (OperationCanceledException)
            {
                LogManager.Log("Database", "用户验证操作被取消");
                throw;
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
                            SELECT t.*, COALESCE(uta.is_default, 0) as is_default
                            FROM trading_accounts t
                            LEFT JOIN user_trading_accounts uta ON t.id = uta.account_id AND uta.user_id = @user_id
                            WHERE t.is_active = 1
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
                            SELECT t.*, COALESCE(uta.is_default, 0) as is_default
                            FROM trading_accounts t
                            LEFT JOIN user_trading_accounts uta ON t.id = uta.account_id AND uta.user_id = @user_id
                            WHERE t.id = @account_id AND t.is_active = 1
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
                             create_time, update_time, is_default)
                    VALUES 
                            (@account_name, @binance_account_id, @api_key, @api_secret, @api_passphrase,
                             @equity, @initial_equity, @opportunity_count, @status, @is_active,
                             @create_time, @update_time, @is_default);
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
                            command.Parameters.AddWithValue("@is_default", account.IsDefault);

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
                        LogManager.LogException("Database", ex, "创建交易账户失败");
                        await transaction.RollbackAsync(cancellationToken);
                        throw;
                    }
                }
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
                                update_time = @update_time,
                                is_default = @is_default
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
                            command.Parameters.AddWithValue("@is_default", account.IsDefault);

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
                    // 做多：止损金额 = (开仓价 - 止损价) * 数量 * 合约面值
                    realPnL = (order.EntryPrice - order.InitialStopLoss) * (decimal)order.Quantity * order.ContractSize;
                }
                else if (order.Direction.ToLower() == "sell")
                {
                    // 做空：止损金额 = (止损价 - 开仓价) * 数量 * 合约面值
                    realPnL = (order.InitialStopLoss - order.EntryPrice) * (decimal)order.Quantity * order.ContractSize;
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
                            status, open_time, real_pnl, floating_pnl, current_price, last_update_time
                        ) VALUES (
                            @order_id, @account_id, @contract, @contract_size,
                            @direction, @quantity, @entry_price, @initial_stop_loss,
                            @current_stop_loss, @leverage, @margin, @total_value,
                            @status, @open_time, @real_pnl, 0, @current_price, NOW()
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
                    command.Parameters.AddWithValue("@real_pnl", realPnL);
                    command.Parameters.AddWithValue("@current_price", order.EntryPrice);

                    orderId = Convert.ToInt64(await command.ExecuteScalarAsync());
                }

                // 3. 检查是否已存在推仓信息
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
                        // 4. 创建新的推仓信息
                        command.CommandText = @"
                            INSERT INTO position_push_info 
                            (account_id, contract, status, create_time) 
                            VALUES 
                            (@account_id, @contract, 'open', NOW());
                            SELECT LAST_INSERT_ID();";
                        
                        pushId = Convert.ToInt64(await command.ExecuteScalarAsync());
                    }
                }

                // 5. 创建推仓与订单关联
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
                                LastUpdateTime = reader.IsDBNull(reader.GetOrdinal("last_update_time")) ? null : (DateTime?)reader.GetDateTime("last_update_time"),
                                RealPnL = reader.IsDBNull(reader.GetOrdinal("real_pnl")) ? null : (decimal?)reader.GetDecimal("real_pnl")
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
                                        status, open_time, real_pnl
                                    ) VALUES (
                                        @order_id, @account_id, @contract, @contract_size,
                                        @direction, @quantity, @entry_price, @initial_stop_loss,
                                        @current_stop_loss, @leverage, @margin, @total_value,
                                        @status, @open_time, @real_pnl
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
                                    command.Parameters.AddWithValue("@real_pnl", DBNull.Value); // 添加real_pnl字段，初始化为NULL

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
            await EnsureConnectionStringLoadedAsync();
                
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

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (!await reader.ReadAsync())
                            {
                                LogManager.Log("Database", $"未找到推仓信息 - 账户ID: {accountId}, 合约: {contract}");
                                return new PushSummaryInfo
                                {
                                    PushId = 0,
                                    Contract = contract,
                                    CreateTime = DateTime.Now,
                                    Status = "open",
                                    TotalFloatingPnL = 0m,
                                    TotalRealPnL = 0m,
                                    TotalOrderCount = 0,
                                    OpenOrderCount = 0,
                                    ClosedOrderCount = 0,
                                    RiskAmount = 0m,
                                    AvailableRiskAmount = 0m,
                                    Orders = new List<SimulationOrder>()
                                };
                            }

                            LogManager.Log("Database", "成功读取推仓基本信息");
                            summary = new PushSummaryInfo
                            {
                                PushId = reader.GetInt64("id"),
                                Contract = reader.GetString("contract"),
                                CreateTime = reader.GetDateTime("create_time"),
                                Status = reader.GetString("status"),
                                TotalOrderCount = reader.GetInt32("total_orders"),
                                OpenOrderCount = reader.GetInt32("open_orders"),
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
                        LastUpdateTime = reader.IsDBNull(reader.GetOrdinal("last_update_time")) ? null : (DateTime?)reader.GetDateTime("last_update_time"),
                                    RealPnL = reader.IsDBNull(reader.GetOrdinal("real_profit")) ? 0m : reader.GetDecimal("real_profit")
                    };
                    summary.Orders.Add(order);
                                LogManager.Log("Database", $"成功读取订单: ID={order.Id}, 状态={order.Status}, 数量={order.Quantity}");
                            }
                        }
                    }
                }

                // 第三个连接：获取可用风险金
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
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
                                summary.AvailableRiskAmount = opportunityCount > 0 ? equity / opportunityCount : 0;
                                LogManager.Log("Database", $"获取可用风险金: {summary.AvailableRiskAmount}");
                            }
                        }
                    }
                }

                // 计算占用风险金
            summary.RiskAmount = summary.Orders.Where(o => o.Status == "open").Sum(o => o.Margin);
                LogManager.Log("Database", $"计算占用风险金: {summary.RiskAmount}");

                LogManager.Log("Database", "成功获取推仓信息");
            return summary;
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, "获取推仓信息失败");
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

        // 添加GetUserAsync方法实现
        public async Task<User> GetUserAsync(string username, CancellationToken cancellationToken = default)
        {
            try
            {
                LogManager.Log("Database", $"开始获取用户信息: {username}");
                await EnsureConnectionStringLoadedAsync();
                
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT * FROM users WHERE username = @username";
                        command.Parameters.AddWithValue("@username", username);
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
                                
                                LogManager.Log("Database", $"成功获取用户信息: ID={user.Id}, 用户名={user.Username}");
                                return user;
                            }
                        }
                    }
                }
                
                LogManager.Log("Database", $"未找到用户: {username}");
                return null;
            }
            catch (Exception ex)
            {
                LogManager.LogException("Database", ex, $"获取用户 {username} 信息失败");
                throw;
            }
        }
    }
} 