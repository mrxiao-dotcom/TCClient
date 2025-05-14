using System.Collections.Generic;
using System.Threading.Tasks;
using TCClient.Models;

namespace TCClient.Services
{
    public interface IUserService
    {
        Task<bool> ValidateUserAsync(string username, string password);
        Task<bool> CreateUserAsync(string username, string password);
        Task<IEnumerable<TradingAccount>> GetTradingAccountsAsync();
        Task<bool> CreateTradingAccountAsync(TradingAccount account);
        Task<bool> UpdateTradingAccountAsync(TradingAccount account);
        Task<bool> DeleteTradingAccountAsync(long accountId);
    }
} 