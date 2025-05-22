using System.Threading.Tasks;
using System.Linq;
using TCClient.Models;
using TCClient.Utils;

namespace TCClient.Services
{
    public class AccountService : IAccountService
    {
        private readonly IUserService _userService;

        public AccountService(IUserService userService)
        {
            _userService = userService;
        }

        public async Task<TradingAccount> GetAccountByIdAsync(int accountId)
        {
            var accounts = await _userService.GetTradingAccountsAsync();
            return accounts.FirstOrDefault(a => a.Id == accountId);
        }

        public async Task<bool> UpdateAccountAsync(TradingAccount account)
        {
            try
            {
                await _userService.UpdateTradingAccountAsync(account);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> DeleteAccountAsync(int accountId)
        {
            try
            {
                await _userService.DeleteTradingAccountAsync(accountId);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> SetDefaultAccountAsync(int accountId)
        {
            try
            {
                var accounts = await _userService.GetTradingAccountsAsync();
                foreach (var account in accounts)
                {
                    account.IsDefaultAccount = account.Id == accountId;
                    await _userService.UpdateTradingAccountAsync(account);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
} 