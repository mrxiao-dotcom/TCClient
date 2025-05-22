using System.Threading.Tasks;
using TCClient.Models;

namespace TCClient.Services
{
    public interface IAccountService
    {
        Task<TradingAccount> GetAccountByIdAsync(int accountId);
        Task<bool> UpdateAccountAsync(TradingAccount account);
        Task<bool> DeleteAccountAsync(int accountId);
        Task<bool> SetDefaultAccountAsync(int accountId);
    }
} 