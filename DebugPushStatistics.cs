using System;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;

class DebugPushStatistics
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
                SELECT p.*, ta.account_name 
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
                var id = pushInfoReader.GetInt64("id");
                var accountId = pushInfoReader.GetInt64("account_id");
                var accountName = pushInfoReader.IsDBNull("account_name") ? "未知" : pushInfoReader.GetString("account_name");
                var contract = pushInfoReader.GetString("contract");
                var status = pushInfoReader.GetString("status");
                var createTime = pushInfoReader.GetDateTime("create_time");
                
                Console.WriteLine($"{id}\t{accountId}\t{accountName}\t\t{contract}\t\t{status}\t{createTime:yyyy-MM-dd HH:mm}");
            }
            
            await pushInfoReader.CloseAsync();
            
            // 2. 检查推仓订单关联表
            Console.WriteLine("\n=== 检查推仓订单关联表 ===");
            var relSql = @"
                SELECT r.*, p.contract, o.order_id, o.status as order_status
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
                var relId = relReader.GetInt64("id");
                var pushId = relReader.GetInt64("push_id");
                var orderId = relReader.GetInt64("order_id");
                var contract = relReader.IsDBNull("contract") ? "未知" : relReader.GetString("contract");
                var orderStatus = relReader.IsDBNull("order_status") ? "未知" : relReader.GetString("order_status");
                var createTime = relReader.GetDateTime("create_time");
                
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
                var id = orderReader.GetInt64("id");
                var orderId = orderReader.GetString("order_id");
                var accountId = orderReader.GetInt64("account_id");
                var contract = orderReader.GetString("contract");
                var status = orderReader.GetString("status");
                var floatingPnl = orderReader.IsDBNull("floating_pnl") ? 0m : orderReader.GetDecimal("floating_pnl");
                var realProfit = orderReader.IsDBNull("real_profit") ? 0m : orderReader.GetDecimal("real_profit");
                var openTime = orderReader.GetDateTime("open_time");
                
                Console.WriteLine($"{id}\t{orderId.Substring(0, Math.Min(20, orderId.Length))}...\t{accountId}\t{contract}\t\t{status}\t{floatingPnl:N2}\t\t{realProfit:N2}\t\t{openTime:yyyy-MM-dd HH:mm}");
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
                var id = accountReader.GetInt64("id");
                var accountName = accountReader.GetString("account_name");
                var equity = accountReader.GetDecimal("equity");
                var isActive = accountReader.GetInt32("is_active");
                
                Console.WriteLine($"{id}\t{accountName}\t\t\t{equity:N2}\t\t{(isActive == 1 ? "是" : "否")}");
            }
            
            await accountReader.CloseAsync();
            
            // 5. 测试推仓统计查询
            Console.WriteLine("\n=== 测试推仓统计查询 ===");
            Console.Write("请输入要查询的账户ID: ");
            var inputAccountId = Console.ReadLine();
            
            if (long.TryParse(inputAccountId, out long accountId))
            {
                var testSql = @"
                    SELECT p.*, 
                           COUNT(o.id) as total_orders,
                           SUM(CASE WHEN o.status = 'open' THEN 1 ELSE 0 END) as open_orders,
                           SUM(CASE WHEN o.status = 'closed' THEN 1 ELSE 0 END) as closed_orders
                    FROM position_push_info p
                    LEFT JOIN position_push_order_rel r ON p.id = r.push_id
                    LEFT JOIN simulation_orders o ON r.order_id = o.id
                    WHERE p.account_id = @accountId
                    GROUP BY p.id, p.contract, p.account_id, p.create_time, p.status, p.close_time
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
                    var pushId = testReader.GetInt64("id");
                    var contract = testReader.GetString("contract");
                    var status = testReader.GetString("status");
                    var totalOrders = testReader.GetInt32("total_orders");
                    var openOrders = testReader.GetInt32("open_orders");
                    var closedOrders = testReader.GetInt32("closed_orders");
                    var createTime = testReader.GetDateTime("create_time");
                    
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