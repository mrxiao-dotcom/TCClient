using System;
using System.Collections.Generic;
using System.Linq;

namespace TCClient.Models
{
    /// <summary>
    /// 每日涨跌幅排名数据模型
    /// </summary>
    public class DailyRanking
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public string TopGainers { get; set; } = string.Empty;
        public string TopLosers { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// 解析涨幅前十数据
        /// 格式：1#合约名#涨幅|2#合约名#涨幅|...
        /// </summary>
        public List<RankingItem> GetTopGainersList()
        {
            return ParseRankingData(TopGainers);
        }

        /// <summary>
        /// 解析跌幅前十数据
        /// 格式：1#合约名#跌幅|2#合约名#跌幅|...
        /// </summary>
        public List<RankingItem> GetTopLosersList()
        {
            return ParseRankingData(TopLosers);
        }

        private List<RankingItem> ParseRankingData(string data)
        {
            var result = new List<RankingItem>();
            
            if (string.IsNullOrEmpty(data))
                return result;

            try
            {
                var items = data.Split('|', StringSplitOptions.RemoveEmptyEntries);
                foreach (var item in items)
                {
                    var parts = item.Split('#');
                    if (parts.Length >= 3)
                    {
                        if (int.TryParse(parts[0], out int rank) && 
                            decimal.TryParse(parts[2], out decimal changeRate))
                        {
                            result.Add(new RankingItem
                            {
                                Rank = rank,
                                Symbol = parts[1],
                                ChangeRate = changeRate
                            });
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 解析失败时返回空列表
            }

            return result.OrderBy(x => x.Rank).ToList();
        }
    }

    /// <summary>
    /// 排名项目
    /// </summary>
    public class RankingItem
    {
        public int Rank { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public decimal ChangeRate { get; set; }

        /// <summary>
        /// 格式化显示文本
        /// </summary>
        public string DisplayText => $"{Symbol}\n{ChangeRate:N2}%";
    }
} 