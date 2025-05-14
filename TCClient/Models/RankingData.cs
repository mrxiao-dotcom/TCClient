using System;

namespace TCClient.Models
{
    public class RankingData
    {
        public long Id { get; set; }
        public string Symbol { get; set; }
        public decimal LastPrice { get; set; }
        public decimal ChangeRate { get; set; }
        public decimal Amount24h { get; set; }
        public int Ranking { get; set; }
        public long Timestamp { get; set; }
        public DateTime RecordTime { get; set; }
        public DateTime CreatedAt { get; set; }
        public decimal Volume { get; set; }

        // 为了UI显示方便，添加一些计算属性
        public DateTime Date => RecordTime.Date;
        public string Name => Symbol; // 暂时使用Symbol作为名称，如果需要可以添加Name字段
        public decimal ChangePercent => ChangeRate * 100; // 转换为百分比显示
        public decimal OpenPrice => LastPrice / (1 + ChangeRate); // 根据最新价和涨跌幅计算开盘价
        public decimal ClosePrice => LastPrice;
        public decimal HighPrice => LastPrice * (1 + Math.Abs(ChangeRate)); // 估算最高价
        public decimal LowPrice => LastPrice * (1 - Math.Abs(ChangeRate)); // 估算最低价
        public decimal Amount => Amount24h; // 使用24小时成交额
        public int Rank => Ranking; // 使用数据库中的排名
    }
} 