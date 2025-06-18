using System;

namespace TCClient.Models
{
    /// <summary>
    /// 净值曲线数据点
    /// </summary>
    public class NetValuePoint
    {
        public DateTime Time { get; set; }
        public decimal Value { get; set; }
        public string? Symbol { get; set; }
    }

    /// <summary>
    /// 市场成交额数据点
    /// </summary>
    public class MarketVolumePoint
    {
        public DateTime Time { get; set; }
        public decimal Volume24h { get; set; }
        public decimal Volume7d { get; set; }
        public decimal Volume30d { get; set; }
        public int ExchangeId { get; set; }
        public string? ExchangeName { get; set; }
    }
} 