using System;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;

class Program
{
    static async Task Main(string[] args)
    {
        // 数据库连接字符串
        string connectionString = "Server=154.23.181.75;Port=3306;Database=ordermanager;Uid=root;Pwd=Xj774913@;";
        
        try
        {
            Console.WriteLine("开始检查推仓统计数据...");
            
            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            
            Console.WriteLine("数据库连接成功！");
            
            // 1. 检查推仓信息表
            Console.WriteLine("\n=== 检查推仓信息表 ===");
            var pushInfoSql = @"
                SELECT p.id, p.account_id, p.contract, p.status, p.create_time, ta.account_name 
                FROM position_push_info p
                LEFT JOIN trading_accounts ta ON p.account_id = ta.id
                ORDER BY p.create_time DESC 
                LIMIT 10";
                
            using var pushInfoCmd = new MySqlCommand(pushInfoSql, connection);
            using var pushInfoReader = await pushInfoCmd.ExecuteReaderAsync();
            
            Console.WriteLine("推仓信息记录:");
            Console.WriteLine("ID\t账户ID\t账户名\t\t合约\t\t状态\t创建时间");
            Console.WriteLine("".PadRight(80, '-'));
            
            while (await pushInfoReader.ReadAsync())
            {
                var id = pushInfoReader.GetInt64(0);
                var accId = pushInfoReader.GetInt64(1);
                var contract = pushInfoReader.GetString(2);
                var status = pushInfoReader.GetString(3);
                var createTime = pushInfoReader.GetDateTime(4);
                var accountName = pushInfoReader.IsDBNull(5) ? "未知" : pushInfoReader.GetString(5);
                
                Console.WriteLine($"{id}\t{accId}\t{accountName}\t\t{contract}\t\t{status}\t{createTime:yyyy-MM-dd HH:mm}");
            }
            
            await pushInfoReader.CloseAsync();
            
            // 2. 检查推仓订单关联表
            Console.WriteLine("\n=== 检查推仓订单关联表 ===");
            var relSql = @"
                SELECT r.id, r.push_id, r.order_id, r.create_time, p.contract, o.order_id as order_uuid, o.status as order_status
                FROM position_push_order_rel r
                LEFT JOIN position_push_info p ON r.push_id = p.id
                LEFT JOIN simulation_orders o ON r.order_id = o.id
                ORDER BY r.create_time DESC 
                LIMIT 10";
                
            using var relCmd = new MySqlCommand(relSql, connection);
            using var relReader = await relCmd.ExecuteReaderAsync();
            
            Console.WriteLine("推仓订单关联记录:");
            Console.WriteLine("关联ID\t推仓ID\t订单ID\t合约\t\t订单状态\t创建时间");
            Console.WriteLine("".PadRight(80, '-'));
            
            while (await relReader.ReadAsync())
            {
                var relId = relReader.GetInt64(0);
                var pushId = relReader.GetInt64(1);
                var orderId = relReader.GetInt64(2);
                var createTime = relReader.GetDateTime(3);
                var contract = relReader.IsDBNull(4) ? "未知" : relReader.GetString(4);
                var orderStatus = relReader.IsDBNull(6) ? "未知" : relReader.GetString(6);
                
                Console.WriteLine($"{relId}\t{pushId}\t{orderId}\t{contract}\t\t{orderStatus}\t\t{createTime:yyyy-MM-dd HH:mm}");
            }
            
            await relReader.CloseAsync();
            
            // 3. 检查模拟订单表
            Console.WriteLine("\n=== 检查模拟订单表 ===");
            var orderSql = @"
                SELECT o.id, o.order_id, o.account_id, o.contract, o.status, 
                       o.floating_pnl, o.real_profit, o.open_time
                FROM simulation_orders o
                ORDER BY o.open_time DESC 
                LIMIT 10";
                
            using var orderCmd = new MySqlCommand(orderSql, connection);
            using var orderReader = await orderCmd.ExecuteReaderAsync();
            
            Console.WriteLine("模拟订单记录:");
            Console.WriteLine("ID\t订单ID\t\t\t\t账户ID\t合约\t\t状态\t浮动盈亏\t实际盈亏\t开仓时间");
            Console.WriteLine("".PadRight(120, '-'));
            
            while (await orderReader.ReadAsync())
            {
                var id = orderReader.GetInt64(0);
                var orderId = orderReader.GetString(1);
                var accId = orderReader.GetInt64(2);
                var contract = orderReader.GetString(3);
                var status = orderReader.GetString(4);
                var floatingPnl = orderReader.IsDBNull(5) ? 0m : orderReader.GetDecimal(5);
                var realProfit = orderReader.IsDBNull(6) ? 0m : orderReader.GetDecimal(6);
                var openTime = orderReader.GetDateTime(7);
                
                Console.WriteLine($"{id}\t{orderId.Substring(0, Math.Min(20, orderId.Length))}...\t{accId}\t{contract}\t\t{status}\t{floatingPnl:N2}\t\t{realProfit:N2}\t\t{openTime:yyyy-MM-dd HH:mm}");
            }
            
            await orderReader.CloseAsync();
            
            // 4. 检查交易账户表
            Console.WriteLine("\n=== 检查交易账户表 ===");
            var accountSql = @"
                SELECT id, account_name, equity, is_active
                FROM trading_accounts
                WHERE is_active = 1
                ORDER BY create_time DESC 
                LIMIT 10";
                
            using var accountCmd = new MySqlCommand(accountSql, connection);
            using var accountReader = await accountCmd.ExecuteReaderAsync();
            
            Console.WriteLine("交易账户记录:");
            Console.WriteLine("ID\t账户名\t\t\t权益\t\t是否激活");
            Console.WriteLine("".PadRight(60, '-'));
            
            while (await accountReader.ReadAsync())
            {
                var id = accountReader.GetInt64(0);
                var accountName = accountReader.GetString(1);
                var equity = accountReader.GetDecimal(2);
                var isActive = accountReader.GetInt32(3);
                
                Console.WriteLine($"{id}\t{accountName}\t\t\t{equity:N2}\t\t{(isActive == 1 ? "是" : "否")}");
            }
            
            await accountReader.CloseAsync();
            
            // 5. 测试推仓统计查询
            Console.WriteLine("\n=== 测试推仓统计查询 ===");
            Console.Write("请输入要查询的账户ID (直接回车使用账户ID 1): ");
            var inputAccountId = Console.ReadLine();
            
            if (string.IsNullOrEmpty(inputAccountId))
            {
                inputAccountId = "1";
            }
            
            if (long.TryParse(inputAccountId, out long accountId))
            {
                var testSql = @"
                    SELECT p.id, p.contract, p.status, p.create_time,
                           COUNT(o.id) as total_orders,
                           SUM(CASE WHEN o.status = 'open' THEN 1 ELSE 0 END) as open_orders,
                           SUM(CASE WHEN o.status = 'closed' THEN 1 ELSE 0 END) as closed_orders
                    FROM position_push_info p
                    LEFT JOIN position_push_order_rel r ON p.id = r.push_id
                    LEFT JOIN simulation_orders o ON r.order_id = o.id
                    WHERE p.account_id = @accountId
                    GROUP BY p.id, p.contract, p.status, p.create_time
                    ORDER BY p.create_time DESC";
                    
                using var testCmd = new MySqlCommand(testSql, connection);
                testCmd.Parameters.AddWithValue("@accountId", accountId);
                using var testReader = await testCmd.ExecuteReaderAsync();
                
                Console.WriteLine($"账户 {accountId} 的推仓统计:");
                Console.WriteLine("推仓ID\t合约\t\t状态\t总订单数\t开仓订单\t已平仓订单\t创建时间");
                Console.WriteLine("".PadRight(100, '-'));
                
                var totalCount = 0;
                while (await testReader.ReadAsync())
                {
                    var pushId = testReader.GetInt64(0);
                    var contract = testReader.GetString(1);
                    var status = testReader.GetString(2);
                    var createTime = testReader.GetDateTime(3);
                    var totalOrders = testReader.GetInt32(4);
                    var openOrders = testReader.GetInt32(5);
                    var closedOrders = testReader.GetInt32(6);
                    
                    Console.WriteLine($"{pushId}\t{contract}\t\t{status}\t{totalOrders}\t\t{openOrders}\t\t{closedOrders}\t\t{createTime:yyyy-MM-dd HH:mm}");
                    totalCount++;
                }
                
                Console.WriteLine($"\n总共找到 {totalCount} 条推仓记录");
            }
            else
            {
                Console.WriteLine("无效的账户ID");
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
