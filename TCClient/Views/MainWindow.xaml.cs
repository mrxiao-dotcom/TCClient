using System;
using System.Windows;
using TCClient.ViewModels;
using TCClient.Services;
using TCClient.Models;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Threading;

namespace TCClient.Views
{
    public partial class MainWindow : Window
    {
        private readonly IExchangeService _exchangeService;
        private bool _userInitiatedClose = false;

        public MainWindow()
        {
            Utils.LogManager.Log("MainWindow", "=== MainWindow构造函数开始 ===");
            
            try
            {
                Utils.LogManager.Log("MainWindow", "准备初始化组件");
                InitializeComponent();
                Utils.LogManager.Log("MainWindow", "组件初始化完成");

                Utils.LogManager.Log("MainWindow", $"窗口初始状态: IsVisible={IsVisible}, WindowState={WindowState}, Width={Width}, Height={Height}");

                // 窗口关闭事件处理 - 与菜单"退出"功能保持一致
                this.Closing += (s, e) => 
                {
                    try
                    {
                        if (!_userInitiatedClose && !TCClient.Utils.AppSession.UserRequestedExit)
                        {
                            Utils.LogManager.Log("MainWindow", "Closing事件：用户点击关闭按钮，执行退出确认流程");
                            e.Cancel = true; // 先取消关闭
                            
                            // 使用Dispatcher.BeginInvoke延迟执行确认对话框，避免在Closing事件中直接操作窗口
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try
                                {
                                    // 调用与菜单"退出"相同的确认流程
                                    if (DataContext is MainViewModel viewModel)
                                    {
                                        Utils.LogManager.Log("MainWindow", "调用MainViewModel的ExitCommand");
                                        if (viewModel.ExitCommand.CanExecute(null))
                                        {
                                            viewModel.ExitCommand.Execute(null);
                                        }
                                    }
                                    else
                                    {
                                        Utils.LogManager.Log("MainWindow", "DataContext不是MainViewModel，直接执行退出确认");
                                        // 如果无法获取ViewModel，直接执行简单的确认对话框
                                        var result = MessageBox.Show(
                                            "确定要退出应用程序吗？",
                                            "确认",
                                            MessageBoxButton.YesNo,
                                            MessageBoxImage.Question);
                                        
                                        if (result == MessageBoxResult.Yes)
                                        {
                                            Utils.LogManager.Log("MainWindow", "用户确认退出，设置退出标志");
                                            _userInitiatedClose = true;
                                            TCClient.Utils.AppSession.UserRequestedExit = true;
                                            Close(); // 重新调用关闭
                                        }
                                    }
                                }
                                catch (Exception dialogEx)
                                {
                                    Utils.LogManager.LogException("MainWindow", dialogEx, "延迟执行确认对话框时发生异常");
                                    // 如果确认对话框失败，直接允许关闭
                                    _userInitiatedClose = true;
                                    TCClient.Utils.AppSession.UserRequestedExit = true;
                                    Close();
                                }
                            }), System.Windows.Threading.DispatcherPriority.Background);
                        }
                        else
                        {
                            Utils.LogManager.Log("MainWindow", "Closing事件：用户请求的关闭，允许继续");
                        }
                    }
                    catch (Exception closingEx)
                    {
                        Utils.LogManager.LogException("MainWindow", closingEx, "处理Closing事件时发生异常");
                        // 发生异常时允许窗口关闭，避免程序无法退出
                        e.Cancel = false;
                    }
                };
                Utils.LogManager.Log("MainWindow", "已添加Closing事件保护");

                // 创建默认的交易账户，但不强制要求 API 密钥
                var defaultAccount = new TradingAccount
                {
                    AccountName = "默认账户",
                    ApiKey = string.Empty,
                    ApiSecret = string.Empty,
                    IsActive = 1,
                    CreateTime = DateTime.Now,
                    UpdateTime = DateTime.Now
                };
                Utils.LogManager.Log("MainWindow", "默认账户创建完成");

                try
                {
                    // 尝试创建交易服务，但不强制要求 API 密钥
                    Utils.LogManager.Log("MainWindow", "准备获取交易服务工厂");
                    var exchangeServiceFactory = ((App)Application.Current).Services.GetRequiredService<IExchangeServiceFactory>();
                    Utils.LogManager.Log("MainWindow", "交易服务工厂获取成功");
                    
                    _exchangeService = exchangeServiceFactory.CreateExchangeService(defaultAccount);
                    Utils.LogManager.Log("MainWindow", "交易服务创建成功");
                }
                catch (Exception ex)
                {
                    // 如果创建失败，记录日志但不抛出异常
                    Utils.LogManager.LogException("MainWindow", ex);
                    _exchangeService = null;
                }

                // 注册加载和关闭事件
                this.Loaded += MainWindow_Loaded;
                this.Closed += MainWindow_Closed;
                
                // 记录当前应用程序的ShutdownMode
                if (Application.Current is App)
                {
                    Utils.LogManager.Log("MainWindow", $"当前App的ShutdownMode={Application.Current.ShutdownMode}");
                }
                
                Utils.LogManager.Log("MainWindow", "事件处理程序注册完成");
            }
            catch (Exception ex)
            {
                Utils.LogManager.LogException("MainWindow", ex);
                MessageBox.Show($"初始化主窗口时发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Utils.LogManager.Log("MainWindow", "=== MainWindow构造函数结束 ===");
        }
        
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Utils.LogManager.Log("MainWindow", "=== MainWindow_Loaded事件触发 ===");
            Utils.LogManager.Log("MainWindow", $"窗口加载状态: IsVisible={IsVisible}, WindowState={WindowState}");
            
            try
            {
                // 设置状态消息
                if (DataContext is MainViewModel viewModel)
                {
                    viewModel.StatusMessage = "主窗口已加载 - " + DateTime.Now.ToString("HH:mm:ss");
                    Utils.LogManager.Log("MainWindow", "状态消息已更新");
                }
                
                // 确保窗口最大化并可见
                Visibility = Visibility.Visible;
                WindowState = WindowState.Maximized;
                Utils.LogManager.Log("MainWindow", "窗口已设置为最大化并可见");
                
                // 强制焦点
                Activate();
                Focus();
                Utils.LogManager.Log("MainWindow", "窗口已激活并获取焦点");
            }
            catch (Exception ex)
            {
                Utils.LogManager.LogException("MainWindow", ex);
            }
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            Utils.LogManager.Log("MainWindow", "=== MainWindow_Closed事件触发 ===");
            
            try
            {
                // 强制刷新日志，确保所有日志都被写入
                Utils.LogManager.FlushLogs();
            }
            catch
            {
                // 忽略最后的日志刷新错误
            }
        }

        /// <summary>
        /// 由用户明确调用的关闭方法
        /// </summary>
        public void CloseByUser()
        {
            Utils.LogManager.Log("MainWindow", "用户明确要求关闭主窗口 (CloseByUser方法被调用)");
            _userInitiatedClose = true;
            Utils.AppSession.UserRequestedExit = true;
            Utils.LogManager.Log("MainWindow", "已设置_userInitiatedClose = true和UserRequestedExit = true，允许关闭");
            Close();
            Utils.LogManager.Log("MainWindow", "Close方法已调用");
        }

        public void SetStatusMessage(string message)
        {
            Utils.LogManager.Log("MainWindow", $"设置状态消息: {message}");
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.StatusMessage = message;
                Utils.LogManager.Log("MainWindow", "状态消息已更新");
            }
            else
            {
                Utils.LogManager.Log("MainWindow", "警告: 无法设置状态消息，DataContext不是MainViewModel类型");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                Utils.LogManager.Log("MainWindow", "OnClosed方法开始执行");
                
                // 使用单独的资源清理方法，避免重复调用
                if (_exchangeService != null)
                {
                    CleanupResources();
                }
                
                Utils.LogManager.Log("MainWindow", "OnClosed方法执行完成");
            }
            catch (Exception ex)
            {
                Utils.LogManager.LogException("MainWindow", ex);
            }
            finally
            {
                try
                {
                    Utils.LogManager.Log("MainWindow", "调用base.OnClosed");
                    base.OnClosed(e);
                    Utils.LogManager.Log("MainWindow", "base.OnClosed调用完成");
                }
                catch (Exception ex)
                {
                    Utils.LogManager.LogException("MainWindow", ex);
                }
            }
        }

        // 单独提取资源清理方法，避免重复调用
        private void CleanupResources()
        {
            try
            {
                Utils.LogManager.Log("MainWindow", "开始清理资源");
                
                // 释放交易服务资源
                if (_exchangeService is IDisposable disposable)
                {
                    Utils.LogManager.Log("MainWindow", "释放交易服务资源");
                    disposable.Dispose();
                    Utils.LogManager.Log("MainWindow", "交易服务资源已释放");
                }
                
                Utils.LogManager.Log("MainWindow", "资源清理完成");
            }
            catch (Exception ex)
            {
                Utils.LogManager.LogException("MainWindow", ex);
            }
        }

        /// <summary>
        /// 添加一个简单的初始化方法，让App.xaml.cs调用
        /// </summary>
        public void Initialize()
        {
            Utils.LogManager.Log("MainWindow", "=== Initialize方法调用 ===");
        }
    }
} 