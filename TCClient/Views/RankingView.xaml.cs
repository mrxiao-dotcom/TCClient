using System.Windows.Controls;
using TCClient.ViewModels;
using TCClient.Services;
using TCClient.Utils;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace TCClient.Views
{
    public partial class RankingView : UserControl
    {
        public RankingView()
        {
            InitializeComponent();

            LogManager.Log("RankingView", "开始初始化RankingView");

            try
            {
                // 优先使用依赖注入获取服务
                var app = Application.Current as App;
                LogManager.Log("RankingView", $"获取到App实例: {app != null}");
                
                if (app?.Services != null)
                {
                    LogManager.Log("RankingView", "使用依赖注入获取服务");
                    var databaseService = app.Services.GetRequiredService<IDatabaseService>();
                    var messageService = app.Services.GetRequiredService<IMessageService>();
                    LogManager.Log("RankingView", "成功获取到依赖注入服务，创建RankingViewModel");
                    DataContext = new RankingViewModel(databaseService, messageService);
                    LogManager.Log("RankingView", "RankingViewModel创建成功");
                }
                else
                {
                    LogManager.Log("RankingView", "依赖注入服务不可用，使用ServiceLocator");
                    // 备用方案：使用ServiceLocator
                    var databaseService = ServiceLocator.GetService<IDatabaseService>();
                    var messageService = ServiceLocator.GetService<IMessageService>();
                    LogManager.Log("RankingView", "成功获取到ServiceLocator服务，创建RankingViewModel");
                    DataContext = new RankingViewModel(databaseService, messageService);
                    LogManager.Log("RankingView", "RankingViewModel创建成功");
                }
            }
            catch (System.Exception ex)
            {
                LogManager.LogException("RankingView", ex, "创建RankingView时发生错误");
                // 如果依赖注入失败，尝试使用ServiceLocator
                try
                {
                    LogManager.Log("RankingView", "尝试使用ServiceLocator作为备用方案");
                    var databaseService = ServiceLocator.GetService<IDatabaseService>();
                    var messageService = ServiceLocator.GetService<IMessageService>();
                    LogManager.Log("RankingView", "ServiceLocator备用方案成功获取服务");
                    DataContext = new RankingViewModel(databaseService, messageService);
                    LogManager.Log("RankingView", "ServiceLocator备用方案创建RankingViewModel成功");
                }
                catch (System.Exception ex2)
                {
                    LogManager.LogException("RankingView", ex2, "使用ServiceLocator创建RankingView也失败");
                    throw;
                }
            }
            
            LogManager.Log("RankingView", "RankingView初始化完成");
        }
    }
} 