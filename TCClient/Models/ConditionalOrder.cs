using System;

namespace TCClient.Models
{
    public enum ConditionalOrderType
    {
        BREAK_UP,      // 向上突破
        BREAK_DOWN     // 向下突破
    }

    public enum ConditionalOrderStatus
    {
        WAITING,       // 等待触发
        TRIGGERED,     // 已触发
        EXECUTED,      // 已执行
        CANCELLED,     // 已取消
        FAILED         // 执行失败
    }

    public class ConditionalOrder
    {
        public long Id { get; set; }
        public long AccountId { get; set; }
        public string Symbol { get; set; }
        public string Direction { get; set; }
        public ConditionalOrderType ConditionType { get; set; }
        public decimal TriggerPrice { get; set; }
        public decimal Quantity { get; set; }
        public int Leverage { get; set; }
        public decimal? StopLossPrice { get; set; }
        public ConditionalOrderStatus Status { get; set; }
        public string ExecutionOrderId { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime CreateTime { get; set; }
        public DateTime? TriggerTime { get; set; }
        public DateTime? ExecutionTime { get; set; }
        public DateTime UpdateTime { get; set; }

        // UI显示用的属性
        public string ConditionTypeDisplay => ConditionType == ConditionalOrderType.BREAK_UP ? "向上突破" : "向下突破";
        public string StatusDisplay => Status switch
        {
            ConditionalOrderStatus.WAITING => "等待触发",
            ConditionalOrderStatus.TRIGGERED => "已触发",
            ConditionalOrderStatus.EXECUTED => "已执行",
            ConditionalOrderStatus.CANCELLED => "已取消",
            ConditionalOrderStatus.FAILED => "执行失败",
            _ => Status.ToString()
        };
    }
} 