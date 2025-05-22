using System;

namespace TCClient.Models
{
    public class TickerInfo
    {
        public string Symbol { get; set; }
        public decimal LastPrice { get; set; }
        public decimal BidPrice { get; set; }
        public decimal AskPrice { get; set; }
        public decimal Volume { get; set; }
        public DateTime Timestamp { get; set; }
        public decimal PriceChangePercent { get; set; }
    }
} 