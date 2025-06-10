using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Linq;

namespace TCClient.Utils
{
    /// <summary>
    /// 网络异常统一处理工具类
    /// </summary>
    public static class NetworkExceptionHandler
    {
        /// <summary>
        /// 处理网络异常并显示友好的错误提示
        /// </summary>
        /// <param name="ex">异常对象</param>
        /// <param name="operation">操作描述（如"获取价格数据"）</param>
        /// <param name="showDialog">是否显示对话框（默认true）</param>
        public static void HandleNetworkException(Exception ex, string operation = "网络操作", bool showDialog = true)
        {
            try
            {
                string title = "网络连接异常";
                string message = "";
                MessageBoxImage icon = MessageBoxImage.Warning;

                // 根据异常类型生成不同的提示信息
                switch (ex)
                {
                    case TaskCanceledException _:
                        title = "网络连接超时";
                        message = $"📡 {operation}失败，连接超时\n\n" +
                                "🔍 可能的原因：\n" +
                                "• 网络连接不稳定\n" +
                                "• Binance API服务器响应慢\n" +
                                "• 防火墙或代理服务器阻止连接\n" +
                                "• 本地网络环境异常\n\n" +
                                "💡 建议解决方案：\n" +
                                "• 等待2-3分钟后重试\n" +
                                "• 检查网络连接状态\n" +
                                "• 尝试切换网络环境\n" +
                                "• 点击菜单栏'设置' > '网络诊断'进行检测";
                        break;

                    case TimeoutException _:
                        title = "网络请求超时";
                        message = $"⏰ {operation}超时\n\n" +
                                "💡 建议：\n" +
                                "• 等待几分钟后重试\n" +
                                "• 检查网络连接是否稳定";
                        break;

                    case System.Net.Http.HttpRequestException httpEx:
                        title = "网络请求失败";
                        message = $"🌐 {operation}失败\n\n" +
                                $"错误详情：{httpEx.Message}\n\n" +
                                "💡 建议：\n" +
                                "• 检查网络连接\n" +
                                "• 稍后重试";
                        break;

                    case System.Net.NetworkInformation.NetworkInformationException _:
                        title = "网络信息异常";
                        message = $"📶 网络信息获取失败\n\n" +
                                "💡 建议：\n" +
                                "• 检查网络适配器状态\n" +
                                "• 重启网络连接";
                        break;

                    default:
                        title = "网络异常";
                        message = $"❌ {operation}时发生异常\n\n" +
                                $"错误信息：{ex.Message}\n\n" +
                                "💡 建议：\n" +
                                "• 等待几分钟后重试\n" +
                                "• 检查网络连接状态\n" +
                                "• 如问题持续，请联系技术支持";
                        icon = MessageBoxImage.Error;
                        break;
                }

                // 记录到日志
                AppSession.Log($"[网络异常处理] {title}: {ex.Message}");
                AppSession.Log($"[网络异常处理] 异常类型: {ex.GetType().FullName}");
                AppSession.Log($"[网络异常处理] 操作: {operation}");

                // 在UI线程中显示对话框
                if (showDialog)
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        try
                        {
                            var result = MessageBox.Show(
                                message,
                                title,
                                MessageBoxButton.OK,
                                icon);

                            AppSession.Log($"[网络异常处理] 用户确认对话框: {result}");
                        }
                        catch (Exception dialogEx)
                        {
                            AppSession.Log($"[网络异常处理] 显示对话框失败: {dialogEx.Message}");
                        }
                    });
                }
            }
            catch (Exception handlerEx)
            {
                // 如果异常处理器本身出错，记录日志但不再抛出异常
                AppSession.Log($"[网络异常处理] 处理异常时发生错误: {handlerEx.Message}");
            }
        }

        /// <summary>
        /// 异步处理网络异常
        /// </summary>
        /// <param name="ex">异常对象</param>
        /// <param name="operation">操作描述</param>
        /// <param name="showDialog">是否显示对话框</param>
        public static async Task HandleNetworkExceptionAsync(Exception ex, string operation = "网络操作", bool showDialog = true)
        {
            await Task.Run(() => HandleNetworkException(ex, operation, showDialog));
        }

        /// <summary>
        /// 检查是否为网络相关异常
        /// </summary>
        /// <param name="ex">异常对象</param>
        /// <returns>是否为网络异常</returns>
        public static bool IsNetworkException(Exception ex)
        {
            if (ex == null) return false;

            // 处理AggregateException，检查其内部异常
            if (ex is AggregateException aggEx)
            {
                return aggEx.InnerExceptions.Any(innerEx => IsNetworkException(innerEx));
            }

            // 递归检查内部异常
            var currentEx = ex;
            while (currentEx != null)
            {
                switch (currentEx)
                {
                    case TaskCanceledException:
                    case TimeoutException:
                    case HttpRequestException:
                    case NetworkInformationException:
                    case SocketException:
                    case WebException:
                        return true;
                }
                
                // 检查数据库连接相关异常
                if (currentEx.Message.Contains("Authentication to host") ||
                    currentEx.Message.Contains("I/O error occurred") ||
                    currentEx.Message.Contains("connection failed") ||
                    currentEx.Message.Contains("timeout") ||
                    currentEx.Message.Contains("Timeout expired") ||
                    currentEx.Message.Contains("server is not responding") ||
                    currentEx.GetType().Name.Contains("Connection") ||
                    currentEx.GetType().Name.Contains("Timeout") ||
                    currentEx.GetType().Name.Contains("MySql"))
                {
                    return true;
                }
                
                currentEx = currentEx.InnerException;
            }

            return false;
        }

        /// <summary>
        /// 处理数据库异常并显示友好提示
        /// </summary>
        /// <param name="ex">数据库异常</param>
        /// <param name="operation">操作描述</param>
        /// <param name="showDialog">是否显示对话框</param>
        public static void HandleDatabaseException(Exception ex, string operation = "数据库操作", bool showDialog = true)
        {
            try
            {
                string title = "数据库连接异常";
                string message = "";
                MessageBoxImage icon = MessageBoxImage.Warning;

                // 检查具体的数据库异常类型
                if (ex.Message.Contains("Timeout expired") || ex.Message.Contains("server is not responding"))
                {
                    title = "数据库连接超时";
                    message = $"🗄️ {operation}超时\n\n" +
                            "🔍 可能的原因：\n" +
                            "• 数据库服务器响应慢\n" +
                            "• 网络连接不稳定\n" +
                            "• 查询数据量过大\n" +
                            "• 数据库服务器繁忙\n\n" +
                            "💡 建议解决方案：\n" +
                            "• 等待1-2分钟后重试\n" +
                            "• 检查网络连接状态\n" +
                            "• 如问题持续，程序会自动使用缓存数据\n" +
                            "• 联系管理员检查数据库服务器状态";
                }
                else if (ex.Message.Contains("Authentication") || ex.Message.Contains("I/O error"))
                {
                    title = "数据库认证失败";
                    message = $"🔐 {operation}认证失败\n\n" +
                            "🔍 可能的原因：\n" +
                            "• 数据库用户名或密码错误\n" +
                            "• 数据库服务器地址不正确\n" +
                            "• 网络连接中断\n\n" +
                            "💡 建议解决方案：\n" +
                            "• 检查数据库配置信息\n" +
                            "• 联系管理员确认数据库服务状态\n" +
                            "• 程序将使用缓存数据继续运行";
                    icon = MessageBoxImage.Error;
                }
                else
                {
                    title = "数据库操作异常";
                    message = $"❌ {operation}失败\n\n" +
                            $"错误信息：{ex.Message}\n\n" +
                            "💡 建议：\n" +
                            "• 等待几分钟后重试\n" +
                            "• 程序将尝试使用缓存数据\n" +
                            "• 如问题持续，请联系技术支持";
                    icon = MessageBoxImage.Error;
                }

                // 记录到日志
                AppSession.Log($"[数据库异常处理] {title}: {ex.Message}");
                AppSession.Log($"[数据库异常处理] 异常类型: {ex.GetType().FullName}");
                AppSession.Log($"[数据库异常处理] 操作: {operation}");
                if (ex.InnerException != null)
                {
                    AppSession.Log($"[数据库异常处理] 内部异常: {ex.InnerException.Message}");
                }

                // 在UI线程中显示对话框
                if (showDialog)
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        try
                        {
                            var result = MessageBox.Show(
                                message,
                                title,
                                MessageBoxButton.OK,
                                icon);

                            AppSession.Log($"[数据库异常处理] 用户确认对话框: {result}");
                        }
                        catch (Exception dialogEx)
                        {
                            AppSession.Log($"[数据库异常处理] 显示对话框失败: {dialogEx.Message}");
                        }
                    });
                }
            }
            catch (Exception handlerEx)
            {
                // 如果异常处理器本身出错，记录日志但不再抛出异常
                AppSession.Log($"[数据库异常处理] 处理异常时发生错误: {handlerEx.Message}");
            }
        }

        /// <summary>
        /// 检查是否为数据库相关异常
        /// </summary>
        /// <param name="ex">异常对象</param>
        /// <returns>是否为数据库异常</returns>
        public static bool IsDatabaseException(Exception ex)
        {
            if (ex == null) return false;

            // 处理AggregateException，检查其内部异常
            if (ex is AggregateException aggEx)
            {
                return aggEx.InnerExceptions.Any(innerEx => IsDatabaseException(innerEx));
            }

            // 递归检查内部异常
            var currentEx = ex;
            while (currentEx != null)
            {
                // 检查异常类型名称
                var typeName = currentEx.GetType().Name;
                if (typeName.Contains("MySql") || 
                    typeName.Contains("Database") || 
                    typeName.Contains("Connection") ||
                    typeName.Contains("Timeout"))
                {
                    return true;
                }

                // 检查异常消息
                if (currentEx.Message.Contains("Timeout expired") ||
                    currentEx.Message.Contains("server is not responding") ||
                    currentEx.Message.Contains("Authentication to host") ||
                    currentEx.Message.Contains("I/O error occurred") ||
                    currentEx.Message.Contains("connection failed") ||
                    currentEx.Message.Contains("database") ||
                    currentEx.Message.Contains("mysql"))
                {
                    return true;
                }
                
                currentEx = currentEx.InnerException;
            }

            return false;
        }

        /// <summary>
        /// 显示ticker获取失败的专用提示
        /// </summary>
        public static void ShowTickerFailureDialog()
        {
            try
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    var message = "📊 价格数据获取失败\n\n" +
                                "🔍 可能的原因：\n" +
                                "• 网络连接不稳定\n" +
                                "• Binance API服务器繁忙\n" +
                                "• 网络防火墙限制\n\n" +
                                "💡 解决建议：\n" +
                                "• 等待2-3分钟后程序会自动重试\n" +
                                "• 检查网络连接状态\n" +
                                "• 点击菜单栏'设置' > '网络诊断'进行检测\n" +
                                "• 如问题持续，请尝试重启程序";

                    MessageBox.Show(
                        message,
                        "价格数据获取失败",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                });

                AppSession.Log("[网络异常处理] 显示ticker获取失败对话框");
            }
            catch (Exception ex)
            {
                AppSession.Log($"[网络异常处理] 显示ticker失败对话框时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 显示网络异常对话框
        /// </summary>
        /// <param name="owner">父窗口</param>
        /// <param name="ex">网络异常</param>
        /// <param name="context">异常发生的上下文描述</param>
        /// <param name="showRetryOption">是否显示重试选项</param>
        /// <returns>用户的选择结果</returns>
        public static MessageBoxResult ShowNetworkExceptionDialog(Window owner, Exception ex, string context = "", bool showRetryOption = true)
        {
            if (owner?.Dispatcher.CheckAccess() == false)
            {
                // 如果不在UI线程，切换到UI线程执行
                return owner.Dispatcher.Invoke(() => ShowNetworkExceptionDialog(owner, ex, context, showRetryOption));
            }

            var exceptionType = GetExceptionTypeName(ex);
            var message = $"网络连接出现问题";
            
            if (!string.IsNullOrEmpty(context))
            {
                message = $"{context}：网络连接出现问题";
            }

            message += $"\n\n错误类型：{exceptionType}";
            message += "\n\n可能的原因：";
            message += "\n• 网络连接不稳定或已断开";
            message += "\n• Binance API服务器响应缓慢";
            message += "\n• 防火墙或安全软件阻止连接";
            message += "\n• 本地网络环境限制";

            message += "\n\n建议解决方案：";
            message += "\n• 检查网络连接是否正常";
            message += "\n• 等待2-3分钟后再试";
            message += "\n• 使用网络诊断工具检查连接";

            var buttons = showRetryOption ? MessageBoxButton.YesNo : MessageBoxButton.OK;
            var icon = MessageBoxImage.Warning;

            if (showRetryOption)
            {
                message += "\n\n是否要重试连接？";
            }

            return MessageBox.Show(owner, message, "网络连接问题", buttons, icon);
        }

        /// <summary>
        /// 获取异常类型的友好名称
        /// </summary>
        /// <param name="ex">异常对象</param>
        /// <returns>友好的异常类型名称</returns>
        private static string GetExceptionTypeName(Exception ex)
        {
            if (ex == null) return "未知异常";

            // 处理AggregateException，获取最具体的内部异常类型
            if (ex is AggregateException aggEx && aggEx.InnerExceptions.Any())
            {
                // 优先返回网络相关的内部异常类型
                foreach (var innerEx in aggEx.InnerExceptions)
                {
                    if (IsNetworkException(innerEx))
                    {
                        return GetExceptionTypeName(innerEx);
                    }
                }
                // 如果没有网络异常，返回第一个内部异常的类型
                return GetExceptionTypeName(aggEx.InnerExceptions.First());
            }

            // 递归查找最具体的网络异常
            var currentEx = ex;
            while (currentEx != null)
            {
                switch (currentEx)
                {
                    case TaskCanceledException:
                        return "连接超时";
                    case TimeoutException:
                        return "请求超时";
                    case HttpRequestException:
                        return "HTTP请求失败";
                    case NetworkInformationException:
                        return "网络信息异常";
                    case SocketException:
                        return "网络连接异常";
                    case WebException:
                        return "Web请求异常";
                }
                
                // 检查数据库连接相关异常
                if (currentEx.Message.Contains("Authentication to host"))
                {
                    return "数据库认证失败";
                }
                if (currentEx.Message.Contains("I/O error occurred"))
                {
                    return "网络I/O错误";
                }
                if (currentEx.Message.Contains("connection failed"))
                {
                    return "连接失败";
                }
                if (currentEx.GetType().Name.Contains("Connection"))
                {
                    return "连接异常";
                }
                if (currentEx.GetType().Name.Contains("Timeout"))
                {
                    return "超时异常";
                }
                
                currentEx = currentEx.InnerException;
            }

            return ex.GetType().Name;
        }

        /// <summary>
        /// 记录网络异常到日志
        /// </summary>
        /// <param name="context">上下文信息</param>
        /// <param name="ex">异常对象</param>
        public static void LogNetworkException(string context, Exception ex)
        {
            var exceptionType = GetExceptionTypeName(ex);
            AppSession.Log($"[网络异常] {context}: {exceptionType} - {ex.Message}");
            
            if (ex.InnerException != null)
            {
                AppSession.Log($"[内部异常] {ex.InnerException.Message}");
            }
        }

        /// <summary>
        /// 显示Ticker获取失败的专用对话框
        /// </summary>
        /// <param name="owner">父窗口</param>
        /// <param name="context">上下文描述</param>
        /// <returns>用户的选择结果</returns>
        public static MessageBoxResult ShowTickerFailureDialog(Window owner, string context = "获取价格数据")
        {
            if (owner?.Dispatcher.CheckAccess() == false)
            {
                return owner.Dispatcher.Invoke(() => ShowTickerFailureDialog(owner, context));
            }

            var message = $"{context}失败，ticker数据为空。";
            message += "\n\n这通常是由以下原因造成的：";
            message += "\n• 网络连接问题或超时";
            message += "\n• Binance API服务器响应慢";
            message += "\n• 防火墙或代理服务器阻止连接";
            message += "\n• 本地网络环境不稳定";

            message += "\n\n建议解决方案：";
            message += "\n• 检查网络连接";
            message += "\n• 等待2-3分钟后重试";
            message += "\n• 使用'设置'→'网络诊断'检查连接";

            message += "\n\n是否要重试获取数据？";

            return MessageBox.Show(owner, message, "数据获取失败", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        }

        /// <summary>
        /// 显示止损监控服务网络异常的专用提示
        /// </summary>
        /// <param name="contractName">合约名称</param>
        /// <param name="isFirstTime">是否首次显示</param>
        public static void ShowStopLossMonitorNetworkIssue(string contractName = "", bool isFirstTime = true)
        {
            try
            {
                // 避免频繁弹窗，如果不是首次显示且时间间隔太短则跳过
                var lastShowTime = _lastStopLossWarningTime;
                var now = DateTime.Now;
                if (!isFirstTime && (now - lastShowTime).TotalMinutes < 5)
                {
                    AppSession.Log("[止损监控] 网络提示间隔过短，跳过弹窗显示");
                    return;
                }
                _lastStopLossWarningTime = now;

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    var contractInfo = string.IsNullOrEmpty(contractName) ? "" : $"合约 {contractName} ";
                    var message = $"🛡️ 止损监控网络异常\n\n" +
                                $"⚠️ {contractInfo}价格获取失败，可能影响止损功能\n\n" +
                                "🔍 可能的原因：\n" +
                                "• 网络连接不稳定或中断\n" +
                                "• Binance API服务器响应慢\n" +
                                "• 防火墙或代理服务器阻止连接\n" +
                                "• 合约代码不正确或已下市\n\n" +
                                "💡 安全建议：\n" +
                                "• 程序将自动重试获取价格\n" +
                                "• 如网络问题持续，建议手动检查持仓\n" +
                                "• 可使用'设置' > '网络诊断'检查连接\n" +
                                "• 建议设置手机APP备用监控\n\n" +
                                "ℹ️ 止损监控会继续运行并定期重试";

                    MessageBox.Show(
                        message,
                        "止损监控网络异常",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                });

                AppSession.Log($"[止损监控] 显示网络异常提示，合约: {contractName}");
            }
            catch (Exception ex)
            {
                AppSession.Log($"[止损监控] 显示网络异常提示失败: {ex.Message}");
            }
        }

        // 记录上次显示止损监控警告的时间，避免频繁弹窗
        private static DateTime _lastStopLossWarningTime = DateTime.MinValue;
    }
} 