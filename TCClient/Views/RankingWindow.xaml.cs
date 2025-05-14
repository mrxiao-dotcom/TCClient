using System.Windows;
using TCClient.ViewModels;
using TCClient.Services;
using TCClient.Utils;

namespace TCClient.Views
{
    public partial class RankingWindow : Window
    {
        public RankingWindow()
        {
            InitializeComponent();

            var databaseService = ServiceLocator.GetService<IDatabaseService>();
            var messageService = ServiceLocator.GetService<IMessageService>();

            DataContext = new RankingViewModel(databaseService, messageService);
        }
    }
} 