using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TCClient.Models;

namespace TCClient.Services
{
    public interface IDatabaseService
    {
        // 数据库连接相关
        Task<bool> TestConnectionAsync();
        Task<bool> ValidateUserAsync(string username, string password);
        Task<bool> CreateUserAsync(string username, string password);

        // 账户相关
        Task<List<Account>> GetUserAccountsAsync(string username);
        Task<Account> GetAccountByIdAsync(int accountId);
        Task<bool> CreateAccountAsync(Account account);
        Task<bool> UpdateAccountAsync(Account account);
        Task<bool> DeleteAccountAsync(int accountId);

        // 持仓相关
        Task<List<Position>> GetPositionsAsync(int accountId);
        Task<Position> GetPositionByIdAsync(int positionId);
        Task<bool> CreatePositionAsync(Position position);
        Task<bool> UpdatePositionAsync(Position position);
        Task<bool> ClosePositionAsync(int positionId);

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

        // 用户-账户关联相关
        Task AddUserTradingAccountAsync(long userId, long accountId, bool isDefault);
        Task SetUserDefaultAccountAsync(long userId, long accountId);
    }
} 