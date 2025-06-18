using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace TCClient.Models
{
    /// <summary>
    /// 策略详情模型
    /// </summary>
    public class StrategyDetail : INotifyPropertyChanged
    {
        private int _id;
        private string _symbol = string.Empty;
        private decimal _close;
        private decimal _totalProfit;
        private DateTime _timestamp;

        /// <summary>
        /// 记录ID
        /// </summary>
        public int Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        /// <summary>
        /// 合约符号
        /// </summary>
        public string Symbol
        {
            get => _symbol;
            set => SetProperty(ref _symbol, value);
        }

        /// <summary>
        /// 收盘价
        /// </summary>
        public decimal Close
        {
            get => _close;
            set => SetProperty(ref _close, value);
        }

        /// <summary>
        /// 总利润
        /// </summary>
        public decimal TotalProfit
        {
            get => _totalProfit;
            set => SetProperty(ref _totalProfit, value);
        }

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp
        {
            get => _timestamp;
            set => SetProperty(ref _timestamp, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
} 