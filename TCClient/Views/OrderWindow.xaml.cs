using System;
using System.Windows;
using System.Windows.Controls;
using TCClient.Services;
using TCClient.ViewModels;
using TCClient.Views.Controls;
using TCClient.Models;
using System.Threading.Tasks;
using System.Net.Http;
using System.IO;
using TCClient.Exceptions;
using System.Windows.Input;
using System.Windows.Media;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace TCClient.Views
{
    public partial class OrderWindow : Window
    {
        private readonly OrderViewModel _viewModel;
        private readonly IExchangeService _exchangeService;
        private readonly IDatabaseService _databaseService;
        private readonly long _accountId;
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "TCClient_OrderWindow.log");
        private decimal _defaultStopLossAmount = 0m;
        private decimal _currentPrice = 0m;
        private bool _isUpdatingStopLoss = false; // 防止循环更新

        private static void LogToFile(string message)
        {
            // 暂时关闭日志输出
            /*
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
            */
        }

        public OrderWindow(OrderViewModel viewModel, IExchangeService exchangeService, long accountId)
        {
            try
            {
                //LogToFile("=== 开始初始化下单窗口 ===");
            InitializeComponent();
                _viewModel = viewModel;
                _exchangeService = exchangeService ?? new MockExchangeService();
                _accountId = accountId;

                // 从依赖注入容器获取 IDatabaseService
                _databaseService = ((App)Application.Current).Services.GetRequiredService<IDatabaseService>();

                DataContext = _viewModel;
                //LogToFile("基础组件初始化完成");

                // 初始化K线图控件
                //LogToFile("开始初始化K线图控件");
                KLineChartControl.Initialize(_exchangeService);
                
                // 将K线图控件传递给ViewModel，以便更新自选合约价格
                _viewModel.KLineChartControl = KLineChartControl;
                
                // 订阅合约选择事件
                KLineChartControl.ContractSelected += OnContractSelected;
                //LogToFile("K线图控件初始化完成");

                // 添加回车键触发查询
                ContractTextBox.KeyDown += async (s, e) =>
                {
                    if (e.Key == Key.Return)
                    {
                        e.Handled = true;
                        await QueryContractAsync();
                    }
                };

                // 添加查询按钮点击事件
                QueryContractButton.Click += async (s, e) =>
                {
                    await QueryContractAsync();
                };

                //LogToFile("合约输入监听器设置完成");

                // 异步加载账户信息
                //LogToFile("开始加载账户信息");
                _ = LoadAccountInfoAsync();

                // 注释掉默认值设置，让LoadAccountInfoAsync来处理
                // StopLossAmountTextBox.Text = _defaultStopLossAmount.ToString("F2");
            }
            catch (Exception ex)
            {
                //LogToFile($"初始化下单窗口时发生错误: {ex.Message}");
                //LogToFile($"异常堆栈: {ex.StackTrace}");
                MessageBox.Show($"初始化下单窗口失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadAccountInfoAsync()
        {
            try
            {
                // 从数据库获取交易账户信息
                var account = await _databaseService.GetTradingAccountByIdAsync(_accountId);
                
                if (account != null)
                {
                    // 在UI线程更新账户信息
                    Dispatcher.Invoke(() =>
                    {
                        // 计算单笔风险金额（总权益除以机会次数）
                        if (account.OpportunityCount > 0)
                        {
                            _defaultStopLossAmount = account.Equity / account.OpportunityCount;
                            
                            // 首次打开时，默认填写单笔风险金到金额输入框
                            // 只有在金额输入框为空或为默认值时才填写
                            if (string.IsNullOrEmpty(StopLossAmountTextBox.Text) || 
                                StopLossAmountTextBox.Text == "100.00" || 
                                StopLossAmountTextBox.Text == "0.00")
                            {
                                StopLossAmountTextBox.Text = _defaultStopLossAmount.ToString("F2");
                                Utils.LogManager.Log("OrderWindow", $"首次打开，默认填写单笔风险金：{_defaultStopLossAmount:N2}");
                            }
                            
                            // 记录日志而不是显示弹窗
                            Utils.LogManager.Log("OrderWindow", $"已加载账户信息，权益：{account.Equity:N2}，单笔风险金额：{_defaultStopLossAmount:N2}");
                        }
                        else
                        {
                            // 记录日志而不是显示弹窗
                            Utils.LogManager.Log("OrderWindow", "账户机会次数设置为0，使用默认风险金额");
                            _defaultStopLossAmount = 100; // 使用默认值
                            
                            // 首次打开时，填写默认值
                            if (string.IsNullOrEmpty(StopLossAmountTextBox.Text) || 
                                StopLossAmountTextBox.Text == "0.00")
                            {
                                StopLossAmountTextBox.Text = _defaultStopLossAmount.ToString("F2");
                            }
                        }
                    });
                }
                else
                {
                    Utils.LogManager.Log("OrderWindow", $"未找到ID为{_accountId}的交易账户");
                }
            }
            catch (Exception ex)
            {
                Utils.LogManager.Log("OrderWindow", $"获取账户信息失败：{ex.Message}");
            }
        }

        private async Task QueryContractAsync()
        {
            if (!string.IsNullOrEmpty(ContractTextBox.Text))
            {
                try
                {
                    // 清理输入的交易对名称，移除多余空格
                    string contractSymbol = ContractTextBox.Text.Trim();
                    
                    // 记录用户输入
                    Utils.LogManager.Log("OrderWindow", $"正在查询合约：{contractSymbol}");
                    
                    // 设置ViewModel中的合约名称
                    _viewModel.ContractName = contractSymbol;
                    
                    // 设置K线图合约 - 可能需要自动添加USDT后缀
                    var klineTask = KLineChartControl.SetSymbolAsync(contractSymbol);
                    
                    // 使用Task.WhenAny和超时控制，防止K线图加载卡住UI
                    var timeoutTask = Task.Delay(5000); // 5秒超时
                    var completedTask = await Task.WhenAny(klineTask, timeoutTask);
                    
                    if (completedTask == timeoutTask)
                    {
                        Utils.LogManager.Log("OrderWindow", "K线图加载超时");
                        // 继续执行，不等待K线图加载完成
                    }
                    else
                    {
                        // K线图加载完成，等待任务结束
                        await klineTask;
                    }
                    
                    // 获取当前价格并更新止损计算
                    try
                    {
                        Utils.LogManager.Log("OrderWindow", $"开始获取 {contractSymbol} 的价格信息");
                        
                        var tickerTask = _exchangeService.GetTickerAsync(contractSymbol);
                        var tickerTimeoutTask = Task.Delay(10000); // 给价格查询更长的超时时间
                        
                        var tickerCompletedTask = await Task.WhenAny(tickerTask, tickerTimeoutTask);
                        
                        if (tickerCompletedTask == tickerTimeoutTask)
                        {
                            throw new TimeoutException("获取价格信息超时，请检查网络连接或币安API状态");
                        }
                        
                        var ticker = await tickerTask;
                        
                        if (ticker != null)
                        {
                            // 检查价格是否为零
                            if (ticker.LastPrice <= 0)
                            {
                                throw new Exception($"获取到的{contractSymbol}价格为0，请确认交易对是否正确或尝试其他交易对");
                            }
                            
                            // 获取到正确的价格，更新UI
                            Utils.LogManager.Log("OrderWindow", $"成功获取 {contractSymbol} 价格: {ticker.LastPrice}");
                            _currentPrice = ticker.LastPrice;
                            _viewModel.LatestPrice = _currentPrice;
                            
                            // 在UI线程更新相关控件
                            Dispatcher.Invoke(() =>
                            {
                                // 根据价格大小决定显示精度
                                string priceFormat = GetPriceFormat(_currentPrice);
                                PriceTextBox.Text = _currentPrice.ToString(priceFormat);
                                
                                // 使用当前止损金额重新计算止损比例和价格
                                if (decimal.TryParse(StopLossAmountTextBox.Text, out decimal currentAmount))
                                {
                                    UpdateStopLossValues(newAmount: currentAmount);
                                }
                                
                                // 保持合约文本框显示不带USDT后缀的合约名称，以便与数据库中的推仓信息匹配
                                // ContractTextBox.Text = ticker.Symbol; // 不要更新为完整的交易对名称
                                // 保持原有的合约名称（不带USDT后缀），确保与数据库中的推仓信息格式一致
                                
                                // 记录日志
                                Utils.LogManager.Log("OrderWindow", $"查询合约成功：{ticker.Symbol}，当前价格：{_currentPrice.ToString(priceFormat)}");
                            });
                            
                            // 启动ViewModel中的价格更新定时器
                            _viewModel.QueryContractCommand.Execute(null);
                        }
                        else
                        {
                            throw new Exception("获取价格失败：API返回了空数据");
                        }
                    }
                    catch (Exception priceEx)
                    {
                        Utils.LogManager.Log("OrderWindow", $"获取价格数据失败: {priceEx.Message}");
                        
                        // 确定要展示的建议
                        string suggestions = "请检查：\n"
                            + "1. 交易对名称是否正确\n";
                        
                        if (contractSymbol.ToUpper().EndsWith("USD") && !contractSymbol.ToUpper().EndsWith("USDT"))
                        {
                            suggestions += "2. 尝试使用USDT结尾的交易对 (如 BTCUSDT 而不是 BTCUSD)\n";
                        }
                        else if (!contractSymbol.ToUpper().EndsWith("USDT"))
                        {
                            suggestions += "2. 尝试添加USDT后缀 (如 SOLUSDT 而不是 SOL)\n";
                        }
                        
                        suggestions += "3. 网络连接是否正常\n"
                                    + "4. 交易所API是否可用\n\n"
                                    + "请重试或使用其他交易对\n\n"
                                    + "常见交易对格式示例：\n"
                                    + "- BTCUSDT (比特币)\n"
                                    + "- ETHUSDT (以太坊)\n"
                                    + "- SOLUSDT (索拉纳)";
                        
                        MessageBox.Show(
                            $"获取 {contractSymbol} 的价格数据失败：\n{priceEx.Message}\n\n{suggestions}",
                            "价格获取失败",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                            
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Utils.LogManager.Log("OrderWindow", $"加载K线数据失败：{ex.Message}");
                    MessageBox.Show($"加载K线数据失败：{ex.Message}", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else
            {
                Utils.LogManager.Log("OrderWindow", "请输入合约代码");
                
                // 提供更有帮助的提示
                MessageBox.Show(
                    "请输入合约代码，例如：\n"
                    + "- BTCUSDT (比特币)\n"
                    + "- ETHUSDT (以太坊)\n"
                    + "- SOLUSDT (索拉纳)",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private async void QueryContractButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await QueryContractAsync();
            }
            catch (Exception ex)
            {
                Utils.LogManager.Log("OrderWindow", $"查询合约失败：{ex.Message}");
                MessageBox.Show($"查询合约失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PeriodButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string period)
            {
                try
                {
                    //LogToFile($"切换K线周期: {period}");
                    
                    // 更新按钮样式，突出显示当前选中的周期
                    foreach (Button btn in ((StackPanel)button.Parent).Children)
                    {
                        btn.Background = btn == button ? new SolidColorBrush(Colors.LightBlue) : null;
                    }

                    // 更新K线图周期
                    if (DataContext is OrderViewModel viewModel)
                    {
                        viewModel.UpdateKLinePeriod(period);
                        // 直接调用K线图控件的更新方法
                        KLineChartControl.UpdatePeriod(period);
                    }
                }
                catch (Exception ex)
                {
                    //LogToFile($"切换K线周期失败: {ex.Message}");
                    //LogToFile($"异常堆栈: {ex.StackTrace}");
                    MessageBox.Show($"切换K线周期失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void PlaceOrderButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is OrderViewModel viewModel)
            {
                try
                {
                    // 验证基本输入
                    if (string.IsNullOrEmpty(ContractTextBox.Text))
                    {
                        MessageBox.Show("请输入合约代码", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // 首先尝试从ViewModel获取数量，如果为0则尝试从输入框获取
                    decimal quantity = viewModel.OrderQuantity;
                    if (quantity <= 0)
                    {
                        // 如果ViewModel中的数量为0，尝试从输入框解析
                        if (decimal.TryParse(QuantityTextBox.Text, out decimal textBoxQuantity) && textBoxQuantity > 0)
                        {
                            quantity = textBoxQuantity;
                            viewModel.OrderQuantity = quantity; // 同步到ViewModel
                        }
                        else
                        {
                            MessageBox.Show("请输入有效的数量。\n\n提示：您可以手动输入数量，或者点击下方的'以损定量'按钮自动计算合适的数量。", "数量不能为空", MessageBoxButton.OK, MessageBoxImage.Warning);
                            QuantityTextBox.Focus();
                            return;
                        }
                    }

                    // 条件单特有验证
                    if (viewModel.IsConditionalOrder)
                    {
                        if (!decimal.TryParse(TriggerPriceTextBox.Text, out decimal triggerPrice) || triggerPrice <= 0)
                        {
                            MessageBox.Show("请输入有效的触发价格", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        // 更新条件单类型和触发价格
                        viewModel.ConditionalOrderType = BreakUpRadio.IsChecked == true ? 
                            ConditionalOrderType.BREAK_UP : 
                            ConditionalOrderType.BREAK_DOWN;
                        viewModel.TriggerPrice = triggerPrice;
                    }

                    // 获取交易方向
                    string direction = BuyRadioButton.IsChecked == true ? "多" : "空";
                    viewModel.OrderDirection = direction;

                    // 设置止损价格
                    if (decimal.TryParse(StopLossPriceTextBox.Text, out decimal stopLossPrice))
                    {
                        viewModel.StopLossPrice = stopLossPrice;
                    }

                    // 验证价格是否有效
                    if (decimal.TryParse(PriceTextBox.Text, out decimal price))
                    {
                        if (price <= 0)
                        {
                            MessageBox.Show("价格必须大于零", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        
                        viewModel.LatestPrice = price;
                        _currentPrice = price;
                    }
                    else
                    {
                        MessageBox.Show("请输入有效的价格", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // 执行下单操作
                    bool success = await viewModel.PlaceOrderAsync();
                    
                    if (success)
                    {
                        // 下单成功后显示订单列表窗口
                        ShowOrderListWindow();
                    }
                }
                catch (Exception ex)
                {
                    Utils.LogManager.Log("OrderWindow", $"下单操作失败：{ex.Message}");
                    MessageBox.Show($"下单操作失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // 显示订单列表窗口方法
        private void ShowOrderListWindow()
        {
            try
            {
                var services = ((App)Application.Current).Services;
                var databaseService = services.GetRequiredService<IDatabaseService>();
                var messageService = services.GetRequiredService<IMessageService>();
                
                // 重用下单窗口中的交易所服务
                var orderListWindow = new OrderListWindow(
                    databaseService,
                    messageService,
                    _exchangeService,
                    _accountId)
                {
                    Owner = this
                };
                
                // 显示窗口
                orderListWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开订单列表窗口失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async void CloseAllPositionsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _viewModel.CloseAllPositionsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"一键平仓失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LeverageButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string leverageStr)
            {
                try
                {
                    // 更新按钮样式
                    if (button.Parent is StackPanel panel)
                    {
                        foreach (Button btn in panel.Children.OfType<Button>())
                        {
                            btn.Background = btn == button ? new SolidColorBrush(Colors.LightBlue) : null;
                        }
                    }

                    // 更新输入框
                    LeverageTextBox.Text = leverageStr;

                    // 更新ViewModel
                    if (DataContext is OrderViewModel viewModel)
                    {
                        if (decimal.TryParse(leverageStr, out decimal leverage))
                        {
                            viewModel.Leverage = leverage;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Utils.LogManager.Log("OrderWindow", $"设置杠杆失败: {ex.Message}");
                    MessageBox.Show($"设置杠杆失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void LeverageTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox && DataContext is OrderViewModel viewModel)
            {
                try
                {
                    // 清除所有杠杆按钮的选中状态
                    ClearLeverageButtonSelection();

                    // 更新ViewModel
                    if (decimal.TryParse(textBox.Text, out decimal leverage))
                    {
                        // 验证杠杆值是否在有效范围内
                        if (leverage >= 1 && leverage <= 20)
                        {
                            viewModel.Leverage = leverage;
                        }
                        else
                        {
                            MessageBox.Show("杠杆倍数必须在1-20倍之间", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                            textBox.Text = "3"; // 重置为默认值
                        }
                    }
                }
                catch (Exception ex)
                {
                    Utils.LogManager.Log("OrderWindow", $"更新杠杆值失败: {ex.Message}");
                }
            }
        }

        private void ClearLeverageButtonSelection()
        {
            try
            {
                // 查找杠杆快捷按钮面板
                var leveragePanel = FindLeverageButtonPanel(this);
                if (leveragePanel != null)
                {
                    foreach (Button btn in leveragePanel.Children.OfType<Button>())
                    {
                        btn.Background = null;
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.LogManager.Log("OrderWindow", $"清除杠杆按钮选中状态失败: {ex.Message}");
            }
        }

        private StackPanel FindLeverageButtonPanel(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                
                if (child is StackPanel panel)
                {
                    // 检查是否包含杠杆按钮（通过检查第一个按钮的Tag）
                    var firstButton = panel.Children.OfType<Button>().FirstOrDefault();
                    if (firstButton?.Tag?.ToString() == "1")
                    {
                        return panel;
                    }
                }
                
                var result = FindLeverageButtonPanel(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        private void UpdateStopLossValues(decimal? newPercentage = null, decimal? newPrice = null, decimal? newAmount = null)
        {
            if (_isUpdatingStopLoss || _currentPrice <= 0) return;

            try
            {
                _isUpdatingStopLoss = true;

                decimal stopLossPercentage = 0m;
                decimal stopLossPrice = 0m;
                decimal stopLossAmount = 0m;
                
                // 获取当前交易方向
                bool isLong = _viewModel.OrderDirection == "多";

                // 根据输入值计算其他值
                if (newPercentage.HasValue)
                {
                    // 输入是百分比
                    stopLossPercentage = newPercentage.Value;
                    
                    // 做多时，止损价格 = 当前价格 * (1 - 止损百分比)
                    // 做空时，止损价格 = 当前价格 * (1 + 止损百分比)
                    stopLossPrice = isLong ? 
                        _currentPrice * (1 - stopLossPercentage) : 
                        _currentPrice * (1 + stopLossPercentage);
                        
                    // 使用当前的止损金额
                    stopLossAmount = decimal.TryParse(StopLossAmountTextBox.Text, out decimal amount) ? 
                        amount : _defaultStopLossAmount;
                }
                else if (newPrice.HasValue)
                {
                    // 输入是价格
                    stopLossPrice = newPrice.Value;
                    
                    // 计算止损百分比
                    stopLossPercentage = isLong ? 
                        (_currentPrice - stopLossPrice) / _currentPrice : 
                        (stopLossPrice - _currentPrice) / _currentPrice;
                        
                    // 确保百分比为正值
                    stopLossPercentage = Math.Abs(stopLossPercentage);
                    
                    // 使用当前的止损金额
                    stopLossAmount = decimal.TryParse(StopLossAmountTextBox.Text, out decimal amount) ? 
                        amount : _defaultStopLossAmount;
                }
                else if (newAmount.HasValue)
                {
                    // 输入是金额
                    stopLossAmount = newAmount.Value;
                    
                    // 使用当前的止损百分比
                    stopLossPercentage = decimal.TryParse(StopLossPercentageTextBox.Text.TrimEnd('%'), out decimal percent) ? 
                        percent / 100 : 0.05m; // 默认5%
                        
                    // 计算止损价格
                    stopLossPrice = isLong ? 
                        _currentPrice * (1 - stopLossPercentage) : 
                        _currentPrice * (1 + stopLossPercentage);
                }

                // 确保百分比不超过100%
                if (stopLossPercentage > 1) stopLossPercentage = 1;
                // 确保止损价格大于0
                if (stopLossPrice <= 0) stopLossPrice = 0.01m;

                // 根据价格大小决定显示精度
                string priceFormat = GetPriceFormat(stopLossPrice);

                // 更新UI
                Dispatcher.Invoke(() =>
                {
                    StopLossPercentageTextBox.Text = stopLossPercentage.ToString("P2");
                    StopLossPriceTextBox.Text = stopLossPrice.ToString(priceFormat);
                    StopLossAmountTextBox.Text = stopLossAmount.ToString("F2");
                });

                // 更新ViewModel
                _viewModel.StopLossPrice = stopLossPrice;
                _viewModel.StopLossAmount = stopLossAmount;
                
                Utils.LogManager.Log("OrderWindow", $"止损参数已更新 - 百分比: {stopLossPercentage:P2}, 价格: {stopLossPrice.ToString(priceFormat)}, 金额: {stopLossAmount:F2}");
            }
            catch (Exception ex)
            {
                Utils.LogManager.Log("OrderWindow", $"更新止损参数时出错: {ex.Message}");
            }
            finally
            {
                _isUpdatingStopLoss = false;
            }
        }

        private void StopLossPercentageTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingStopLoss) return;

            if (sender is TextBox textBox && decimal.TryParse(textBox.Text.TrimEnd('%'), out decimal percentage))
            {
                percentage = percentage / 100; // 转换为小数
                if (percentage > 0 && percentage <= 1)
                {
                    UpdateStopLossValues(newPercentage: percentage);
                }
            }
        }

        private void StopLossPriceTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingStopLoss) return;

            if (sender is TextBox textBox && decimal.TryParse(textBox.Text, out decimal price))
            {
                if (price > 0)
                {
                    UpdateStopLossValues(newPrice: price);
                }
            }
        }

        private void StopLossAmountTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingStopLoss) return;

            if (sender is TextBox textBox && decimal.TryParse(textBox.Text, out decimal amount))
            {
                if (amount > 0)
                {
                    UpdateStopLossValues(newAmount: amount);
                }
            }
        }

        private void IncreaseStopLossAmount_Click(object sender, RoutedEventArgs e)
        {
            if (decimal.TryParse(StopLossAmountTextBox.Text, out decimal currentAmount))
            {
                decimal newAmount = currentAmount * 1.1m; // 增加10%
                UpdateStopLossValues(newAmount: newAmount);
            }
        }

        private void DecreaseStopLossAmount_Click(object sender, RoutedEventArgs e)
        {
            if (decimal.TryParse(StopLossAmountTextBox.Text, out decimal currentAmount))
            {
                decimal newAmount = currentAmount * 0.9m; // 减少10%
                UpdateStopLossValues(newAmount: newAmount);
            }
        }

        /// <summary>
        /// 增加止损比例1%
        /// </summary>
        private void IncreaseStopLossPercentage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 获取当前止损比例
                decimal currentPercentage = 0m;
                if (decimal.TryParse(StopLossPercentageTextBox.Text.TrimEnd('%'), out decimal percentage))
                {
                    currentPercentage = percentage / 100; // 转换为小数
                }
                else
                {
                    // 如果解析失败，使用默认值5%
                    currentPercentage = 0.05m;
                }

                // 增加1%
                decimal newPercentage = currentPercentage + 0.01m;
                
                // 限制最大值为50%（防止设置过高的止损比例）
                if (newPercentage > 0.5m)
                {
                    newPercentage = 0.5m;
                    MessageBox.Show("止损比例不能超过50%", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                // 更新止损设置
                UpdateStopLossValues(newPercentage: newPercentage);
                
                Utils.LogManager.Log("OrderWindow", $"止损比例已增加1% - 新比例: {newPercentage:P2}");
            }
            catch (Exception ex)
            {
                Utils.LogManager.LogException("OrderWindow", ex, "增加止损比例失败");
                MessageBox.Show($"增加止损比例失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 减少止损比例1%
        /// </summary>
        private void DecreaseStopLossPercentage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 获取当前止损比例
                decimal currentPercentage = 0m;
                if (decimal.TryParse(StopLossPercentageTextBox.Text.TrimEnd('%'), out decimal percentage))
                {
                    currentPercentage = percentage / 100; // 转换为小数
                }
                else
                {
                    // 如果解析失败，使用默认值5%
                    currentPercentage = 0.05m;
                }

                // 减少1%
                decimal newPercentage = currentPercentage - 0.01m;
                
                // 限制最小值为1%（防止设置过低的止损比例）
                if (newPercentage < 0.01m)
                {
                    newPercentage = 0.01m;
                    MessageBox.Show("止损比例不能低于1%", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                // 更新止损设置
                UpdateStopLossValues(newPercentage: newPercentage);
                
                Utils.LogManager.Log("OrderWindow", $"止损比例已减少1% - 新比例: {newPercentage:P2}");
            }
            catch (Exception ex)
            {
                Utils.LogManager.LogException("OrderWindow", ex, "减少止损比例失败");
                MessageBox.Show($"减少止损比例失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CalculateQuantityByStopLoss_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(ContractTextBox.Text))
                {
                    MessageBox.Show("请先输入合约代码", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!decimal.TryParse(StopLossPriceTextBox.Text, out decimal stopLossPrice) || stopLossPrice <= 0)
                {
                    MessageBox.Show("请输入有效的止损价格", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!decimal.TryParse(StopLossAmountTextBox.Text, out decimal stopLossAmount) || stopLossAmount <= 0)
                {
                    MessageBox.Show("请输入有效的止损金额", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 获取当前价格（如果需要更新）
                if (_currentPrice <= 0)
                {
                    var ticker = await _exchangeService.GetTickerAsync(_viewModel.ContractName);
                    if (ticker != null && ticker.LastPrice > 0)
                    {
                        _currentPrice = ticker.LastPrice;
                        _viewModel.LatestPrice = _currentPrice;
                    }
                    else
                    {
                        MessageBox.Show("无法获取当前价格，请先查询合约", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                // 获取当前交易方向
                bool isLong = _viewModel.OrderDirection == "多";

                // 计算止损比例(百分比)
                decimal stopLossPercentage = isLong ?
                    (_currentPrice - stopLossPrice) / _currentPrice :
                    (stopLossPrice - _currentPrice) / _currentPrice;

                // 确保比例为正值
                stopLossPercentage = Math.Abs(stopLossPercentage);

                if (stopLossPercentage <= 0)
                {
                    MessageBox.Show("止损比例不能为零，请确保设置了合理的止损价格", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 计算开仓数量：止损金额/(价格*止损比例)
                // 注意：杠杆不影响下单数量，只影响保证金使用量
                decimal quantity = stopLossAmount / (_currentPrice * stopLossPercentage);

                // 获取当前价格的精度
                string priceFormat = GetPriceFormat(_currentPrice);

                // 记录详细计算过程
                Utils.LogManager.Log("OrderWindow", $"以损定量计算 - 止损金额: {stopLossAmount:F2}, 当前价格: {_currentPrice.ToString(priceFormat)}, " +
                                   $"止损价格: {stopLossPrice.ToString(priceFormat)}, 止损比例: {stopLossPercentage:P2}, 杠杆: {_viewModel.Leverage}, " +
                                   $"计算公式: 止损金额 / (价格 * 止损比例) = {stopLossAmount} / ({_currentPrice} * {stopLossPercentage}) = {quantity}");

                // 根据数量大小决定是向下取整还是保留更多小数位
                string quantityStr;
                if (quantity >= 1)
                {
                    // 大于等于1的数量向下取整
                    quantityStr = Math.Floor(quantity).ToString();
                }
                else if (quantity >= 0.001m)
                {
                    // 小于1但大于0.001的保留3位小数
                    quantityStr = quantity.ToString("F3");
                }
                else
                {
                    // 非常小的数字保留8位小数
                    quantityStr = quantity.ToString("F8");
                }

                // 更新数量输入框和ViewModel
                QuantityTextBox.Text = quantityStr;
                
                // 同步更新ViewModel中的OrderQuantity属性
                if (decimal.TryParse(quantityStr, out decimal parsedQuantity))
                {
                    _viewModel.OrderQuantity = parsedQuantity;
                    Utils.LogManager.Log("OrderWindow", $"以损定量计算完成，设置交易数量为：{quantityStr}，ViewModel.OrderQuantity已同步更新为：{parsedQuantity}");
                }
                else
                {
                    Utils.LogManager.Log("OrderWindow", $"以损定量计算完成，但无法解析数量字符串：{quantityStr}");
                }
            }
            catch (Exception ex)
            {
                Utils.LogManager.Log("OrderWindow", $"以损定量计算失败：{ex.Message}");
                MessageBox.Show($"以损定量计算失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 可用风险金按钮点击事件
        /// </summary>
        private async void UseAvailableRiskAmount_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(ContractTextBox.Text))
                {
                    MessageBox.Show("请先输入合约代码", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var accountId = Utils.AppSession.CurrentAccountId;
                if (accountId <= 0)
                {
                    MessageBox.Show("无效的账户ID", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 获取该合约的可用风险金
                decimal availableRiskAmount = 0m;
                
                // 首先尝试从ViewModel的推仓信息中获取
                if (_viewModel.PushSummary != null)
                {
                    availableRiskAmount = _viewModel.AvailableRiskAmount;
                    Utils.LogManager.Log("OrderWindow", $"从推仓信息获取可用风险金: {availableRiskAmount:F2}");
                }
                else
                {
                    // 如果没有推仓信息，使用单笔风险金
                    availableRiskAmount = _viewModel.SingleRiskAmount;
                    Utils.LogManager.Log("OrderWindow", $"使用单笔风险金: {availableRiskAmount:F2}");
                }

                if (availableRiskAmount <= 0)
                {
                    // 如果ViewModel中没有数据，直接从数据库获取
                    availableRiskAmount = await _databaseService.GetContractAvailableRiskAmountAsync(accountId, ContractTextBox.Text);
                    Utils.LogManager.Log("OrderWindow", $"从数据库获取可用风险金: {availableRiskAmount:F2}");
                }

                if (availableRiskAmount <= 0)
                {
                    MessageBox.Show("无法获取可用风险金，请检查账户设置", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 将可用风险金填入金额输入框
                StopLossAmountTextBox.Text = availableRiskAmount.ToString("F2");
                
                // 同步更新ViewModel
                _viewModel.StopLossAmount = availableRiskAmount;
                
                // 如果已设置止损价格，重新计算止损参数
                if (decimal.TryParse(StopLossPriceTextBox.Text, out decimal stopLossPrice) && stopLossPrice > 0)
                {
                    UpdateStopLossValues(newAmount: availableRiskAmount);
                }

                Utils.LogManager.Log("OrderWindow", $"已将可用风险金 {availableRiskAmount:F2} 填入金额输入框");
                
                // 显示提示信息
                MessageBox.Show($"已将可用风险金 {availableRiskAmount:F2} 填入金额输入框", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Utils.LogManager.LogException("OrderWindow", ex, "使用可用风险金失败");
                MessageBox.Show($"使用可用风险金失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 处理K线图控件的合约选择事件
        /// </summary>
        private async void OnContractSelected(object sender, string contractSymbol)
        {
            try
            {
                // 在UI线程上更新合约输入框
                await Dispatcher.InvokeAsync(() =>
                {
                    ContractTextBox.Text = contractSymbol;
                    Utils.LogManager.Log("OrderWindow", $"从自选列表选择合约: {contractSymbol}");
                });
                
                // 自动查询合约信息
                await QueryContractAsync();
            }
            catch (Exception ex)
            {
                Utils.LogManager.LogException("OrderWindow", ex, "处理合约选择事件失败");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try
        {
            //LogToFile("=== 下单窗口关闭 ===");
                
                // 取消订阅K线图控件事件
                if (KLineChartControl != null)
                {
                    KLineChartControl.ContractSelected -= OnContractSelected;
                }
                
                // 首先处理K线图控件，停止其可能进行的任何操作
                try 
                {
                    KLineChartControl?.Initialize(null);
                }
                catch (Exception klineEx)
                {
                    System.Diagnostics.Debug.WriteLine($"释放K线图控件时发生错误: {klineEx.Message}");
                }
                
                // 然后释放交易所服务资源
                try
                {
            if (_exchangeService is IDisposable disposable)
            {
                //LogToFile("释放交易所服务资源");
                disposable.Dispose();
            }
                }
                catch (Exception svcEx)
                {
                    System.Diagnostics.Debug.WriteLine($"释放交易所服务时发生错误: {svcEx.Message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"关闭下单窗口时发生错误: {ex.Message}");
            }
            finally
            {
                base.OnClosed(e);
            }
        }

        // 查找价格输入框
        private TextBox FindPriceTextBox()
        {
            // 直接返回已命名的控件
            return PriceTextBox;
        }

        private void OrderTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is OrderViewModel viewModel && OrderTypeComboBox.SelectedIndex >= 0)
            {
                try
                {
                    // 根据选择更新ViewModel中的订单类型
                    viewModel.SelectedOrderType = OrderTypeComboBox.SelectedIndex == 0 ? 
                        TCClient.ViewModels.OrderType.Market : 
                        TCClient.ViewModels.OrderType.Conditional;

                    // 更新UI可见性
                    ConditionalOrderGrid.Visibility = viewModel.IsConditionalOrder ? 
                        Visibility.Visible : Visibility.Collapsed;

                    // 如果选择的是条件单，初始化触发价格为当前价格
                    if (viewModel.IsConditionalOrder && TriggerPriceTextBox != null)
                    {
                        TriggerPriceTextBox.Text = viewModel.LatestPrice.ToString("F2");
                        viewModel.TriggerPrice = viewModel.LatestPrice;
                    }

                    // 日志记录
                    Utils.LogManager.Log("OrderWindow", $"订单类型已更改为: {(viewModel.IsConditionalOrder ? "条件单" : "市价下单")}");
                }
                catch (Exception ex)
                {
                    Utils.LogManager.Log("OrderWindow", $"更改订单类型时出错: {ex.Message}");
                    MessageBox.Show($"更新订单类型失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void PriceTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (DataContext is OrderViewModel viewModel && !string.IsNullOrEmpty(PriceTextBox.Text))
            {
                if (decimal.TryParse(PriceTextBox.Text, out decimal price))
                {
                    viewModel.LatestPrice = price;
                    _currentPrice = price;
                    
                    // 记录价格变化
                    Utils.LogManager.Log("OrderWindow", $"价格已手动更新为: {price}");
                    
                    // 如果已设置止损金额，更新止损设置
                    if (decimal.TryParse(StopLossAmountTextBox.Text, out decimal currentAmount) && currentAmount > 0)
                    {
                        UpdateStopLossValues(newAmount: currentAmount);
                    }
                }
            }
        }

        // 根据价格大小确定使用的格式字符串
        private string GetPriceFormat(decimal price)
        {
            if (price < 0.0001m) return "F8";
            if (price < 0.01m) return "F6";
            if (price < 1m) return "F4";
            if (price < 1000m) return "F2";
            return "F0";
        }

        /// <summary>
        /// 方向选择改变事件处理器
        /// </summary>
        private void DirectionRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (DataContext is OrderViewModel viewModel && _currentPrice > 0)
                {
                    // 获取当前选择的方向
                    bool isLong = BuyRadioButton.IsChecked == true;
                    string direction = isLong ? "多" : "空";
                    
                    // 更新ViewModel中的方向
                    viewModel.OrderDirection = direction;
                    
                    Utils.LogManager.Log("OrderWindow", $"交易方向已切换为: {direction}");
                    
                    // 如果已设置止损金额，根据新方向重新计算止损价格
                    if (decimal.TryParse(StopLossAmountTextBox.Text, out decimal currentAmount) && currentAmount > 0)
                    {
                        Utils.LogManager.Log("OrderWindow", $"根据新方向重新计算止损价格，当前止损金额: {currentAmount}");
                        UpdateStopLossValues(newAmount: currentAmount);
                    }
                    else
                    {
                        // 如果没有设置止损金额，使用默认的5%止损比例
                        decimal defaultPercentage = 0.05m; // 5%
                        decimal stopLossPrice = isLong ? 
                            _currentPrice * (1 - defaultPercentage) : 
                            _currentPrice * (1 + defaultPercentage);
                            
                        string priceFormat = GetPriceFormat(stopLossPrice);
                        
                        Dispatcher.Invoke(() =>
                        {
                            StopLossPercentageTextBox.Text = defaultPercentage.ToString("P2");
                            StopLossPriceTextBox.Text = stopLossPrice.ToString(priceFormat);
                            if (string.IsNullOrEmpty(StopLossAmountTextBox.Text))
                            {
                                StopLossAmountTextBox.Text = _defaultStopLossAmount.ToString("F2");
                            }
                        });
                        
                        viewModel.StopLossPrice = stopLossPrice;
                        
                        Utils.LogManager.Log("OrderWindow", $"使用默认止损设置 - 方向: {direction}, 止损比例: {defaultPercentage:P2}, 止损价格: {stopLossPrice.ToString(priceFormat)}");
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.LogManager.LogException("OrderWindow", ex, "处理方向切换失败");
            }
        }

        /// <summary>
        /// 查看止损单事件处理器
        /// </summary>
        private void ViewStopLossOrders_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 检查窗口是否正在关闭或已关闭
                if (!IsLoaded || !IsVisible)
                {
                    return;
                }

                // 获取当前选中的订单
                var menuItem = sender as MenuItem;
                var contextMenu = menuItem?.Parent as ContextMenu;
                var dataGrid = contextMenu?.PlacementTarget as DataGrid;
                var selectedOrder = dataGrid?.SelectedItem as SimulationOrder;

                if (selectedOrder == null)
                {
                    MessageBox.Show("请先选择一个订单", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 获取数据库服务
                var app = System.Windows.Application.Current as App;
                var databaseService = app?.Services?.GetService(typeof(TCClient.Services.IDatabaseService)) as TCClient.Services.IDatabaseService;
                var logger = app?.Services?.GetService(typeof(Microsoft.Extensions.Logging.ILogger<StopLossOrdersWindow>)) as Microsoft.Extensions.Logging.ILogger<StopLossOrdersWindow>;

                if (databaseService == null)
                {
                    MessageBox.Show("无法获取数据库服务", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 再次检查窗口状态，确保在显示对话框前窗口仍然有效
                if (!IsLoaded || !IsVisible)
                {
                    return;
                }

                // 创建并显示止损单查看窗口
                var stopLossWindow = new StopLossOrdersWindow(selectedOrder, databaseService, logger)
                {
                    Owner = this
                };
                
                // 使用Dispatcher确保在UI线程中显示对话框
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (IsLoaded && IsVisible)
                        {
                            stopLossWindow.ShowDialog();
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // 窗口已关闭，忽略此异常
                    }
                }));
            }
            catch (Exception ex)
            {
                Utils.LogManager.LogException("OrderWindow", ex, "查看止损单失败");
                MessageBox.Show($"查看止损单失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
} 