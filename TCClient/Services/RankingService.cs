using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;
using TCClient.Models;

namespace TCClient.Services
{
    public class RankingService : IRankingService
    {
        private readonly string _connectionString;

        public RankingService(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<Dictionary<DateTime, List<RankingData>>> GetTopRankingsAsync(DateTime startDate, DateTime endDate, int topCount = 10)
        {
            var result = new Dictionary<DateTime, List<RankingData>>();
            
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var query = @"
                    WITH RankedData AS (
                        SELECT 
                            DATE(record_time) as record_date,
                            symbol,
                            last_price,
                            change_rate,
                            amount_24h,
                            volume,
                            record_time,
                            ROW_NUMBER() OVER (PARTITION BY DATE(record_time) ORDER BY change_rate DESC) as daily_rank
                        FROM realtime_ranking_history
                        WHERE record_time BETWEEN @startDate AND @endDate
                    )
                    SELECT *
                    FROM RankedData
                    WHERE daily_rank <= @topCount
                    ORDER BY record_date DESC, daily_rank ASC;";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@startDate", startDate);
                    command.Parameters.AddWithValue("@endDate", endDate);
                    command.Parameters.AddWithValue("@topCount", topCount);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var recordDate = reader.GetDateTime("record_date");
                            var rankingData = new RankingData
                            {
                                Symbol = reader.GetString("symbol"),
                                LastPrice = reader.GetDecimal("last_price"),
                                ChangeRate = reader.GetDecimal("change_rate"),
                                Amount24h = reader.GetDecimal("amount_24h"),
                                Volume = reader.GetDecimal("volume"),
                                RecordTime = reader.GetDateTime("record_time"),
                                Ranking = reader.GetInt32("daily_rank")
                            };

                            if (!result.ContainsKey(recordDate))
                            {
                                result[recordDate] = new List<RankingData>();
                            }
                            result[recordDate].Add(rankingData);
                        }
                    }
                }
            }

            return result;
        }

        public async Task<Dictionary<DateTime, List<RankingData>>> GetBottomRankingsAsync(DateTime startDate, DateTime endDate, int bottomCount = 10)
        {
            var result = new Dictionary<DateTime, List<RankingData>>();
            
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                var query = @"
                    WITH RankedData AS (
                        SELECT 
                            DATE(record_time) as record_date,
                            symbol,
                            last_price,
                            change_rate,
                            amount_24h,
                            volume,
                            record_time,
                            ROW_NUMBER() OVER (PARTITION BY DATE(record_time) ORDER BY change_rate ASC) as daily_rank
                        FROM realtime_ranking_history
                        WHERE record_time BETWEEN @startDate AND @endDate
                    )
                    SELECT *
                    FROM RankedData
                    WHERE daily_rank <= @bottomCount
                    ORDER BY record_date DESC, daily_rank ASC;";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@startDate", startDate);
                    command.Parameters.AddWithValue("@endDate", endDate);
                    command.Parameters.AddWithValue("@bottomCount", bottomCount);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var recordDate = reader.GetDateTime("record_date");
                            var rankingData = new RankingData
                            {
                                Symbol = reader.GetString("symbol"),
                                LastPrice = reader.GetDecimal("last_price"),
                                ChangeRate = reader.GetDecimal("change_rate"),
                                Amount24h = reader.GetDecimal("amount_24h"),
                                Volume = reader.GetDecimal("volume"),
                                RecordTime = reader.GetDateTime("record_time"),
                                Ranking = reader.GetInt32("daily_rank")
                            };

                            if (!result.ContainsKey(recordDate))
                            {
                                result[recordDate] = new List<RankingData>();
                            }
                            result[recordDate].Add(rankingData);
                        }
                    }
                }
            }

            return result;
        }
    }
} 