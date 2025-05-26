using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using TCClient.Utils;

namespace TCClient.Services
{
    /// <summary>
    /// 自选合约管理服务
    /// 负责自选合约列表的本地存储和管理
    /// </summary>
    public class FavoriteContractsService
    {
        private readonly string _dataDirectory;
        private readonly string _favoriteContractsFile;
        private List<string> _favoriteContracts;
        private readonly object _lock = new object();

        // 默认自选合约列表
        private readonly List<string> _defaultContracts = new List<string>
        {
            "BTC", "ETH", "BNB", "ADA", "XRP", "SOL", "DOT", "DOGE", "AVAX", "MATIC",
            "LINK", "UNI", "LTC", "BCH", "ATOM", "FIL", "TRX", "ETC", "XLM", "VET"
        };

        public FavoriteContractsService()
        {
            // 获取应用程序数据目录
            _dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TCClient");
            _favoriteContractsFile = Path.Combine(_dataDirectory, "favorite_contracts.json");
            
            // 确保目录存在
            if (!Directory.Exists(_dataDirectory))
            {
                Directory.CreateDirectory(_dataDirectory);
            }

            // 初始化自选合约列表
            _favoriteContracts = new List<string>();
        }

        /// <summary>
        /// 获取自选合约列表
        /// </summary>
        /// <returns>自选合约列表</returns>
        public async Task<List<string>> GetFavoriteContractsAsync()
        {
            lock (_lock)
            {
                if (_favoriteContracts.Count > 0)
                {
                    return new List<string>(_favoriteContracts);
                }
            }

            // 如果内存中没有数据，从文件加载
            await LoadFromFileAsync();
            
            lock (_lock)
            {
                return new List<string>(_favoriteContracts);
            }
        }

        /// <summary>
        /// 设置自选合约列表
        /// </summary>
        /// <param name="contracts">合约列表</param>
        public async Task SetFavoriteContractsAsync(List<string> contracts)
        {
            if (contracts == null)
            {
                contracts = new List<string>();
            }

            lock (_lock)
            {
                _favoriteContracts = new List<string>(contracts);
            }

            // 保存到文件
            await SaveToFileAsync();
            
            LogManager.Log("FavoriteContractsService", $"自选合约列表已更新，共 {contracts.Count} 个合约");
        }

        /// <summary>
        /// 添加自选合约
        /// </summary>
        /// <param name="contract">合约名称</param>
        public async Task AddFavoriteContractAsync(string contract)
        {
            if (string.IsNullOrWhiteSpace(contract))
            {
                return;
            }

            contract = contract.ToUpper();
            
            lock (_lock)
            {
                if (!_favoriteContracts.Contains(contract))
                {
                    _favoriteContracts.Add(contract);
                }
            }

            await SaveToFileAsync();
            LogManager.Log("FavoriteContractsService", $"已添加自选合约: {contract}");
        }

        /// <summary>
        /// 移除自选合约
        /// </summary>
        /// <param name="contract">合约名称</param>
        public async Task RemoveFavoriteContractAsync(string contract)
        {
            if (string.IsNullOrWhiteSpace(contract))
            {
                return;
            }

            contract = contract.ToUpper();
            
            lock (_lock)
            {
                _favoriteContracts.Remove(contract);
            }

            await SaveToFileAsync();
            LogManager.Log("FavoriteContractsService", $"已移除自选合约: {contract}");
        }

        /// <summary>
        /// 检查是否为自选合约
        /// </summary>
        /// <param name="contract">合约名称</param>
        /// <returns>是否为自选合约</returns>
        public async Task<bool> IsFavoriteContractAsync(string contract)
        {
            if (string.IsNullOrWhiteSpace(contract))
            {
                return false;
            }

            var favorites = await GetFavoriteContractsAsync();
            return favorites.Contains(contract.ToUpper());
        }

        /// <summary>
        /// 重置为默认自选合约列表
        /// </summary>
        public async Task ResetToDefaultAsync()
        {
            await SetFavoriteContractsAsync(new List<string>(_defaultContracts));
            LogManager.Log("FavoriteContractsService", "已重置为默认自选合约列表");
        }

        /// <summary>
        /// 从文件加载自选合约列表
        /// </summary>
        private async Task LoadFromFileAsync()
        {
            try
            {
                if (!File.Exists(_favoriteContractsFile))
                {
                    LogManager.Log("FavoriteContractsService", "自选合约文件不存在，使用默认列表");
                    // 文件不存在，使用默认列表并保存
                    lock (_lock)
                    {
                        _favoriteContracts = new List<string>(_defaultContracts);
                    }
                    await SaveToFileAsync();
                    return;
                }

                var jsonContent = await File.ReadAllTextAsync(_favoriteContractsFile);
                if (string.IsNullOrWhiteSpace(jsonContent))
                {
                    LogManager.Log("FavoriteContractsService", "自选合约文件为空，使用默认列表");
                    lock (_lock)
                    {
                        _favoriteContracts = new List<string>(_defaultContracts);
                    }
                    await SaveToFileAsync();
                    return;
                }

                var contracts = JsonSerializer.Deserialize<List<string>>(jsonContent);
                if (contracts == null || contracts.Count == 0)
                {
                    LogManager.Log("FavoriteContractsService", "自选合约文件解析为空，使用默认列表");
                    lock (_lock)
                    {
                        _favoriteContracts = new List<string>(_defaultContracts);
                    }
                    await SaveToFileAsync();
                    return;
                }

                lock (_lock)
                {
                    _favoriteContracts = contracts;
                }

                LogManager.Log("FavoriteContractsService", $"从文件加载自选合约列表成功，共 {contracts.Count} 个合约");
            }
            catch (Exception ex)
            {
                LogManager.LogException("FavoriteContractsService", ex, "加载自选合约文件失败，使用默认列表");
                lock (_lock)
                {
                    _favoriteContracts = new List<string>(_defaultContracts);
                }
                await SaveToFileAsync();
            }
        }

        /// <summary>
        /// 保存自选合约列表到文件
        /// </summary>
        private async Task SaveToFileAsync()
        {
            try
            {
                List<string> contractsToSave;
                lock (_lock)
                {
                    contractsToSave = new List<string>(_favoriteContracts);
                }

                var jsonContent = JsonSerializer.Serialize(contractsToSave, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(_favoriteContractsFile, jsonContent);
                LogManager.Log("FavoriteContractsService", $"自选合约列表已保存到文件: {_favoriteContractsFile}");
            }
            catch (Exception ex)
            {
                LogManager.LogException("FavoriteContractsService", ex, "保存自选合约文件失败");
            }
        }

        /// <summary>
        /// 获取默认自选合约列表
        /// </summary>
        /// <returns>默认自选合约列表</returns>
        public List<string> GetDefaultContracts()
        {
            return new List<string>(_defaultContracts);
        }

        /// <summary>
        /// 获取自选合约文件路径
        /// </summary>
        /// <returns>文件路径</returns>
        public string GetFavoriteContractsFilePath()
        {
            return _favoriteContractsFile;
        }
    }
} 