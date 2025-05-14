using System;

namespace TCClient.Models
{
    public class Order
    {
        public int Id { get; set; }
        public int AccountId { get; set; }
        public string OrderId { get; set; }  // 委托编号
        public string Contract { get; set; }  // 合约代码
        public string Direction { get; set; }  // 买入/卖出
        public string OffsetFlag { get; set; }  // 开仓/平仓
        public int Quantity { get; set; }  // 委托数量
        public decimal Price { get; set; }  // 委托价格
        public string OrderType { get; set; }  // 委托类型（限价/市价）
        public string Status { get; set; }  // 委托状态
        public DateTime CreateTime { get; set; }  // 委托时间
        public DateTime? UpdateTime { get; set; }  // 更新时间
        public string Message { get; set; }  // 委托信息
    }
} 