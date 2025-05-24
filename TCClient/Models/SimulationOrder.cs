using System;

namespace TCClient.Models
{
    public class SimulationOrder
    {
        public long Id { get; set; }
        public string OrderId { get; set; }
        public long AccountId { get; set; }
        public string Contract { get; set; }
        public decimal ContractSize { get; set; }
        public string Direction { get; set; }
        public float Quantity { get; set; }
        public decimal EntryPrice { get; set; }
        public decimal InitialStopLoss { get; set; }
        public decimal CurrentStopLoss { get; set; }
        public decimal? HighestPrice { get; set; }
        public decimal? MaxFloatingProfit { get; set; }
        public int Leverage { get; set; }
        public decimal Margin { get; set; }
        public decimal TotalValue { get; set; }
        public string Status { get; set; }
        public DateTime OpenTime { get; set; }
        public DateTime? CloseTime { get; set; }
        public decimal? ClosePrice { get; set; }
        public decimal? RealizedProfit { get; set; }
        public string CloseType { get; set; }
        public decimal? RealProfit { get; set; }
        public decimal? FloatingPnL { get; set; }
        public decimal? CurrentPrice { get; set; }
        public DateTime? LastUpdateTime { get; set; }
    }
} 