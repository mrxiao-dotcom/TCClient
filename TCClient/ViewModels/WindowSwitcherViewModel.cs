using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using TCClient.Commands;
using TCClient.Services;
using TCClient.Utils;

namespace TCClient.ViewModels
{
    /// <summary>
    /// 窗口项目模型
    /// </summary>
    public class WindowItemViewModel : INotifyPropertyChanged
    {
        private readonly Window _window;
        private readonly WindowSwitcherViewModel _parent;

        public WindowItemViewModel(Window window, string windowType, WindowSwitcherViewModel parent)
        {
            _window = window;
            _parent = parent;
            WindowType = windowType;
            
            // 设置图标
            Icon = GetWindowIcon(windowType);
            
            // 监听窗口状态变化
            _window.StateChanged += OnWindowStateChanged;
            _window.Activated += OnWindowActivated;
            _window.Deactivated += OnWindowDeactivated;
            
            // 初始化命令
            ActivateCommand = new RelayCommand(ActivateWindow);
            MinimizeCommand = new RelayCommand(MinimizeWindow);
            MaximizeCommand = new RelayCommand(MaximizeWindow);
            CloseCommand = new RelayCommand(CloseWindow);
            
            UpdateTitle();
        }

        public string WindowType { get; }
        public string Icon { get; }
        
        private string _title;
        public string Title
        {
            get => _title;
            set
            {
                if (_title != value)
                {
                    _title = value;
                    OnPropertyChanged(nameof(Title));
                }
            }
        }

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive != value)
                {
                    _isActive = value;
                    OnPropertyChanged(nameof(IsActive));
                }
            }
        }

        public ICommand ActivateCommand { get; }
        public ICommand MinimizeCommand { get; }
        public ICommand MaximizeCommand { get; }
        public ICommand CloseCommand { get; }

        private void ActivateWindow()
        {
            try
            {
                if (_window.WindowState == WindowState.Minimized)
                {
                    _window.WindowState = WindowState.Normal;
                }
                _window.Activate();
                _window.Focus();
                LogManager.Log("WindowSwitcher", $"激活窗口: {_window.Title}");
            }
            catch (Exception ex)
            {
                LogManager.LogException("WindowSwitcher", ex, "激活窗口失败");
            }
        }

        private void MinimizeWindow()
        {
            try
            {
                _window.WindowState = WindowState.Minimized;
                LogManager.Log("WindowSwitcher", $"最小化窗口: {_window.Title}");
            }
            catch (Exception ex)
            {
                LogManager.LogException("WindowSwitcher", ex, "最小化窗口失败");
            }
        }

        private void MaximizeWindow()
        {
            try
            {
                _window.WindowState = _window.WindowState == WindowState.Maximized ? 
                    WindowState.Normal : WindowState.Maximized;
                LogManager.Log("WindowSwitcher", $"切换窗口状态: {_window.Title}");
            }
            catch (Exception ex)
            {
                LogManager.LogException("WindowSwitcher", ex, "切换窗口状态失败");
            }
        }

        private void CloseWindow()
        {
            try
            {
                _window.Close();
                LogManager.Log("WindowSwitcher", $"关闭窗口: {_window.Title}");
            }
            catch (Exception ex)
            {
                LogManager.LogException("WindowSwitcher", ex, "关闭窗口失败");
            }
        }

        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            UpdateTitle();
        }

        private void OnWindowActivated(object sender, EventArgs e)
        {
            IsActive = true;
        }

        private void OnWindowDeactivated(object sender, EventArgs e)
        {
            IsActive = false;
        }

        private void UpdateTitle()
        {
            var state = _window.WindowState == WindowState.Minimized ? " (最小化)" :
                       _window.WindowState == WindowState.Maximized ? " (最大化)" : "";
            Title = $"{_window.Title}{state}";
        }

        private string GetWindowIcon(string windowType)
        {
            return windowType switch
            {
                "MarketOverview" => "📊",
                "ServiceManager" => "⚙️",
                "PushStatistics" => "📈",
                "AccountQuery" => "🔍",
                "Order" => "💰",
                "DatabaseConfig" => "🗄️",
                "AccountConfig" => "👤",
                _ => "🪟"
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            _window.StateChanged -= OnWindowStateChanged;
            _window.Activated -= OnWindowActivated;
            _window.Deactivated -= OnWindowDeactivated;
        }
    }

    /// <summary>
    /// 窗口切换器ViewModel
    /// </summary>
    public class WindowSwitcherViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly WindowManagerService _windowManager;
        private bool _isPinned = false;

        public WindowSwitcherViewModel(WindowManagerService windowManager)
        {
            _windowManager = windowManager;
            WindowItems = new ObservableCollection<WindowItemViewModel>();
            
            // 初始化命令
            TogglePinCommand = new RelayCommand(TogglePin);
            MinimizeAllCommand = new RelayCommand(MinimizeAll);
            RestoreAllCommand = new RelayCommand(RestoreAll);
            CloseAllCommand = new RelayCommand(CloseAll);
            
            // 刷新窗口列表
            RefreshWindowList();
            
            LogManager.Log("WindowSwitcher", "窗口切换器已初始化");
        }

        public ObservableCollection<WindowItemViewModel> WindowItems { get; }

        private int _totalWindowCount;
        public int TotalWindowCount
        {
            get => _totalWindowCount;
            set
            {
                if (_totalWindowCount != value)
                {
                    _totalWindowCount = value;
                    OnPropertyChanged(nameof(TotalWindowCount));
                }
            }
        }

        public bool IsPinned
        {
            get => _isPinned;
            set
            {
                if (_isPinned != value)
                {
                    _isPinned = value;
                    OnPropertyChanged(nameof(IsPinned));
                }
            }
        }

        public ICommand TogglePinCommand { get; }
        public ICommand MinimizeAllCommand { get; }
        public ICommand RestoreAllCommand { get; }
        public ICommand CloseAllCommand { get; }

        /// <summary>
        /// 刷新窗口列表
        /// </summary>
        public void RefreshWindowList()
        {
            try
            {
                // 清理现有项目
                foreach (var item in WindowItems)
                {
                    item.Dispose();
                }
                WindowItems.Clear();

                // 获取所有应用程序窗口
                var allWindows = Application.Current.Windows.Cast<Window>()
                    .Where(w => w != Application.Current.MainWindow && w.IsVisible)
                    .ToList();

                // 添加窗口项目
                foreach (var window in allWindows)
                {
                    var windowType = GetWindowType(window);
                    var item = new WindowItemViewModel(window, windowType, this);
                    WindowItems.Add(item);
                }

                TotalWindowCount = WindowItems.Count;
                LogManager.Log("WindowSwitcher", $"刷新窗口列表完成，共 {TotalWindowCount} 个窗口");
            }
            catch (Exception ex)
            {
                LogManager.LogException("WindowSwitcher", ex, "刷新窗口列表失败");
            }
        }

        /// <summary>
        /// 添加窗口
        /// </summary>
        public void AddWindow(Window window, string windowType)
        {
            try
            {
                var existingItem = WindowItems.FirstOrDefault(item => 
                    item.Title.Contains(window.Title));
                
                if (existingItem == null)
                {
                    var item = new WindowItemViewModel(window, windowType, this);
                    WindowItems.Add(item);
                    TotalWindowCount = WindowItems.Count;
                    
                    LogManager.Log("WindowSwitcher", $"添加窗口: {window.Title}");
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("WindowSwitcher", ex, "添加窗口失败");
            }
        }

        /// <summary>
        /// 移除窗口
        /// </summary>
        public void RemoveWindow(Window window)
        {
            try
            {
                var item = WindowItems.FirstOrDefault(item => 
                    item.Title.Contains(window.Title));
                
                if (item != null)
                {
                    item.Dispose();
                    WindowItems.Remove(item);
                    TotalWindowCount = WindowItems.Count;
                    
                    LogManager.Log("WindowSwitcher", $"移除窗口: {window.Title}");
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("WindowSwitcher", ex, "移除窗口失败");
            }
        }

        private void TogglePin()
        {
            IsPinned = !IsPinned;
            LogManager.Log("WindowSwitcher", $"切换固定状态: {IsPinned}");
        }

        private void MinimizeAll()
        {
            try
            {
                foreach (var item in WindowItems)
                {
                    item.MinimizeCommand.Execute(null);
                }
                LogManager.Log("WindowSwitcher", "最小化所有窗口");
            }
            catch (Exception ex)
            {
                LogManager.LogException("WindowSwitcher", ex, "最小化所有窗口失败");
            }
        }

        private void RestoreAll()
        {
            try
            {
                foreach (var item in WindowItems)
                {
                    item.ActivateCommand.Execute(null);
                }
                LogManager.Log("WindowSwitcher", "恢复所有窗口");
            }
            catch (Exception ex)
            {
                LogManager.LogException("WindowSwitcher", ex, "恢复所有窗口失败");
            }
        }

        private void CloseAll()
        {
            try
            {
                var itemsToClose = WindowItems.ToList();
                foreach (var item in itemsToClose)
                {
                    item.CloseCommand.Execute(null);
                }
                LogManager.Log("WindowSwitcher", "关闭所有窗口");
            }
            catch (Exception ex)
            {
                LogManager.LogException("WindowSwitcher", ex, "关闭所有窗口失败");
            }
        }

        private string GetWindowType(Window window)
        {
            var typeName = window.GetType().Name;
            return typeName switch
            {
                "MarketOverviewWindow" => "MarketOverview",
                "ServiceManagerWindow" => "ServiceManager", 
                "PushStatisticsWindow" => "PushStatistics",
                "AccountQueryWindow" => "AccountQuery",
                "OrderWindow" => "Order",
                "DatabaseConfigWindow" => "DatabaseConfig",
                "AccountConfigWindow" => "AccountConfig",
                _ => "Unknown"
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            foreach (var item in WindowItems)
            {
                item.Dispose();
            }
            WindowItems.Clear();
            LogManager.Log("WindowSwitcher", "窗口切换器已释放");
        }
    }
} 