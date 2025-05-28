using System;

namespace TCClient.Models
{
    /// <summary>
    /// 账户余额信息
    /// </summary>
    public class AccountBalance
    {
        public long AccountId { get; set; }
        public decimal TotalEquity { get; set; }
        public decimal AvailableBalance { get; set; }
        public decimal MarginBalance { get; set; }
        public decimal UnrealizedPnL { get; set; }
        public DateTime Timestamp { get; set; }
        public string Source { get; set; } // 数据来源：account_balances 或 trading_accounts
        public int OpportunityCount { get; set; } // 风险次数（仅从trading_accounts获取时有值）
    }
} 