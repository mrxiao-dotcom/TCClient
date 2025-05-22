using System.Windows;
using TCClient.ViewModels;
using TCClient.Services;
using TCClient.Utils;

namespace TCClient.Views
{
    public partial class RankingWindow : Window
    {
        private readonly IRankingService _rankingService;
        private readonly IMessageService _messageService;

        public RankingWindow(
            IRankingService rankingService,
            IMessageService messageService)
        {
            InitializeComponent();
            _rankingService = rankingService;
            _messageService = messageService;
            LoadRankingData();
        }

        private void LoadRankingData()
        {
            // Implementation of LoadRankingData method
        }
    }
} 