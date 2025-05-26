using System;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;

class TestDatabaseConnection
{
    static async Task Main(string[] args)
    {
        // 请根据实际情况修改连接字符串
        string connectionString = "Server=localhost;Database=tclient;Uid=root;Pwd=your_password;";
        
        try
        {
            Console.WriteLine("开始测试数据库连接...");
            
            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            
            Console.WriteLine("数据库连接成功！");
            
            // 检查表是否存在
            var checkTableSql = @"
                SELECT COUNT(*) 
                FROM information_schema.tables 
                WHERE table_schema = DATABASE() 
                AND table_name = 'daily_ranking'";
                
            using var checkCommand = new MySqlCommand(checkTableSql, connection);
            var tableExists = Convert.ToInt32(await checkCommand.ExecuteScalarAsync()) > 0;
            
            Console.WriteLine($"daily_ranking 表存在: {tableExists}");
            
            if (tableExists)
            {
                // 检查总记录数
                var countSql = "SELECT COUNT(*) FROM daily_ranking";
                using var countCommand = new MySqlCommand(countSql, connection);
                var totalRecords = Convert.ToInt32(await countCommand.ExecuteScalarAsync());
                Console.WriteLine($"daily_ranking 表中总共有 {totalRecords} 条记录");
                
                // 查看最近的几条记录
                var selectSql = @"
                    SELECT 
                        id,
                        date,
                        SUBSTRING(top_gainers, 1, 50) as top_gainers_preview,
                        SUBSTRING(top_losers, 1, 50) as top_losers_preview
                    FROM daily_ranking 
                    ORDER BY date DESC 
                    LIMIT 5";
                    
                using var selectCommand = new MySqlCommand(selectSql, connection);
                using var reader = await selectCommand.ExecuteReaderAsync();
                
                Console.WriteLine("\n最近的记录:");
                Console.WriteLine("ID\t日期\t\t涨幅榜预览\t\t\t跌幅榜预览");
                Console.WriteLine("".PadRight(80, '-'));
                
                while (await reader.ReadAsync())
                {
                    var id = reader.GetInt32("id");
                    var date = reader.GetDateTime("date");
                    var gainers = reader.GetString("top_gainers_preview");
                    var losers = reader.GetString("top_losers_preview");
                    
                    Console.WriteLine($"{id}\t{date:yyyy-MM-dd}\t{gainers}\t{losers}");
                }
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