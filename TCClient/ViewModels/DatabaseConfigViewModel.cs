using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;
using TCClient.Services;
using TCClient.Commands;
using System.Collections.ObjectModel;
using TCClient.Models;
using System.IO;

namespace TCClient.ViewModels
{
    public class DatabaseConfigViewModel : ViewModelBase
    {
        private readonly IDatabaseService _databaseService;
        private readonly LocalConfigService _configService;
        private DatabaseConnection _currentConnection;
        private string _statusMessage;
        private bool _isTesting;
        private bool _isSaving;

        public DatabaseConnection CurrentConnection
        {
            get => _currentConnection;
            set
            {
                if (_currentConnection != value)
                {
                    _currentConnection = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<DatabaseConnection> Connections { get; }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsTesting
        {
            get => _isTesting;
            set
            {
                if (_isTesting != value)
                {
                    _isTesting = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsSaving
        {
            get => _isSaving;
            set
            {
                if (_isSaving != value)
                {
                    _isSaving = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand TestConnectionCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand AddConnectionCommand { get; }
        public ICommand RemoveConnectionCommand { get; }

        public DatabaseConfigViewModel()
        {
            _databaseService = new MySqlDatabaseService();
            _configService = new LocalConfigService();
            _currentConnection = new DatabaseConnection();
            Connections = new ObservableCollection<DatabaseConnection>();

            TestConnectionCommand = new RelayCommand(async () => await TestConnectionAsync(), () => !IsTesting && CanTestConnection());
            SaveCommand = new RelayCommand(async () =>
            {
                if (await SaveAsync())
                {
                    // 关闭窗口
                    CloseWindow?.Invoke(true);
                }
            });
            CancelCommand = new RelayCommand(() =>
            {
                // 关闭窗口
                CloseWindow?.Invoke(false);
            });
            AddConnectionCommand = new RelayCommand(() => AddConnection());
            RemoveConnectionCommand = new RelayCommand<DatabaseConnection>(RemoveConnection, CanRemoveConnection);

            LoadConnections();
        }

        private async void LoadConnections()
        {
            try
            {
                var connections = await _configService.LoadDatabaseConnections();
                Connections.Clear();
                foreach (var connection in connections)
                {
                    Connections.Add(connection);
                }

                if (Connections.Count > 0)
                {
                    CurrentConnection = Connections[0];
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载数据库连接配置失败：{ex.Message}";
            }
        }

        private bool CanTestConnection()
        {
            return !string.IsNullOrWhiteSpace(CurrentConnection.Server) &&
                   !string.IsNullOrWhiteSpace(CurrentConnection.Database) &&
                   !string.IsNullOrWhiteSpace(CurrentConnection.Username) &&
                   !string.IsNullOrWhiteSpace(CurrentConnection.Password);
        }

        private async Task TestConnectionAsync()
        {
            if (IsTesting) return;

            try
            {
                IsTesting = true;
                StatusMessage = "正在测试连接...";

                // 记录当前连接信息
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "TCClient_DatabaseTest.log");
                
                var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] 开始测试连接\n" +
                                $"服务器: {CurrentConnection.Server}\n" +
                                $"端口: {CurrentConnection.Port}\n" +
                                $"数据库: {CurrentConnection.Database}\n" +
                                $"用户名: {CurrentConnection.Username}\n";
                
                File.AppendAllText(logPath, logMessage);

                // 先尝试 ping 服务器
                try
                {
                    using (var ping = new System.Net.NetworkInformation.Ping())
                    {
                        StatusMessage = "正在 ping 服务器...";
                        var reply = await ping.SendPingAsync(CurrentConnection.Server);
                        var pingResult = $"Ping 结果: {reply.Status}, 延迟: {reply.RoundtripTime}ms";
                        StatusMessage = pingResult;
                        File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {pingResult}\n");
                    }
                }
                catch (Exception pingEx)
                {
                    var pingError = $"Ping 失败: {pingEx.Message}";
                    StatusMessage = pingError;
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {pingError}\n");
                }

                // 测试数据库连接
                StatusMessage = "正在测试数据库连接...";
                var isConnected = await _databaseService.TestConnectionAsync();
                
                if (isConnected)
                {
                    StatusMessage = "连接测试成功";
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] 数据库连接测试成功\n");
                }
                else
                {
                    StatusMessage = "连接测试失败";
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] 数据库连接测试失败\n");
                }
            }
            catch (Exception ex)
            {
                var errorMessage = $"连接测试失败：{ex.Message}";
                StatusMessage = errorMessage;
                
                // 记录详细错误信息
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "TCClient_DatabaseTest.log");
                
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {errorMessage}\n");
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] 错误详情: {ex}\n");
            }
            finally
            {
                IsTesting = false;
            }
        }

        private bool CanSave()
        {
            return !string.IsNullOrWhiteSpace(CurrentConnection.Server) &&
                   !string.IsNullOrWhiteSpace(CurrentConnection.Database) &&
                   !string.IsNullOrWhiteSpace(CurrentConnection.Username) &&
                   !string.IsNullOrWhiteSpace(CurrentConnection.Password);
        }

        private async Task<bool> SaveAsync()
        {
            if (IsSaving) return false;

            try
            {
                IsSaving = true;
                StatusMessage = "正在保存配置...";

                await _configService.SaveDatabaseConnections(Connections.ToList());
                StatusMessage = "配置保存成功";
                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"保存配置失败：{ex.Message}";
                return false;
            }
            finally
            {
                IsSaving = false;
            }
        }

        private void Cancel()
        {
            // 关闭窗口的逻辑在 View 中处理
        }

        private void AddConnection()
        {
            var newConnection = new DatabaseConnection
            {
                Name = $"新连接 {Connections.Count + 1}",
                Server = "localhost",
                Port = 3306,
                Database = "trading_system",
                Username = "root",
                Password = ""
            };

            Connections.Add(newConnection);
            CurrentConnection = newConnection;
        }

        private bool CanRemoveConnection(DatabaseConnection connection)
        {
            return connection != null && Connections.Count > 1;
        }

        private void RemoveConnection(DatabaseConnection connection)
        {
            if (connection == null || !CanRemoveConnection(connection)) return;

            var index = Connections.IndexOf(connection);
            Connections.Remove(connection);

            if (CurrentConnection == connection)
            {
                CurrentConnection = Connections[Math.Min(index, Connections.Count - 1)];
            }
        }

        // 添加关闭窗口的事件
        public event Action<bool>? CloseWindow;
    }
} 