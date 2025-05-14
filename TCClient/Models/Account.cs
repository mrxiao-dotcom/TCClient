using System;

namespace TCClient.Models
{
    public class Account
    {
        public int Id { get; set; }
        public string AccountName { get; set; }
        public string Type { get; set; }  // 模拟/实盘
        public decimal Balance { get; set; }
        public decimal Equity { get; set; }
        public decimal Margin { get; set; }
        public decimal RiskRatio { get; set; }
        public DateTime CreateTime { get; set; }
        public DateTime? LastLoginTime { get; set; }
        public bool IsActive { get; set; }
        public string Description { get; set; }
    }
} 