using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Text.Json;
using System.Net.Http;
using System.Windows.Input;
using TCClient.Models;
using TCClient.Services;
using TCClient.Utils;
using IOPath = System.IO.Path;
using Microsoft.Extensions.DependencyInjection;

namespace TCClient.Views.Controls
{
    public partial class KLineChartControl : UserControl
    {
        private readonly string _dataDirectory;
        private string _currentSymbol;
        private string _currentPeriod = "1m";
        private int _kLineCount = 200;
        private List<KLineData> _kLineData;
        private List<OrderMarker> _orderMarkers;
        private double _canvasWidth;
        private double _canvasHeight;
        private double _volumeCanvasHeight;
        private double _candleWidth = 8;
        private double _candleSpacing = 2;
        private double _maxPrice;
        private double _minPrice;
        private double _priceRange;
        private double _scaleY;
        private double _maxVolume;
        private double _volumeScaleY;
        private IExchangeService _exchangeService;
        
        // MA设置
        private int _ma1Period = 20;
        private bool _showVolume = true;
        private bool _showMA = true;

        public KLineChartControl()
        {
            InitializeComponent();
            
            // 确保数据目录存在
            _dataDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TCClient", "KLineData");
            if (!Directory.Exists(_dataDirectory))
            {
                Directory.CreateDirectory(_dataDirectory);
            }
            
            _orderMarkers = new List<OrderMarker>();

            // 监听Canvas大小变化 - 添加空值检查
            if (KLineCanvas != null)
            {
                KLineCanvas.SizeChanged += KLineCanvas_SizeChanged;
            }
            if (VolumeCanvas != null)
            {
                VolumeCanvas.SizeChanged += VolumeCanvas_SizeChanged;
            }
        }

        public void Initialize(IExchangeService exchangeService)
        {
            try
            {
                _exchangeService = exchangeService;
                
                // 记录初始化状态
                LogToFile($"K线图控件初始化: ExchangeService = {(exchangeService != null ? "已设置" : "null")}");
                
                // 检查关键UI元素是否已加载
                if (KLineCanvas == null)
                {
                    LogToFile("警告: KLineCanvas为null，控件可能未完全加载");
                }
                if (VolumeCanvas == null)
                {
                    LogToFile("警告: VolumeCanvas为null，控件可能未完全加载");
                }
            }
            catch (Exception ex)
            {
                LogToFile($"K线图控件初始化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置均线周期并重新绘制图表
        /// </summary>
        public void SetMAPeriod(int period)
        {
            try
            {
                if (period > 0 && period <= 200)
                {
                    _ma1Period = period;
                    
                    // 更新UI中的TextBox显示
                    if (MA1TextBox != null)
                    {
                        MA1TextBox.Text = period.ToString();
                    }
                    
                    // 重新绘制图表
                    if (_kLineData != null && _kLineData.Any())
                    {
                        DrawKLineChart();
                    }
                    
                    LogToFile($"均线周期已更新为: {period}");
                }
            }
            catch (Exception ex)
            {
                LogToFile($"设置均线周期失败: {ex.Message}");
            }
        }

        private void KLineCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                LogToFile($"K线Canvas大小变化: 旧大小={_canvasWidth}x{_canvasHeight}, 新大小={e.NewSize.Width}x{e.NewSize.Height}");
                _canvasWidth = e.NewSize.Width;
                _canvasHeight = e.NewSize.Height;
                if (_kLineData != null && _kLineData.Any())
                {
                    DrawKLineChart();
                }
            }
            catch (Exception ex)
            {
                LogToFile($"K线Canvas大小变化处理时发生错误: {ex.Message}");
                LogToFile($"异常堆栈: {ex.StackTrace}");
            }
        }

        private void VolumeCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                LogToFile($"成交额Canvas大小变化: 新大小={e.NewSize.Width}x{e.NewSize.Height}");
                _volumeCanvasHeight = e.NewSize.Height;
                if (_kLineData != null && _kLineData.Any() && _showVolume)
                {
                    DrawVolumeChart();
                }
            }
            catch (Exception ex)
            {
                LogToFile($"成交额Canvas大小变化处理时发生错误: {ex.Message}");
            }
        }

        public async Task SetSymbolAsync(string symbol)
        {
            _currentSymbol = symbol;
            await FetchKLineData();
        }

        public void AddOrderMarker(DateTime time, double price, bool isEntry)
        {
            _orderMarkers.Add(new OrderMarker
            {
                Time = time,
                Price = price,
                IsEntry = isEntry
            });
            if (_kLineData != null && _kLineData.Any())
            {
                DrawKLineChart();
            }
        }

        private void UpdateStatusMessage(string message)
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.SetStatusMessage(message);
            }
        }

        private async Task FetchKLineData()
        {
            if (string.IsNullOrEmpty(_currentSymbol))
            {
                MessageBox.Show("请先选择交易合约");
                return;
            }

            if (_exchangeService == null)
            {
                MessageBox.Show("交易所服务未初始化");
                return;
            }

            try
            {
                // 检查本地缓存
                var symbol = _currentSymbol.ToLower().Replace("usdt", "");
                var fileName = $"{symbol}_{DateTime.Now:ddMMyy}_{_currentPeriod}.json";
                var filePath = System.IO.Path.Combine(_dataDirectory, fileName);
                
                if (File.Exists(filePath))
                {
                    var fileInfo = new FileInfo(filePath);
                    var timeSinceLastUpdate = DateTime.Now - fileInfo.LastWriteTime;
                    
                    // 根据周期判断是否需要更新
                    var updateInterval = GetUpdateInterval(_currentPeriod);
                    if (timeSinceLastUpdate < updateInterval)
                    {
                        // 使用缓存数据
                        LogToFile($"使用缓存数据: {fileName}，最后更新时间: {fileInfo.LastWriteTime}");
                        LoadLocalData();
                        if (_kLineData != null && _kLineData.Any())
                        {
                            DrawKLineChart();
                            if (_showVolume) DrawVolumeChart();
                            UpdateStatusMessage($"已加载缓存数据: {_currentSymbol} {_currentPeriod}，共 {_kLineData.Count} 根K线");
                            return;
                        }
                    }
                }

                // 需要从API获取新数据
                LogToFile($"开始获取K线数据: {_currentSymbol} {_currentPeriod} {_kLineCount}");
                _kLineData = await _exchangeService.GetKLineDataAsync(_currentSymbol, _currentPeriod, _kLineCount);
                await SaveKLineData();
                DrawKLineChart();
                if (_showVolume) DrawVolumeChart();
                
                UpdateStatusMessage($"已更新K线数据: {_currentSymbol} {_currentPeriod}，共 {_kLineData.Count} 根K线");
                LogToFile("K线数据获取成功并已保存");
            }
            catch (HttpRequestException ex)
            {
                LogToFile($"网络请求失败: {ex.Message}");
                LogToFile("尝试加载本地数据...");
                
                // 尝试加载本地数据
                LoadLocalData();
                
                if (_kLineData == null || !_kLineData.Any())
                {
                    UpdateStatusMessage($"获取K线数据失败: {ex.Message}");
                }
                else
                {
                    UpdateStatusMessage($"已加载本地数据: {_currentSymbol} {_currentPeriod}，共 {_kLineData.Count} 根K线");
                }
            }
            catch (Exception ex)
            {
                LogToFile($"获取K线数据时发生错误: {ex.Message}");
                LogToFile($"异常堆栈: {ex.StackTrace}");
                UpdateStatusMessage($"获取K线数据失败: {ex.Message}");
            }
        }

        private TimeSpan GetUpdateInterval(string period)
        {
            return period switch
            {
                "1m" => TimeSpan.FromMinutes(1),
                "5m" => TimeSpan.FromMinutes(5),
                "15m" => TimeSpan.FromMinutes(15),
                "30m" => TimeSpan.FromMinutes(30),
                "1h" => TimeSpan.FromHours(1),
                "4h" => TimeSpan.FromHours(4),
                "1d" => TimeSpan.FromDays(1),
                "1w" => TimeSpan.FromDays(7),
                _ => TimeSpan.FromMinutes(5)
            };
        }

        private async Task SaveKLineData()
        {
            if (_kLineData == null || !_kLineData.Any())
            {
                UpdateStatusMessage("没有可保存的K线数据");
                return;
            }

            try
            {
                // 使用新的命名规则：btc_250515_1m.json
                var symbol = _currentSymbol.ToLower().Replace("usdt", "");
                var fileName = $"{symbol}_{DateTime.Now:ddMMyy}_{_currentPeriod}.json";
                var filePath = System.IO.Path.Combine(_dataDirectory, fileName);
                var json = JsonSerializer.Serialize(_kLineData);
                await File.WriteAllTextAsync(filePath, json);
                UpdateStatusMessage($"K线数据已保存：{fileName}");
            }
            catch (Exception ex)
            {
                UpdateStatusMessage($"保存K线数据失败：{ex.Message}");
            }
        }

        private void LoadLocalData()
        {
            if (string.IsNullOrEmpty(_currentSymbol))
            {
                return;
            }

            try
            {
                var symbol = _currentSymbol.ToLower().Replace("usdt", "");
                var pattern = $"{symbol}_*_{_currentPeriod}.json";
                var files = Directory.GetFiles(_dataDirectory, pattern);
                
                if (files.Length > 0)
                {
                    // 按文件名排序，获取最新的数据文件
                    var latestFile = files.OrderByDescending(f => f).First();
                    var json = File.ReadAllText(latestFile);
                    _kLineData = JsonSerializer.Deserialize<List<KLineData>>(json);
                    
                    if (_kLineData != null && _kLineData.Any())
                    {
                        DrawKLineChart();
                        LogToFile($"从文件加载K线数据成功：{IOPath.GetFileName(latestFile)}");
                        LogToFile($"加载了 {_kLineData.Count} 根K线数据");
                        UpdateStatusMessage($"已加载本地数据：{_currentSymbol} {_currentPeriod}，共 {_kLineData.Count} 根K线");
                    }
                    else
                    {
                        LogToFile("加载的K线数据为空");
                        UpdateStatusMessage("加载的K线数据为空");
                    }
                }
                else
                {
                    LogToFile($"未找到匹配的K线数据文件：{pattern}");
                    UpdateStatusMessage($"未找到匹配的K线数据文件：{pattern}");
                }
            }
            catch (Exception ex)
            {
                LogToFile($"加载本地数据失败: {ex.Message}");
                LogToFile($"异常堆栈: {ex.StackTrace}");
                UpdateStatusMessage($"加载本地数据失败：{ex.Message}");
            }
        }

        private void DrawKLineChart()
        {
            try
            {
                LogToFile($"开始绘制K线图，Canvas大小: {_canvasWidth}x{_canvasHeight}");
                
                // 检查Canvas是否可用
                if (KLineCanvas == null)
                {
                    LogToFile("错误: KLineCanvas为null，无法绘制K线图");
                    return;
                }
                
                KLineCanvas.Children.Clear();
                if (_kLineData == null || !_kLineData.Any())
                {
                    LogToFile("没有K线数据可绘制");
                    MaxPriceTextBlock.Text = "";
                    MinPriceTextBlock.Text = "";
                    return;
                }

                LogToFile($"K线数据数量: {_kLineData.Count}");
                
                // 计算价格范围，添加一些边距
                var priceMargin = (_kLineData.Max(k => k.High) - _kLineData.Min(k => k.Low)) * 0.1;
                _maxPrice = _kLineData.Max(k => k.High) + priceMargin;
                _minPrice = _kLineData.Min(k => k.Low) - priceMargin;
                _priceRange = _maxPrice - _minPrice;
                
                // 计算缩放比例，留出上下边距
                var verticalMargin = 40.0;
                _scaleY = (_canvasHeight - verticalMargin) / _priceRange;

                // 更新价格标签
                MaxPriceTextBlock.Text = $"最高: {_maxPrice:F2}";
                MinPriceTextBlock.Text = $"最低: {_minPrice:F2}";

                LogToFile($"价格范围: 最低={_minPrice}, 最高={_maxPrice}, 范围={_priceRange}, 缩放比例={_scaleY}");

                // ========== 关键修改：统一X轴计算逻辑 ==========
                // 计算K线宽度和间距，确保所有K线都能显示在Canvas中
                var sharedXAxisParams = CalculateSharedXAxisParameters();
                var scaledCandleWidth = sharedXAxisParams.ScaledWidth;
                var scaledCandleSpacing = sharedXAxisParams.ScaledSpacing;
                var startX = sharedXAxisParams.StartX;
                var scaleX = sharedXAxisParams.ScaleX;

                LogToFile($"🎯 K线图X轴参数: 可用宽度={sharedXAxisParams.AvailableWidth}, 缩放比例={scaleX:F4}, 缩放后宽度={scaledCandleWidth:F2}, 缩放后间距={scaledCandleSpacing:F2}");

                // 绘制K线
                for (int i = 0; i < _kLineData.Count; i++)
                {
                    var kline = _kLineData[i];
                    var x = startX + i * (scaledCandleWidth + scaledCandleSpacing);
                    
                    // 计算实体高度和位置（注意Y坐标的计算方式）
                    var bodyHeight = Math.Abs(kline.Close - kline.Open) * _scaleY;
                    var bodyY = _canvasHeight - verticalMargin - 
                               (Math.Max(kline.Open, kline.Close) - _minPrice) * _scaleY;
                    
                    // 绘制实体
                    var body = new Rectangle
                    {
                        Width = scaledCandleWidth,
                        Height = Math.Max(1, bodyHeight),
                        Fill = kline.Close >= kline.Open ? Brushes.Red : Brushes.Green,
                        Stroke = Brushes.White,
                        StrokeThickness = 0.5
                    };
                    Canvas.SetLeft(body, x);
                    Canvas.SetTop(body, bodyY);
                    KLineCanvas.Children.Add(body);

                    // 绘制上下影线
                    var line = new Line
                    {
                        X1 = x + scaledCandleWidth / 2,
                        X2 = x + scaledCandleWidth / 2,
                        Y1 = _canvasHeight - verticalMargin - (kline.High - _minPrice) * _scaleY,
                        Y2 = _canvasHeight - verticalMargin - (kline.Low - _minPrice) * _scaleY,
                        Stroke = kline.Close >= kline.Open ? Brushes.Red : Brushes.Green,
                        StrokeThickness = 1
                    };
                    KLineCanvas.Children.Add(line);

                    // 每10根K线绘制一个时间标签
                    if (i % 10 == 0)
                    {
                        string timeFormat = _currentPeriod switch
                        {
                            "1m" or "5m" or "15m" or "30m" => "HH:mm",
                            "1h" or "4h" => "MM-dd HH:mm",
                            "1d" => "MM-dd",
                            "1w" => "yyyy-MM-dd",
                            _ => "HH:mm"
                        };

                        var timeText = new TextBlock
                        {
                            Text = kline.Time.ToString(timeFormat),
                            Foreground = Brushes.White,
                            FontSize = 10,
                            Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0))
                        };
                        Canvas.SetLeft(timeText, x - 10);
                        Canvas.SetTop(timeText, _canvasHeight - 15);
                        KLineCanvas.Children.Add(timeText);

                        // 记录日志，特别关注日线的时间对齐
                        if (i == 0 || i == _kLineData.Count - 1 || _currentPeriod == "1d")
                        {
                            LogToFile($"⏰ K线时间标签 {i}: 周期={_currentPeriod}, 时间={kline.Time:yyyy-MM-dd HH:mm:ss}, 格式化显示={kline.Time.ToString(timeFormat)}, X轴位置={x:F2}");
                        }
                    }

                    if (i == 0 || i == _kLineData.Count - 1)
                    {
                        LogToFile($"📊 K线 {i}: 时间={kline.Time:yyyy-MM-dd HH:mm:ss}, 开盘={kline.Open}, 最高={kline.High}, 最低={kline.Low}, 收盘={kline.Close}, X={x:F2}, Y={bodyY:F2}, 高度={bodyHeight:F2}");
                    }
                }

                // 绘制订单标记
                foreach (var marker in _orderMarkers)
                {
                    var klineIndex = _kLineData.FindIndex(k => k.Time >= marker.Time);
                    if (klineIndex >= 0)
                    {
                        var x = startX + klineIndex * (scaledCandleWidth + scaledCandleSpacing) + scaledCandleWidth / 2;
                        var y = _canvasHeight - verticalMargin - (marker.Price - _minPrice) * _scaleY;

                        var triangle = new Polygon
                        {
                            Points = marker.IsEntry ?
                                new PointCollection { new Point(0, 0), new Point(10, 0), new Point(5, -10) } :
                                new PointCollection { new Point(0, -10), new Point(10, -10), new Point(5, 0) },
                            Fill = marker.IsEntry ? Brushes.Red : Brushes.Green,
                            Stroke = Brushes.White,
                            StrokeThickness = 0.5
                        };
                        Canvas.SetLeft(triangle, x - 5);
                        Canvas.SetTop(triangle, y);
                        KLineCanvas.Children.Add(triangle);
                    }
                }

                // 绘制网格线
                DrawGridLines();

                // 绘制均线
                if (_showMA)
                {
                    DrawMovingAverages();
                }

                LogToFile($"K线图绘制完成，共绘制 {KLineCanvas.Children.Count} 个元素");
            }
            catch (Exception ex)
            {
                LogToFile($"绘制K线图时发生错误: {ex.Message}");
                LogToFile($"异常堆栈: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// 计算共享的X轴参数，确保K线图和成交量图使用完全相同的X轴布局
        /// </summary>
        private XAxisParameters CalculateSharedXAxisParameters()
        {
            // 使用K线Canvas的宽度作为基准，因为它是主要显示区域
            var canvasWidth = Math.Max(_canvasWidth, 100); // 确保最小宽度
            var availableWidth = canvasWidth - 40; // 留出左右边距
            var totalKLineWidth = _kLineData.Count * (_candleWidth + _candleSpacing);
            var scaleX = Math.Min(1.0, availableWidth / totalKLineWidth);
            var scaledWidth = _candleWidth * scaleX;
            var scaledSpacing = _candleSpacing * scaleX;
            var startX = 20.0; // 左边距

            return new XAxisParameters
            {
                AvailableWidth = availableWidth,
                ScaleX = scaleX,
                ScaledWidth = scaledWidth,
                ScaledSpacing = scaledSpacing,
                StartX = startX
            };
        }

        private void DrawGridLines()
        {
            try
            {
                // 计算网格线的数量和间距
                var gridCount = 5;
                var priceStep = _priceRange / gridCount;
                var heightStep = (_canvasHeight - 40) / gridCount;
                var startX = 20.0; // 左边距

                // 绘制水平网格线
                for (int i = 0; i <= gridCount; i++)
                {
                    var y = _canvasHeight - 40 - i * heightStep;
                    var price = _minPrice + i * priceStep;

                    // 绘制网格线
                    var line = new Line
                    {
                        X1 = startX,
                        X2 = _canvasWidth - 20, // 右边距
                        Y1 = y,
                        Y2 = y,
                        Stroke = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255)),
                        StrokeThickness = 0.5
                    };
                    KLineCanvas.Children.Add(line);

                    // 添加价格标签
                    var priceText = new TextBlock
                    {
                        Text = price.ToString("F2"),
                        Foreground = Brushes.White,
                        FontSize = 10,
                        Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0))
                    };
                    Canvas.SetLeft(priceText, 5);
                    Canvas.SetTop(priceText, y - 10);
                    KLineCanvas.Children.Add(priceText);
                }

                // 绘制垂直网格线，使用与K线图相同的X轴参数
                var timeStep = _kLineData.Count / 5;
                var sharedXAxisParams = CalculateSharedXAxisParameters();

                for (int i = 0; i <= 5; i++)
                {
                    var x = sharedXAxisParams.StartX + i * timeStep * (sharedXAxisParams.ScaledWidth + sharedXAxisParams.ScaledSpacing);
                    var line = new Line
                    {
                        X1 = x,
                        X2 = x,
                        Y1 = 0,
                        Y2 = _canvasHeight - 40,
                        Stroke = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255)),
                        StrokeThickness = 0.5
                    };
                    KLineCanvas.Children.Add(line);
                }
            }
            catch (Exception ex)
            {
                LogToFile($"绘制网格线时发生错误: {ex.Message}");
                LogToFile($"异常堆栈: {ex.StackTrace}");
            }
        }

        private static void LogToFile(string message)
        {
            try
            {
                var logPath = IOPath.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "TCClient_KLineChart.log");
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var logMessage = $"[{timestamp}] {message}{Environment.NewLine}";
                File.AppendAllText(logPath, logMessage);
            }
            catch
            {
                // 忽略日志写入失败
            }
        }

        private List<KLineData> GenerateMockKLineData()
        {
            var data = new List<KLineData>();
            var random = new Random();
            var basePrice = 100.0;
            var time = DateTime.Now.AddMinutes(-_kLineCount);

            for (int i = 0; i < _kLineCount; i++)
            {
                var change = (random.NextDouble() - 0.5) * 2;
                var open = basePrice;
                var close = basePrice + change;
                var high = Math.Max(open, close) + random.NextDouble();
                var low = Math.Min(open, close) - random.NextDouble();

                data.Add(new KLineData
                {
                    Time = time.AddMinutes(i),
                    Open = open,
                    High = high,
                    Low = low,
                    Close = close,
                    Volume = random.Next(100, 1000)
                });

                basePrice = close;
            }

            return data;
        }

        public void UpdatePeriod(string period)
        {
            if (string.IsNullOrEmpty(period))
                return;

            // 更新K线图周期
            _currentPeriod = period;
            // 重新加载K线数据
            LoadKLineData();
        }

        private async void LoadKLineData()
        {
            if (string.IsNullOrEmpty(_currentSymbol) || string.IsNullOrEmpty(_currentPeriod))
                return;

            try
            {
                // 获取K线数据
                var data = await _exchangeService.GetKLineDataAsync(_currentSymbol, _currentPeriod, _kLineCount);
                if (data != null && data.Any())
                {
                    // 更新K线图显示
                    _kLineData = data;
                    DrawKLineChart();
                }
            }
            catch (Exception ex)
            {
                // 记录错误日志
                LogToFile($"加载K线数据失败: {ex.Message}");
            }
        }

        private void UpdateKLineDisplay(List<KLineData> data)
        {
            _kLineData = data;
            if (_kLineData != null && _kLineData.Any())
            {
                DrawKLineChart();
            }
        }

        private void DrawVolumeChart()
        {
            try
            {
                // 检查VolumeCanvas是否可用
                if (VolumeCanvas == null)
                {
                    LogToFile("错误: VolumeCanvas为null，无法绘制成交额图");
                    return;
                }
                
                if (!_showVolume || _kLineData == null || !_kLineData.Any() || _volumeCanvasHeight <= 0)
                {
                    VolumeCanvas.Children.Clear();
                    return;
                }

                LogToFile($"开始绘制成交额图，Canvas大小: {_canvasWidth}x{_volumeCanvasHeight}");
                VolumeCanvas.Children.Clear();

                // 计算成交额范围 - 优先使用QuoteVolume（成交额），如果为0则使用Volume（成交量）
                var validQuoteVolumes = _kLineData.Where(k => k.QuoteVolume > 0).ToList();
                if (validQuoteVolumes.Any())
                {
                    _maxVolume = (double)validQuoteVolumes.Max(k => k.QuoteVolume);
                    var minVolume = (double)validQuoteVolumes.Min(k => k.QuoteVolume);
                    LogToFile($"使用成交额数据，最大值: {_maxVolume:F2}, 最小值: {minVolume:F2}");
                }
                else
                {
                    _maxVolume = (double)_kLineData.Max(k => k.Volume);
                    var minVolume = (double)_kLineData.Min(k => k.Volume);
                    LogToFile($"使用成交量数据，最大值: {_maxVolume:F2}, 最小值: {minVolume:F2}");
                }
                var volumeRange = _maxVolume;
                
                // 计算缩放比例
                var verticalMargin = 10.0;
                _volumeScaleY = (_volumeCanvasHeight - verticalMargin) / _maxVolume;

                // ========== 关键修改：使用与K线图完全相同的X轴参数 ==========
                var sharedXAxisParams = CalculateSharedXAxisParameters();
                var scaledWidth = sharedXAxisParams.ScaledWidth;
                var scaledSpacing = sharedXAxisParams.ScaledSpacing;
                var startX = sharedXAxisParams.StartX;
                var scaleX = sharedXAxisParams.ScaleX;

                LogToFile($"🎯 成交额图X轴参数: 可用宽度={sharedXAxisParams.AvailableWidth}, 缩放比例={scaleX:F4}, 缩放后宽度={scaledWidth:F2}, 缩放后间距={scaledSpacing:F2}");

                // 绘制成交额柱状图 - 确保与K线的X轴位置完全对应
                for (int i = 0; i < _kLineData.Count; i++)
                {
                    var kline = _kLineData[i];
                    var x = startX + i * (scaledWidth + scaledSpacing);
                    
                    // 优先使用QuoteVolume（成交额），如果为0则使用Volume（成交量）
                    var volumeValue = kline.QuoteVolume > 0 ? (double)kline.QuoteVolume : (double)kline.Volume;
                    var height = volumeValue * _volumeScaleY;
                    var y = _volumeCanvasHeight - height - verticalMargin / 2;

                    var volumeBar = new Rectangle
                    {
                        Width = scaledWidth,
                        Height = Math.Max(1, height), // 确保最小高度为1像素
                        Fill = kline.Close >= kline.Open ? 
                               new SolidColorBrush(Color.FromArgb(128, 255, 0, 0)) : 
                               new SolidColorBrush(Color.FromArgb(128, 0, 255, 0)),
                        Stroke = Brushes.Transparent
                    };
                    Canvas.SetLeft(volumeBar, x);
                    Canvas.SetTop(volumeBar, y);
                    VolumeCanvas.Children.Add(volumeBar);
                    
                    // 记录调试信息
                    if (i == 0 || i == _kLineData.Count - 1)
                    {
                        LogToFile($"📊 成交额柱 {i}: 时间={kline.Time:yyyy-MM-dd HH:mm:ss}, 成交额={volumeValue:F2}, X={x:F2}, 高度={height:F2}");
                    }
                }

                LogToFile($"成交额图绘制完成，最大成交额: {_maxVolume:F2}");
                
                // 验证K线和成交额的时间对齐
                LogToFile("🔍 === K线和成交额时间对齐验证 ===");
                for (int i = 0; i < Math.Min(5, _kLineData.Count); i++)
                {
                    var kline = _kLineData[i];
                    var volumeValue = kline.QuoteVolume > 0 ? kline.QuoteVolume : kline.Volume;
                    var x = startX + i * (scaledWidth + scaledSpacing);
                    LogToFile($"🔍 验证 {i}: 时间={kline.Time:yyyy-MM-dd HH:mm:ss}, 成交额={volumeValue:F2}, X轴位置={x:F2}");
                }
                LogToFile("🔍 === 验证结束 ===");
            }
            catch (Exception ex)
            {
                LogToFile($"绘制成交额图时发生错误: {ex.Message}");
            }
        }

        private void DrawMovingAverages()
        {
            try
            {
                if (!_showMA || _kLineData == null || _kLineData.Count < _ma1Period)
                {
                    return;
                }

                // 计算MA数据
                var ma1Data = CalculateMA(_ma1Period);

                // 绘制MA线
                DrawMALine(ma1Data, Brushes.Yellow, _ma1Period);
            }
            catch (Exception ex)
            {
                LogToFile($"绘制均线时发生错误: {ex.Message}");
            }
        }

        private List<double> CalculateMA(int period)
        {
            var maData = new List<double>();
            
            for (int i = 0; i < _kLineData.Count; i++)
            {
                if (i < period - 1)
                {
                    maData.Add(double.NaN);
                }
                else
                {
                    var sum = 0.0;
                    for (int j = i - period + 1; j <= i; j++)
                    {
                        sum += (double)_kLineData[j].Close;
                    }
                    maData.Add(sum / period);
                }
            }
            
            return maData;
        }

        private void DrawMALine(List<double> maData, Brush brush, int period)
        {
            try
            {
                // 使用与K线图相同的X轴参数
                var sharedXAxisParams = CalculateSharedXAxisParameters();
                var scaledWidth = sharedXAxisParams.ScaledWidth;
                var scaledSpacing = sharedXAxisParams.ScaledSpacing;
                var startX = sharedXAxisParams.StartX;
                var verticalMargin = 40.0;

                Point? lastPoint = null;

                for (int i = 0; i < maData.Count; i++)
                {
                    if (double.IsNaN(maData[i])) continue;

                    var x = startX + i * (scaledWidth + scaledSpacing) + scaledWidth / 2;
                    var y = _canvasHeight - verticalMargin - (maData[i] - _minPrice) * _scaleY;
                    var currentPoint = new Point(x, y);

                    if (lastPoint.HasValue)
                    {
                        var line = new Line
                        {
                            X1 = lastPoint.Value.X,
                            Y1 = lastPoint.Value.Y,
                            X2 = currentPoint.X,
                            Y2 = currentPoint.Y,
                            Stroke = brush,
                            StrokeThickness = 1.5
                        };
                        KLineCanvas.Children.Add(line);
                    }

                    lastPoint = currentPoint;
                }
            }
            catch (Exception ex)
            {
                LogToFile($"绘制MA{period}线时发生错误: {ex.Message}");
            }
        }

        // 事件处理方法
        private void MATextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                if (sender is TextBox textBox)
                {
                    if (int.TryParse(textBox.Text, out int value) && value > 0)
                    {
                        if (textBox == MA1TextBox)
                            _ma1Period = value;

                        // 重新绘制图表
                        if (_kLineData != null && _kLineData.Any())
                        {
                            DrawKLineChart();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogToFile($"MA参数更改时发生错误: {ex.Message}");
            }
        }

        private void ShowVolumeCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ShowVolumeCheckBox == null || VolumeCanvas == null) return;
                
                _showVolume = ShowVolumeCheckBox.IsChecked == true;
                
                if (_showVolume)
                {
                    VolumeCanvas.Visibility = Visibility.Visible;
                    if (_kLineData != null && _kLineData.Any())
                    {
                        DrawVolumeChart();
                    }
                }
                else
                {
                    VolumeCanvas.Visibility = Visibility.Collapsed;
                    VolumeCanvas.Children.Clear();
                }
            }
            catch (Exception ex)
            {
                LogToFile($"切换成交额显示时发生错误: {ex.Message}");
            }
        }

        private void ShowMACheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                _showMA = ShowMACheckBox.IsChecked == true;
                
                // 重新绘制图表
                if (_kLineData != null && _kLineData.Any())
                {
                    DrawKLineChart();
                }
            }
            catch (Exception ex)
            {
                LogToFile($"切换均线显示时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 验证K线和成交量图的时间对齐情况（用于调试）
        /// </summary>
        public void VerifyTimeAlignment()
        {
            if (_kLineData == null || !_kLineData.Any())
            {
                LogToFile("⚠️ 无K线数据，无法验证时间对齐");
                return;
            }

            LogToFile("🔍 === 开始验证K线和成交量时间对齐 ===");
            
            var sharedXAxisParams = CalculateSharedXAxisParameters();
            LogToFile($"🎯 共享X轴参数: 可用宽度={sharedXAxisParams.AvailableWidth:F2}, 缩放比例={sharedXAxisParams.ScaleX:F4}");
            LogToFile($"🎯 缩放后宽度={sharedXAxisParams.ScaledWidth:F2}, 间距={sharedXAxisParams.ScaledSpacing:F2}, 起始X={sharedXAxisParams.StartX:F2}");
            
            // 验证前5个K线的位置
            for (int i = 0; i < Math.Min(5, _kLineData.Count); i++)
            {
                var kline = _kLineData[i];
                var x = sharedXAxisParams.StartX + i * (sharedXAxisParams.ScaledWidth + sharedXAxisParams.ScaledSpacing);
                var volumeValue = kline.QuoteVolume > 0 ? kline.QuoteVolume : kline.Volume;
                
                LogToFile($"🔍 K线 {i}: 时间={kline.Time:yyyy-MM-dd HH:mm:ss}, X轴位置={x:F2}, 成交额={volumeValue:F2}");
            }
            
            LogToFile("🔍 === 时间对齐验证完成 ===");
        }
    }

    public class OrderMarker
    {
        public DateTime Time { get; set; }
        public double Price { get; set; }
        public bool IsEntry { get; set; }
    }

    /// <summary>
    /// X轴参数类，用于确保K线图和成交量图使用相同的X轴布局
    /// </summary>
    internal class XAxisParameters
    {
        public double AvailableWidth { get; set; }
        public double ScaleX { get; set; }
        public double ScaledWidth { get; set; }
        public double ScaledSpacing { get; set; }
        public double StartX { get; set; }
    }
} 