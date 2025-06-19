using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;
using TCClient.Models;
using Microsoft.Extensions.Logging;

namespace TCClient.Services
{
    /// <summary>
    /// 策略追踪数据库服务
    /// </summary>
    public class StrategyTrackingService
    {
        private readonly ILogger<StrategyTrackingService> _logger;
        private readonly string _connectionString;

        public StrategyTrackingService(ILogger<StrategyTrackingService> logger)
        {
            _logger = logger;
            // 独立的数据库连接字符串
            _connectionString = "Server=45.153.131.217;Port=3306;Database=localdb;Uid=root;Pwd=Xj774913@;CharSet=utf8mb4;";
        }

        /// <summary>
        /// 测试数据库连接
        /// </summary>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();
                _logger.LogInformation("策略追踪数据库连接成功");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "策略追踪数据库连接失败");
                return false;
            }
        }

        /// <summary>
        /// 获取所有产品组合
        /// </summary>
        public async Task<List<ProductGroup>> GetProductGroupsAsync()
        {
            var groups = new List<ProductGroup>();
            
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
                    SELECT group_id, group_name, symbols, created_at, status 
                    FROM product_groups 
                    WHERE status = 1
                    ORDER BY group_name";

                using var command = new MySqlCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var group = new ProductGroup
                    {
                        Id = Convert.ToInt32(reader["group_id"]),
                        GroupName = Convert.ToString(reader["group_name"]) ?? string.Empty,
                        Symbols = reader["symbols"]?.ToString() ?? string.Empty,
                        CreatedAt = reader["created_at"] != DBNull.Value ? Convert.ToDateTime(reader["created_at"]) : DateTime.Now,
                        UpdatedAt = reader["created_at"] != DBNull.Value ? Convert.ToDateTime(reader["created_at"]) : DateTime.Now
                    };
                    groups.Add(group);
                }

                _logger.LogInformation($"获取到 {groups.Count} 个产品组合");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取产品组合失败");
                throw;
            }

            return groups;
        }

        /// <summary>
        /// 获取指定合约的策略详情数据
        /// </summary>
        public async Task<List<StrategyDetail>> GetStrategyDetailsAsync(string symbol, DateTime? startTime = null, DateTime? endTime = null)
        {
            var details = new List<StrategyDetail>();

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    SELECT stratage_id, product_code, close, total_profit, stratage_time 
                    FROM stratage_detail_gate 
                    WHERE product_code = @symbol";

                var parameters = new List<MySqlParameter>
                {
                    new MySqlParameter("@symbol", symbol)
                };

                if (startTime.HasValue)
                {
                    sql += " AND stratage_time >= @startTime";
                    parameters.Add(new MySqlParameter("@startTime", startTime.Value));
                }

                if (endTime.HasValue)
                {
                    sql += " AND stratage_time <= @endTime";
                    parameters.Add(new MySqlParameter("@endTime", endTime.Value));
                }

                sql += " ORDER BY stratage_time";

                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddRange(parameters.ToArray());

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var detail = new StrategyDetail
                    {
                        Id = Convert.ToInt32(reader["stratage_id"]),
                        Symbol = Convert.ToString(reader["product_code"]) ?? string.Empty,
                        Close = Convert.ToDecimal(reader["close"]),
                        TotalProfit = Convert.ToDecimal(reader["total_profit"]),
                        Timestamp = Convert.ToDateTime(reader["stratage_time"])
                    };
                    details.Add(detail);
                }

                _logger.LogInformation($"获取到合约 {symbol} 的 {details.Count} 条策略详情数据");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"获取合约 {symbol} 的策略详情失败");
                throw;
            }

            return details;
        }

        /// <summary>
        /// 获取组合的净值曲线数据
        /// </summary>
        public async Task<List<NetValuePoint>> GetGroupNetValueAsync(List<string> symbols, DateTime? startTime = null, DateTime? endTime = null)
        {
            var netValuePoints = new List<NetValuePoint>();

            if (symbols == null || symbols.Count == 0)
            {
                return netValuePoints;
            }

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                // 设置默认时间范围：最近60天
                if (!startTime.HasValue)
                {
                    startTime = DateTime.Now.AddDays(-60);
                }
                if (!endTime.HasValue)
                {
                    endTime = DateTime.Now;
                }

                // 构建IN子句的参数
                var symbolParams = new List<string>();
                var parameters = new List<MySqlParameter>();
                
                for (int i = 0; i < symbols.Count; i++)
                {
                    var paramName = $"@symbol{i}";
                    symbolParams.Add(paramName);
                    parameters.Add(new MySqlParameter(paramName, symbols[i]));
                }

                // 使用30分钟间隔采样数据
                var sql = $@"
                    SELECT 
                        DATE_FORMAT(stratage_time, '%Y-%m-%d %H:%i:00') as sample_time,
                        SUM(total_profit) as total_profit
                    FROM stratage_detail_gate 
                    WHERE product_code IN ({string.Join(",", symbolParams)})
                      AND stratage_time >= @startTime 
                      AND stratage_time <= @endTime
                      AND MINUTE(stratage_time) % 30 = 0
                    GROUP BY DATE_FORMAT(stratage_time, '%Y-%m-%d %H:%i:00')
                    ORDER BY sample_time";

                parameters.Add(new MySqlParameter("@startTime", startTime.Value));
                parameters.Add(new MySqlParameter("@endTime", endTime.Value));

                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddRange(parameters.ToArray());

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var point = new NetValuePoint
                    {
                        Time = Convert.ToDateTime(reader["sample_time"]),
                        Value = Convert.ToDecimal(reader["total_profit"])
                    };
                    netValuePoints.Add(point);
                }

                _logger.LogInformation($"获取到组合净值曲线 {netValuePoints.Count} 个数据点（最近60天，30分钟间隔）");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取组合净值曲线失败");
                throw;
            }

            return netValuePoints;
        }

        /// <summary>
        /// 获取单个合约的净值曲线数据
        /// </summary>
        public async Task<List<NetValuePoint>> GetSymbolNetValueAsync(string symbol, DateTime? startTime = null, DateTime? endTime = null)
        {
            var result = new List<NetValuePoint>();
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            // 默认最近60天
            if (!startTime.HasValue) startTime = DateTime.Now.AddDays(-60);
            if (!endTime.HasValue) endTime = DateTime.Now;

            var sql = @"
                SELECT stratage_time, total_profit
                FROM stratage_detail_gate
                WHERE product_code = @symbol
                  AND stratage_time >= @startTime
                  AND stratage_time <= @endTime
                ORDER BY stratage_time";
            var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@symbol", symbol);
            command.Parameters.AddWithValue("@startTime", startTime.Value);
            command.Parameters.AddWithValue("@endTime", endTime.Value);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new NetValuePoint
                {
                    Time = Convert.ToDateTime(reader["stratage_time"]),
                    Value = Convert.ToDecimal(reader["total_profit"]),
                    Symbol = symbol
                });
            }
            return result;
        }

        /// <summary>
        /// 获取测试数据（用于调试）
        /// </summary>
        public async Task<List<ProductGroup>> GetTestDataAsync()
        {
            await Task.Delay(1000); // 模拟网络延迟
            
            var testGroups = new List<ProductGroup>
            {
                new ProductGroup
                {
                    Id = 1,
                    GroupName = "测试组合1",
                    Symbols = "BTCUSDT#ETHUSDT#ADAUSDT",
                    CreatedAt = DateTime.Now.AddDays(-30),
                    UpdatedAt = DateTime.Now.AddDays(-1)
                },
                new ProductGroup
                {
                    Id = 2,
                    GroupName = "测试组合2", 
                    Symbols = "BNBUSDT#SOLUSDT#DOTUSDT",
                    CreatedAt = DateTime.Now.AddDays(-20),
                    UpdatedAt = DateTime.Now.AddHours(-2)
                },
                new ProductGroup
                {
                    Id = 3,
                    GroupName = "测试组合3",
                    Symbols = "LINKUSDT#UNIUSDT#AVAXUSDT",
                    CreatedAt = DateTime.Now.AddDays(-10),
                    UpdatedAt = DateTime.Now.AddMinutes(-30)
                }
            };

            _logger.LogInformation($"返回 {testGroups.Count} 个测试产品组合");
            return testGroups;
        }

        /// <summary>
        /// 获取测试净值数据
        /// </summary>
        public async Task<List<NetValuePoint>> GetTestNetValueAsync(List<string> symbols)
        {
            await Task.Delay(500);
            
            var random = new Random();
            var testData = new List<NetValuePoint>();
            var baseTime = DateTime.Now.AddDays(-60); // 最近60天
            
            // 生成60天 * 24小时 * 2个30分钟间隔 = 2880个数据点
            for (int i = 0; i < 2880; i++)
            {
                var time = baseTime.AddMinutes(i * 30); // 每30分钟一个数据点
                
                // 确保时间点在30分钟间隔上（0分或30分）
                var adjustedTime = new DateTime(time.Year, time.Month, time.Day, time.Hour, 
                    time.Minute >= 30 ? 30 : 0, 0);
                
                testData.Add(new NetValuePoint
                {
                    Time = adjustedTime,
                    Value = 1000 + (decimal)(random.NextDouble() * 500 - 250) + i * 0.1m, // 添加趋势
                    Symbol = string.Join(",", symbols)
                });
            }
            
            _logger.LogInformation($"返回 {testData.Count} 个测试净值数据点（最近60天，30分钟间隔）");
            return testData;
        }

        /// <summary>
        /// 获取测试合约净值数据
        /// </summary>
        public async Task<List<NetValuePoint>> GetTestSymbolNetValueAsync(string symbol)
        {
            await Task.Delay(300);
            
            var random = new Random();
            var testData = new List<NetValuePoint>();
            var baseTime = DateTime.Now.AddDays(-60); // 最近60天
            
            // 生成60天 * 24小时 * 2个30分钟间隔 = 2880个数据点
            for (int i = 0; i < 2880; i++)
            {
                var time = baseTime.AddMinutes(i * 30); // 每30分钟一个数据点
                
                // 确保时间点在30分钟间隔上（0分或30分）
                var adjustedTime = new DateTime(time.Year, time.Month, time.Day, time.Hour, 
                    time.Minute >= 30 ? 30 : 0, 0);
                
                testData.Add(new NetValuePoint
                {
                    Time = adjustedTime,
                    Value = 500 + (decimal)(random.NextDouble() * 200 - 100) + i * 0.05m, // 添加趋势
                    Symbol = symbol
                });
            }
            
            _logger.LogInformation($"返回合约 {symbol} 的 {testData.Count} 个测试净值数据点（最近60天，30分钟间隔）");
            return testData;
        }

        /// <summary>
        /// 获取市场成交额变化数据
        /// </summary>
        public async Task<List<MarketVolumePoint>> GetMarketVolumeAsync(DateTime? startTime = null, DateTime? endTime = null)
        {
            var volumePoints = new List<MarketVolumePoint>();

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                // 设置默认时间范围：最近30天
                if (!startTime.HasValue)
                {
                    startTime = DateTime.Now.AddDays(-30);
                }
                if (!endTime.HasValue)
                {
                    endTime = DateTime.Now;
                }

                // 查询市场总成交额变化（按天聚合）
                var sql = @"
                    SELECT 
                        DATE(created_at) as trade_date,
                        SUM(volume_24h) as total_volume_24h,
                        SUM(volume_7d) as total_volume_7d,
                        SUM(volume_30d) as total_volume_30d
                    FROM exchange_volume_history
                    WHERE created_at >= @startTime 
                      AND created_at <= @endTime
                    GROUP BY DATE(created_at)
                    ORDER BY trade_date";

                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddRange(new[]
                {
                    new MySqlParameter("@startTime", startTime.Value),
                    new MySqlParameter("@endTime", endTime.Value)
                });

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var point = new MarketVolumePoint
                    {
                        Time = Convert.ToDateTime(reader["trade_date"]),
                        Volume24h = Convert.ToDecimal(reader["total_volume_24h"]),
                        Volume7d = Convert.ToDecimal(reader["total_volume_7d"]),
                        Volume30d = Convert.ToDecimal(reader["total_volume_30d"]),
                        ExchangeId = 0, // 聚合数据
                        ExchangeName = "市场总计"
                    };
                    volumePoints.Add(point);
                }

                _logger.LogInformation($"获取到市场成交额数据 {volumePoints.Count} 个数据点（最近30天）");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取市场成交额数据失败");
                throw;
            }

            return volumePoints;
        }

        /// <summary>
        /// 获取测试市场成交额数据
        /// </summary>
        public async Task<List<MarketVolumePoint>> GetTestMarketVolumeAsync()
        {
            await Task.Delay(300);
            
            var random = new Random();
            var testData = new List<MarketVolumePoint>();
            var baseTime = DateTime.Now.AddDays(-30);
            
            for (int i = 0; i < 30; i++)
            {
                testData.Add(new MarketVolumePoint
                {
                    Time = baseTime.AddDays(i),
                    Volume24h = 50000000000 + (decimal)(random.NextDouble() * 20000000000 - 10000000000), // 500亿±100亿
                    Volume7d = 350000000000 + (decimal)(random.NextDouble() * 70000000000 - 35000000000), // 3500亿±350亿
                    Volume30d = 1500000000000 + (decimal)(random.NextDouble() * 300000000000 - 150000000000), // 1.5万亿±1500亿
                    ExchangeId = 0,
                    ExchangeName = "市场总计"
                });
            }
            
            _logger.LogInformation($"返回 {testData.Count} 个测试市场成交额数据点（最近30天）");
            return testData;
        }

        /// <summary>
        /// 获取合约状态列表（方向、累计盈利、半小时盈亏）
        /// </summary>
        public async Task<List<SymbolStatus>> GetSymbolStatusListAsync(List<string> symbols)
        {
            var result = new List<SymbolStatus>();
            if (symbols == null || symbols.Count == 0) return result;
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();
                var sql = $@"SELECT product_code, stg, total_profit, winner FROM stratage_latest_gate WHERE product_code IN ({string.Join(",", symbols.Select((s, i) => "@symbol" + i))})";
                var command = new MySqlCommand(sql, connection);
                for (int i = 0; i < symbols.Count; i++)
                {
                    command.Parameters.AddWithValue($"@symbol{i}", symbols[i]);
                }
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    result.Add(new SymbolStatus
                    {
                        Symbol = reader["product_code"].ToString() ?? string.Empty,
                        Stg = reader["stg"] == DBNull.Value ? 0 : Convert.ToInt32(reader["stg"]),
                        TotalProfit = reader["total_profit"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["total_profit"]),
                        Winner = reader["winner"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["winner"])
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取合约状态失败");
            }
            return result;
        }

        /// <summary>
        /// 获取所有可用合约的状态列表（用于候选池）
        /// 只显示24小时内有更新的合约
        /// </summary>
        public async Task<List<SymbolStatus>> GetAllSymbolStatusAsync()
        {
            var result = new List<SymbolStatus>();
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();
                
                // 计算24小时前的时间
                var cutoffTime = DateTime.Now.AddHours(-24);
                
                // 尝试多个可能的时间字段名称
                var timeFields = new[] { "update_time", "updated_at", "timestamp", "create_time", "created_at", "stratage_time" };
                var sql = "";
                
                // 先检查表结构，找到时间字段
                foreach (var timeField in timeFields)
                {
                    try
                    {
                        var testSql = $@"
                            SELECT product_code, stg, total_profit, winner, {timeField}
                            FROM stratage_latest_gate 
                            WHERE {timeField} >= @cutoffTime
                            ORDER BY total_profit DESC 
                            LIMIT 1";
                        
                        using var testCommand = new MySqlCommand(testSql, connection);
                        testCommand.Parameters.Add(new MySqlParameter("@cutoffTime", cutoffTime));
                        using var testReader = await testCommand.ExecuteReaderAsync();
                        testReader.Close();
                        
                        // 如果没有异常，说明找到了正确的时间字段
                        sql = $@"
                            SELECT product_code, stg, total_profit, winner, {timeField}
                            FROM stratage_latest_gate 
                            WHERE {timeField} >= @cutoffTime
                            ORDER BY total_profit DESC";
                        break;
                    }
                    catch
                    {
                        // 继续尝试下一个字段
                        continue;
                    }
                }
                
                // 如果没有找到时间字段，使用原始查询但记录警告
                if (string.IsNullOrEmpty(sql))
                {
                    _logger.LogWarning("未找到时间字段，无法过滤24小时内的数据，返回所有数据");
                    sql = @"
                        SELECT product_code, stg, total_profit, winner 
                        FROM stratage_latest_gate 
                        ORDER BY total_profit DESC";
                }
                
                using var command = new MySqlCommand(sql, connection);
                if (sql.Contains("@cutoffTime"))
                {
                    command.Parameters.Add(new MySqlParameter("@cutoffTime", cutoffTime));
                }
                using var reader = await command.ExecuteReaderAsync();
                
                var random = new Random();
                while (await reader.ReadAsync())
                {
                    result.Add(new SymbolStatus
                    {
                        Symbol = reader["product_code"].ToString() ?? string.Empty,
                        Stg = reader["stg"] == DBNull.Value ? 0 : Convert.ToInt32(reader["stg"]),
                        TotalProfit = reader["total_profit"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["total_profit"]),
                        Winner = reader["winner"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["winner"]),
                        // 暂时使用随机成交额，后续可以从其他表获取真实数据
                        Volume24h = (decimal)(random.NextDouble() * 1000000000) // 0-10亿的随机成交额
                    });
                }
                
                var logMessage = sql.Contains("@cutoffTime") 
                    ? $"获取到 {result.Count} 个24小时内更新的合约状态数据（过滤时间: {cutoffTime:yyyy-MM-dd HH:mm:ss}）"
                    : $"获取到 {result.Count} 个合约状态数据（未进行时间过滤）";
                _logger.LogInformation(logMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取所有合约状态失败");
                throw;
            }
            return result;
        }

        /// <summary>
        /// 保存产品组合到数据库
        /// </summary>
        public async Task<bool> SaveProductGroupAsync(string groupName, List<string> symbols)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();
                
                // 使用#分隔符连接合约列表
                var symbolsString = string.Join("#", symbols);
                
                var sql = @"
                    INSERT INTO product_groups (group_name, symbols, created_at, status) 
                    VALUES (@groupName, @symbols, @createdAt, 1)";
                
                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddRange(new[]
                {
                    new MySqlParameter("@groupName", groupName),
                    new MySqlParameter("@symbols", symbolsString),
                    new MySqlParameter("@createdAt", DateTime.Now)
                });
                
                var rowsAffected = await command.ExecuteNonQueryAsync();
                
                _logger.LogInformation($"成功保存产品组合: {groupName}, 合约: {symbolsString}, 影响行数: {rowsAffected}");
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"保存产品组合失败: {groupName}");
                throw;
            }
        }

        /// <summary>
        /// 更新产品组合
        /// </summary>
        public async Task<bool> UpdateProductGroupAsync(int groupId, string groupName, List<string> symbols)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();
                
                // 使用#分隔符连接合约列表
                var symbolsString = string.Join("#", symbols);
                
                var sql = @"
                    UPDATE product_groups 
                    SET group_name = @groupName, symbols = @symbols, created_at = @updatedAt 
                    WHERE group_id = @groupId";
                
                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddRange(new[]
                {
                    new MySqlParameter("@groupId", groupId),
                    new MySqlParameter("@groupName", groupName),
                    new MySqlParameter("@symbols", symbolsString),
                    new MySqlParameter("@updatedAt", DateTime.Now)
                });
                
                var rowsAffected = await command.ExecuteNonQueryAsync();
                
                _logger.LogInformation($"成功更新产品组合: {groupName}, 合约: {symbolsString}, 影响行数: {rowsAffected}");
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"更新产品组合失败: {groupName}");
                throw;
            }
        }

        /// <summary>
        /// 删除产品组合
        /// </summary>
        public async Task<bool> DeleteProductGroupAsync(int groupId)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = @"
                    UPDATE product_groups 
                    SET status = 0 
                    WHERE group_id = @groupId";
                
                using var command = new MySqlCommand(sql, connection);
                command.Parameters.Add(new MySqlParameter("@groupId", groupId));
                
                var rowsAffected = await command.ExecuteNonQueryAsync();
                
                _logger.LogInformation($"成功删除产品组合: ID={groupId}, 影响行数: {rowsAffected}");
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"删除产品组合失败: ID={groupId}");
                throw;
            }
        }
    }
} 