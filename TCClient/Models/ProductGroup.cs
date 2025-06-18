using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TCClient.Models
{
    /// <summary>
    /// 产品组合模型
    /// </summary>
    public class ProductGroup : INotifyPropertyChanged
    {
        private int _id;
        private string _groupName = string.Empty;
        private string _symbols = string.Empty;
        private DateTime _createdAt;
        private DateTime _updatedAt;
        private List<string> _symbolList = new List<string>();

        /// <summary>
        /// 组合ID
        /// </summary>
        public int Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        /// <summary>
        /// 组合名称
        /// </summary>
        public string GroupName
        {
            get => _groupName;
            set => SetProperty(ref _groupName, value);
        }

        /// <summary>
        /// 合约符号字符串（用#分隔）
        /// </summary>
        public string Symbols
        {
            get => _symbols;
            set
            {
                if (SetProperty(ref _symbols, value))
                {
                    // 更新合约列表
                    UpdateSymbolList();
                }
            }
        }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt
        {
            get => _createdAt;
            set => SetProperty(ref _createdAt, value);
        }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime UpdatedAt
        {
            get => _updatedAt;
            set => SetProperty(ref _updatedAt, value);
        }

        /// <summary>
        /// 合约列表（从Symbols字段解析）
        /// </summary>
        public List<string> SymbolList
        {
            get => _symbolList;
            private set => SetProperty(ref _symbolList, value);
        }

        /// <summary>
        /// 合约数量
        /// </summary>
        public int SymbolCount => SymbolList?.Count ?? 0;

        /// <summary>
        /// 更新合约列表
        /// </summary>
        private void UpdateSymbolList()
        {
            if (string.IsNullOrWhiteSpace(Symbols))
            {
                SymbolList = new List<string>();
            }
            else
            {
                SymbolList = new List<string>(Symbols.Split('#', StringSplitOptions.RemoveEmptyEntries));
            }
            OnPropertyChanged(nameof(SymbolCount));
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