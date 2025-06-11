using System.Windows;
using System.Windows.Controls;
using TCClient.ViewModels;
using System.IO;
using TCClient.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Threading;
using System.Threading.Tasks;
using System;
using TCClient.Utils;

namespace TCClient.Views
{
    public partial class LoginWindow : Window
    {
        private readonly LoginViewModel _viewModel;
        private bool _isUpdatingPassword;
        private TextBox _passwordTextBox;
        private bool _isPasswordVisible;
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "TCClient_LoginWindow.log");
        private static readonly SemaphoreSlim _logSemaphore = new SemaphoreSlim(1, 1);
        private readonly IServiceProvider _services;

        private static async Task LogToFileAsync(string message)
        {
            // 日志输出已禁用
            // 如需启用，请取消注释以下代码：
            /*
            try
            {
                await _logSemaphore.WaitAsync();
                try
                {
                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    var logMessage = $"[{timestamp}] {message}{Environment.NewLine}";
                    await File.AppendAllTextAsync(LogFilePath, logMessage);
                }
                finally
                {
                    _logSemaphore.Release();
                }
            }
            catch
            {
                // 忽略日志写入失败
            }
            */
        }

        public LoginWindow(IServiceProvider services)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("LoginWindow 构造函数开始");
                
                _services = services;
                try
                {
                    InitializeComponent();
                    System.Diagnostics.Debug.WriteLine("InitializeComponent 完成");
                    InitializePasswordToggle();

                    _viewModel = new LoginViewModel(
                        _services.GetRequiredService<IDatabaseService>(),
                        _services.GetRequiredService<LocalConfigService>(),
                        _services.GetRequiredService<IUserService>(),
                        _services.GetRequiredService<IMessageService>()
                    );

                    DataContext = _viewModel;

                    // 监听密码属性的变化
                    _viewModel.PropertyChanged += async (s, e) =>
                    {
                        if (e.PropertyName == nameof(_viewModel.Password))
                        {
                            await LogToFileAsync($"密码属性已更改");
                            await Dispatcher.InvokeAsync(() => UpdatePasswordBox());
                        }
                    };

                    // 监听登录成功事件
                    _viewModel.LoginSuccess += async (s, args) =>
                    {
                        try
                        {
                            await LogToFileAsync("登录成功事件触发");
                            
                            // 使用当前线程直接设置DialogResult并关闭窗口，避免跨线程操作
                            await LogToFileAsync("准备设置DialogResult和关闭窗口");
                            await LogToFileAsync($"当前线程ID: {Thread.CurrentThread.ManagedThreadId}");
                            
                            // 清理事件订阅，防止内存泄漏和冲突
                            await LogToFileAsync("清理事件订阅");
                            _viewModel.LoginSuccess -= (sender, eventArgs) => { };
                            _viewModel.PropertyChanged -= (sender, eventArgs) => { };
                            this.Closing -= (sender, eventArgs) => { };
                            this.Closed -= (sender, eventArgs) => { };
                            await LogToFileAsync("事件订阅已清理");
                            
                            if (Dispatcher.CheckAccess())
                            {
                                await LogToFileAsync("当前在UI线程上");
                                
                                try
                                {
                                    // 检查窗口是否作为对话框显示
                                    bool canSetDialogResult = Owner != null || IsActive;
                                    await LogToFileAsync($"窗口状态检查: Owner={Owner != null}, IsActive={IsActive}, CanSetDialogResult={canSetDialogResult}");
                                    
                                    // 先隐藏窗口
                                    Visibility = Visibility.Hidden;
                                    await LogToFileAsync("窗口已隐藏");
                                    
                                    // 设置全局登录状态
                                    AppSession.IsLoggedIn = true;
                                    await LogToFileAsync("已设置AppSession.IsLoggedIn = true标志");
                                    
                                    // 安全设置对话框结果
                                    if (canSetDialogResult)
                                    {
                                        DialogResult = true;
                                        await LogToFileAsync($"DialogResult设置为: {DialogResult}");
                                    }
                                    else
                                    {
                                        await LogToFileAsync("窗口无法设置DialogResult，将直接关闭");
                                    }
                                    
                                    // 安全关闭窗口
                                    try
                                    {
                                        Close();
                                        await LogToFileAsync("窗口已关闭");
                                    }
                                    catch (Exception closeEx)
                                    {
                                        await LogToFileAsync($"关闭窗口时出错: {closeEx.Message}");
                                    }
                                }
                                catch (InvalidOperationException ex)
                                {
                                    // 特殊处理DialogResult无法设置的情况
                                    await LogToFileAsync($"无法设置DialogResult: {ex.Message}");
                                    AppSession.IsLoggedIn = true; // 确保标记登录成功
                                    await LogToFileAsync("已设置AppSession.IsLoggedIn = true标志");
                                    
                                    // 直接关闭窗口
                                    Close();
                                    await LogToFileAsync("已直接关闭窗口");
                                }
                            }
                            else
                            {
                                await LogToFileAsync("不在UI线程上，使用Dispatcher.Invoke");
                                await Dispatcher.InvokeAsync(async () =>
                                {
                                    try
                                    {
                                        // 检查窗口是否作为对话框显示
                                        bool canSetDialogResult = Owner != null || IsActive;
                                        await LogToFileAsync($"通过Dispatcher检查窗口状态: Owner={Owner != null}, IsActive={IsActive}, CanSetDialogResult={canSetDialogResult}");
                                        
                                        // 先隐藏窗口
                                        Visibility = Visibility.Hidden;
                                        await LogToFileAsync("通过Dispatcher隐藏窗口");
                                        
                                        // 设置全局登录状态
                                        AppSession.IsLoggedIn = true;
                                        await LogToFileAsync("已设置AppSession.IsLoggedIn = true标志");
                                        
                                        // 安全设置对话框结果
                                        if (canSetDialogResult)
                                        {
                                            DialogResult = true;
                                            await LogToFileAsync($"DialogResult设置为: {DialogResult}");
                                        }
                                        else
                                        {
                                            await LogToFileAsync("窗口无法设置DialogResult，将直接关闭");
                                        }
                                        
                                        // 安全关闭窗口
                                        try
                                        {
                                            Close();
                                            await LogToFileAsync("窗口已关闭");
                                        }
                                        catch (Exception closeEx)
                                        {
                                            await LogToFileAsync($"关闭窗口时出错: {closeEx.Message}");
                                        }
                                    }
                                    catch (Exception dispatcherEx)
                                    {
                                        await LogToFileAsync($"Dispatcher线程内发生异常: {dispatcherEx.Message}");
                                        await LogToFileAsync($"异常堆栈: {dispatcherEx.StackTrace}");
                                        
                                        // 特殊处理DialogResult无法设置的情况
                                        if (dispatcherEx is InvalidOperationException)
                                        {
                                            AppSession.IsLoggedIn = true; // 确保标记登录成功
                                            await LogToFileAsync("设置AppSession.IsLoggedIn = true标志");
                                            Close(); // 直接关闭窗口
                                            await LogToFileAsync("已直接关闭窗口");
                                        }
                                        else
                                        {
                                            MessageBox.Show($"关闭窗口时出错: {dispatcherEx.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                                        }
                                    }
                                });
                                await LogToFileAsync("Dispatcher.Invoke已调用");
                            }
                        }
                        catch (Exception ex)
                        {
                            await LogToFileAsync($"登录成功处理失败: {ex.Message}");
                            await LogToFileAsync($"异常堆栈: {ex.StackTrace}");
                            // 确保标记登录成功，即使异常也要尝试
                            AppSession.IsLoggedIn = true;
                            await LogToFileAsync("已设置AppSession.IsLoggedIn = true标志");
                            
                            try
                            {
                                // 尝试关闭窗口
                                Close();
                                await LogToFileAsync("已在异常处理中关闭窗口");
                            }
                            catch
                            {
                                // 忽略关闭窗口时的异常
                            }
                        }
                    };

                    // 初始化密码框
                    UpdatePasswordBox();

                    // 设置窗口为对话框模式
                    WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    ResizeMode = ResizeMode.NoResize;
                    ShowInTaskbar = false;

                    // 在窗口加载完成后标记为已初始化
                    Loaded += async (s, e) => 
                    {
                        await LogToFileAsync("窗口已加载完成");
                    };

                    // 监听窗口关闭事件
                    Closing += async (s, e) =>
                    {
                        await LogToFileAsync($"窗口 Closing 事件触发");
                        if (DialogResult != true)
                        {
                            await LogToFileAsync("登录未成功，取消登录操作");
                            _viewModel.CancelLogin();
                        }
                    };

                    // 监听窗口关闭完成事件
                    Closed += async (s, e) =>
                    {
                        await LogToFileAsync("窗口 Closed 事件触发");
                    };
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"初始化登录窗口时发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    throw;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"LoginWindow 构造函数发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }

        private void InitializePasswordToggle()
        {
            try
            {
                _passwordTextBox = new TextBox
                {
                    Visibility = Visibility.Collapsed,
                    Height = PasswordBox.Height,
                    Margin = PasswordBox.Margin,
                    VerticalContentAlignment = VerticalAlignment.Center
                };

                Grid.SetColumn(_passwordTextBox, 0);
                ((Grid)PasswordBox.Parent).Children.Add(_passwordTextBox);

                _passwordTextBox.TextChanged += async (s, e) =>
                {
                    if (!_isUpdatingPassword)
                    {
                        _isUpdatingPassword = true;
                        try
                        {
                            await Dispatcher.InvokeAsync(() =>
                            {
                                PasswordBox.Password = _passwordTextBox.Text;
                                _viewModel.Password = _passwordTextBox.Text;
                            });
                        }
                        finally
                        {
                            _isUpdatingPassword = false;
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                MessageBox.Show($"初始化密码切换功能时发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdatePasswordBox()
        {
            if (!_isUpdatingPassword)
            {
                _isUpdatingPassword = true;
                try
                {
                    if (_isPasswordVisible)
                    {
                        _passwordTextBox.Text = _viewModel.Password;
                        _passwordTextBox.Visibility = Visibility.Visible;
                        PasswordBox.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        PasswordBox.Password = _viewModel.Password;
                        PasswordBox.Visibility = Visibility.Visible;
                        _passwordTextBox.Visibility = Visibility.Collapsed;
                    }
                }
                finally
                {
                    _isUpdatingPassword = false;
                }
            }
        }

        private async void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (!_isUpdatingPassword)
            {
                _isUpdatingPassword = true;
                try
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        _viewModel.Password = PasswordBox.Password;
                        if (_isPasswordVisible)
                        {
                            _passwordTextBox.Text = PasswordBox.Password;
                        }
                    });
                }
                finally
                {
                    _isUpdatingPassword = false;
                }
            }
        }

        private async void TogglePasswordVisibility_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _isPasswordVisible = !_isPasswordVisible;
                var button = (Button)sender;
                button.Content = _isPasswordVisible ? "隐藏" : "显示";
                UpdatePasswordBox();
            }
            catch (Exception ex)
            {
                await LogToFileAsync($"切换密码可见性时发生错误: {ex.Message}");
                MessageBox.Show($"切换密码可见性时发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override async void OnClosed(EventArgs e)
        {
            try
            {
                base.OnClosed(e);
                await LogToFileAsync("窗口已关闭，清理事件订阅");
            }
            catch (Exception ex)
            {
                await LogToFileAsync($"窗口关闭事件处理时发生错误: {ex.Message}");
            }
        }

        public LoginViewModel ViewModel => _viewModel;
    }
} 