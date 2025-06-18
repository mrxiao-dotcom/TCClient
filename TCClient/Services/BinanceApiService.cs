using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Linq;
using TCClient.Models;

namespace TCClient.Services
{
    public class BinanceApiService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _secretKey;
        private const string BaseUrl = "https://api.binance.com";

        public BinanceApiService(string apiKey = "", string secretKey = "")
        {
            _httpClient = new HttpClient();
            _apiKey = apiKey;
            _secretKey = secretKey;
            
            // 设置请求头
            if (!string.IsNullOrEmpty(_apiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("X-MBX-APIKEY", _apiKey);
            }
        }

        /// <summary>
        /// 获取K线数据
        /// </summary>
        /// <param name="symbol">交易对</param>
        /// <param name="interval">时间间隔</param>
        /// <param name="limit">数量限制</param>
        /// <returns>K线数据列表</returns>
        public async Task<List<BinanceKLineData>> GetKLineDataAsync(string symbol, string interval, int limit = 100)
        {
            try
            {
                var url = $"{BaseUrl}/api/v3/klines?symbol={symbol}&interval={interval}&limit={limit}";
                var response = await _httpClient.GetStringAsync(url);
                
                var jsonArray = JsonDocument.Parse(response).RootElement;
                var klineData = new List<BinanceKLineData>();

                foreach (var item in jsonArray.EnumerateArray())
                {
                    var data = item.EnumerateArray().ToArray();
                    
                    klineData.Add(new BinanceKLineData
                    {
                        OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(data[0].GetInt64()).DateTime,
                        Open = decimal.Parse(data[1].GetString()),
                        High = decimal.Parse(data[2].GetString()),
                        Low = decimal.Parse(data[3].GetString()),
                        Close = decimal.Parse(data[4].GetString()),
                        Volume = decimal.Parse(data[5].GetString()),
                        CloseTime = DateTimeOffset.FromUnixTimeMilliseconds(data[6].GetInt64()).DateTime,
                        QuoteAssetVolume = decimal.Parse(data[7].GetString()),
                        NumberOfTrades = data[8].GetInt32(),
                        TakerBuyBaseAssetVolume = decimal.Parse(data[9].GetString()),
                        TakerBuyQuoteAssetVolume = decimal.Parse(data[10].GetString())
                    });
                }

                return klineData;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取K线数据失败: {ex.Message}");
                return new List<BinanceKLineData>();
            }
        }

        /// <summary>
        /// 获取24小时价格变动统计
        /// </summary>
        /// <param name="symbol">交易对</param>
        /// <returns>价格统计数据</returns>
        public async Task<TickerData> GetTickerDataAsync(string symbol)
        {
            try
            {
                var url = $"{BaseUrl}/api/v3/ticker/24hr?symbol={symbol}";
                var response = await _httpClient.GetStringAsync(url);
                
                var json = JsonDocument.Parse(response).RootElement;
                
                return new TickerData
                {
                    Symbol = json.GetProperty("symbol").GetString(),
                    PriceChange = decimal.Parse(json.GetProperty("priceChange").GetString()),
                    PriceChangePercent = decimal.Parse(json.GetProperty("priceChangePercent").GetString()),
                    WeightedAvgPrice = decimal.Parse(json.GetProperty("weightedAvgPrice").GetString()),
                    PrevClosePrice = decimal.Parse(json.GetProperty("prevClosePrice").GetString()),
                    LastPrice = decimal.Parse(json.GetProperty("lastPrice").GetString()),
                    LastQty = decimal.Parse(json.GetProperty("lastQty").GetString()),
                    BidPrice = decimal.Parse(json.GetProperty("bidPrice").GetString()),
                    AskPrice = decimal.Parse(json.GetProperty("askPrice").GetString()),
                    OpenPrice = decimal.Parse(json.GetProperty("openPrice").GetString()),
                    HighPrice = decimal.Parse(json.GetProperty("highPrice").GetString()),
                    LowPrice = decimal.Parse(json.GetProperty("lowPrice").GetString()),
                    Volume = decimal.Parse(json.GetProperty("volume").GetString()),
                    QuoteVolume = decimal.Parse(json.GetProperty("quoteVolume").GetString()),
                    OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(json.GetProperty("openTime").GetInt64()).DateTime,
                    CloseTime = DateTimeOffset.FromUnixTimeMilliseconds(json.GetProperty("closeTime").GetInt64()).DateTime,
                    Count = json.GetProperty("count").GetInt32()
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取Ticker数据失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 获取当前价格
        /// </summary>
        /// <param name="symbol">交易对</param>
        /// <returns>当前价格</returns>
        public async Task<decimal> GetCurrentPriceAsync(string symbol)
        {
            try
            {
                var url = $"{BaseUrl}/api/v3/ticker/price?symbol={symbol}";
                var response = await _httpClient.GetStringAsync(url);
                
                var json = JsonDocument.Parse(response).RootElement;
                return decimal.Parse(json.GetProperty("price").GetString());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取当前价格失败: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 批量获取多个交易对的价格
        /// </summary>
        /// <param name="symbols">交易对列表</param>
        /// <returns>价格字典</returns>
        public async Task<Dictionary<string, decimal>> GetMultiplePricesAsync(List<string> symbols)
        {
            try
            {
                var symbolsParam = string.Join(",", symbols.Select(s => $"\"{s}\""));
                var url = $"{BaseUrl}/api/v3/ticker/price";
                var response = await _httpClient.GetStringAsync(url);
                
                var jsonArray = JsonDocument.Parse(response).RootElement;
                var prices = new Dictionary<string, decimal>();

                foreach (var item in jsonArray.EnumerateArray())
                {
                    var symbol = item.GetProperty("symbol").GetString();
                    var price = decimal.Parse(item.GetProperty("price").GetString());
                    
                    if (symbols.Contains(symbol))
                    {
                        prices[symbol] = price;
                    }
                }

                return prices;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"批量获取价格失败: {ex.Message}");
                return new Dictionary<string, decimal>();
            }
        }

        /// <summary>
        /// 测试API连接
        /// </summary>
        /// <returns>是否连接成功</returns>
        public async Task<bool> TestConnectivityAsync()
        {
            try
            {
                var url = $"{BaseUrl}/api/v3/ping";
                var response = await _httpClient.GetStringAsync(url);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    // K线数据模型
    public class BinanceKLineData
    {
        public DateTime OpenTime { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }
        public DateTime CloseTime { get; set; }
        public decimal QuoteAssetVolume { get; set; }
        public int NumberOfTrades { get; set; }
        public decimal TakerBuyBaseAssetVolume { get; set; }
        public decimal TakerBuyQuoteAssetVolume { get; set; }
    }

    // Ticker数据模型
    public class TickerData
    {
        public string Symbol { get; set; }
        public decimal PriceChange { get; set; }
        public decimal PriceChangePercent { get; set; }
        public decimal WeightedAvgPrice { get; set; }
        public decimal PrevClosePrice { get; set; }
        public decimal LastPrice { get; set; }
        public decimal LastQty { get; set; }
        public decimal BidPrice { get; set; }
        public decimal AskPrice { get; set; }
        public decimal OpenPrice { get; set; }
        public decimal HighPrice { get; set; }
        public decimal LowPrice { get; set; }
        public decimal Volume { get; set; }
        public decimal QuoteVolume { get; set; }
        public DateTime OpenTime { get; set; }
        public DateTime CloseTime { get; set; }
        public int Count { get; set; }
    }
} 