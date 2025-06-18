using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace TCClient.Models
{
    /// <summary>
    /// 市场总览数据模型
    /// </summary>
    public class MarketOverviewData
    {
        /// <summary>
        /// 今日涨跌统计
        /// </summary>
        public TodayMarketStats TodayStats { get; set; } = new TodayMarketStats();

        /// <summary>
        /// 历史统计数据（最近20天）
        /// </summary>
        public List<DailyMarketStats> HistoricalStats { get; set; } = new List<DailyMarketStats>();
    }

    /// <summary>
    /// 今日市场统计
    /// </summary>
    public class TodayMarketStats
    {
        /// <summary>
        /// 上涨家数
        /// </summary>
        public int RisingCount { get; set; }

        /// <summary>
        /// 下跌家数
        /// </summary>
        public int FallingCount { get; set; }

        /// <summary>
        /// 平盘家数
        /// </summary>
        public int FlatCount { get; set; }

        /// <summary>
        /// 24小时成交额（USDT）
        /// </summary>
        public decimal TotalVolume24h { get; set; }

        /// <summary>
        /// 总计合约数
        /// </summary>
        public int TotalCount => RisingCount + FallingCount + FlatCount;

        /// <summary>
        /// 涨跌比例文本
        /// </summary>
        public string RiseFallRatio => $"{RisingCount}|{FallingCount}";

        /// <summary>
        /// 格式化的成交额（单位B）
        /// </summary>
        public string FormattedVolume => $"{TotalVolume24h / 1_000_000_000m:F1}B";
    }

    /// <summary>
    /// 单日市场统计
    /// </summary>
    public class DailyMarketStats
    {
        /// <summary>
        /// 日期
        /// </summary>
        public DateTime Date { get; set; }

        /// <summary>
        /// 上涨家数
        /// </summary>
        public int RisingCount { get; set; }

        /// <summary>
        /// 下跌家数
        /// </summary>
        public int FallingCount { get; set; }

        /// <summary>
        /// 平盘家数
        /// </summary>
        public int FlatCount { get; set; }

        /// <summary>
        /// 当日成交额（USDT）
        /// </summary>
        public decimal DailyVolume { get; set; }

        /// <summary>
        /// 涨跌比例文本
        /// </summary>
        public string RiseFallRatio => $"{RisingCount}|{FallingCount}";

        /// <summary>
        /// 格式化的成交额（单位B）
        /// </summary>
        public string FormattedVolume => $"{DailyVolume / 1_000_000_000m:F1}B";

        /// <summary>
        /// 格式化的日期
        /// </summary>
        public string FormattedDate => Date.ToString("MM-dd");

        /// <summary>
        /// 是否为上涨日（上涨数量大于下跌数量）
        /// </summary>
        public bool IsRisingDay => RisingCount > FallingCount;
    }

    /// <summary>
    /// 投资机会数据
    /// </summary>
    public class OpportunityData : INotifyPropertyChanged
    {
        private bool _isHighlighted = false;

        /// <summary>
        /// 合约名称
        /// </summary>
        public string Symbol { get; set; } = string.Empty;

        /// <summary>
        /// 当前价格
        /// </summary>
        public decimal CurrentPrice { get; set; }

        /// <summary>
        /// 涨跌幅（百分比）
        /// </summary>
        public decimal ChangePercent { get; set; }

        /// <summary>
        /// 计算基准价格（最低价或最高价）
        /// </summary>
        public decimal BasePrice { get; set; }

        /// <summary>
        /// 统计周期（天数）
        /// </summary>
        public int PeriodDays { get; set; }

        /// <summary>
        /// 24小时成交额（USDT）
        /// </summary>
        public decimal Volume24h { get; set; }

        /// <summary>
        /// 是否为做空机会显示
        /// </summary>
        public bool IsShortOpportunity { get; set; } = false;

        /// <summary>
        /// 是否高亮显示（用于跨列表选中效果）
        /// </summary>
        public bool IsHighlighted
        {
            get => _isHighlighted;
            set
            {
                if (_isHighlighted != value)
                {
                    _isHighlighted = value;
                    OnPropertyChanged(nameof(IsHighlighted));
                }
            }
        }

        /// <summary>
        /// 格式化的涨跌幅文本
        /// </summary>
        public string FormattedChange => IsShortOpportunity ? 
            $"-{Math.Abs(ChangePercent):F2}%" : 
            $"{ChangePercent:+0.00;-0.00;0.00}%";

        /// <summary>
        /// 格式化的24小时成交额（单位万）
        /// </summary>
        public string FormattedVolume24h => $"{Volume24h / 10_000m:F0}万";

        /// <summary>
        /// 涨跌幅颜色（用于UI显示）
        /// </summary>
        public string ChangeColor => ChangePercent > 0 ? "Red" : ChangePercent < 0 ? "Green" : "Gray";

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }

    /// <summary>
    /// 价格缓存数据
    /// </summary>
    public class PriceCacheData
    {
        /// <summary>
        /// 缓存日期
        /// </summary>
        public DateTime Date { get; set; }

        /// <summary>
        /// 合约价格统计
        /// </summary>
        public Dictionary<string, ContractPriceStats> ContractStats { get; set; } = new Dictionary<string, ContractPriceStats>();
    }

    /// <summary>
    /// 合约价格统计
    /// </summary>
    public class ContractPriceStats
    {
        /// <summary>
        /// 合约名称
        /// </summary>
        public string Symbol { get; set; } = string.Empty;

        /// <summary>
        /// 最高价
        /// </summary>
        public decimal HighPrice { get; set; }

        /// <summary>
        /// 最低价
        /// </summary>
        public decimal LowPrice { get; set; }

        /// <summary>
        /// 开盘价
        /// </summary>
        public decimal OpenPrice { get; set; }

        /// <summary>
        /// 收盘价
        /// </summary>
        public decimal ClosePrice { get; set; }

        /// <summary>
        /// 成交量
        /// </summary>
        public decimal Volume { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime UpdateTime { get; set; }
    }
} 