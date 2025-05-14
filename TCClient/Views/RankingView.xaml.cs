using System.Windows.Controls;
using TCClient.ViewModels;
using TCClient.Services;
using TCClient.Utils;

namespace TCClient.Views
{
    public partial class RankingView : UserControl
    {
        public RankingView()
        {
            InitializeComponent();

            var databaseService = ServiceLocator.GetService<IDatabaseService>();
            var messageService = ServiceLocator.GetService<IMessageService>();

            DataContext = new RankingViewModel(databaseService, messageService);
        }
    }
} 