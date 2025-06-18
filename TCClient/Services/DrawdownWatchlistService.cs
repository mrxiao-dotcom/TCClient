using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;

namespace TCClient.Services
{
    /// <summary>
    /// 回撤预警自选列表数据模型
    /// </summary>
    public class DrawdownWatchlistData
    {
        public List<string> LongContracts { get; set; } = new List<string>();
        public List<string> ShortContracts { get; set; } = new List<string>();
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 回撤预警自选列表管理服务
    /// 负责回撤预警窗口中做多和做空合约列表的本地存储和管理
    /// </summary>
    public class DrawdownWatchlistService
    {
        private readonly string _dataDirectory;
        private readonly string _watchlistFile;
        private DrawdownWatchlistData _watchlistData;
        private readonly object _lock = new object();

        public DrawdownWatchlistService()
        {
            // 获取应用程序数据目录
            _dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TCClient");
            _watchlistFile = Path.Combine(_dataDirectory, "drawdown_watchlist.json");
            
            // 确保目录存在
            if (!Directory.Exists(_dataDirectory))
            {
                Directory.CreateDirectory(_dataDirectory);
            }

            // 初始化数据
            _watchlistData = new DrawdownWatchlistData();
        }

        /// <summary>
        /// 获取回撤预警自选列表数据
        /// </summary>
        /// <returns>自选列表数据</returns>
        public async Task<DrawdownWatchlistData> GetWatchlistDataAsync()
        {
            lock (_lock)
            {
                if (_watchlistData.LongContracts.Count > 0 || _watchlistData.ShortContracts.Count > 0)
                {
                    return new DrawdownWatchlistData
                    {
                        LongContracts = new List<string>(_watchlistData.LongContracts),
                        ShortContracts = new List<string>(_watchlistData.ShortContracts),
                        LastUpdated = _watchlistData.LastUpdated
                    };
                }
            }

            // 如果内存中没有数据，从文件加载
            await LoadFromFileAsync();
            
            lock (_lock)
            {
                return new DrawdownWatchlistData
                {
                    LongContracts = new List<string>(_watchlistData.LongContracts),
                    ShortContracts = new List<string>(_watchlistData.ShortContracts),
                    LastUpdated = _watchlistData.LastUpdated
                };
            }
        }

        /// <summary>
        /// 保存回撤预警自选列表数据
        /// </summary>
        /// <param name="longContracts">做多合约列表</param>
        /// <param name="shortContracts">做空合约列表</param>
        public async Task SaveWatchlistDataAsync(List<string> longContracts, List<string> shortContracts)
        {
            if (longContracts == null) longContracts = new List<string>();
            if (shortContracts == null) shortContracts = new List<string>();

            lock (_lock)
            {
                _watchlistData.LongContracts = new List<string>(longContracts);
                _watchlistData.ShortContracts = new List<string>(shortContracts);
                _watchlistData.LastUpdated = DateTime.Now;
            }

            // 保存到文件
            await SaveToFileAsync();
            
            System.Diagnostics.Debug.WriteLine($"回撤预警自选列表已保存: 做多 {longContracts.Count} 个, 做空 {shortContracts.Count} 个");
        }

        /// <summary>
        /// 添加做多合约
        /// </summary>
        /// <param name="contract">合约名称</param>
        public async Task AddLongContractAsync(string contract)
        {
            if (string.IsNullOrWhiteSpace(contract))
            {
                return;
            }

            contract = contract.ToUpper();
            
            lock (_lock)
            {
                if (!_watchlistData.LongContracts.Contains(contract))
                {
                    _watchlistData.LongContracts.Add(contract);
                    _watchlistData.LastUpdated = DateTime.Now;
                }
            }

            await SaveToFileAsync();
            System.Diagnostics.Debug.WriteLine($"已添加做多合约: {contract}");
        }

        /// <summary>
        /// 移除做多合约
        /// </summary>
        /// <param name="contract">合约名称</param>
        public async Task RemoveLongContractAsync(string contract)
        {
            if (string.IsNullOrWhiteSpace(contract))
            {
                return;
            }

            contract = contract.ToUpper();
            
            lock (_lock)
            {
                if (_watchlistData.LongContracts.Remove(contract))
                {
                    _watchlistData.LastUpdated = DateTime.Now;
                }
            }

            await SaveToFileAsync();
            System.Diagnostics.Debug.WriteLine($"已移除做多合约: {contract}");
        }

        /// <summary>
        /// 添加做空合约
        /// </summary>
        /// <param name="contract">合约名称</param>
        public async Task AddShortContractAsync(string contract)
        {
            if (string.IsNullOrWhiteSpace(contract))
            {
                return;
            }

            contract = contract.ToUpper();
            
            lock (_lock)
            {
                if (!_watchlistData.ShortContracts.Contains(contract))
                {
                    _watchlistData.ShortContracts.Add(contract);
                    _watchlistData.LastUpdated = DateTime.Now;
                }
            }

            await SaveToFileAsync();
            System.Diagnostics.Debug.WriteLine($"已添加做空合约: {contract}");
        }

        /// <summary>
        /// 移除做空合约
        /// </summary>
        /// <param name="contract">合约名称</param>
        public async Task RemoveShortContractAsync(string contract)
        {
            if (string.IsNullOrWhiteSpace(contract))
            {
                return;
            }

            contract = contract.ToUpper();
            
            lock (_lock)
            {
                if (_watchlistData.ShortContracts.Remove(contract))
                {
                    _watchlistData.LastUpdated = DateTime.Now;
                }
            }

            await SaveToFileAsync();
            System.Diagnostics.Debug.WriteLine($"已移除做空合约: {contract}");
        }

        /// <summary>
        /// 获取做多合约列表
        /// </summary>
        /// <returns>做多合约列表</returns>
        public async Task<List<string>> GetLongContractsAsync()
        {
            var data = await GetWatchlistDataAsync();
            return data.LongContracts;
        }

        /// <summary>
        /// 获取做空合约列表
        /// </summary>
        /// <returns>做空合约列表</returns>
        public async Task<List<string>> GetShortContractsAsync()
        {
            var data = await GetWatchlistDataAsync();
            return data.ShortContracts;
        }

        /// <summary>
        /// 清空所有自选列表
        /// </summary>
        public async Task ClearAllAsync()
        {
            lock (_lock)
            {
                _watchlistData.LongContracts.Clear();
                _watchlistData.ShortContracts.Clear();
                _watchlistData.LastUpdated = DateTime.Now;
            }

            await SaveToFileAsync();
            System.Diagnostics.Debug.WriteLine("已清空所有回撤预警自选列表");
        }

        /// <summary>
        /// 从文件加载自选列表数据
        /// </summary>
        private async Task LoadFromFileAsync()
        {
            try
            {
                if (!File.Exists(_watchlistFile))
                {
                    System.Diagnostics.Debug.WriteLine("回撤预警自选列表文件不存在，使用空列表");
                    lock (_lock)
                    {
                        _watchlistData = new DrawdownWatchlistData();
                    }
                    return;
                }

                var jsonContent = await File.ReadAllTextAsync(_watchlistFile);
                if (string.IsNullOrWhiteSpace(jsonContent))
                {
                    System.Diagnostics.Debug.WriteLine("回撤预警自选列表文件为空，使用空列表");
                    lock (_lock)
                    {
                        _watchlistData = new DrawdownWatchlistData();
                    }
                    return;
                }

                var data = JsonSerializer.Deserialize<DrawdownWatchlistData>(jsonContent);
                if (data == null)
                {
                    System.Diagnostics.Debug.WriteLine("回撤预警自选列表文件解析失败，使用空列表");
                    lock (_lock)
                    {
                        _watchlistData = new DrawdownWatchlistData();
                    }
                    return;
                }

                lock (_lock)
                {
                    _watchlistData = data;
                }

                System.Diagnostics.Debug.WriteLine($"已加载回撤预警自选列表: 做多 {data.LongContracts.Count} 个, 做空 {data.ShortContracts.Count} 个");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载回撤预警自选列表失败: {ex.Message}");
                lock (_lock)
                {
                    _watchlistData = new DrawdownWatchlistData();
                }
            }
        }

        /// <summary>
        /// 保存自选列表数据到文件
        /// </summary>
        private async Task SaveToFileAsync()
        {
            try
            {
                DrawdownWatchlistData dataToSave;
                lock (_lock)
                {
                    dataToSave = new DrawdownWatchlistData
                    {
                        LongContracts = new List<string>(_watchlistData.LongContracts),
                        ShortContracts = new List<string>(_watchlistData.ShortContracts),
                        LastUpdated = _watchlistData.LastUpdated
                    };
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                var jsonContent = JsonSerializer.Serialize(dataToSave, options);
                await File.WriteAllTextAsync(_watchlistFile, jsonContent);
                
                System.Diagnostics.Debug.WriteLine($"回撤预警自选列表已保存到: {_watchlistFile}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存回撤预警自选列表失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 获取自选列表文件路径
        /// </summary>
        /// <returns>文件路径</returns>
        public string GetWatchlistFilePath()
        {
            return _watchlistFile;
        }
    }
} 