using System;

namespace TCClient.Models
{
    public class KLineData
    {
        public string Symbol { get; set; }
        public DateTime OpenTime { get; set; }
        public decimal OpenPrice { get; set; }
        public decimal HighPrice { get; set; }
        public decimal LowPrice { get; set; }
        public decimal ClosePrice { get; set; }
        public decimal Volume { get; set; }
        public DateTime CloseTime { get; set; }
        public decimal QuoteVolume { get; set; }
        public int Trades { get; set; }
        public decimal TakerBuyVolume { get; set; }
        public decimal TakerBuyQuoteVolume { get; set; }
        
        // 兼容性属性
        public DateTime Time 
        { 
            get => OpenTime; 
            set => OpenTime = value; 
        }
        public double Open 
        { 
            get => (double)OpenPrice; 
            set => OpenPrice = (decimal)value; 
        }
        public double High 
        { 
            get => (double)HighPrice; 
            set => HighPrice = (decimal)value; 
        }
        public double Low 
        { 
            get => (double)LowPrice; 
            set => LowPrice = (decimal)value; 
        }
        public double Close 
        { 
            get => (double)ClosePrice; 
            set => ClosePrice = (decimal)value; 
        }
    }
} 