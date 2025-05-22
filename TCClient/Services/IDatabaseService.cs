using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TCClient.Models;

namespace TCClient.Services
{
    public interface IDatabaseService
    {
        // 数据库连接相关
        bool TestConnection(string connectionString);
        Task<bool> ConnectAsync(string connectionString, CancellationToken cancellationToken = default);
        Task<bool> DisconnectAsync();
        Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
        Task<List<DatabaseInfo>> GetDatabasesAsync(CancellationToken cancellationToken = default);

        // 交易账户相关
        Task<IEnumerable<TradingAccount>> GetTradingAccountsAsync(CancellationToken cancellationToken = default);
        Task<TradingAccount> GetTradingAccountByIdAsync(long accountId, CancellationToken cancellationToken = default);
        Task<bool> AddTradingAccountAsync(TradingAccount account, CancellationToken cancellationToken = default);
        Task<bool> UpdateTradingAccountAsync(TradingAccount account, CancellationToken cancellationToken = default);
        Task<bool> DeleteTradingAccountAsync(int accountId, CancellationToken cancellationToken = default);

        // 模拟订单相关
        Task<List<SimulationOrder>> GetSimulationOrdersAsync(int accountId, CancellationToken cancellationToken = default);
        Task<bool> AddSimulationOrderAsync(SimulationOrder order, CancellationToken cancellationToken = default);
        Task<bool> UpdateSimulationOrderAsync(SimulationOrder order, CancellationToken cancellationToken = default);
        Task<bool> DeleteSimulationOrderAsync(int orderId, CancellationToken cancellationToken = default);

        // 数据库连接相关
        Task<bool> ValidateUserAsync(string username, string password, CancellationToken cancellationToken = default);
        Task<bool> CreateUserAsync(string username, string password, CancellationToken cancellationToken = default);

        // 账户相关
        Task<List<Account>> GetUserAccountsAsync(string username, CancellationToken cancellationToken = default);
        Task<Account> GetAccountByIdAsync(int accountId, CancellationToken cancellationToken = default);
        Task<bool> CreateAccountAsync(Account account, CancellationToken cancellationToken = default);
        Task<bool> UpdateAccountAsync(Account account, CancellationToken cancellationToken = default);
        Task<bool> DeleteAccountAsync(int accountId, CancellationToken cancellationToken = default);

        // 持仓相关
        Task<List<Position>> GetPositionsAsync(int accountId, CancellationToken cancellationToken = default);
        Task<Position> GetPositionByIdAsync(int positionId, CancellationToken cancellationToken = default);
        Task<bool> CreatePositionAsync(Position position, CancellationToken cancellationToken = default);
        Task<bool> UpdatePositionAsync(Position position, CancellationToken cancellationToken = default);
        Task<bool> ClosePositionAsync(int positionId, CancellationToken cancellationToken = default);

        // 委托相关
        Task<List<Order>> GetOrdersAsync(int accountId);
        Task<Order> GetOrderByIdAsync(int orderId);
        Task<bool> CreateOrderAsync(Order order);
        Task<bool> UpdateOrderAsync(Order order);
        Task<bool> CancelOrderAsync(int orderId);

        // 成交相关
        Task<List<Trade>> GetTradesAsync(int accountId);
        Task<Trade> GetTradeByIdAsync(int tradeId);
        Task<bool> CreateTradeAsync(Trade trade);
        Task<List<Trade>> GetTradesByOrderIdAsync(string orderId);

        // 排行榜相关
        Task<List<RankingData>> GetRankingDataAsync(DateTime startDate, DateTime endDate);

        // 推仓相关
        Task<PositionPushInfo> GetOpenPushInfoAsync(long accountId, string contract);
        Task<PositionPushInfo> CreatePushInfoAsync(long accountId, string contract);
        Task<long> InsertSimulationOrderAsync(SimulationOrder order);
        Task InsertPushOrderRelAsync(long pushId, long orderId);
        Task<PushSummaryInfo> GetPushSummaryInfoAsync(long accountId, string contract);
        Task<decimal> GetAccountAvailableRiskAmountAsync(long accountId);

        // 用户-账户关联相关
        Task AddUserTradingAccountAsync(long userId, long accountId, bool isDefault, CancellationToken cancellationToken = default);
        Task SetUserDefaultAccountAsync(long userId, long accountId, CancellationToken cancellationToken = default);
    }
} 