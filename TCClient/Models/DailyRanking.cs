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

        /// <summary>
        /// 解析涨幅前十数据，过滤不可交易的合约
        /// 格式：1#合约名#涨幅|2#合约名#涨幅|...
        /// </summary>
        public List<RankingItem> GetTopGainersList(HashSet<string> tradableSymbols)
        {
            return ParseRankingData(TopGainers, tradableSymbols);
        }

        /// <summary>
        /// 解析跌幅前十数据，过滤不可交易的合约
        /// 格式：1#合约名#跌幅|2#合约名#跌幅|...
        /// </summary>
        public List<RankingItem> GetTopLosersList(HashSet<string> tradableSymbols)
        {
            return ParseRankingData(TopLosers, tradableSymbols);
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

        private List<RankingItem> ParseRankingData(string data, HashSet<string> tradableSymbols)
        {
            var result = new List<RankingItem>();
            
            if (string.IsNullOrEmpty(data) || tradableSymbols == null)
                return result;

            try
            {
                var items = data.Split('|', StringSplitOptions.RemoveEmptyEntries);
                int filteredRank = 1; // 重新排名
                
                foreach (var item in items)
                {
                    var parts = item.Split('#');
                    if (parts.Length >= 3)
                    {
                        if (int.TryParse(parts[0], out int originalRank) && 
                            decimal.TryParse(parts[2], out decimal changeRate))
                        {
                            var symbol = parts[1];
                            
                            // 检查合约是否可交易（支持多种格式匹配）
                            if (IsSymbolTradable(symbol, tradableSymbols))
                            {
                                result.Add(new RankingItem
                                {
                                    Rank = filteredRank++,
                                    Symbol = symbol,
                                    ChangeRate = changeRate
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 解析失败时返回空列表
            }

            return result;
        }

        /// <summary>
        /// 检查合约是否可交易（支持多种格式匹配）
        /// </summary>
        private bool IsSymbolTradable(string symbol, HashSet<string> tradableSymbols)
        {
            if (string.IsNullOrEmpty(symbol) || tradableSymbols == null)
                return false;

            // 直接匹配
            if (tradableSymbols.Contains(symbol))
                return true;

            // 添加USDT后缀匹配
            if (!symbol.EndsWith("USDT") && tradableSymbols.Contains($"{symbol}USDT"))
                return true;

            // 移除USDT后缀匹配
            if (symbol.EndsWith("USDT") && tradableSymbols.Contains(symbol.Replace("USDT", "")))
                return true;

            return false;
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