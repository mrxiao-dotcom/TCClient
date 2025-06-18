# K线图显示修复说明

## 问题描述

### 原始问题
- **最右侧K线图区域没有显示K线图**：双击合约后，K线图区域只显示"K线图加载中..."文本，没有实际的图表显示

### 根本原因
1. **缺少图表绘制逻辑**：`UpdateChartDisplay`方法只显示文本信息，没有绘制实际的K线图
2. **没有图表库依赖**：项目中没有包含图表绘制库
3. **UI结构不适合图表显示**：使用StackPanel布局不适合图表控件

## 解决方案

### 1. 实现自定义K线图绘制

#### 技术选择
- **使用WPF原生绘图**：使用Canvas、Rectangle、Line等基本图形控件
- **避免外部依赖**：不引入复杂的图表库，保持项目轻量化
- **红涨绿跌配色**：符合中国股市习惯

#### 核心实现

**修改文件：** `TCClient/Views/DrawdownAlertWindow.xaml.cs`

**主要改进：**

1. **重构UI布局**
```csharp
private void UpdateChartDisplay(Border chartBorder, List<BinanceKLineData> klineData, string symbol, string interval)
{
    // 创建Grid布局：标题 + 图表区域
    var mainGrid = new Grid();
    mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
    
    // 添加标题
    var titleBlock = new TextBlock { /* 标题配置 */ };
    
    // 创建K线图
    var chartCanvas = CreateKLineChart(klineData);
    
    chartBorder.Child = mainGrid;
}
```

2. **K线图绘制算法**
```csharp
private Canvas CreateKLineChart(List<BinanceKLineData> klineData)
{
    // 1. 数据处理：取最后20根K线
    var displayData = klineData.TakeLast(20).ToList();
    
    // 2. 价格范围计算
    var minPrice = displayData.Min(k => k.Low);
    var maxPrice = displayData.Max(k => k.High);
    var priceRange = maxPrice - minPrice;
    
    // 3. 坐标映射
    // Y轴翻转：Canvas坐标系Y轴向下，价格坐标系Y轴向上
    var openY = canvas.Height - (open - minPrice) / priceRange * canvas.Height;
    
    // 4. K线绘制
    // - 上影线：最高价到实体顶部
    // - 下影线：实体底部到最低价  
    // - 实体：开盘价到收盘价的矩形
}
```

### 2. K线图特性

#### 视觉效果
- **画布尺寸**：200×120像素，适合小窗口显示
- **显示数量**：最后20根K线，避免过于拥挤
- **颜色方案**：
  - 红色：上涨（收盘价≥开盘价）
  - 绿色：下跌（收盘价<开盘价）
  - 深色边框：增强视觉效果

#### 图表元素
1. **K线实体**：Rectangle表示开盘价到收盘价
2. **上影线**：Line表示最高价到实体顶部
3. **下影线**：Line表示实体底部到最低价
4. **价格标签**：显示最新收盘价

#### 数据处理
- **自动缩放**：根据价格范围自动调整Y轴比例
- **防零除**：价格范围为0时使用1%作为默认范围
- **最小高度**：实体高度至少1像素，确保可见性

### 3. 技术实现细节

#### 坐标系转换
```csharp
// Canvas坐标系：原点在左上角，Y轴向下
// 价格坐标系：Y轴向上（价格越高Y值越大）
// 转换公式：
var yCanvas = canvas.Height - (price - minPrice) / priceRange * canvas.Height;
```

#### K线绘制逻辑
```csharp
// 1. 判断涨跌
var isRising = close >= open;
var candleColor = isRising ? Brushes.Red : Brushes.Green;

// 2. 绘制影线
if (high > Math.Max(open, close)) {
    // 绘制上影线
}
if (low < Math.Min(open, close)) {
    // 绘制下影线  
}

// 3. 绘制实体
var bodyRect = new Rectangle {
    Width = candleWidth,
    Height = Math.Abs(closeY - openY),
    Fill = candleColor
};
```

#### 布局计算
```csharp
var candleWidth = canvas.Width / displayData.Count * 0.6;  // 60%宽度用于K线
var candleSpacing = canvas.Width / displayData.Count;      // 均匀分布
var x = i * candleSpacing + candleSpacing / 2;            // 居中对齐
```

### 4. 使用效果

#### 功能特性
- ✅ **真实K线图显示**：绘制完整的OHLC K线图
- ✅ **多时间周期**：支持日线、1小时、15分钟、5分钟
- ✅ **实时数据**：使用真实的币安API数据
- ✅ **自适应缩放**：根据价格范围自动调整显示比例
- ✅ **中式配色**：红涨绿跌，符合中国用户习惯

#### 显示内容
- **标题**：合约名称 + 时间周期（如"BTCUSDT - 日线"）
- **K线图**：最近20根K线的完整OHLC显示
- **价格标签**：最新收盘价，便于快速查看
- **错误处理**：数据加载失败时显示错误信息

#### 性能优化
- **轻量级绘制**：使用WPF原生控件，性能优秀
- **数据限制**：只显示最后20根K线，避免过度绘制
- **异步加载**：不阻塞UI线程，用户体验流畅

## 使用方法

1. **添加合约**：在回撤预警窗口点击"添加合约"
2. **双击合约**：在左侧合约列表中双击任意合约
3. **查看K线图**：右侧会显示4个时间周期的K线图
4. **实时更新**：K线图会随着数据更新自动刷新

## 技术优势

1. **无外部依赖**：不需要额外的图表库，减少项目复杂度
2. **高度定制**：完全控制绘制逻辑，可以根据需要调整
3. **性能优秀**：使用WPF原生绘图，渲染效率高
4. **维护简单**：代码逻辑清晰，易于理解和修改

现在用户双击合约后，可以在最右侧看到真正的K线图显示，包含完整的OHLC信息和专业的视觉效果！ 