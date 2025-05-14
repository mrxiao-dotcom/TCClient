using System;

namespace TCClient.Models
{
    public class Position
    {
        public int Id { get; set; }
        public int AccountId { get; set; }
        public string Contract { get; set; }  // 合约代码
        public string Direction { get; set; }  // 多/空
        public int Quantity { get; set; }  // 持仓数量
        public decimal EntryPrice { get; set; }  // 开仓均价
        public decimal CurrentPrice { get; set; }  // 当前价格
        public decimal FloatingPnL { get; set; }  // 浮动盈亏
        public decimal StopLoss { get; set; }  // 止损价
        public decimal TakeProfit { get; set; }  // 止盈价
        public DateTime OpenTime { get; set; }  // 开仓时间
        public DateTime? CloseTime { get; set; }  // 平仓时间
        public string Status { get; set; }  // 持仓状态
    }
} 