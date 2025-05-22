using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TCClient.Models;

namespace TCClient.Services
{
    public interface IExchangeService
    {
        /// <summary>
        /// 获取K线数据
        /// </summary>
        /// <param name="symbol">交易对符号</param>
        /// <param name="interval">K线周期（1m, 5m, 15m, 30m, 1h, 2h, 1d）</param>
        /// <param name="limit">获取的K线数量</param>
        /// <returns>K线数据列表</returns>
        Task<List<KLineData>> GetKLineDataAsync(string symbol, string interval, int limit);

        /// <summary>
        /// 获取当前账户信息
        /// </summary>
        /// <returns>账户信息</returns>
        Task<AccountInfo> GetAccountInfoAsync();

        /// <summary>
        /// 下单
        /// </summary>
        /// <param name="symbol">交易对符号</param>
        /// <param name="direction">交易方向（BUY, SELL）</param>
        /// <param name="quantity">交易数量</param>
        /// <param name="price">交易价格</param>
        /// <param name="stopLossPrice">止损价格</param>
        /// <param name="leverage">杠杆倍数</param>
        /// <returns>是否成功</returns>
        Task<bool> PlaceOrderAsync(string symbol, string direction, decimal quantity, decimal price, decimal stopLossPrice, decimal leverage);

        /// <summary>
        /// 取消订单
        /// </summary>
        /// <param name="orderId">订单ID</param>
        /// <returns>是否成功</returns>
        Task<bool> CancelOrderAsync(string orderId);

        /// <summary>
        /// 获取行情
        /// </summary>
        /// <param name="symbol">交易对符号</param>
        /// <returns>行情信息</returns>
        Task<TickerInfo> GetTickerAsync(string symbol);

        /// <summary>
        /// 获取所有合约的行情数据
        /// </summary>
        /// <returns>所有合约的行情信息列表</returns>
        Task<List<TickerInfo>> GetAllTickersAsync();
    }

    public class OrderRequest
    {
        public string Symbol { get; set; }
        public string Side { get; set; }  // BUY, SELL
        public string Type { get; set; }  // LIMIT, MARKET, STOP, STOP_MARKET
        public decimal Quantity { get; set; }
        public decimal? Price { get; set; }
        public decimal? StopPrice { get; set; }
        public string TimeInForce { get; set; }  // GTC, IOC, FOK
        public bool ReduceOnly { get; set; }
        public bool ClosePosition { get; set; }
    }
} 