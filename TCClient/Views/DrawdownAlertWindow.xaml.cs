using System.Windows;
using System.Windows.Input;
using System.Threading.Tasks;
using System.Linq;
using System.Windows.Shapes;
using System.Windows.Media;
using TCClient.ViewModels;
using TCClient.Services;

namespace TCClient.Views
{
    public partial class DrawdownAlertWindow : Window
    {
        public DrawdownAlertWindow()
        {
            InitializeComponent();
            DataContext = new DrawdownAlertViewModel();
        }

        private void OnLongContractDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is DrawdownAlertViewModel viewModel && viewModel.SelectedLongContract != null)
            {
                ShowKLineCharts(viewModel.SelectedLongContract.Symbol, LongKLineChartContainer);
            }
        }

        private void OnShortContractDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is DrawdownAlertViewModel viewModel && viewModel.SelectedShortContract != null)
            {
                ShowKLineCharts(viewModel.SelectedShortContract.Symbol, ShortKLineChartContainer);
            }
        }

        private async void ShowKLineCharts(string symbol, System.Windows.Controls.Grid container)
        {
            // 清空容器
            container.Children.Clear();

            // 创建4个K线图的网格布局
            var grid = new System.Windows.Controls.Grid();
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition());
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition());
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());

            // 创建4个K线图占位符
            var charts = new[]
            {
                CreateChartPlaceholder($"{symbol} - 日线", 0, 0, "1d"),
                CreateChartPlaceholder($"{symbol} - 1小时线", 0, 1, "1h"),
                CreateChartPlaceholder($"{symbol} - 15分钟线", 1, 0, "15m"),
                CreateChartPlaceholder($"{symbol} - 5分钟线", 1, 1, "5m")
            };

            foreach (var chart in charts)
            {
                grid.Children.Add(chart);
            }

            container.Children.Add(grid);

            // 异步加载K线数据
            await LoadKLineDataAsync(symbol, grid);
        }

        private System.Windows.Controls.Border CreateChartPlaceholder(string title, int row, int column, string interval)
        {
            var border = new System.Windows.Controls.Border
            {
                BorderBrush = System.Windows.Media.Brushes.LightGray,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(2),
                Tag = interval // 存储时间间隔信息
            };

            System.Windows.Controls.Grid.SetRow(border, row);
            System.Windows.Controls.Grid.SetColumn(border, column);

            var stackPanel = new System.Windows.Controls.StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var titleBlock = new System.Windows.Controls.TextBlock
            {
                Text = title,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var placeholderBlock = new System.Windows.Controls.TextBlock
            {
                Text = "K线图加载中...",
                FontSize = 10,
                Foreground = System.Windows.Media.Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                Name = "StatusText"
            };

            stackPanel.Children.Add(titleBlock);
            stackPanel.Children.Add(placeholderBlock);
            border.Child = stackPanel;

            return border;
        }

        private async Task LoadKLineDataAsync(string symbol, System.Windows.Controls.Grid chartGrid)
        {
            try
            {
                // 获取ViewModel中的市场数据管理器
                if (DataContext is DrawdownAlertViewModel viewModel)
                {
                    // 创建BinanceApiService实例来获取K线数据
                    using var binanceApi = new BinanceApiService();
                    
                    // 定义时间间隔
                    var intervals = new[] { "1d", "1h", "15m", "5m" };
                    
                    foreach (var interval in intervals)
                    {
                        try
                        {
                            // 获取K线数据 - 增加到120根以确保有足够数据显示100根
                            var klineData = await binanceApi.GetKLineDataAsync(symbol, interval, 120);
                            
                            // 找到对应的图表容器
                            var chartBorder = chartGrid.Children.OfType<System.Windows.Controls.Border>()
                                .FirstOrDefault(b => b.Tag?.ToString() == interval);
                            
                            if (chartBorder != null)
                            {
                                UpdateChartDisplay(chartBorder, klineData, symbol, interval);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"获取{symbol} {interval}K线数据失败: {ex.Message}");
                            
                            // 更新错误状态
                            var chartBorder = chartGrid.Children.OfType<System.Windows.Controls.Border>()
                                .FirstOrDefault(b => b.Tag?.ToString() == interval);
                            
                            if (chartBorder != null)
                            {
                                UpdateChartError(chartBorder, $"加载失败: {ex.Message}");
                            }
                        }
                        
                        // 避免请求过于频繁
                        await Task.Delay(200);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载K线数据失败: {ex.Message}");
            }
        }

        private void UpdateChartDisplay(System.Windows.Controls.Border chartBorder, List<BinanceKLineData> klineData, string symbol, string interval)
        {
            // 创建新的Grid来容纳标题和图表
            var mainGrid = new System.Windows.Controls.Grid();
            mainGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            
            // 添加标题
            var titleBlock = new System.Windows.Controls.TextBlock
            {
                Text = $"{symbol} - {GetIntervalDisplayName(interval)}",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(2),
                Foreground = Brushes.DarkBlue
            };
            System.Windows.Controls.Grid.SetRow(titleBlock, 0);
            mainGrid.Children.Add(titleBlock);
            
            if (klineData != null && klineData.Any())
            {
                // 创建K线图
                var chartCanvas = CreateKLineChart(klineData);
                System.Windows.Controls.Grid.SetRow(chartCanvas, 1);
                mainGrid.Children.Add(chartCanvas);
            }
            else
            {
                var noDataText = new System.Windows.Controls.TextBlock
                {
                    Text = "暂无数据",
                    FontSize = 10,
                    Foreground = System.Windows.Media.Brushes.Orange,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                System.Windows.Controls.Grid.SetRow(noDataText, 1);
                mainGrid.Children.Add(noDataText);
            }
            
            chartBorder.Child = mainGrid;
        }

        private System.Windows.Controls.Canvas CreateKLineChart(List<BinanceKLineData> klineData)
        {
            var canvas = new System.Windows.Controls.Canvas
            {
                Background = Brushes.White,
                Margin = new Thickness(2)
            };

            if (!klineData.Any()) return canvas;

            // 取最后100根K线进行显示
            var displayData = klineData.TakeLast(100).ToList();
            
            // 计算价格范围
            var minPrice = (double)displayData.Min(k => k.Low);
            var maxPrice = (double)displayData.Max(k => k.High);
            var priceRange = maxPrice - minPrice;
            
            if (priceRange == 0) priceRange = maxPrice * 0.01; // 避免除零

            // 设置画布尺寸 - 增加宽度以显示更多K线
            canvas.Width = 400;
            canvas.Height = 150;
            
            var candleSpacing = canvas.Width / displayData.Count;
            var candleWidth = Math.Max(candleSpacing * 0.6, 1.0); // 最小宽度1像素

            // 绘制K线
            for (int i = 0; i < displayData.Count; i++)
            {
                var candle = displayData[i];
                var x = i * candleSpacing + candleSpacing / 2;
                
                var open = (double)candle.Open;
                var high = (double)candle.High;
                var low = (double)candle.Low;
                var close = (double)candle.Close;
                
                // 计算Y坐标（翻转，因为Canvas的Y轴向下）
                var openY = canvas.Height - (open - minPrice) / priceRange * canvas.Height;
                var highY = canvas.Height - (high - minPrice) / priceRange * canvas.Height;
                var lowY = canvas.Height - (low - minPrice) / priceRange * canvas.Height;
                var closeY = canvas.Height - (close - minPrice) / priceRange * canvas.Height;
                
                // 确定颜色（红涨绿跌）
                var isRising = close >= open;
                var candleColor = isRising ? Brushes.Red : Brushes.Green;
                var borderColor = isRising ? Brushes.DarkRed : Brushes.DarkGreen;
                
                // 绘制上影线
                if (high > Math.Max(open, close))
                {
                    var upperShadow = new Line
                    {
                        X1 = x,
                        Y1 = highY,
                        X2 = x,
                        Y2 = Math.Min(openY, closeY),
                        Stroke = borderColor,
                        StrokeThickness = 1
                    };
                    canvas.Children.Add(upperShadow);
                }
                
                // 绘制下影线
                if (low < Math.Min(open, close))
                {
                    var lowerShadow = new Line
                    {
                        X1 = x,
                        Y1 = Math.Max(openY, closeY),
                        X2 = x,
                        Y2 = lowY,
                        Stroke = borderColor,
                        StrokeThickness = 1
                    };
                    canvas.Children.Add(lowerShadow);
                }
                
                // 绘制实体
                var bodyHeight = Math.Abs(closeY - openY);
                if (bodyHeight < 1) bodyHeight = 1; // 最小高度
                
                var bodyRect = new Rectangle
                {
                    Width = candleWidth,
                    Height = bodyHeight,
                    Fill = isRising ? candleColor : candleColor,
                    Stroke = borderColor,
                    StrokeThickness = 0.5
                };
                
                System.Windows.Controls.Canvas.SetLeft(bodyRect, x - candleWidth / 2);
                System.Windows.Controls.Canvas.SetTop(bodyRect, Math.Min(openY, closeY));
                canvas.Children.Add(bodyRect);
            }
            
            // 添加价格标签
            var latestCandle = displayData.Last();
            var priceLabel = new System.Windows.Controls.TextBlock
            {
                Text = $"${latestCandle.Close:F4}",
                FontSize = 8,
                Foreground = Brushes.DarkBlue,
                Background = Brushes.LightYellow
            };
            
            System.Windows.Controls.Canvas.SetLeft(priceLabel, canvas.Width - 60);
            System.Windows.Controls.Canvas.SetTop(priceLabel, 2);
            canvas.Children.Add(priceLabel);
            
            return canvas;
        }

        private void UpdateChartError(System.Windows.Controls.Border chartBorder, string errorMessage)
        {
            if (chartBorder.Child is System.Windows.Controls.StackPanel stackPanel)
            {
                var statusText = stackPanel.Children.OfType<System.Windows.Controls.TextBlock>()
                    .FirstOrDefault(t => t.Name == "StatusText");
                
                if (statusText != null)
                {
                    statusText.Text = errorMessage;
                    statusText.Foreground = System.Windows.Media.Brushes.Red;
                }
            }
        }

        private string GetIntervalDisplayName(string interval)
        {
            return interval switch
            {
                "1d" => "日线",
                "1h" => "1小时线",
                "15m" => "15分钟线",
                "5m" => "5分钟线",
                _ => interval
            };
        }
    }
} 