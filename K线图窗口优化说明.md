# K线图窗口优化说明

## 问题解决

### 1. 最近浏览记录显示空白问题修复

**问题分析**：
- UI绑定正确，但数据刷新机制有问题
- ObservableCollection更新后没有正确触发UI刷新

**解决方案**：
- 修复了LoadRecentSymbols方法中的UI刷新逻辑
- 使用`Items.Refresh()`强制刷新DataGrid
- 增强了调试日志，便于追踪数据加载过程

**修复代码位置**：
```csharp
// 刷新UI - 强制刷新DataGrid
Dispatcher.Invoke(() =>
{
    Utils.AppSession.Log($"📊 UI线程刷新，当前ObservableCollection包含 {_recentSymbols.Count} 个项目");
    
    // 强制刷新DataGrid
    if (RecentSymbolsDataGrid != null)
    {
        RecentSymbolsDataGrid.Items.Refresh();
        Utils.AppSession.Log($"📊 DataGrid已刷新");
    }
});
```

### 2. 窗口关闭问题修复

**问题分析**：
- OnClosed方法中的异步操作阻止了窗口正常关闭
- "fire and forget"异步调用导致窗口挂起

**解决方案**：
- 添加了Closing事件处理，在关闭前完成异步保存操作
- 简化了OnClosed方法，避免阻塞操作

## 新功能：全局均线参数控制

### 功能特性

1. **UI控件**：
   - 输入框显示当前均线参数（默认20）
   - "+"和"-"按钮快速调整参数
   - 参数范围：5-200天

2. **全局同步**：
   - 修改参数后，所有周期的K线图同时更新
   - 设置保存到本地，下次打开时保留
   - 新打开的K线图窗口使用最新参数

3. **数据持久化**：
   - 使用SettingsManager保存到settings.json
   - 自动加载上次保存的参数

### UI设计

添加在左侧面板的均线参数控制区域：
```xml
<!-- 全局均线参数控制 -->
<Border Background="#F8F9FA" CornerRadius="5" Padding="8" Margin="0,5">
    <StackPanel Orientation="Vertical">
        <TextBlock Text="📊 全局均线参数" FontWeight="Bold" FontSize="11" 
                   Foreground="#2196F3" HorizontalAlignment="Center" Margin="0,0,0,5"/>
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            
            <Button Grid.Column="0" x:Name="DecreaseMAButton" Content="-" 
                    Background="#FF5722" Foreground="White" BorderThickness="0"
                    Width="25" Height="25" FontWeight="Bold" Click="DecreaseMAButton_Click"/>
            
            <TextBox Grid.Column="1" x:Name="MAPeriodTextBox" Text="20" 
                     Width="40" Height="25" Margin="5,0" TextAlignment="Center"
                     VerticalContentAlignment="Center" FontWeight="Bold"
                     PreviewTextInput="MAPeriodTextBox_PreviewTextInput"
                     TextChanged="MAPeriodTextBox_TextChanged"/>
            
            <Button Grid.Column="2" x:Name="IncreaseMAButton" Content="+" 
                    Background="#4CAF50" Foreground="White" BorderThickness="0"
                    Width="25" Height="25" FontWeight="Bold" Click="IncreaseMAButton_Click"/>
        </Grid>
        <TextBlock Text="天均线" FontSize="10" Foreground="#666" 
                   HorizontalAlignment="Center" Margin="0,2,0,0"/>
    </StackPanel>
</Border>
```

### 技术实现

#### 1. KLineChartControl增强
添加了`SetMAPeriod`方法：
```csharp
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
```

#### 2. 全局参数管理
使用静态变量存储全局参数：
```csharp
private static int _globalMAPeriod = 20; // 全局均线参数
```

#### 3. 事件处理方法
- `DecreaseMAButton_Click` - 减少参数
- `IncreaseMAButton_Click` - 增加参数  
- `MAPeriodTextBox_TextChanged` - 文本框输入处理
- `MAPeriodTextBox_PreviewTextInput` - 输入验证（只允许数字）

#### 4. 数据同步方法
- `UpdateGlobalMAPeriod()` - 更新所有K线图的均线参数
- `SaveGlobalMASettingsAsync()` - 保存到设置文件
- `LoadGlobalMASettingsAsync()` - 从设置文件加载

### 工作流程

1. **窗口初始化**：
   - 加载保存的全局均线参数
   - 更新UI显示当前参数值

2. **参数修改**：
   - 用户点击+/-按钮或输入框修改参数
   - 验证参数有效性（5-200范围）
   - 同步更新所有K线图控件
   - 自动保存到设置文件

3. **窗口关闭**：
   - 保存当前参数到设置文件
   - 确保下次打开时保留设置

## 日志增强

添加了详细的操作日志：
- 📊 参数更新相关
- 💾 设置保存相关
- ✅ 操作成功
- ❌ 操作失败
- 📝 一般信息

## 测试建议

1. **最近浏览记录测试**：
   - 打开不同合约的K线图
   - 检查最近浏览列表是否正确显示
   - 重启应用验证数据持久化

2. **全局均线参数测试**：
   - 修改均线参数，观察所有周期图表是否同步更新
   - 关闭窗口重新打开，验证参数是否保留
   - 打开新的K线图窗口，验证是否使用最新参数

3. **窗口关闭测试**：
   - 点击X按钮应该能够正常关闭窗口
   - 检查日志确认数据正确保存

## 修改的文件

- `TCClient/Views/KLineFloatingWindow.xaml` - UI布局
- `TCClient/Views/KLineFloatingWindow.xaml.cs` - 窗口逻辑和全局参数控制
- `TCClient/Views/Controls/KLineChartControl.xaml.cs` - 添加SetMAPeriod方法

## 技术特性

- **数据持久化**：使用JSON文件保存设置
- **全局同步**：静态变量确保所有窗口使用相同参数
- **输入验证**：防止无效参数输入
- **异常处理**：完善的错误捕获和日志记录
- **UI响应性**：使用Dispatcher确保UI线程安全 