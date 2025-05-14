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

namespace TCClient.Services
{
    public class MySqlDatabaseService : IDatabaseService, IUserService
    {
        private readonly LocalConfigService _configService;
        private string _connectionString;
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "TCClient_Database.log");

        private static void LogToFile(string message)
        {
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
        }

        public MySqlDatabaseService()
        {
            LogToFile("MySqlDatabaseService 构造函数开始执行...");
            _configService = new LocalConfigService();
            LogToFile("MySqlDatabaseService 构造函数执行完成");
        }

        private async Task LoadConnectionStringAsync()
        {
            LogToFile("开始加载数据库连接字符串...");
            if (string.IsNullOrEmpty(_connectionString))
            {
                try
                {
                    var connections = await _configService.LoadDatabaseConnections();
                    LogToFile($"加载到 {connections.Count} 个数据库连接配置");
                    if (connections.Count > 0)
                    {
                        _connectionString = GetConnectionString(connections[0]);
                        LogToFile($"已设置连接字符串，服务器: {connections[0].Server}, 数据库: {connections[0].Database}");
                    }
                    else
                    {
                        LogToFile("警告：没有可用的数据库连接配置");
                    }
                }
                catch (Exception ex)
                {
                    LogToFile($"加载数据库连接配置时发生错误: {ex.Message}");
                    LogToFile($"错误详情: {ex}");
                    throw;
                }
            }
            LogToFile("数据库连接字符串加载完成");
        }

        private string GetConnectionString(DatabaseConnection connection)
        {
            LogToFile($"构建连接字符串 - 服务器: {connection.Server}, 端口: {connection.Port}, 数据库: {connection.Database}");
            
            // 使用最简单的连接字符串格式
            var connectionString = $"Server={connection.Server};Port={connection.Port};Database={connection.Database};Uid={connection.Username};Pwd={connection.Password};";
            
            LogToFile($"构建的连接字符串: {connectionString}");
            return connectionString;
        }

        private async Task EnsureConnectionStringLoadedAsync()
        {
            LogToFile("确保连接字符串已加载...");
            if (string.IsNullOrEmpty(_connectionString))
            {
                await LoadConnectionStringAsync();
            }
            LogToFile("连接字符串检查完成");
        }

        public async Task<bool> TestConnectionAsync()
        {
            LogToFile("开始测试数据库连接...");
            MySqlConnection connection = null;
            
            try
            {
                await EnsureConnectionStringLoadedAsync();
                
                if (string.IsNullOrEmpty(_connectionString))
                {
                    LogToFile("错误：数据库连接字符串未设置");
                    throw new InvalidOperationException("数据库连接字符串未设置");
                }

                // 解析连接字符串以获取服务器信息
                var builder = new MySqlConnectionStringBuilder(_connectionString);
                LogToFile($"连接信息 - 服务器: {builder.Server}, 端口: {builder.Port}, 数据库: {builder.Database}, 用户: {builder.UserID}");
                
                // 检查 DNS 解析
                try
                {
                    LogToFile("尝试解析服务器地址...");
                    var addresses = System.Net.Dns.GetHostAddresses(builder.Server);
                    foreach (var address in addresses)
                    {
                        LogToFile($"解析到的IP地址: {address}");
                    }
                }
                catch (Exception dnsEx)
                {
                    LogToFile($"DNS解析失败: {dnsEx.Message}");
                }

                // 先尝试 ping 服务器
                try
                {
                    using (var ping = new System.Net.NetworkInformation.Ping())
                    {
                        LogToFile("尝试 ping 服务器...");
                        var reply = await ping.SendPingAsync(builder.Server, 5000);
                        LogToFile($"Ping 服务器结果: {reply.Status}, 延迟: {reply.RoundtripTime}ms");
                        
                        if (reply.Status != System.Net.NetworkInformation.IPStatus.Success)
                        {
                            LogToFile("服务器 ping 失败，可能是网络问题");
                            return false;
                        }
                    }
                }
                catch (Exception pingEx)
                {
                    LogToFile($"Ping 服务器失败: {pingEx.Message}");
                    return false;
                }

                // 尝试 telnet 测试端口
                try
                {
                    using (var tcpClient = new System.Net.Sockets.TcpClient())
                    {
                        LogToFile($"尝试连接端口 {builder.Port}...");
                        var connectTask = tcpClient.ConnectAsync(builder.Server, (int)builder.Port);
                        if (await Task.WhenAny(connectTask, Task.Delay(5000)) != connectTask)
                        {
                            LogToFile("端口连接超时");
                            return false;
                        }
                        await connectTask;
                        LogToFile("端口连接成功");
                        
                        // 检查连接状态
                        LogToFile($"TCP连接状态: {tcpClient.Connected}");
                        LogToFile($"本地端点: {tcpClient.Client.LocalEndPoint}");
                        LogToFile($"远程端点: {tcpClient.Client.RemoteEndPoint}");
                        
                        tcpClient.Close();
                    }
                }
                catch (Exception telnetEx)
                {
                    LogToFile($"端口连接失败: {telnetEx.Message}");
                    return false;
                }

                // 尝试数据库连接
                try
                {
                    LogToFile("正在尝试数据库连接...");
                    connection = new MySqlConnection(_connectionString);
                    
                    // 使用同步方式连接，但带超时控制
                    using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10)))
                    {
                        var connectTask = Task.Run(() => 
                        {
                            try 
                            {
                                LogToFile("开始打开连接...");
                                LogToFile($"连接字符串: {_connectionString}");
                                connection.Open();
                                LogToFile("连接已打开");
                                
                                // 获取连接信息
                                using (var command = connection.CreateCommand())
                                {
                                    command.CommandText = "SELECT CONNECTION_ID(), VERSION(), DATABASE(), USER()";
                                    using (var reader = command.ExecuteReader())
                                    {
                                        if (reader.Read())
                                        {
                                            LogToFile($"连接ID: {reader.GetInt32(0)}");
                                            LogToFile($"MySQL版本: {reader.GetString(1)}");
                                            LogToFile($"当前数据库: {reader.GetString(2)}");
                                            LogToFile($"当前用户: {reader.GetString(3)}");
                                        }
                                    }
                                }
                                
                                return true;
                            }
                            catch (Exception ex)
                            {
                                LogToFile($"连接过程中发生错误: {ex.Message}");
                                if (ex is MySqlException mySqlEx)
                                {
                                    LogToFile($"MySQL 错误代码: {mySqlEx.Number}");
                                    LogToFile($"MySQL 错误消息: {mySqlEx.Message}");
                                    LogToFile($"MySQL 错误详情: {mySqlEx}");
                                    
                                    // 检查是否是特定错误
                                    switch (mySqlEx.Number)
                                    {
                                        case 1045: // Access denied
                                            LogToFile("错误：访问被拒绝，请检查用户名和密码");
                                            break;
                                        case 1042: // Can't get hostname
                                            LogToFile("错误：无法获取主机名");
                                            break;
                                        case 2003: // Can't connect to server
                                            LogToFile("错误：无法连接到服务器，请检查服务器地址和端口");
                                            break;
                                        case 2013: // Lost connection
                                            LogToFile("错误：连接丢失");
                                            break;
                                        case 2026: // SSL connection error
                                            LogToFile("错误：SSL连接错误");
                                            break;
                                    }
                                }
                                throw;
                            }
                        }, cts.Token);

                        if (await Task.WhenAny(connectTask, Task.Delay(10000, cts.Token)) != connectTask)
                        {
                            LogToFile("数据库连接超时");
                            throw new TimeoutException("数据库连接超时");
                        }

                        await connectTask;
                    }

                    LogToFile("数据库连接成功");
                    return true;
                }
                catch (MySqlException ex)
                {
                    LogToFile($"MySQL 错误: {ex.Message}");
                    LogToFile($"错误代码: {ex.Number}");
                    LogToFile($"错误详情: {ex}");
                    return false;
                }
                catch (Exception ex)
                {
                    LogToFile($"连接测试时发生错误: {ex.Message}");
                    LogToFile($"错误详情: {ex}");
                    return false;
                }
            }
            finally
            {
                if (connection != null)
                {
                    try
                    {
                        if (connection.State != System.Data.ConnectionState.Closed)
                        {
                            LogToFile("正在关闭连接...");
                            connection.Close();
                            LogToFile("连接已关闭");
                        }
                        connection.Dispose();
                        LogToFile("连接已释放");
                    }
                    catch (Exception ex)
                    {
                        LogToFile($"关闭连接时发生错误: {ex.Message}");
                    }
                }
            }
        }

        public async Task<bool> ValidateUserAsync(string username, string password)
        {
            LogToFile($"开始验证用户: {username}");
            try
            {
                await EnsureConnectionStringLoadedAsync();
                LogToFile("连接字符串已加载，准备连接数据库");

                using var connection = new MySqlConnection(_connectionString);
                LogToFile("正在打开数据库连接...");
                await connection.OpenAsync();
                LogToFile("数据库连接已打开");

                using var cmd = new MySqlCommand(
                    "SELECT password_hash FROM users WHERE username = @username",
                    connection);
                cmd.Parameters.AddWithValue("@username", username);
                LogToFile($"执行查询: SELECT password_hash FROM users WHERE username = '{username}'");

                var result = await cmd.ExecuteScalarAsync();
                if (result == null)
                {
                    LogToFile($"用户 {username} 不存在");
                    return false;
                }

                var storedHash = result.ToString();
                var inputHash = HashPassword(password);
                LogToFile($"密码验证: 存储的哈希值 = {storedHash}, 输入的哈希值 = {inputHash}");
                
                var isValid = storedHash == inputHash;
                LogToFile($"密码验证结果: {(isValid ? "成功" : "失败")}");
                return isValid;
            }
            catch (MySqlException ex)
            {
                LogToFile($"MySQL 错误: {ex.Message}");
                LogToFile($"错误代码: {ex.Number}");
                LogToFile($"错误详情: {ex}");
                throw;
            }
            catch (Exception ex)
            {
                LogToFile($"验证用户时发生错误: {ex.Message}");
                LogToFile($"错误详情: {ex}");
                throw;
            }
        }

        public async Task<bool> CreateUserAsync(string username, string password)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                // 检查用户名是否已存在
                using var checkCmd = new MySqlCommand(
                    "SELECT COUNT(*) FROM users WHERE username = @username",
                    connection);
                checkCmd.Parameters.AddWithValue("@username", username);

                var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
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

                await cmd.ExecuteNonQueryAsync();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"创建用户失败：{ex}");
                throw;
            }
        }

        // 账户相关方法
        public async Task<List<Account>> GetUserAccountsAsync(string username)
        {
            var accounts = new List<Account>();
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT a.* FROM trading_accounts a
                        INNER JOIN user_trading_accounts ua ON a.id = ua.account_id
                        INNER JOIN users u ON ua.user_id = u.id
                        WHERE u.username = @username AND a.is_active = 1";

                    command.Parameters.AddWithValue("@username", username);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            accounts.Add(new Account
                            {
                                Id = reader.GetInt32("id"),
                                AccountName = reader.GetString("account_name"),
                                Type = reader.GetString("type"),
                                Balance = reader.GetDecimal("balance"),
                                Equity = reader.GetDecimal("equity"),
                                Margin = reader.GetDecimal("margin"),
                                RiskRatio = reader.GetDecimal("risk_ratio"),
                                CreateTime = reader.GetDateTime("create_time"),
                                LastLoginTime = reader.IsDBNull("last_login_time") ? null : (DateTime?)reader.GetDateTime("last_login_time"),
                                IsActive = reader.GetBoolean("is_active"),
                                Description = reader.IsDBNull("description") ? null : reader.GetString("description")
                            });
                        }
                    }
                }
            }
            return accounts;
        }

        public async Task<Account> GetAccountByIdAsync(int accountId)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM trading_accounts WHERE id = @id";
                    command.Parameters.AddWithValue("@id", accountId);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return new Account
                            {
                                Id = reader.GetInt32("id"),
                                AccountName = reader.GetString("account_name"),
                                Type = reader.GetString("type"),
                                Balance = reader.GetDecimal("balance"),
                                Equity = reader.GetDecimal("equity"),
                                Margin = reader.GetDecimal("margin"),
                                RiskRatio = reader.GetDecimal("risk_ratio"),
                                CreateTime = reader.GetDateTime("create_time"),
                                LastLoginTime = reader.IsDBNull("last_login_time") ? null : (DateTime?)reader.GetDateTime("last_login_time"),
                                IsActive = reader.GetBoolean("is_active"),
                                Description = reader.IsDBNull("description") ? null : reader.GetString("description")
                            };
                        }
                    }
                }
            }
            return null;
        }

        public async Task<bool> CreateAccountAsync(Account account)
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
                                INSERT INTO trading_accounts (
                                    account_name, type, balance, equity, margin, risk_ratio,
                                    create_time, is_active, description
                                ) VALUES (
                                    @account_name, @type, @balance, @equity, @margin, @risk_ratio,
                                    @create_time, @is_active, @description
                                )";

                            command.Parameters.AddWithValue("@account_name", account.AccountName);
                            command.Parameters.AddWithValue("@type", account.Type);
                            command.Parameters.AddWithValue("@balance", account.Balance);
                            command.Parameters.AddWithValue("@equity", account.Equity);
                            command.Parameters.AddWithValue("@margin", account.Margin);
                            command.Parameters.AddWithValue("@risk_ratio", account.RiskRatio);
                            command.Parameters.AddWithValue("@create_time", DateTime.Now);
                            command.Parameters.AddWithValue("@is_active", true);
                            command.Parameters.AddWithValue("@description", (object)account.Description ?? DBNull.Value);

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

        public async Task<bool> UpdateAccountAsync(Account account)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        UPDATE trading_accounts SET
                            account_name = @account_name,
                            type = @type,
                            balance = @balance,
                            equity = @equity,
                            margin = @margin,
                            risk_ratio = @risk_ratio,
                            last_login_time = @last_login_time,
                            is_active = @is_active,
                            description = @description
                        WHERE id = @id";

                    command.Parameters.AddWithValue("@id", account.Id);
                    command.Parameters.AddWithValue("@account_name", account.AccountName);
                    command.Parameters.AddWithValue("@type", account.Type);
                    command.Parameters.AddWithValue("@balance", account.Balance);
                    command.Parameters.AddWithValue("@equity", account.Equity);
                    command.Parameters.AddWithValue("@margin", account.Margin);
                    command.Parameters.AddWithValue("@risk_ratio", account.RiskRatio);
                    command.Parameters.AddWithValue("@last_login_time", (object)account.LastLoginTime ?? DBNull.Value);
                    command.Parameters.AddWithValue("@is_active", account.IsActive);
                    command.Parameters.AddWithValue("@description", (object)account.Description ?? DBNull.Value);

                    var result = await command.ExecuteNonQueryAsync();
                    return result > 0;
                }
            }
        }

        public async Task<bool> DeleteAccountAsync(int accountId)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "UPDATE trading_accounts SET is_active = 0 WHERE id = @id";
                    command.Parameters.AddWithValue("@id", accountId);

                    var result = await command.ExecuteNonQueryAsync();
                    return result > 0;
                }
            }
        }

        // 持仓相关方法
        public async Task<List<Position>> GetPositionsAsync(int accountId)
        {
            var positions = new List<Position>();
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM positions WHERE account_id = @account_id AND status = 'active'";
                    command.Parameters.AddWithValue("@account_id", accountId);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
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

        public async Task<Position> GetPositionByIdAsync(int positionId)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM positions WHERE id = @id";
                    command.Parameters.AddWithValue("@id", positionId);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
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

        public async Task<bool> CreatePositionAsync(Position position)
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

        public async Task<bool> UpdatePositionAsync(Position position)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
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

                    var result = await command.ExecuteNonQueryAsync();
                    return result > 0;
                }
            }
        }

        public async Task<bool> ClosePositionAsync(int positionId)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        UPDATE positions SET
                            status = 'closed',
                            close_time = @close_time
                        WHERE id = @id";

                    command.Parameters.AddWithValue("@id", positionId);
                    command.Parameters.AddWithValue("@close_time", DateTime.Now);

                    var result = await command.ExecuteNonQueryAsync();
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
        public async Task<IEnumerable<TradingAccount>> GetTradingAccountsAsync()
        {
            var accounts = new List<TradingAccount>();
            
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var query = @"
                    SELECT id, name, type, status, create_time, update_time, 
                           description, balance, available_balance, risk_limit, current_risk
                    FROM trading_accounts
                    ORDER BY create_time DESC;";

                using (var command = new MySqlCommand(query, connection))
                {
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            accounts.Add(new TradingAccount
                            {
                                Id = reader.GetInt64("id"),
                                AccountName = reader.GetString("name"),
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
                                // IsDefault 由业务逻辑赋值
                            });
                        }
                    }
                }
            }

            return accounts;
        }

        public async Task<bool> CreateTradingAccountAsync(TradingAccount account)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var query = @"
                    INSERT INTO trading_accounts 
                    (account_name, binance_account_id, api_key, api_secret, api_passphrase, equity, initial_equity, opportunity_count, status, is_active, create_time, update_time)
                    VALUES 
                    (@account_name, @binance_account_id, @api_key, @api_secret, @api_passphrase, @equity, @initial_equity, @opportunity_count, @status, @is_active, @create_time, @update_time);";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@account_name", account.AccountName);
                    command.Parameters.AddWithValue("@binance_account_id", account.BinanceAccountId ?? "");
                    command.Parameters.AddWithValue("@api_key", account.ApiKey ?? "");
                    command.Parameters.AddWithValue("@api_secret", account.ApiSecret ?? "");
                    command.Parameters.AddWithValue("@api_passphrase", account.ApiPassphrase ?? "");
                    command.Parameters.AddWithValue("@equity", account.Equity);
                    command.Parameters.AddWithValue("@initial_equity", account.InitialEquity);
                    command.Parameters.AddWithValue("@opportunity_count", account.OpportunityCount);
                    command.Parameters.AddWithValue("@status", account.Status);
                    command.Parameters.AddWithValue("@is_active", account.IsActive);
                    command.Parameters.AddWithValue("@create_time", DateTime.Now);
                    command.Parameters.AddWithValue("@update_time", DateTime.Now);

                    var result = await command.ExecuteNonQueryAsync();
                    return result > 0;
                }
            }
        }

        public async Task<bool> UpdateTradingAccountAsync(TradingAccount account)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();

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
                    command.Parameters.AddWithValue("@id", account.Id);
                    command.Parameters.AddWithValue("@account_name", account.AccountName);
                    command.Parameters.AddWithValue("@binance_account_id", account.BinanceAccountId ?? "");
                    command.Parameters.AddWithValue("@api_key", account.ApiKey ?? "");
                    command.Parameters.AddWithValue("@api_secret", account.ApiSecret ?? "");
                    command.Parameters.AddWithValue("@api_passphrase", account.ApiPassphrase ?? "");
                    command.Parameters.AddWithValue("@equity", account.Equity);
                    command.Parameters.AddWithValue("@initial_equity", account.InitialEquity);
                    command.Parameters.AddWithValue("@opportunity_count", account.OpportunityCount);
                    command.Parameters.AddWithValue("@status", account.Status);
                    command.Parameters.AddWithValue("@is_active", account.IsActive);
                    command.Parameters.AddWithValue("@update_time", DateTime.Now);

                    var result = await command.ExecuteNonQueryAsync();
                    return result > 0;
                }
            }
        }

        public async Task<bool> DeleteTradingAccountAsync(long accountId)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var query = "DELETE FROM trading_accounts WHERE id = @id;";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@id", accountId);

                    var result = await command.ExecuteNonQueryAsync();
                    return result > 0;
                }
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

                LogToFile($"成功获取排行榜数据，共 {result.Count} 条记录");
            }
            catch (Exception ex)
            {
                LogToFile($"获取排行榜数据失败: {ex.Message}");
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
            using var command = connection.CreateCommand();
            command.CommandText = @"INSERT INTO simulation_orders
                (order_id, account_id, contract, contract_size, direction, quantity, entry_price, initial_stop_loss, current_stop_loss, leverage, margin, total_value, status, open_time)
                VALUES (@orderId, @accountId, @contract, @contractSize, @direction, @quantity, @entryPrice, @initialStopLoss, @currentStopLoss, @leverage, @margin, @totalValue, @status, @openTime);
                SELECT LAST_INSERT_ID();";
            command.Parameters.AddWithValue("@orderId", order.OrderId);
            command.Parameters.AddWithValue("@accountId", order.AccountId);
            command.Parameters.AddWithValue("@contract", order.Contract);
            command.Parameters.AddWithValue("@contractSize", order.ContractSize);
            command.Parameters.AddWithValue("@direction", order.Direction);
            command.Parameters.AddWithValue("@quantity", order.Quantity);
            command.Parameters.AddWithValue("@entryPrice", order.EntryPrice);
            command.Parameters.AddWithValue("@initialStopLoss", order.InitialStopLoss);
            command.Parameters.AddWithValue("@currentStopLoss", order.CurrentStopLoss);
            command.Parameters.AddWithValue("@leverage", order.Leverage);
            command.Parameters.AddWithValue("@margin", order.Margin);
            command.Parameters.AddWithValue("@totalValue", order.TotalValue);
            command.Parameters.AddWithValue("@status", order.Status);
            command.Parameters.AddWithValue("@openTime", order.OpenTime);
            var id = Convert.ToInt64(await command.ExecuteScalarAsync());
            return id;
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

        public async Task AddUserTradingAccountAsync(long userId, long accountId, bool isDefault)
        {
            await EnsureConnectionStringLoadedAsync();
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = await connection.BeginTransactionAsync();
            try
            {
                if (isDefault)
                {
                    // 先将该用户其它账户的is_default全部置为0
                    using var clearCmd = connection.CreateCommand();
                    clearCmd.Transaction = transaction;
                    clearCmd.CommandText = "UPDATE user_trading_accounts SET is_default=0 WHERE user_id=@userId";
                    clearCmd.Parameters.AddWithValue("@userId", userId);
                    await clearCmd.ExecuteNonQueryAsync();
                }
                // 插入新关联
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = @"INSERT INTO user_trading_accounts (user_id, account_id, is_default, create_time, update_time) VALUES (@userId, @accountId, @isDefault, NOW(), NOW())";
                cmd.Parameters.AddWithValue("@userId", userId);
                cmd.Parameters.AddWithValue("@accountId", accountId);
                cmd.Parameters.AddWithValue("@isDefault", isDefault ? 1 : 0);
                await cmd.ExecuteNonQueryAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task SetUserDefaultAccountAsync(long userId, long accountId)
        {
            await EnsureConnectionStringLoadedAsync();
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = await connection.BeginTransactionAsync();
            try
            {
                // 先全部清零
                using var clearCmd = connection.CreateCommand();
                clearCmd.Transaction = transaction;
                clearCmd.CommandText = "UPDATE user_trading_accounts SET is_default=0 WHERE user_id=@userId";
                clearCmd.Parameters.AddWithValue("@userId", userId);
                await clearCmd.ExecuteNonQueryAsync();
                // 再设置目标为1
                using var setCmd = connection.CreateCommand();
                setCmd.Transaction = transaction;
                setCmd.CommandText = "UPDATE user_trading_accounts SET is_default=1 WHERE user_id=@userId AND account_id=@accountId";
                setCmd.Parameters.AddWithValue("@userId", userId);
                setCmd.Parameters.AddWithValue("@accountId", accountId);
                await setCmd.ExecuteNonQueryAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
} 