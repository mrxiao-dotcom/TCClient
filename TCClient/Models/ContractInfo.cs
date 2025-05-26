using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TCClient.Models
{
    public class ContractInfo : INotifyPropertyChanged
    {
        private string _symbol;
        private decimal _priceChangePercent;
        private decimal _lastPrice;
        private decimal _volume;
        private bool _isSelected;

        /// <summary>
        /// 合约名称
        /// </summary>
        public string Symbol
        {
            get => _symbol;
            set { _symbol = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 24小时涨幅（百分比）
        /// </summary>
        public decimal PriceChangePercent
        {
            get => _priceChangePercent;
            set { _priceChangePercent = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 最新价格
        /// </summary>
        public decimal LastPrice
        {
            get => _lastPrice;
            set { _lastPrice = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 24小时成交额
        /// </summary>
        public decimal Volume
        {
            get => _volume;
            set { _volume = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 是否被选中
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 