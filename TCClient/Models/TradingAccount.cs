using System;

namespace TCClient.Models
{
    public class TradingAccount
    {
        public long Id { get; set; }
        public string AccountName { get; set; }
        public string Description { get; set; }
        public string BinanceAccountId { get; set; }
        public string ApiKey { get; set; }
        public string ApiSecret { get; set; }
        public string ApiPassphrase { get; set; }
        public decimal Equity { get; set; }
        public decimal InitialEquity { get; set; }
        public int OpportunityCount { get; set; }
        public int Status { get; set; }
        public int IsActive { get; set; }
        public DateTime CreateTime { get; set; }
        public DateTime? UpdateTime { get; set; }
        public int IsDefault { get; set; }  // 数据库字段
        public bool IsDefaultAccount { get; set; }  // 逻辑字段，界面用
    }
} 