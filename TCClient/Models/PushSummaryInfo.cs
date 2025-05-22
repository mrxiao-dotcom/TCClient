using System;
using System.Collections.Generic;

namespace TCClient.Models
{
    public class PushSummaryInfo
    {
        public long PushId { get; set; }
        public string Contract { get; set; }
        public DateTime CreateTime { get; set; }
        public string Status { get; set; }
        public decimal TotalFloatingPnL { get; set; }  // 总浮动盈亏
        public decimal TotalRealPnL { get; set; }      // 总实际盈亏（按止损价计算）
        public int TotalOrderCount { get; set; }       // 总订单数
        public int OpenOrderCount { get; set; }        // 持仓中订单数
        public int ClosedOrderCount { get; set; }      // 已平仓订单数
        public decimal RiskAmount { get; set; }        // 占用风险金
        public decimal AvailableRiskAmount { get; set; } // 可用风险金
        public List<SimulationOrder> Orders { get; set; } = new List<SimulationOrder>(); // 相关订单列表
    }
} 