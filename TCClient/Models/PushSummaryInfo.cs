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
        
        // 新增：可用风险金计算详情
        public decimal SingleRiskAmount { get; set; }   // 单笔可用风险金（权益/次数）
        public decimal AccumulatedRealProfit { get; set; } // 该合约累加实际盈亏
        
        // 计算属性：显示计算公式
        public string AvailableRiskAmountFormula => $"{SingleRiskAmount:N2} + {AccumulatedRealProfit:N2} = {AvailableRiskAmount:N2}";
    }
} 