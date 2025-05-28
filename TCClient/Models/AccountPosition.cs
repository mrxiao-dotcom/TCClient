using System;

namespace TCClient.Models
{
    /// <summary>
    /// 账户持仓信息
    /// </summary>
    public class AccountPosition
    {
        public long Id { get; set; }
        public long AccountId { get; set; }
        public string Symbol { get; set; }
        public string PositionSide { get; set; } // LONG, SHORT
        public decimal EntryPrice { get; set; }
        public decimal MarkPrice { get; set; }
        public decimal PositionAmt { get; set; }
        public int Leverage { get; set; }
        public string MarginType { get; set; } // ISOLATED, CROSS
        public decimal? IsolatedMargin { get; set; }
        public decimal UnrealizedPnl { get; set; }
        public decimal? LiquidationPrice { get; set; }
        public DateTime Timestamp { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        
        /// <summary>
        /// 持仓方向显示文本
        /// </summary>
        public string PositionSideDisplay => PositionSide == "LONG" ? "多头" : "空头";
        
        /// <summary>
        /// 保证金类型显示文本
        /// </summary>
        public string MarginTypeDisplay => MarginType == "ISOLATED" ? "逐仓" : "全仓";
        
        /// <summary>
        /// 格式化的未实现盈亏
        /// </summary>
        public string FormattedUnrealizedPnl => $"{UnrealizedPnl:N2}";
        
        /// <summary>
        /// 格式化的持仓数量
        /// </summary>
        public string FormattedPositionAmt => $"{Math.Abs(PositionAmt):N4}";
    }
} 