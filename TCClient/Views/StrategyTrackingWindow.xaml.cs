using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Extensions.DependencyInjection;
using TCClient.ViewModels;
using TCClient.Models;
using TCClient.Utils;

namespace TCClient.Views
{
    /// <summary>
    /// 策略追踪窗口
    /// </summary>
    public partial class StrategyTrackingWindow : Window
    {
        private StrategyTrackingViewModel? _viewModel;

        public StrategyTrackingWindow(StrategyTrackingViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;
            
            // 订阅数据变化事件
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            
            // 窗口加载完成后初始化数据
            Loaded += async (s, e) => 
            {
                await _viewModel.InitializeAsync();
                
                // 强制刷新图表显示
                UpdateMarketVolumeChart();
                UpdateGroupChart();
                UpdateSymbolChart();
            };
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(StrategyTrackingViewModel.GroupNetValueData))
            {
                UpdateGroupChart();
            }
            else if (e.PropertyName == nameof(StrategyTrackingViewModel.SymbolNetValueData))
            {
                UpdateSymbolChart();
            }
            else if (e.PropertyName == nameof(StrategyTrackingViewModel.MarketVolumeData))
            {
                UpdateMarketVolumeChart();
            }
        }

        /// <summary>
        /// 测试数据库连接按钮点击事件
        /// </summary>
        private async void TestConnection_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
            {
                try
                {
                    _viewModel.StatusMessage = "正在测试数据库连接...";
                    
                    // 创建服务实例进行测试
                    var services = ((App)Application.Current).Services;
                    var strategyService = services.GetRequiredService<TCClient.Services.StrategyTrackingService>();
                    
                    var connected = await strategyService.TestConnectionAsync();
                    
                    if (connected)
                    {
                        _viewModel.StatusMessage = "数据库连接成功！";
                        MessageBox.Show("数据库连接成功！\n\n服务器：45.153.131.217:3306\n数据库：localdb", 
                            "连接测试", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        _viewModel.StatusMessage = "数据库连接失败";
                        MessageBox.Show("数据库连接失败！\n\n请检查：\n1. 网络连接\n2. 服务器地址和端口\n3. 用户名密码", 
                            "连接测试", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    _viewModel.StatusMessage = $"连接测试失败: {ex.Message}";
                    MessageBox.Show($"连接测试失败：\n\n{ex.Message}", 
                        "连接测试", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 更新组合净值曲线图
        /// </summary>
        private void UpdateGroupChart()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"UpdateGroupChart called. Data count: {_viewModel?.GroupNetValueData?.Count ?? 0}");
                
                if (_viewModel?.GroupNetValueData == null || !_viewModel.GroupNetValueData.Any())
                {
                    // 清空图表，显示提示信息
                    GroupChartContainer.Child = new TextBlock
                    {
                        Text = "暂无组合净值数据",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = new SolidColorBrush(Color.FromRgb(153, 153, 153)),
                        FontSize = 14
                    };
                    return;
                }

                var canvas = CreateLineChart(_viewModel.GroupNetValueData.ToList(), "组合净值", Colors.Blue);
                
                // 确保Canvas填充满容器
                canvas.Width = double.NaN; // 自动宽度
                canvas.Height = double.NaN; // 自动高度
                canvas.HorizontalAlignment = HorizontalAlignment.Stretch;
                canvas.VerticalAlignment = VerticalAlignment.Stretch;
                
                GroupChartContainer.Child = canvas;
                System.Diagnostics.Debug.WriteLine("Group chart updated successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating group chart: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新合约净值曲线图
        /// </summary>
        public void UpdateSymbolChart()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"UpdateSymbolChart called. Data count: {_viewModel?.SymbolNetValueData?.Count ?? 0}");
                
                if (_viewModel?.SymbolNetValueData == null || !_viewModel.SymbolNetValueData.Any())
                {
                    // 清空图表，显示提示信息
                    SymbolChartContainer.Child = new TextBlock
                    {
                        Text = "暂无合约净值数据",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = new SolidColorBrush(Color.FromRgb(153, 153, 153)),
                        FontSize = 14
                    };
                    return;
                }

                var canvas = CreateLineChart(_viewModel.SymbolNetValueData.ToList(), 
                    $"合约净值 - {_viewModel.SelectedSymbol}", Colors.Green);
                
                // 确保Canvas填充满容器
                canvas.Width = double.NaN; // 自动宽度
                canvas.Height = double.NaN; // 自动高度
                canvas.HorizontalAlignment = HorizontalAlignment.Stretch;
                canvas.VerticalAlignment = VerticalAlignment.Stretch;
                
                SymbolChartContainer.Child = canvas;
                System.Diagnostics.Debug.WriteLine("Symbol chart updated successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating symbol chart: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新市场成交额曲线图
        /// </summary>
        private void UpdateMarketVolumeChart()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"UpdateMarketVolumeChart called. Data count: {_viewModel?.MarketVolumeData?.Count ?? 0}");
                
                if (_viewModel?.MarketVolumeData == null || !_viewModel.MarketVolumeData.Any())
                {
                    // 清空图表，显示提示信息
                    MarketVolumeChartContainer.Child = new TextBlock
                    {
                        Text = "暂无市场成交额数据",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = new SolidColorBrush(Color.FromRgb(153, 153, 153)),
                        FontSize = 14
                    };
                    return;
                }

                // 将MarketVolumePoint转换为NetValuePoint以复用现有的图表绘制逻辑
                var chartData = _viewModel.MarketVolumeData.Select(p => new NetValuePoint
                {
                    Time = p.Time,
                    Value = p.Volume24h, // 使用24小时成交量
                    Symbol = "24H成交量"
                }).ToList();

                var canvas = CreateLineChart(chartData, "市场24小时成交额", Colors.Red);
                
                // 确保Canvas填充满容器
                canvas.Width = double.NaN; // 自动宽度
                canvas.Height = double.NaN; // 自动高度
                canvas.HorizontalAlignment = HorizontalAlignment.Stretch;
                canvas.VerticalAlignment = VerticalAlignment.Stretch;
                
                MarketVolumeChartContainer.Child = canvas;
                System.Diagnostics.Debug.WriteLine("Market volume chart updated successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating market volume chart: {ex.Message}");
            }
        }

        /// <summary>
        /// 创建折线图
        /// </summary>
        private Canvas CreateLineChart(List<NetValuePoint> data, string title, Color lineColor)
        {
            var canvas = new Canvas
            {
                Background = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            
            if (data.Count < 2)
            {
                var noDataText = new TextBlock
                {
                    Text = "数据点不足，无法绘制图表",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromRgb(153, 153, 153)),
                    FontSize = 12
                };
                Canvas.SetLeft(noDataText, 50);
                Canvas.SetTop(noDataText, 50);
                canvas.Children.Add(noDataText);
                return canvas;
            }

            // 当Canvas加载完成后再绘制图表
            canvas.Loaded += (s, e) =>
            {
                DrawChartContent(canvas, data, title, lineColor);
            };

            // 当Canvas大小改变时重新绘制
            canvas.SizeChanged += (s, e) =>
            {
                if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
                {
                    canvas.Children.Clear();
                    DrawChartContent(canvas, data, title, lineColor);
                }
            };

            return canvas;
        }

        /// <summary>
        /// 绘制图表内容
        /// </summary>
        private void DrawChartContent(Canvas canvas, List<NetValuePoint> data, string title, Color lineColor)
        {
            if (canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0)
                return;

            // 图表区域设置 - 使用实际Canvas大小
            const double margin = 50;
            var chartWidth = Math.Max(100, canvas.ActualWidth - margin * 2);
            var chartHeight = Math.Max(50, canvas.ActualHeight - margin * 2 - 30); // 为标题留出空间

            // 计算数据范围
            var minValue = data.Min(d => d.Value);
            var maxValue = data.Max(d => d.Value);
            var minTime = data.Min(d => d.Time);
            var maxTime = data.Max(d => d.Time);

            var valueRange = maxValue - minValue;
            if (valueRange == 0) valueRange = 1; // 避免除零

            var timeRange = maxTime - minTime;
            if (timeRange.TotalSeconds == 0) timeRange = TimeSpan.FromHours(1); // 避免除零

            // 绘制坐标轴
            DrawAxes(canvas, margin, chartWidth, chartHeight, minValue, maxValue, minTime, maxTime);

            // 绘制折线
            DrawLine(canvas, data, margin, chartWidth, chartHeight, minValue, valueRange, minTime, timeRange, lineColor);

            // 添加标题
            var titleText = new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(51, 51, 51))
            };
            Canvas.SetLeft(titleText, margin);
            Canvas.SetTop(titleText, 5);
            canvas.Children.Add(titleText);
        }

        /// <summary>
        /// 绘制坐标轴
        /// </summary>
        private void DrawAxes(Canvas canvas, double margin, double chartWidth, double chartHeight, 
            decimal minValue, decimal maxValue, DateTime minTime, DateTime maxTime)
        {
            // Y轴
            var yAxis = new Line
            {
                X1 = margin,
                Y1 = margin,
                X2 = margin,
                Y2 = margin + chartHeight,
                Stroke = new SolidColorBrush(Colors.Black),
                StrokeThickness = 1
            };
            canvas.Children.Add(yAxis);

            // X轴
            var xAxis = new Line
            {
                X1 = margin,
                Y1 = margin + chartHeight,
                X2 = margin + chartWidth,
                Y2 = margin + chartHeight,
                Stroke = new SolidColorBrush(Colors.Black),
                StrokeThickness = 1
            };
            canvas.Children.Add(xAxis);

            // Y轴刻度和标签
            for (int i = 0; i <= 5; i++)
            {
                var y = margin + (chartHeight / 5) * i;
                var value = maxValue - (maxValue - minValue) * i / 5;

                // 刻度线
                var tick = new Line
                {
                    X1 = margin - 5,
                    Y1 = y,
                    X2 = margin,
                    Y2 = y,
                    Stroke = new SolidColorBrush(Colors.Black),
                    StrokeThickness = 1
                };
                canvas.Children.Add(tick);

                // 标签
                var label = new TextBlock
                {
                    Text = value.ToString("F2"),
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Colors.Black)
                };
                Canvas.SetLeft(label, margin - 35);
                Canvas.SetTop(label, y - 7);
                canvas.Children.Add(label);
            }

            // X轴刻度和标签
            for (int i = 0; i <= 4; i++)
            {
                var x = margin + (chartWidth / 4) * i;
                var time = minTime.AddTicks((maxTime - minTime).Ticks * i / 4);

                // 刻度线
                var tick = new Line
                {
                    X1 = x,
                    Y1 = margin + chartHeight,
                    X2 = x,
                    Y2 = margin + chartHeight + 5,
                    Stroke = new SolidColorBrush(Colors.Black),
                    StrokeThickness = 1
                };
                canvas.Children.Add(tick);

                // 标签
                var label = new TextBlock
                {
                    Text = time.ToString("MM-dd HH:mm"),
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Colors.Black)
                };
                Canvas.SetLeft(label, x - 30);
                Canvas.SetTop(label, margin + chartHeight + 10);
                canvas.Children.Add(label);
            }
        }

        /// <summary>
        /// 绘制折线
        /// </summary>
        private void DrawLine(Canvas canvas, List<NetValuePoint> data, double margin, double chartWidth, double chartHeight,
            decimal minValue, decimal valueRange, DateTime minTime, TimeSpan timeRange, Color lineColor)
        {
            var polyline = new Polyline
            {
                Stroke = new SolidColorBrush(lineColor),
                StrokeThickness = 2,
                Fill = null
            };

            foreach (var point in data)
            {
                var x = margin + chartWidth * (point.Time - minTime).TotalSeconds / timeRange.TotalSeconds;
                var y = margin + chartHeight - chartHeight * (double)(point.Value - minValue) / (double)valueRange;
                
                polyline.Points.Add(new Point(x, y));
            }

            canvas.Children.Add(polyline);

            // 添加数据点
            foreach (var point in data)
            {
                var x = margin + chartWidth * (point.Time - minTime).TotalSeconds / timeRange.TotalSeconds;
                var y = margin + chartHeight - chartHeight * (double)(point.Value - minValue) / (double)valueRange;

                var dot = new Ellipse
                {
                    Width = 4,
                    Height = 4,
                    Fill = new SolidColorBrush(lineColor)
                };
                Canvas.SetLeft(dot, x - 2);
                Canvas.SetTop(dot, y - 2);
                canvas.Children.Add(dot);
            }
        }

        private void SymbolStatusDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var dataGrid = this.FindName("SymbolStatusDataGrid") as DataGrid;
            if (dataGrid?.SelectedItem is SymbolStatus status && _viewModel != null)
            {
                _viewModel.LoadSymbolNetValueCommand.Execute(status.Symbol);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // 清理资源
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
                _viewModel.Dispose();
            }
            base.OnClosed(e);
        }
    }
} 