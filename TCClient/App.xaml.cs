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
                
                // 检查是否为网络异常，使用友好的错误提示
                if (Utils.NetworkExceptionHandler.IsNetworkException(ex))
                {
                    Utils.NetworkExceptionHandler.ShowNetworkExceptionDialog(
                        Current.MainWindow, ex, "应用程序运行时发生网络异常", false);
                }
                else
                {
                    MessageBox.Show($"未处理的异常: {ex?.GetType().FullName}\n{ex?.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            
            DispatcherUnhandledException += (s, e) =>
            {
                var ex = e.Exception;
                LogManager.LogException("App.DispatcherUnhandledException", ex);
                
                // 检查是否为数据库异常，优先处理
                if (Utils.NetworkExceptionHandler.IsDatabaseException(ex))
                {
                    LogManager.Log("App", "检测到UI线程数据库异常，使用友好提示处理");
                    Utils.NetworkExceptionHandler.HandleDatabaseException(ex, "UI操作时的数据库操作", true);
                }
                // 检查是否为网络异常，使用友好的错误提示
                else if (Utils.NetworkExceptionHandler.IsNetworkException(ex))
                {
                    LogManager.Log("App", "检测到UI线程网络异常，使用友好提示处理");
                    Utils.NetworkExceptionHandler.ShowNetworkExceptionDialog(
                        Current.MainWindow, ex, "UI操作时发生网络异常", false);
                }
                else
                {
                    LogManager.Log("App", $"UI线程未处理的一般异常: {ex.GetType().FullName}");
                    MessageBox.Show($"UI线程未处理的异常: {ex.GetType().FullName}\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                e.Handled = true;
            };
            
            // 添加TaskScheduler未观察到的异常处理
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                var ex = e.Exception;
                
                // 检查是否是TaskCanceledException，如果是则不记录为异常
                bool isTaskCanceled = false;
                if (ex is AggregateException aggEx)
                {
                    isTaskCanceled = aggEx.InnerExceptions.All(innerEx => 
                        innerEx is TaskCanceledException || innerEx is OperationCanceledException);
                }
                else if (ex is TaskCanceledException || ex is OperationCanceledException)
                {
                    isTaskCanceled = true;
                }
                
                if (isTaskCanceled)
                {
                    LogManager.Log("App", "检测到任务取消异常，这是正常的应用程序关闭行为");
                }
                else
                {
                    LogManager.LogException("App.UnobservedTaskException", ex);
                    
                    // 检查是否为数据库异常，优先处理
                    if (Utils.NetworkExceptionHandler.IsDatabaseException(ex))
                    {
                        LogManager.Log("App", "检测到未观察的数据库异常，已使用友好提示处理");
                        // 对于后台任务的数据库异常，我们记录日志但不显示对话框，避免干扰用户
                        Utils.NetworkExceptionHandler.HandleDatabaseException(ex, "后台任务数据库操作", false);
                    }
                    // 检查是否为网络异常，提供友好的用户提示
                    else if (Utils.NetworkExceptionHandler.IsNetworkException(ex))
                    {
                        LogManager.Log("App", "检测到未观察的网络异常，已使用友好提示处理");
                        // 对于后台任务的网络异常，我们记录日志但不显示对话框，避免干扰用户
                        Utils.NetworkExceptionHandler.LogNetworkException("后台任务", ex);
                    }
                    // 检查是否为止损监控服务的异常
                    else if (ex.StackTrace?.Contains("StopLossMonitorService") == true)
                    {
                        LogManager.Log("App", "检测到止损监控服务的未观察异常，已记录但不影响程序运行");
                        LogManager.Log("App", $"止损监控异常详情: {ex.GetType().Name} - {ex.Message}");
                        // 对于止损监控的异常，只记录日志，不显示用户对话框
                    }
                }
                
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

        // 注册服务（使用工厂模式确保依赖注入正确）
        services.AddSingleton<LocalConfigService>();
        services.AddSingleton<MySqlDatabaseService>();
        services.AddSingleton<IDatabaseService>(provider => provider.GetRequiredService<MySqlDatabaseService>());
        services.AddSingleton<IUserService>(provider => provider.GetRequiredService<MySqlDatabaseService>());
        services.AddSingleton<IMessageService, MessageBoxService>();
        services.AddSingleton<IRankingService>(provider =>
        {
            var configService = provider.GetRequiredService<LocalConfigService>();
            return new RankingService(configService.GetCurrentConnectionString());
        });
        services.AddSingleton<IAccountService>(provider =>
        {
            var userService = provider.GetRequiredService<IUserService>();
            return new AccountService(userService);
        });
        services.AddSingleton<IExchangeServiceFactory>(provider =>
        {
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            return new ExchangeServiceFactory(loggerFactory);
        });

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
        
        // 注册止损监控服务
        services.AddSingleton<StopLossMonitorService>();
        
        // 注册后台服务管理器
        services.AddSingleton<BackgroundServiceManager>();
        
        // 注册自选合约服务
        services.AddSingleton<FavoriteContractsService>();

        // 注册 ViewModel
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<LoginViewModel>();
        services.AddSingleton<OrderViewModel>();
        services.AddSingleton<AccountConfigViewModel>();
        services.AddSingleton<AddAccountViewModel>();
        services.AddSingleton<RegisterViewModel>();
        services.AddSingleton<RankingViewModel>(provider =>
        {
            var databaseService = provider.GetRequiredService<IDatabaseService>();
            var messageService = provider.GetRequiredService<IMessageService>();
            var exchangeService = provider.GetRequiredService<IExchangeService>();
            return new RankingViewModel(databaseService, messageService, exchangeService);
        });
        services.AddTransient<OrderListViewModel>();
        services.AddTransient<PushStatisticsViewModel>();
        services.AddTransient<AccountQueryViewModel>();

        // 注册 View
        services.AddTransient<MainWindow>();
        services.AddTransient<LoginWindow>();
        services.AddTransient<OrderWindow>();
        services.AddTransient<AccountConfigWindow>();
        services.AddTransient<AddAccountWindow>();
        services.AddTransient<RegisterWindow>();
        services.AddTransient<PushStatisticsWindow>();
        services.AddTransient<AccountQueryWindow>();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            LogManager.Log("App", "应用程序启动开始");
            base.OnStartup(e);

            // 启动后台服务管理器
            try
            {
                var backgroundServiceManager = _serviceProvider.GetRequiredService<BackgroundServiceManager>();
                backgroundServiceManager.StartAllEnabledServices();
                LogManager.Log("App", "后台服务管理器已启动");
            }
            catch (Exception serviceEx)
            {
                LogManager.LogException("App", serviceEx, "启动后台服务管理器失败");
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

            // 注册服务到ServiceLocator（为了向后兼容）
            LogManager.Log("App", "注册服务到ServiceLocator");
            ServiceLocator.RegisterService<IDatabaseService>(_serviceProvider.GetRequiredService<IDatabaseService>());
            ServiceLocator.RegisterService<IMessageService>(_serviceProvider.GetRequiredService<IMessageService>());
            ServiceLocator.RegisterService<IUserService>(_serviceProvider.GetRequiredService<IUserService>());
            ServiceLocator.RegisterService<IExchangeService>(_serviceProvider.GetRequiredService<IExchangeService>());
            LogManager.Log("App", "ServiceLocator服务注册完成");

            // 创建主窗口实例但不显示
            LogManager.Log("App", "预先创建主窗口实例");
            var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.DataContext = mainViewModel;
            
            // 设置为主窗口
            Current.MainWindow = mainWindow;
            LogManager.Log("App", "主窗口已设置为Current.MainWindow");
            
            // 创建并显示登录窗口
            try
            {
                LogManager.Log("App", "开始创建登录窗口");
                var loginWindow = _serviceProvider.GetRequiredService<LoginWindow>();
                LogManager.Log("App", "登录窗口创建成功");
                
                // 确保登录窗口正确显示
                loginWindow.Owner = null;
                loginWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                loginWindow.Topmost = false; // 不要设置为最顶层，可能会有问题
                loginWindow.ShowInTaskbar = true; // 显示在任务栏
                loginWindow.WindowState = WindowState.Normal;
                
                LogManager.Log("App", "准备显示登录窗口");
                
                // 直接显示模态对话框，ShowDialog会自动显示窗口
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
    private async void ShowMainWindow()
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
            
            // 更新状态栏信息
            try
            {
                LogManager.Log("App", "开始更新状态栏信息");
                
                // 获取当前登录的用户信息
                var userService = _serviceProvider.GetRequiredService<IUserService>();
                var currentUser = await userService.GetCurrentUserAsync();
                string username = currentUser?.Username ?? "未知用户";
                
                // 数据库连接状态
                var databaseService = _serviceProvider.GetRequiredService<IDatabaseService>();
                bool isConnected = await databaseService.TestConnectionAsync();
                string databaseStatus = isConnected ? "已连接" : "未连接";
                
                LogManager.Log("App", $"状态栏信息: 用户={username}, 数据库={databaseStatus}");
                
                // 更新主窗体状态栏
                await mainViewModel.UpdateStatusBarInfo(username, databaseStatus, databaseStatus);
                
                LogManager.Log("App", "状态栏信息更新完成");
            }
            catch (Exception statusEx)
            {
                LogManager.LogException("App", statusEx, "更新状态栏信息失败");
                // 即使状态栏更新失败，也不影响主窗口显示
            }
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
            
            // 停止后台服务管理器
            try
            {
                LogManager.Log("App", "开始停止后台服务管理器...");
                
                var backgroundServiceManager = _serviceProvider.GetService<BackgroundServiceManager>();
                if (backgroundServiceManager != null)
                {
                    backgroundServiceManager.StopAllServices();
                    backgroundServiceManager.Dispose();
                    LogManager.Log("App", "后台服务管理器已停止并释放");
                }
                
                LogManager.Log("App", "后台服务清理完成");
            }
            catch (Exception serviceEx)
            {
                LogManager.LogException("App", serviceEx, "停止后台服务管理器失败");
            }
            
            // 停止所有后台线程和定时器
            try
            {
                LogManager.Log("App", "开始清理后台线程和定时器...");
                
                // 清理可能的定时器和后台任务
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is Views.FindOpportunityWindow findOpportunityWindow)
                    {
                        // 强制关闭FindOpportunityWindow中的定时器和后台任务
                        window.Close();
                    }
                }
                
                LogManager.Log("App", "后台线程清理完成");
            }
            catch (Exception threadEx)
            {
                LogManager.LogException("App", threadEx, "清理后台线程失败");
            }
            
            // 确保所有数据库连接被关闭
            try
            {
                LogManager.Log("App", "开始关闭数据库连接...");
                var databaseService = _serviceProvider.GetService<IDatabaseService>();
                if (databaseService != null)
                {
                    // 使用同步调用避免异步过程被截断
                    var disconnectTask = databaseService.DisconnectAsync();
                    if (!disconnectTask.Wait(2000)) // 最多等待2秒
                    {
                        LogManager.Log("App", "数据库断开连接超时，强制继续");
                    }
                    else
                    {
                        LogManager.Log("App", "数据库连接已正常关闭");
                    }
                }
            }
            catch (Exception dbEx)
            {
                LogManager.LogException("App", dbEx, "关闭数据库连接失败");
            }
            
            // 确保所有HttpClient实例被释放
            try
            {
                LogManager.Log("App", "开始释放网络资源...");
                var exchangeServiceFactory = _serviceProvider.GetService<IExchangeServiceFactory>();
                if (exchangeServiceFactory is IDisposable disposableFactory)
                {
                    disposableFactory.Dispose();
                    LogManager.Log("App", "交易所服务工厂已释放");
                }
                
                var exchangeService = _serviceProvider.GetService<IExchangeService>();
                if (exchangeService is IDisposable disposableExchange)
                {
                    disposableExchange.Dispose();
                    LogManager.Log("App", "交易所服务已释放");
                }
            }
            catch (Exception httpEx)
            {
                LogManager.LogException("App", httpEx, "释放网络资源失败");
            }
            
            // 强制垃圾回收
            try
            {
                LogManager.Log("App", "执行垃圾回收...");
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                LogManager.Log("App", "垃圾回收完成");
            }
            catch (Exception gcEx)
            {
                LogManager.LogException("App", gcEx, "垃圾回收失败");
            }
            
            // 释放服务提供者
            try
            {
                LogManager.Log("App", "释放服务提供者...");
                if (_serviceProvider is IDisposable disposableProvider)
                {
                    disposableProvider.Dispose();
                    LogManager.Log("App", "服务提供者已释放");
                }
            }
            catch (Exception serviceProviderEx)
            {
                LogManager.LogException("App", serviceProviderEx, "释放服务提供者失败");
            }
            
            // 刷新日志
            try
            {
                LogManager.FlushLogs();
            }
            catch (Exception logEx)
            {
                // 忽略日志刷新错误
            }
            
            LogManager.Log("App", "OnExit方法执行完成，调用base.OnExit");
            
            // 调用基类方法
            base.OnExit(e);
            
            LogManager.Log("App", "base.OnExit完成");
        }
        catch (Exception ex)
        {
            LogManager.LogException("App.OnExit", ex);
        }
        finally
        {
            try
            {
                // 最后的保险措施：如果程序仍然没有退出，强制终止进程
                LogManager.Log("App", "执行最终清理检查...");
                
                // 给程序一点时间正常退出
                var exitTask = Task.Run(() =>
                {
                    System.Threading.Thread.Sleep(3000); // 等待3秒
                    
                    // 如果3秒后程序还在运行，强制退出
                    LogManager.Log("App", "程序退出超时，强制终止进程");
                    Environment.Exit(0);
                });
                
                LogManager.Log("App", "最终清理检查完成");
            }
            catch (Exception finalEx)
            {
                // 最后的异常也要忽略，确保程序能够退出
                try
                {
                    LogManager.LogException("App.OnExit.Finally", finalEx);
                }
                catch
                {
                    // 彻底忽略
                }
                
                // 强制退出
                Environment.Exit(1);
            }
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

