using System;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;

class InsertTestData
{
    static async Task Main(string[] args)
    {
        // 远程数据库连接字符串
        string connectionString = "Server=154.23.181.75;Port=3306;Database=ordermanager;Uid=root;Pwd=Xj774913@;";
        
        try
        {
            Console.WriteLine("开始连接远程数据库...");
            
            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            
            Console.WriteLine("数据库连接成功！");
            
            // 首先创建表（如果不存在）
            var createTableSql = @"
                CREATE TABLE IF NOT EXISTS `daily_ranking` (
                    `id` INT(10) NOT NULL AUTO_INCREMENT,
                    `date` DATE NOT NULL COMMENT '日期',
                    `top_gainers` TEXT NOT NULL COMMENT '涨幅前十',
                    `top_losers` TEXT NOT NULL COMMENT '跌幅前十',
                    `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
                    `updated_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '更新时间',
                    PRIMARY KEY (`id`) USING BTREE,
                    UNIQUE INDEX `uk_date` (`date`) USING BTREE
                )
                COMMENT='每日合约涨跌幅排名表'
                ENGINE=InnoDB;";
                
            using var createCommand = new MySqlCommand(createTableSql, connection);
            await createCommand.ExecuteNonQueryAsync();
            Console.WriteLine("表创建/检查完成");
            
            // 清空现有数据
            var deleteSql = "DELETE FROM daily_ranking WHERE date >= '2024-01-01'";
            using var deleteCommand = new MySqlCommand(deleteSql, connection);
            var deletedRows = await deleteCommand.ExecuteNonQueryAsync();
            Console.WriteLine($"清空了 {deletedRows} 条旧数据");
            
            // 插入测试数据
            var testData = new[]
            {
                new { 
                    Date = "CURDATE()", 
                    Gainers = "1#BTCUSDT#5.23|2#ETHUSDT#4.87|3#BNBUSDT#3.45|4#ADAUSDT#2.98|5#SOLUSDT#2.76|6#XRPUSDT#2.34|7#DOTUSDT#1.98|8#LINKUSDT#1.76|9#LTCUSDT#1.45|10#AVAXUSDT#1.23",
                    Losers = "1#DOGEUSDT#-3.45|2#SHIBUSDT#-2.98|3#MATICUSDT#-2.76|4#UNIUSDT#-2.34|5#ATOMUSDT#-1.98|6#FILUSDT#-1.76|7#TRXUSDT#-1.45|8#ETCUSDT#-1.23|9#XLMUSDT#-0.98|10#VETUSDT#-0.76"
                },
                new { 
                    Date = "DATE_SUB(CURDATE(), INTERVAL 1 DAY)", 
                    Gainers = "1#ETHUSDT#6.12|2#BNBUSDT#5.34|3#ADAUSDT#4.56|4#SOLUSDT#3.78|5#XRPUSDT#3.21|6#DOTUSDT#2.87|7#LINKUSDT#2.43|8#LTCUSDT#2.01|9#AVAXUSDT#1.89|10#BTCUSDT#1.67",
                    Losers = "1#SHIBUSDT#-4.23|2#DOGEUSDT#-3.87|3#MATICUSDT#-3.45|4#UNIUSDT#-3.12|5#ATOMUSDT#-2.89|6#FILUSDT#-2.56|7#TRXUSDT#-2.23|8#ETCUSDT#-1.90|9#XLMUSDT#-1.67|10#VETUSDT#-1.34"
                },
                new { 
                    Date = "DATE_SUB(CURDATE(), INTERVAL 2 DAY)", 
                    Gainers = "1#SOLUSDT#7.89|2#ADAUSDT#6.45|3#DOTUSDT#5.67|4#LINKUSDT#4.23|5#AVAXUSDT#3.89|6#BTCUSDT#3.45|7#ETHUSDT#2.98|8#BNBUSDT#2.56|9#XRPUSDT#2.12|10#LTCUSDT#1.78",
                    Losers = "1#MATICUSDT#-5.67|2#UNIUSDT#-4.89|3#ATOMUSDT#-4.23|4#DOGEUSDT#-3.78|5#SHIBUSDT#-3.34|6#FILUSDT#-2.89|7#TRXUSDT#-2.45|8#ETCUSDT#-2.01|9#XLMUSDT#-1.78|10#VETUSDT#-1.45"
                },
                new { 
                    Date = "DATE_SUB(CURDATE(), INTERVAL 7 DAY)", 
                    Gainers = "1#AVAXUSDT#8.45|2#LINKUSDT#7.23|3#DOTUSDT#6.78|4#SOLUSDT#5.89|5#ADAUSDT#5.34|6#XRPUSDT#4.67|7#LTCUSDT#4.12|8#BNBUSDT#3.78|9#ETHUSDT#3.23|10#BTCUSDT#2.89",
                    Losers = "1#ATOMUSDT#-6.78|2#UNIUSDT#-5.89|3#MATICUSDT#-5.23|4#FILUSDT#-4.67|5#TRXUSDT#-4.12|6#DOGEUSDT#-3.78|7#SHIBUSDT#-3.34|8#ETCUSDT#-2.89|9#XLMUSDT#-2.45|10#VETUSDT#-2.01"
                },
                new { 
                    Date = "DATE_SUB(CURDATE(), INTERVAL 14 DAY)", 
                    Gainers = "1#LTCUSDT#9.23|2#XRPUSDT#8.67|3#BNBUSDT#7.89|4#ETHUSDT#7.12|5#BTCUSDT#6.45|6#AVAXUSDT#5.78|7#LINKUSDT#5.23|8#DOTUSDT#4.67|9#SOLUSDT#4.12|10#ADAUSDT#3.78",
                    Losers = "1#FILUSDT#-7.89|2#TRXUSDT#-6.78|3#ATOMUSDT#-6.23|4#UNIUSDT#-5.67|5#MATICUSDT#-5.12|6#ETCUSDT#-4.56|7#DOGEUSDT#-4.01|8#SHIBUSDT#-3.67|9#XLMUSDT#-3.23|10#VETUSDT#-2.89"
                }
            };
            
            int insertedCount = 0;
            foreach (var data in testData)
            {
                var insertSql = $@"
                    INSERT INTO daily_ranking (date, top_gainers, top_losers) 
                    VALUES ({data.Date}, @gainers, @losers)
                    ON DUPLICATE KEY UPDATE 
                    top_gainers = VALUES(top_gainers),
                    top_losers = VALUES(top_losers),
                    updated_at = CURRENT_TIMESTAMP";
                    
                using var insertCommand = new MySqlCommand(insertSql, connection);
                insertCommand.Parameters.AddWithValue("@gainers", data.Gainers);
                insertCommand.Parameters.AddWithValue("@losers", data.Losers);
                
                await insertCommand.ExecuteNonQueryAsync();
                insertedCount++;
                Console.WriteLine($"插入第 {insertedCount} 条数据");
            }
            
            Console.WriteLine($"成功插入 {insertedCount} 条测试数据");
            
            // 验证数据
            var selectSql = @"
                SELECT 
                    id,
                    date,
                    SUBSTRING(top_gainers, 1, 50) as top_gainers_preview,
                    SUBSTRING(top_losers, 1, 50) as top_losers_preview
                FROM daily_ranking 
                ORDER BY date DESC 
                LIMIT 10";
                
            using var selectCommand = new MySqlCommand(selectSql, connection);
            using var reader = await selectCommand.ExecuteReaderAsync();
            
            Console.WriteLine("\n验证插入的数据:");
            Console.WriteLine("ID\t日期\t\t涨幅榜预览\t\t\t跌幅榜预览");
            Console.WriteLine("".PadRight(100, '-'));
            
            while (await reader.ReadAsync())
            {
                var id = reader.GetInt32("id");
                var date = reader.GetDateTime("date");
                var gainers = reader.GetString("top_gainers_preview");
                var losers = reader.GetString("top_losers_preview");
                
                Console.WriteLine($"{id}\t{date:yyyy-MM-dd}\t{gainers}\t{losers}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"错误: {ex.Message}");
            Console.WriteLine($"详细信息: {ex}");
        }
        
        Console.WriteLine("\n按任意键退出...");
        Console.ReadKey();
    }
} 