using System;

namespace TCClient.Models
{
    public class Trade
    {
        public int Id { get; set; }
        public int AccountId { get; set; }
        public string TradeId { get; set; }  // 成交编号
        public string OrderId { get; set; }  // 委托编号
        public string Contract { get; set; }  // 合约代码
        public string Direction { get; set; }  // 买入/卖出
        public string OffsetFlag { get; set; }  // 开仓/平仓
        public int Quantity { get; set; }  // 成交数量
        public decimal Price { get; set; }  // 成交价格
        public decimal Commission { get; set; }  // 手续费
        public decimal Tax { get; set; }  // 印花税
        public DateTime TradeTime { get; set; }  // 成交时间
        public string TradeType { get; set; }  // 成交类型
        public string Message { get; set; }  // 成交信息
    }
} 