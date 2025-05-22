using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TCClient.Models;

namespace TCClient.Services
{
    public interface IUserService
    {
        Task<bool> ValidateUserAsync(string username, string password, CancellationToken cancellationToken = default);
        Task<bool> CreateUserAsync(string username, string password, CancellationToken cancellationToken = default);
        Task<User> GetUserAsync(string username, CancellationToken cancellationToken = default);
        Task<List<Account>> GetUserAccountsAsync(string username, CancellationToken cancellationToken = default);
        Task<IEnumerable<TradingAccount>> GetTradingAccountsAsync(CancellationToken cancellationToken = default);
        Task<bool> CreateTradingAccountAsync(TradingAccount account, CancellationToken cancellationToken = default);
        Task<bool> UpdateTradingAccountAsync(TradingAccount account, CancellationToken cancellationToken = default);
        Task<bool> DeleteTradingAccountAsync(long accountId, CancellationToken cancellationToken = default);
        Task SetUserDefaultAccountAsync(long userId, long accountId, CancellationToken cancellationToken = default);
    }
} 