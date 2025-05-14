using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TCClient.Models;

namespace TCClient.Services
{
    public interface IRankingService
    {
        Task<Dictionary<DateTime, List<RankingData>>> GetTopRankingsAsync(DateTime startDate, DateTime endDate, int topCount = 10);
        Task<Dictionary<DateTime, List<RankingData>>> GetBottomRankingsAsync(DateTime startDate, DateTime endDate, int bottomCount = 10);
    }
} 