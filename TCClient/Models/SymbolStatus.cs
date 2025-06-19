using System.ComponentModel;

namespace TCClient.Models
{
    public class SymbolStatus : INotifyPropertyChanged
    {
        /// <summary>
        /// 序号
        /// </summary>
        public int SequenceNumber { get; set; }

        public string Symbol { get; set; } = "";
        public int Stg { get; set; }
        public decimal TotalProfit { get; set; }
        public decimal Winner { get; set; }
        public decimal Volume24h { get; set; }

        public string StgDesc => Stg switch
        {
            -1 or -2 => "空头",
            -3 => "平空",
            0 => "空仓",
            1 or 2 => "多头",
            3 => "平多",
            _ => "未知"
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public override string ToString() => Symbol;
    }
} 