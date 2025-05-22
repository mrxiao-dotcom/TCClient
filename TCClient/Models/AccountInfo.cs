using System;

namespace TCClient.Models
{
    public class AccountInfo
    {
        public decimal TotalEquity { get; set; }
        public decimal AvailableBalance { get; set; }
        public decimal UnrealizedPnL { get; set; }
        public decimal MarginBalance { get; set; }
        public decimal MaintenanceMargin { get; set; }
        public decimal InitialMargin { get; set; }
        public decimal PositionMargin { get; set; }
        public decimal WalletBalance { get; set; }
        public DateTime UpdateTime { get; set; }
        public int OpportunityCount { get; set; }
    }
} 