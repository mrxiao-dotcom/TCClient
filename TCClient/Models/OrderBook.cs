using System.Collections.Generic;

namespace TCClient.Models
{
    public class OrderBook
    {
        public string Symbol { get; set; }
        public List<OrderBookEntry> Bids { get; set; }
        public List<OrderBookEntry> Asks { get; set; }
    }

    public class OrderBookEntry
    {
        public decimal Price { get; set; }
        public decimal Quantity { get; set; }
    }
} 