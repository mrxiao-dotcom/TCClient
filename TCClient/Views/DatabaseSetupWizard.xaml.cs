using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using TCClient.Services;
using Microsoft.Extensions.DependencyInjection;

namespace TCClient.Views
{
    public partial class DatabaseSetupWizard : Window
    {
        private readonly IDatabaseService _databaseService;
        private readonly LocalConfigService _configService;
        private bool _connectionTested = false;

        public DatabaseSetupWizard()
        {
            InitializeComponent();
            
            // 从依赖注入容器获取服务
            var serviceProvider = ((App)Application.Current).Services;
            _databaseService = serviceProvider.GetRequiredService<IDatabaseService>();
            _configService = serviceProvider.GetRequiredService<LocalConfigService>();
            
            LoadCurrentConfiguration();
        }

        private async void LoadCurrentConfiguration()
        {
            try
            {
                var connections = await _configService.LoadDatabaseConnections();
                if (connections.Count > 0)
                {
                    var conn = connections[0];
                    ServerTextBox.Text = conn.Server;
                    PortTextBox.Text = conn.Port.ToString();
                    DatabaseTextBox.Text = conn.Database;
                    UsernameTextBox.Text = conn.Username;
                    PasswordBox.Password = conn.Password;
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"加载配置失败: {ex.Message}", false);
            }
        }

        private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            TestConnectionButton.IsEnabled = false;
            UpdateStatus("正在测试连接...", null);

            try
            {
                // 验证输入
                if (string.IsNullOrWhiteSpace(ServerTextBox.Text))
                {
                    UpdateStatus("请输入服务器地址", false);
                    return;
                }

                if (!int.TryParse(PortTextBox.Text, out int port) || port <= 0 || port > 65535)
                {
                    UpdateStatus("请输入有效的端口号 (1-65535)", false);
                    return;
                }

                if (string.IsNullOrWhiteSpace(DatabaseTextBox.Text))
                {
                    UpdateStatus("请输入数据库名称", false);
                    return;
                }

                if (string.IsNullOrWhiteSpace(UsernameTextBox.Text))
                {
                    UpdateStatus("请输入用户名", false);
                    return;
                }

                // 构建连接字符串
                var connectionString = $"Server={ServerTextBox.Text};Port={port};Database={DatabaseTextBox.Text};Uid={UsernameTextBox.Text};Pwd={PasswordBox.Password};";

                // 测试连接
                bool success = await _databaseService.ConnectAsync(connectionString);
                
                if (success)
                {
                    _connectionTested = true;
                    SaveButton.IsEnabled = true;
                    UpdateStatus("✓ 数据库连接成功！可以保存配置。", true);
                }
                else
                {
                    _connectionTested = false;
                    SaveButton.IsEnabled = false;
                    UpdateStatus("✗ 数据库连接失败，请检查配置信息和网络连接。", false);
                }
            }
            catch (Exception ex)
            {
                _connectionTested = false;
                SaveButton.IsEnabled = false;
                UpdateStatus($"✗ 连接测试出错: {ex.Message}", false);
            }
            finally
            {
                TestConnectionButton.IsEnabled = true;
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_connectionTested)
            {
                MessageBox.Show("请先测试连接成功后再保存配置", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var connection = new DatabaseConnection
                {
                    Name = "主数据库",
                    Server = ServerTextBox.Text.Trim(),
                    Port = int.Parse(PortTextBox.Text),
                    Database = DatabaseTextBox.Text.Trim(),
                    Username = UsernameTextBox.Text.Trim(),
                    Password = PasswordBox.Password
                };

                var connections = new List<DatabaseConnection> { connection };
                await _configService.SaveDatabaseConnections(connections);

                MessageBox.Show("数据库配置已保存成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存配置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void UpdateStatus(string message, bool? success)
        {
            StatusTextBlock.Text = message;
            
            if (success == true)
            {
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
            }
            else if (success == false)
            {
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            }
            else
            {
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
            }
        }
    }
} 