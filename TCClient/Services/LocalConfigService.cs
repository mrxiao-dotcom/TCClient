using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace TCClient.Services
{
    public class DatabaseConnection
    {
        public string Name { get; set; }
        public string Server { get; set; }
        public int Port { get; set; }
        public string Database { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }

        public static DatabaseConnection Default => new DatabaseConnection
        {
            Name = "远程MySQL服务器",
            Server = "154.23.181.75",
            Port = 3306,
            Database = "ordermanager",
            Username = "root",
            Password = "Xj774913@"
        };
    }

    public class UserCredentials
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public bool RememberPassword { get; set; }
    }

    public class LocalConfigService
    {
        private readonly string _configPath;
        private readonly string _credentialsPath;
        private readonly string _apiConfigPath;
        private readonly byte[] _key = Encoding.UTF8.GetBytes("YourSecretKey123"); // 16字节密钥
        private List<DatabaseConnection> _connections;

        public LocalConfigService()
        {
            // 获取应用程序根目录
            string appRootPath = AppDomain.CurrentDomain.BaseDirectory;
            
            // 设置配置文件路径
            _configPath = Path.Combine(appRootPath, "database_config.json");
            _credentialsPath = Path.Combine(appRootPath, "credentials.json");
            _apiConfigPath = Path.Combine(appRootPath, "api_config.json");

            _connections = new List<DatabaseConnection>();
            
            // 如果配置文件不存在，创建默认配置
            if (!File.Exists(_configPath))
            {
                _connections.Add(DatabaseConnection.Default);
                SaveDatabaseConnections(_connections).Wait();
            }
            else
            {
                // 同步加载配置，但避免使用 Wait()
                try
                {
                    var json = File.ReadAllText(_configPath);
                    _connections = JsonSerializer.Deserialize<List<DatabaseConnection>>(json) ?? new List<DatabaseConnection>();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"加载数据库连接配置失败：{ex.Message}");
                    _connections = new List<DatabaseConnection>();
                    _connections.Add(DatabaseConnection.Default);
                    SaveDatabaseConnections(_connections).Wait();
                }
            }
        }

        public async Task SaveDatabaseConnections(List<DatabaseConnection> connections)
        {
            try
            {
                var json = JsonSerializer.Serialize(connections, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_configPath, json);
                _connections = connections;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存数据库连接配置失败：{ex.Message}");
                throw;
            }
        }

        public async Task<List<DatabaseConnection>> LoadDatabaseConnections()
        {
            if (File.Exists(_configPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_configPath);
                    _connections = JsonSerializer.Deserialize<List<DatabaseConnection>>(json) ?? new List<DatabaseConnection>();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"加载数据库连接配置失败：{ex.Message}");
                    _connections = new List<DatabaseConnection>();
                }
            }
            return _connections;
        }

        public async Task SaveUserCredentials(UserCredentials credentials)
        {
            if (!credentials.RememberPassword)
            {
                if (File.Exists(_credentialsPath))
                    File.Delete(_credentialsPath);
                return;
            }

            // 加密密码
            string encryptedPassword = EncryptString(credentials.Password);
            credentials.Password = encryptedPassword;

            string json = JsonSerializer.Serialize(credentials);
            await File.WriteAllTextAsync(_credentialsPath, json);
        }

        public async Task<UserCredentials> LoadUserCredentials()
        {
            if (!File.Exists(_credentialsPath))
                return null;

            string json = await File.ReadAllTextAsync(_credentialsPath);
            var credentials = JsonSerializer.Deserialize<UserCredentials>(json);
            
            if (credentials != null)
            {
                // 解密密码
                credentials.Password = DecryptString(credentials.Password);
            }

            return credentials;
        }

        private string EncryptString(string text)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = _key;
                aes.GenerateIV();

                using (MemoryStream ms = new MemoryStream())
                {
                    ms.Write(aes.IV, 0, aes.IV.Length);

                    using (CryptoStream cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                    using (StreamWriter sw = new StreamWriter(cs))
                    {
                        sw.Write(text);
                    }

                    return Convert.ToBase64String(ms.ToArray());
                }
            }
        }

        private string DecryptString(string cipherText)
        {
            byte[] fullCipher = Convert.FromBase64String(cipherText);

            using (Aes aes = Aes.Create())
            {
                aes.Key = _key;

                byte[] iv = new byte[aes.IV.Length];
                Array.Copy(fullCipher, 0, iv, 0, iv.Length);

                using (MemoryStream ms = new MemoryStream())
                {
                    using (CryptoStream cs = new CryptoStream(ms, aes.CreateDecryptor(aes.Key, iv), CryptoStreamMode.Write))
                    {
                        cs.Write(fullCipher, iv.Length, fullCipher.Length - iv.Length);
                    }

                    return Encoding.UTF8.GetString(ms.ToArray());
                }
            }
        }

        public string GetCurrentConnectionString()
        {
            if (_connections.Count == 0)
            {
                throw new InvalidOperationException("没有可用的数据库连接配置");
            }

            var connection = _connections[0];
            return $"Server={connection.Server};Port={connection.Port};Database={connection.Database};Uid={connection.Username};Pwd={connection.Password};";
        }

        public async Task SaveApiConfig(string apiKey, string apiSecret)
        {
            try
            {
                var config = new
                {
                    ApiKey = EncryptString(apiKey),
                    ApiSecret = EncryptString(apiSecret)
                };

                var json = JsonSerializer.Serialize(config);
                await File.WriteAllTextAsync(_apiConfigPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存API配置失败：{ex.Message}");
                throw;
            }
        }

        public async Task<(string ApiKey, string ApiSecret)> LoadApiConfig()
        {
            if (!File.Exists(_apiConfigPath))
            {
                return (string.Empty, string.Empty);
            }

            try
            {
                var json = await File.ReadAllTextAsync(_apiConfigPath);
                var config = JsonSerializer.Deserialize<ApiConfig>(json);

                if (config == null)
                {
                    return (string.Empty, string.Empty);
                }

                return (
                    DecryptString(config.ApiKey),
                    DecryptString(config.ApiSecret)
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载API配置失败：{ex.Message}");
                return (string.Empty, string.Empty);
            }
        }

        public string GetApiKey()
        {
            var config = LoadApiConfig().GetAwaiter().GetResult();
            return config.ApiKey;
        }

        public string GetApiSecret()
        {
            var config = LoadApiConfig().GetAwaiter().GetResult();
            return config.ApiSecret;
        }

        private class ApiConfig
        {
            public string ApiKey { get; set; }
            public string ApiSecret { get; set; }
        }
    }
} 