using System.Configuration;
using System.Data;
using System.Windows;
using System.IO;
using TCClient.Services;
using TCClient.Utils;
using TCClient.ViewModels;
using TCClient.Views;

namespace TCClient;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        "TCClient_Startup.log");

    private static void LogToFile(string message)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var logMessage = $"[{timestamp}] {message}{Environment.NewLine}";
            File.AppendAllText(LogFilePath, logMessage);
        }
        catch
        {
            // 忽略日志写入失败
        }
    }

    public App()
    {
        LogToFile("App 构造函数开始执行...");
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            LogToFile($"未处理的异常: {e.ExceptionObject}");
        };
        DispatcherUnhandledException += (s, e) =>
        {
            LogToFile($"UI线程未处理的异常: {e.Exception}");
            e.Handled = true;
        };
        LogToFile("App 构造函数执行完成");
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            // 初始化服务
            InitializeServices();

            // 创建主窗口
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;

            // 先显示主窗口（但保持隐藏状态）
            mainWindow.Show();
            mainWindow.Hide();

            // 等待主窗口完全加载
            mainWindow.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

            // 创建登录窗口
            var loginWindow = new LoginWindow
            {
                Owner = mainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            // 显示登录窗口
            var loginResult = loginWindow.ShowDialog();

            // 根据登录结果决定是否继续运行
            if (loginResult == true)
            {
                // 登录成功，显示主窗口
                mainWindow.Show();
            }
            else
            {
                // 登录失败或取消，关闭应用程序
                Shutdown();
            }
        }
        catch (Exception ex)
        {
            LogToFile($"启动过程中发生错误: {ex}");
            MessageBox.Show($"应用程序启动失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void InitializeServices()
    {
        try
        {
            LogToFile("\n=== 应用程序启动开始 ===");
            LogToFile($"启动参数: {string.Join(", ", AppDomain.CurrentDomain.FriendlyName)}");
            
            LogToFile("正在调用基类 OnStartup...");
            LogToFile("正在创建配置服务...");
            var configService = new LocalConfigService();
            LogToFile("配置服务创建完成");

            LogToFile("正在注册 LocalConfigService...");
            ServiceLocator.RegisterService<LocalConfigService>(configService);
            LogToFile("LocalConfigService 注册完成");

            LogToFile("正在创建数据库服务...");
            var databaseService = new MySqlDatabaseService();
            LogToFile("数据库服务创建完成");

            LogToFile("正在注册 IUserService...");
            ServiceLocator.RegisterService<Services.IUserService>(databaseService);
            LogToFile("IUserService 注册完成");

            LogToFile("正在注册 IMessageService...");
            ServiceLocator.RegisterService<IMessageService>(new MessageBoxService());
            LogToFile("IMessageService 注册完成");

            LogToFile("正在注册 IDatabaseService...");
            ServiceLocator.RegisterService<IDatabaseService>(databaseService);
            LogToFile("IDatabaseService 注册完成");
            
            LogToFile("\n--- 开始注册排行榜服务 ---");
            LogToFile("正在获取数据库连接字符串...");
            var connectionString = configService.GetCurrentConnectionString();
            LogToFile("数据库连接字符串获取完成");

            LogToFile("正在注册 IRankingService...");
            ServiceLocator.RegisterService<IRankingService>(new RankingService(connectionString));
            LogToFile("IRankingService 注册完成");

            LogToFile("\n--- 开始创建登录窗口 ---");
            LogToFile("正在实例化 LoginWindow...");
            LogToFile("窗口事件处理设置完成");

            LogToFile("\n=== 应用程序启动完成，等待用户操作 ===\n");
        }
        catch (Exception ex)
        {
            LogToFile("\n!!! 应用程序启动失败 !!!");
            LogToFile($"异常类型: {ex.GetType().FullName}");
            LogToFile($"异常消息: {ex.Message}");
            LogToFile($"异常堆栈: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                LogToFile("\n内部异常:");
                LogToFile($"类型: {ex.InnerException.GetType().FullName}");
                LogToFile($"消息: {ex.InnerException.Message}");
                LogToFile($"堆栈: {ex.InnerException.StackTrace}");
            }
            MessageBox.Show($"应用程序启动失败：{ex.Message}\n\n详细错误：{ex}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    public class MessageBoxService : IMessageService
    {
        public MessageBoxResult ShowMessage(string message, string title, MessageBoxButton buttons, MessageBoxImage icon)
        {
            LogToFile($"显示消息框 - 标题: {title}, 消息: {message}");
            return MessageBox.Show(message, title, buttons, icon);
        }
    }
}

