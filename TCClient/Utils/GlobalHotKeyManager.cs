using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using TCClient.Services;

namespace TCClient.Utils
{
    /// <summary>
    /// 全局快捷键管理器
    /// </summary>
    public class GlobalHotKeyManager : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID_WINDOW_SWITCHER = 1;
        private const int HOTKEY_ID_MINIMIZE_ALL = 2;
        private const int HOTKEY_ID_RESTORE_ALL = 3;
        
        // 修饰键常量
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        
        // Virtual Key Codes
        private const uint VK_TAB = 0x09;
        private const uint VK_M = 0x4D;
        private const uint VK_R = 0x52;
        
        private readonly WindowManagerService _windowManager;
        private IntPtr _windowHandle;
        private HwndSource _hwndSource;
        private bool _disposed = false;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public GlobalHotKeyManager(WindowManagerService windowManager)
        {
            _windowManager = windowManager;
            LogManager.Log("GlobalHotKey", "全局快捷键管理器已初始化");
        }

        /// <summary>
        /// 初始化快捷键（需要在主窗口加载后调用）
        /// </summary>
        /// <param name="mainWindow">主窗口</param>
        public void Initialize(Window mainWindow)
        {
            try
            {
                if (mainWindow == null)
                {
                    LogManager.Log("GlobalHotKey", "主窗口为空，无法初始化快捷键");
                    return;
                }

                // 获取窗口句柄
                var windowInteropHelper = new WindowInteropHelper(mainWindow);
                _windowHandle = windowInteropHelper.Handle;

                if (_windowHandle == IntPtr.Zero)
                {
                    // 如果窗口还没有句柄，等待窗口加载完成
                    mainWindow.SourceInitialized += (s, e) =>
                    {
                        _windowHandle = new WindowInteropHelper(mainWindow).Handle;
                        RegisterHotKeys();
                    };
                }
                else
                {
                    RegisterHotKeys();
                }

                // 添加消息钩子
                _hwndSource = HwndSource.FromHwnd(_windowHandle);
                _hwndSource?.AddHook(WndProc);

                LogManager.Log("GlobalHotKey", "快捷键初始化完成");
            }
            catch (Exception ex)
            {
                LogManager.LogException("GlobalHotKey", ex, "初始化快捷键失败");
            }
        }

        /// <summary>
        /// 注册快捷键
        /// </summary>
        private void RegisterHotKeys()
        {
            try
            {
                if (_windowHandle == IntPtr.Zero)
                {
                    LogManager.Log("GlobalHotKey", "窗口句柄无效，无法注册快捷键");
                    return;
                }

                // 注册 Ctrl+Tab - 窗口切换器
                bool success1 = RegisterHotKey(_windowHandle, HOTKEY_ID_WINDOW_SWITCHER, MOD_CONTROL, VK_TAB);
                LogManager.Log("GlobalHotKey", $"注册 Ctrl+Tab: {(success1 ? "成功" : "失败")}");

                // 注册 Ctrl+Shift+M - 最小化所有窗口
                bool success2 = RegisterHotKey(_windowHandle, HOTKEY_ID_MINIMIZE_ALL, MOD_CONTROL | MOD_SHIFT, VK_M);
                LogManager.Log("GlobalHotKey", $"注册 Ctrl+Shift+M: {(success2 ? "成功" : "失败")}");

                // 注册 Ctrl+Shift+R - 恢复所有窗口
                bool success3 = RegisterHotKey(_windowHandle, HOTKEY_ID_RESTORE_ALL, MOD_CONTROL | MOD_SHIFT, VK_R);
                LogManager.Log("GlobalHotKey", $"注册 Ctrl+Shift+R: {(success3 ? "成功" : "失败")}");

                if (!success1 || !success2 || !success3)
                {
                    LogManager.Log("GlobalHotKey", "部分快捷键注册失败，可能与其他应用程序冲突");
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("GlobalHotKey", ex, "注册快捷键失败");
            }
        }

        /// <summary>
        /// 注销快捷键
        /// </summary>
        private void UnregisterHotKeys()
        {
            try
            {
                if (_windowHandle != IntPtr.Zero)
                {
                    UnregisterHotKey(_windowHandle, HOTKEY_ID_WINDOW_SWITCHER);
                    UnregisterHotKey(_windowHandle, HOTKEY_ID_MINIMIZE_ALL);
                    UnregisterHotKey(_windowHandle, HOTKEY_ID_RESTORE_ALL);
                    LogManager.Log("GlobalHotKey", "快捷键已注销");
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("GlobalHotKey", ex, "注销快捷键失败");
            }
        }

        /// <summary>
        /// 窗口消息处理
        /// </summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            try
            {
                if (msg == WM_HOTKEY)
                {
                    int hotkeyId = wParam.ToInt32();
                    HandleHotKey(hotkeyId);
                    handled = true;
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("GlobalHotKey", ex, "处理快捷键消息失败");
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// 处理快捷键
        /// </summary>
        private void HandleHotKey(int hotkeyId)
        {
            try
            {
                switch (hotkeyId)
                {
                    case HOTKEY_ID_WINDOW_SWITCHER:
                        LogManager.Log("GlobalHotKey", "触发窗口切换器快捷键");
                        _windowManager.ToggleWindowSwitcher();
                        break;

                    case HOTKEY_ID_MINIMIZE_ALL:
                        LogManager.Log("GlobalHotKey", "触发最小化所有窗口快捷键");
                        _windowManager.MinimizeAllWindows();
                        break;

                    case HOTKEY_ID_RESTORE_ALL:
                        LogManager.Log("GlobalHotKey", "触发恢复所有窗口快捷键");
                        _windowManager.RestoreAllWindows();
                        break;

                    default:
                        LogManager.Log("GlobalHotKey", $"未知的快捷键ID: {hotkeyId}");
                        break;
                }
            }
            catch (Exception ex)
            {
                LogManager.LogException("GlobalHotKey", ex, $"处理快捷键失败: {hotkeyId}");
            }
        }

        /// <summary>
        /// 获取快捷键帮助信息
        /// </summary>
        /// <returns>快捷键说明</returns>
        public string GetHotKeyHelp()
        {
            return "快捷键说明：\n" +
                   "Ctrl+Tab - 显示/隐藏窗口切换器\n" +
                   "Ctrl+Shift+M - 最小化所有窗口\n" +
                   "Ctrl+Shift+R - 恢复所有窗口";
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                UnregisterHotKeys();
                _hwndSource?.RemoveHook(WndProc);
                _hwndSource = null;
                _disposed = true;
                LogManager.Log("GlobalHotKey", "全局快捷键管理器已释放");
            }
        }
    }
} 