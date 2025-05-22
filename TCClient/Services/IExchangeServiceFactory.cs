using TCClient.Models;

namespace TCClient.Services
{
    public interface IExchangeServiceFactory
    {
        IExchangeService CreateExchangeService(TradingAccount account);
    }
} 