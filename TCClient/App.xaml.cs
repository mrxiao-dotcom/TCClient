using System.Configuration;
using System.Data;
using System.Windows;
using System.IO;
using TCClient.Services;
using TCClient.Utils;
using TCClient.ViewModels;
using TCClient.Views;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System;
using System.Threading;
using System.Windows.Threading;
using TCClient.Models;

namespace TCClient;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private ServiceProvider _serviceProvider;
    public IServiceProvider Services => _serviceProvider;
    private readonly ILogger<App> _logger;

    public App()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
        _logger = _serviceProvider.GetRequiredService<ILogger<App>>();

        try
        {
            // 设置关闭模式，允许正常关闭
            ShutdownMode = ShutdownMode.OnLastWindowClose;
            LogManager.Log("App", "将ShutdownMode设置为OnLastWindowClose");

            // 设置详细的异常处理
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                LogManager.LogException("App.UnhandledException", ex);
                MessageBox.Show($"未处理的异常: {ex?.GetType().FullName}\n{ex?.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            };
            
            DispatcherUnhandledException += (s, e) =>
            {
                var ex = e.Exception;
                LogManager.LogException("App.DispatcherUnhandledException", ex);
                MessageBox.Show($"UI线程未处理的异常: {ex.GetType().FullName}\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            };
            
            // 添加TaskScheduler未观察到的异常处理
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                var ex = e.Exception;
                LogManager.LogException("App.UnobservedTaskException", ex);
                e.SetObserved();
            };
        }
        catch (Exception ex)
        {
            LogManager.LogException("App.Constructor", ex);
            MessageBox.Show($"应用程序初始化失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // 添加日志服务
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // 创建服务实例
        var configService = new LocalConfigService();
        var databaseService = new MySqlDatabaseService();
        var messageService = new MessageBoxService();
        var rankingService = new RankingService(configService.GetCurrentConnectionString());
        var accountService = new AccountService(databaseService);
        var loggerFactory = services.BuildServiceProvider().GetRequiredService<ILoggerFactory>();
        var exchangeServiceFactory = new ExchangeServiceFactory(loggerFactory);

        // 注册服务
        services.AddSingleton<LocalConfigService>(configService);
        services.AddSingleton<IDatabaseService>(databaseService);
        services.AddSingleton<IUserService>(databaseService);
        services.AddSingleton<IMessageService>(messageService);
        services.AddSingleton<IRankingService>(rankingService);
        services.AddSingleton<IAccountService>(accountService);
        services.AddSingleton<IExchangeServiceFactory>(exchangeServiceFactory);

        // 注册交易所服务
        services.AddSingleton<IExchangeService>(provider =>
        {
            var factory = provider.GetRequiredService<IExchangeServiceFactory>();
            var account = new TradingAccount
            {
                AccountName = "默认账户",
                ApiKey = "temp",
                ApiSecret = "temp"
            };
            return factory.CreateExchangeService(account);
        });
        
        // 注册条件单服务
        services.AddSingleton<ConditionalOrderService>();

        // 注册 ViewModel
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<LoginViewModel>();
        services.AddSingleton<OrderViewModel>();
        services.AddSingleton<AccountConfigViewModel>();
        services.AddSingleton<AddAccountViewModel>();
        services.AddSingleton<RegisterViewModel>();
        services.AddSingleton<RankingViewModel>();
        services.AddTransient<OrderListViewModel>();

        // 注册 View
        services.AddTransient<MainWindow>();
        services.AddTransient<LoginWindow>();
        services.AddTransient<OrderWindow>();
        services.AddTransient<AccountConfigWindow>();
        services.AddTransient<AddAccountWindow>();
        services.AddTransient<RegisterWindow>();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            LogManager.Log("App", "应用程序启动开始");
            base.OnStartup(e);

            // 启动条件单服务
            try
            {
                var conditionalOrderService = _serviceProvider.GetRequiredService<ConditionalOrderService>();
                conditionalOrderService.Start();
                LogManager.Log("App", "条件单监控服务已启动");
            }
            catch (Exception serviceEx)
            {
                LogManager.LogException("App", serviceEx, "启动条件单服务失败");
            }

            // 记录当前ShutdownMode状态
            LogManager.Log("App", $"当前ShutdownMode = {ShutdownMode}");

            // 初始化会话状态
            TCClient.Utils.AppSession.ClearSession();
            
            // 监听登录状态变化
            TCClient.Utils.AppSession.OnLoginStatusChanged += (isLoggedIn) =>
            {
                LogManager.Log("App", $"登录状态变更: {isLoggedIn}");
            };
            
            // 监听退出请求状态变化
            TCClient.Utils.AppSession.OnExitRequestChanged += (isExitRequested) =>
            {
                LogManager.Log("App", $"退出请求状态变更: {isExitRequested}");
                
                // 当用户请求退出时，明确关闭应用程序
                if (isExitRequested)
                {
                    LogManager.Log("App", "检测到退出请求，准备关闭应用程序");
                    Shutdown();
                }
            };
            
            // 添加应用程序退出事件
            Current.Exit += Application_Exit;

            // 创建主窗口实例但不显示
            LogManager.Log("App", "预先创建主窗口实例");
            var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.DataContext = mainViewModel;
            
            // 设置为主窗口
            Current.MainWindow = mainWindow;
            LogManager.Log("App", "主窗口已设置为Current.MainWindow");
            
            // 创建并显示登录窗口
            var loginWindow = _serviceProvider.GetRequiredService<LoginWindow>();
            loginWindow.Owner = null; // 确保登录窗口没有所有者
            
            try
            {
                LogManager.Log("App", "显示登录窗口");
                var result = loginWindow.ShowDialog();
                LogManager.Log("App", $"登录窗口关闭，结果: {result}");

                // 如果登录成功
                bool loginSuccess = result == true || TCClient.Utils.AppSession.IsLoggedIn;
                LogManager.Log("App", $"登录状态: DialogResult={result}, IsLoggedIn={TCClient.Utils.AppSession.IsLoggedIn}, 最终判断={loginSuccess}");

                if (loginSuccess)
                {
                    // 释放登录窗口资源
                    loginWindow = null;
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    
                    // 显示主窗口，简化流程
                    LogManager.Log("App", "登录成功，调用ShowMainWindow");
                    
                    try
                    {
                        // 直接调用ShowMainWindow
                        ShowMainWindow();
                    }
                    catch (Exception mainEx)
                    {
                        LogManager.LogException("App.ShowMainWindow", mainEx);
                        MessageBox.Show($"显示主窗口时出错: {mainEx.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    LogManager.Log("App", "登录取消，关闭应用程序");
                    Shutdown();
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("App.ShowLoginWindow", ex);
                MessageBox.Show($"显示登录窗口时发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }
        catch (Exception ex)
        {
            LogManager.LogException("App.OnStartup", ex);
            MessageBox.Show($"应用程序启动时发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        try
        {
            // 检查是否是用户请求的退出
            bool isUserExit = TCClient.Utils.AppSession.UserRequestedExit;
            LogManager.Log("App", $"应用程序退出事件触发，是否用户请求: {isUserExit}");
            
            if (!isUserExit)
            {
                LogManager.Log("App", "警告：这不是用户通过ExitCommand请求的退出");
            }
            
            // 最后刷新日志
            LogManager.FlushLogs();
        }
        catch (Exception ex)
        {
            LogManager.LogException("App.Exit", ex);
        }
    }

    // 完全重写的主窗口显示方法
    private void ShowMainWindow()
    {
        LogManager.Log("App", "显示主窗口开始");
        try
        {
            // 创建全新的MainWindow实例
            LogManager.Log("App", "创建全新的MainWindow实例");
            var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.DataContext = mainViewModel;
            
            // 直接设置为主窗口并显示
            LogManager.Log("App", "将窗口设置为主窗口并显示");
            Application.Current.MainWindow = mainWindow;
            
            // 使用最简单的Show方法，避免复杂的逻辑
            mainWindow.Show();
            
            LogManager.Log("App", "主窗口显示完成");
        }
        catch (Exception ex)
        {
            LogManager.LogException("App.ShowMainWindow", ex);
            MessageBox.Show($"显示主窗口时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            LogManager.Log("App", "OnExit方法开始执行");
            
            // 确保所有数据库连接被关闭
            var databaseService = _serviceProvider.GetService<IDatabaseService>();
            if (databaseService != null)
            {
                // 使用同步调用避免异步过程被截断
                try
                {
                    var task = databaseService.DisconnectAsync();
                    task.Wait(1000); // 给数据库断开连接一点时间，但不要无限等待
                }
                catch (Exception dbEx)
                {
                    LogManager.LogException("App.DisconnectDatabase", dbEx);
                }
            }
            
            // 确保所有HttpClient实例被释放
            if (_serviceProvider.GetService<IExchangeServiceFactory>() is IDisposable exchangeFactory)
            {
                exchangeFactory.Dispose();
            }
            
            // 告诉GC立即执行垃圾回收，确保资源释放
            GC.Collect();
            GC.WaitForPendingFinalizers();
            
            // 刷新日志
            LogManager.FlushLogs();
            
            // 最后才释放服务提供者
            base.OnExit(e);
            if (_serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
            
            LogManager.Log("App", "OnExit方法执行完成");
        }
        catch (Exception ex)
        {
            LogManager.LogException("App.OnExit", ex);
            // 即使清理过程出错，也不影响应用程序退出
        }
    }

    public class MessageBoxService : IMessageService
    {
        public MessageBoxResult ShowMessage(string message, string title, MessageBoxButton buttons, MessageBoxImage icon)
        {
            return MessageBox.Show(message, title, buttons, icon);
        }
    }
}

