using System;

namespace TCClient.Models
{
    /// <summary>
    /// 止损止盈单模型
    /// </summary>
    public class StopTakeOrder
    {
        public long Id { get; set; }
        public long AccountId { get; set; }
        public long SimulationOrderId { get; set; }
        public string Symbol { get; set; }
        public string OrderType { get; set; } // STOP_LOSS, TAKE_PROFIT
        public string Direction { get; set; } // BUY, SELL
        public decimal Quantity { get; set; }
        public decimal TriggerPrice { get; set; }
        public string WorkingType { get; set; } = "MARK_PRICE"; // MARK_PRICE, CONTRACT_PRICE
        public string Status { get; set; } = "WAITING"; // WAITING, SET, TRIGGERED, EXECUTED, CANCELLED, FAILED
        public string BinanceOrderId { get; set; }
        public decimal? ExecutionPrice { get; set; }
        public DateTime? ExecutionTime { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime CreateTime { get; set; } = DateTime.Now;
        public DateTime UpdateTime { get; set; } = DateTime.Now;
    }
} 