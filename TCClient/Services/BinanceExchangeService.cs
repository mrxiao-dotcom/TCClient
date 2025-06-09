#pragma warning disable CS0160 // 上一个 catch 子句已经捕获了此类型或超类型的所有异常
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TCClient.Exceptions;
using TCClient.Models;
using TCClient.Utils;

namespace TCClient.Services
{
    public class BinanceExchangeService : IExchangeService, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _apiKey;
        private readonly string _apiSecret;
        private bool _isDisposed;
        private const int MaxRetries = 3;
        private const int RetryDelayMs = 1000;
        private const int RequestTimeoutMs = 30000; // 增加到30秒
        private CancellationTokenSource _globalCts;
        private readonly ILogger<BinanceExchangeService> _logger;
        private readonly SemaphoreSlim _requestSemaphore = new SemaphoreSlim(1, 1);
        private readonly Random _random = new Random();
        private const int BASE_TIMEOUT = 30; // 增加基础超时时间到30秒
        private const int MAX_TIMEOUT = 60; // 增加最大超时时间到60秒
        
        // 备用API端点配置
        private static readonly string[] _alternativeBaseUrls = {
            "https://fapi.binance.com",
            "https://fapi1.binance.com",
            "https://fapi2.binance.com",
            "https://fapi3.binance.com"
        };
        private int _currentBaseUrlIndex = 0;
        
        // 请求频率控制
        private static readonly Dictionary<string, DateTime> _lastRequestTimes = new Dictionary<string, DateTime>();
        private static readonly object _rateLimitLock = new object();
        private const int MIN_REQUEST_INTERVAL_MS = 250; // 最小请求间隔250毫秒（每分钟最多240次请求）
        private const int TICKER_REQUEST_INTERVAL_MS = 1000; // 减少行情数据请求间隔到1秒
        
        // 缓存机制
        private static List<TickerInfo> _cachedTickers = null;
        private static DateTime _lastTickerCacheTime = DateTime.MinValue;
        private const int TICKER_CACHE_DURATION_MS = 10000; // 增加行情数据缓存到10秒

        public BinanceExchangeService(ILogger<BinanceExchangeService> logger, string apiKey = null, string apiSecret = null)
        {
            _logger = logger;
            _apiKey = apiKey;
            _apiSecret = apiSecret;
            _baseUrl = _alternativeBaseUrls[0]; // 默认使用第一个端点
            
            // 配置HttpClient with proxy support
            var handler = CreateHttpHandler();

            _httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(_baseUrl),
                Timeout = TimeSpan.FromSeconds(BASE_TIMEOUT)
            };

            // 设置默认请求头
            _httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "TCClient/1.0");

            if (!string.IsNullOrEmpty(_apiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("X-MBX-APIKEY", _apiKey);
            }

            Utils.LogManager.Log("BinanceExchange", "=== 初始化币安交易所服务 ===");
            Utils.LogManager.Log("BinanceExchange", $"API密钥: {(string.IsNullOrEmpty(_apiKey) ? "未设置" : "已设置")}");
            Utils.LogManager.Log("BinanceExchange", $"当前API端点: {_baseUrl}");
            Utils.LogManager.Log("BinanceExchange", "HTTP客户端初始化完成");
            _globalCts = new CancellationTokenSource();
        }

        private SocketsHttpHandler CreateHttpHandler()
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(15), // 增加连接池生命周期
                MaxConnectionsPerServer = 20, // 增加每个服务器的最大连接数
                EnableMultipleHttp2Connections = true, // 启用多个HTTP/2连接
                KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests, // 保持连接活跃
                KeepAlivePingDelay = TimeSpan.FromSeconds(60), // 增加保持连接ping延迟
                KeepAlivePingTimeout = TimeSpan.FromSeconds(10), // 增加保持连接ping超时
                ConnectTimeout = TimeSpan.FromSeconds(15), // 增加连接超时
                ResponseDrainTimeout = TimeSpan.FromSeconds(10), // 增加响应排空超时
                RequestHeaderEncodingSelector = (name, request) => System.Text.Encoding.UTF8
            };

            // 检查是否需要配置代理
            var proxyUrl = Environment.GetEnvironmentVariable("HTTP_PROXY") ?? Environment.GetEnvironmentVariable("HTTPS_PROXY");
            if (!string.IsNullOrEmpty(proxyUrl))
            {
                Utils.LogManager.Log("BinanceExchange", $"检测到代理配置: {proxyUrl}");
                try
                {
                    var proxy = new WebProxy(proxyUrl);
                    handler.Proxy = proxy;
                    handler.UseProxy = true;
                    Utils.LogManager.Log("BinanceExchange", "代理配置成功");
                }
                catch (Exception ex)
                {
                    Utils.LogManager.Log("BinanceExchange", $"代理配置失败: {ex.Message}");
                }
            }
            else
            {
                // 如果没有代理配置，尝试使用系统代理
                handler.UseProxy = true;
                handler.Proxy = WebRequest.GetSystemWebProxy();
            }

            return handler;
        }

        private async Task<bool> TryNextBaseUrl()
        {
            _currentBaseUrlIndex = (_currentBaseUrlIndex + 1) % _alternativeBaseUrls.Length;
            var newBaseUrl = _alternativeBaseUrls[_currentBaseUrlIndex];
            
            Utils.LogManager.Log("BinanceExchange", $"切换到备用API端点: {newBaseUrl}");
            
            // 测试新端点是否可用
            try
            {
                var handler = CreateHttpHandler();
                using var testHttpClient = new HttpClient(handler)
                {
                    BaseAddress = new Uri(newBaseUrl),
                    Timeout = TimeSpan.FromSeconds(BASE_TIMEOUT)
                };
                
                // 设置默认请求头
                testHttpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                testHttpClient.DefaultRequestHeaders.Add("User-Agent", "TCClient/1.0");

                if (!string.IsNullOrEmpty(_apiKey))
                {
                    testHttpClient.DefaultRequestHeaders.Add("X-MBX-APIKEY", _apiKey);
                }

                using var testResponse = await testHttpClient.GetAsync("/fapi/v1/ping", CancellationToken.None);
                if (testResponse.IsSuccessStatusCode)
                {
                    Utils.LogManager.Log("BinanceExchange", $"备用端点 {newBaseUrl} 连接成功");
                    
                    // 更新当前HttpClient
                    _httpClient?.Dispose();
                    var newHandler = CreateHttpHandler();
                    var newHttpClient = new HttpClient(newHandler)
                    {
                        BaseAddress = new Uri(newBaseUrl),
                        Timeout = TimeSpan.FromSeconds(BASE_TIMEOUT)
                    };
                    
                    // 设置默认请求头
                    newHttpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                    newHttpClient.DefaultRequestHeaders.Add("User-Agent", "TCClient/1.0");

                    if (!string.IsNullOrEmpty(_apiKey))
                    {
                        newHttpClient.DefaultRequestHeaders.Add("X-MBX-APIKEY", _apiKey);
                    }
                    
                    // 使用反射更新私有字段
                    var httpClientField = typeof(BinanceExchangeService).GetField("_httpClient", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    httpClientField?.SetValue(this, newHttpClient);
                    
                    return true;
                }
            }
            catch (Exception ex)
            {
                Utils.LogManager.Log("BinanceExchange", $"备用端点 {newBaseUrl} 连接失败: {ex.Message}");
            }
            
            return false;
        }

        private string GenerateSignature(string queryString)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_apiSecret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(queryString));
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }

        /// <summary>
        /// 请求频率控制
        /// </summary>
        private async Task RateLimitAsync(string endpoint)
        {
            int delayMs = 0;
            
            lock (_rateLimitLock)
            {
                var now = DateTime.Now;
                var intervalMs = endpoint.Contains("ticker") ? TICKER_REQUEST_INTERVAL_MS : MIN_REQUEST_INTERVAL_MS;
                
                if (_lastRequestTimes.TryGetValue(endpoint, out var lastTime))
                {
                    var elapsed = (now - lastTime).TotalMilliseconds;
                    if (elapsed < intervalMs)
                    {
                        delayMs = (int)(intervalMs - elapsed);
                    }
                }
                
                _lastRequestTimes[endpoint] = DateTime.Now;
            }
            
            if (delayMs > 0)
            {
                Utils.LogManager.Log("BinanceExchange", $"请求频率控制: 等待 {delayMs}ms");
                await Task.Delay(delayMs);
            }
        }

        private async Task<T> SendRequestAsync<T>(string endpoint, HttpMethod method, Dictionary<string, string> parameters = null, bool requireSignature = false, int retryCount = 0)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(BinanceExchangeService));
            }

            // 应用请求频率控制
            await RateLimitAsync(endpoint);

            // 为每个请求创建独立的CancellationToken，避免全局取消影响其他请求
            using var requestCts = new CancellationTokenSource(TimeSpan.FromSeconds(BASE_TIMEOUT + retryCount * 10)); // 增加重试时的超时时间
            var requestToken = requestCts.Token;

            try
            {
                var url = $"{_baseUrl}{endpoint}";
                var queryString = string.Empty;

                if (parameters != null && parameters.Any())
                {
                    if (requireSignature)
                    {
                        parameters["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
                        parameters["recvWindow"] = "10000"; // 增加接收窗口时间
                    }
                    queryString = string.Join("&", parameters.Select(p => $"{p.Key}={p.Value}"));
                }

                if (requireSignature && !string.IsNullOrEmpty(_apiSecret))
                {
                    var signature = GenerateSignature(queryString);
                    queryString = $"{queryString}&signature={signature}";
                }

                if (!string.IsNullOrEmpty(queryString))
                {
                    url = $"{url}?{queryString}";
                }

                var request = new HttpRequestMessage(method, url);
                Utils.LogManager.Log("BinanceExchange", $"发送请求: {method} {endpoint} (重试: {retryCount}/{MaxRetries})");
                if (!string.IsNullOrEmpty(queryString) && !requireSignature)
                {
                    Utils.LogManager.Log("BinanceExchange", $"请求参数: {queryString}");
                }

                try
                {
                    // 使用独立的CancellationToken，而不是全局的
                    using var response = await _httpClient.SendAsync(request, requestToken);
                    var content = await response.Content.ReadAsStringAsync(requestToken);
                    
                    // 记录原始响应内容
                    Utils.LogManager.Log("BinanceExchange", $"API响应状态码: {response.StatusCode}");
                    if (!string.IsNullOrEmpty(content))
                    {
                        // 只记录前500个字符，避免日志过长
                        var logContent = content.Length > 500 ? content.Substring(0, 500) + "..." : content;
                        Utils.LogManager.Log("BinanceExchange", $"API响应内容: {logContent}");
                    }
                    
                    if (response.IsSuccessStatusCode)
                    {
                        Utils.LogManager.Log("BinanceExchange", "请求成功，开始解析响应");
                        
                        // 检查内容是否为空
                        if (string.IsNullOrEmpty(content))
                        {
                            Utils.LogManager.Log("BinanceExchange", "警告: 响应内容为空");
                            return default;
                        }
                        
                        try
                        {
                            // 设置JSON反序列化选项
                            var options = new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true, // 忽略属性名大小写
                                AllowTrailingCommas = true, // 允许尾随逗号
                                ReadCommentHandling = JsonCommentHandling.Skip, // 跳过注释
                                NumberHandling = JsonNumberHandling.AllowReadingFromString // 允许从字符串读取数字
                            };

                            var result = JsonSerializer.Deserialize<T>(content, options);
                            
                            // 验证反序列化结果
                            if (result == null)
                            {
                                Utils.LogManager.Log("BinanceExchange", "警告: 反序列化结果为null");
                                return default;
                            }
                            
                            // 如果是列表类型，检查是否为空
                            if (typeof(IEnumerable<object>).IsAssignableFrom(typeof(T)) && 
                                !typeof(string).IsAssignableFrom(typeof(T)))
                            {
                                var enumerable = result as IEnumerable<object>;
                                if (enumerable != null)
                                {
                                    var count = enumerable.Count();
                                    Utils.LogManager.Log("BinanceExchange", $"反序列化结果包含 {count} 条记录");
                                    
                                    // 如果是空列表，记录警告
                                    if (count == 0)
                                    {
                                        Utils.LogManager.Log("BinanceExchange", "警告: 反序列化结果为空列表");
                                    }
                                }
                            }
                            
                            Utils.LogManager.Log("BinanceExchange", "响应解析成功");
                            return result;
                        }
                        catch (JsonException ex)
                        {
                            Utils.LogManager.Log("BinanceExchange", $"JSON解析错误: {ex.Message}");
                            Utils.LogManager.Log("BinanceExchange", $"错误位置: 行 {ex.LineNumber}, 列 {ex.BytePositionInLine}");
                            Utils.LogManager.Log("BinanceExchange", $"原始响应内容: {content}");
                            throw new BinanceApiException("JSON解析错误", "解析失败", ex.Message);
                        }
                    }
                    else
                    {
                        Utils.LogManager.Log("BinanceExchange", $"请求失败 - 状态码: {response.StatusCode}, 内容: {content}");
                        
                        // 特殊处理地理位置限制错误
                        if (response.StatusCode == HttpStatusCode.UnavailableForLegalReasons)
                        {
                            Utils.LogManager.Log("BinanceExchange", "检测到地理位置限制错误，尝试切换备用API端点");
                            
                            if (retryCount < MaxRetries)
                            {
                                // 尝试切换到下一个备用端点
                                if (await TryNextBaseUrl())
                                {
                                    Utils.LogManager.Log("BinanceExchange", "成功切换到备用端点，重新发送请求");
                                    return await SendRequestAsync<T>(endpoint, method, parameters, requireSignature, retryCount + 1);
                                }
                                else
                                {
                                    Utils.LogManager.Log("BinanceExchange", "备用端点也无法访问，建议配置代理服务器");
                                }
                            }
                            
                            throw new HttpRequestException($"地理位置限制错误，所有API端点均无法访问。建议配置代理服务器。错误详情: {content}");
                        }
                        
                        // 特殊处理429错误（频率限制）
                        if (response.StatusCode == HttpStatusCode.TooManyRequests)
                        {
                            if (retryCount < MaxRetries)
                            {
                                var delayMs = (retryCount + 1) * 5000; // 5秒、10秒、15秒的递增延迟
                                Utils.LogManager.Log("BinanceExchange", $"遇到频率限制，等待 {delayMs}ms 后重试 ({retryCount + 1}/{MaxRetries})");
                                await Task.Delay(delayMs, CancellationToken.None); // 使用None避免取消
                                return await SendRequestAsync<T>(endpoint, method, parameters, requireSignature, retryCount + 1);
                            }
                            else
                            {
                                Utils.LogManager.Log("BinanceExchange", "频率限制重试次数已用完，建议使用WebSocket获取实时数据");
                                throw new HttpRequestException($"API频率限制: {content}");
                            }
                        }
                        
                        throw new HttpRequestException($"请求失败: {response.StatusCode} - {content}");
                    }
                }
                catch (TaskCanceledException ex) when (ex.CancellationToken == requestToken && retryCount < MaxRetries)
                {
                    if (_isDisposed) 
                    {
                        Utils.LogManager.Log("BinanceExchange", "请求被取消 - 服务已释放");
                        throw new OperationCanceledException("交易所服务已释放");
                    }
                    
                    Utils.LogManager.Log("BinanceExchange", $"请求超时 (重试 {retryCount + 1}/{MaxRetries}): {ex.Message}");
                    Utils.LogManager.Log("BinanceExchange", $"异常堆栈: {ex.StackTrace}");
                    
                    // 尝试切换到下一个API端点
                    if (await TryNextBaseUrl())
                    {
                        Utils.LogManager.Log("BinanceExchange", "已切换到备用API端点");
                    }
                    
                    var delayMs = RetryDelayMs * (retryCount + 1);
                    Utils.LogManager.Log("BinanceExchange", $"等待 {delayMs}ms 后重试");
                    await Task.Delay(delayMs, CancellationToken.None); // 使用None避免取消
                    return await SendRequestAsync<T>(endpoint, method, parameters, requireSignature, retryCount + 1);
                }
                catch (TaskCanceledException ex) when (ex.CancellationToken == requestToken)
                {
                    Utils.LogManager.Log("BinanceExchange", $"请求最终超时，已达到最大重试次数: {ex.Message}");
                    Utils.LogManager.Log("BinanceExchange", $"异常详细信息: {ex}");
                    
                    // 记录详细的网络连接问题信息
                    Utils.LogManager.Log("BinanceExchange", "网络连接问题可能原因:");
                    Utils.LogManager.Log("BinanceExchange", "1. 网络连接不稳定");
                    Utils.LogManager.Log("BinanceExchange", "2. DNS解析问题");
                    Utils.LogManager.Log("BinanceExchange", "3. 防火墙或代理设置问题");
                    Utils.LogManager.Log("BinanceExchange", "4. Binance API服务器响应慢");
                    
                    // 不抛出异常，而是返回默认值，让上层处理
                    Utils.LogManager.Log("BinanceExchange", "返回默认值，让上层代码处理网络问题");
                    return default;
                }
            }
            catch (HttpRequestException ex) when (retryCount < MaxRetries)
            {
                Utils.LogManager.Log("BinanceExchange", $"网络请求失败 (重试 {retryCount + 1}/{MaxRetries}): {ex.Message}");
                var delayMs = RetryDelayMs * (retryCount + 1);
                await Task.Delay(delayMs, CancellationToken.None); // 使用None避免取消
                return await SendRequestAsync<T>(endpoint, method, parameters, requireSignature, retryCount + 1);
            }
            catch (OperationCanceledException) when (_isDisposed)
            {
                Utils.LogManager.Log("BinanceExchange", "请求被取消 - 服务已释放");
                throw new OperationCanceledException("交易所服务已释放");
            }
            catch (Exception ex)
            {
                Utils.LogManager.Log("BinanceExchange", $"请求异常: {ex.Message}, 类型: {ex.GetType().Name}");
                Utils.LogManager.Log("BinanceExchange", $"异常堆栈: {ex.StackTrace}");
                throw;
            }
        }

        public async Task<List<KLineData>> GetKLineDataAsync(string symbol, string interval, int limit)
        {
            try
            {
                string formattedSymbol = symbol.ToUpper();
                if (!formattedSymbol.EndsWith("USDT"))
                {
                    formattedSymbol = $"{formattedSymbol}USDT";
                }
                Utils.LogManager.Log("BinanceExchange", $"格式化后的交易对名称: {formattedSymbol}");

                var parameters = new Dictionary<string, string>
                {
                    { "symbol", formattedSymbol },
                    { "interval", interval },
                    { "limit", limit.ToString() }
                };

                var result = await SendRequestAsync<List<List<JsonElement>>>("/fapi/v1/klines", HttpMethod.Get, parameters);
                
                // 检查结果是否为null
                if (result == null)
                {
                    Utils.LogManager.Log("BinanceExchange", "获取K线数据失败: API返回了null");
                    return new List<KLineData>();
                }
                
                var klines = new List<KLineData>();
                foreach (var k in result)
                {
                    try
                    {
                        // 安全地获取每个值，添加异常处理
                        long time = 0;
                        double open = 0, high = 0, low = 0, close = 0, volume = 0, quoteVolume = 0;
                        
                        try { time = k[0].GetInt64(); } catch { time = DateTimeOffset.Now.ToUnixTimeMilliseconds(); }
                        
                        try { open = double.Parse(k[1].GetString() ?? "0"); } catch { }
                        try { high = double.Parse(k[2].GetString() ?? "0"); } catch { }
                        try { low = double.Parse(k[3].GetString() ?? "0"); } catch { }
                        try { close = double.Parse(k[4].GetString() ?? "0"); } catch { }
                        try { volume = double.Parse(k[5].GetString() ?? "0"); } catch { }
                        try { quoteVolume = double.Parse(k[7].GetString() ?? "0"); } catch { }
                        
                        // 改进时间处理，确保日线周期的时间对齐
                        var klineTime = DateTimeOffset.FromUnixTimeMilliseconds(time).DateTime;
                        
                        // 对于日线周期，确保时间对齐到当天的0点（本地时间）
                        if (interval == "1d")
                        {
                            klineTime = klineTime.Date; // 只保留日期部分，时间设为0点
                            Utils.LogManager.Log("BinanceExchange", $"日线时间对齐: 原始时间={DateTimeOffset.FromUnixTimeMilliseconds(time).DateTime:yyyy-MM-dd HH:mm:ss}, 对齐后={klineTime:yyyy-MM-dd HH:mm:ss}");
                        }
                        
                        klines.Add(new KLineData
                        {
                            Time = klineTime,
                            Open = open,
                            High = high,
                            Low = low,
                            Close = close,
                            Volume = (decimal)volume,
                            // 添加完整的K线数据属性以确保数据完整性
                            Symbol = formattedSymbol,
                            OpenTime = klineTime,
                            CloseTime = interval == "1d" ? klineTime.AddDays(1).AddTicks(-1) : klineTime,
                            OpenPrice = (decimal)open,
                            HighPrice = (decimal)high,
                            LowPrice = (decimal)low,
                            ClosePrice = (decimal)close,
                            QuoteVolume = (decimal)quoteVolume // 使用正确的成交额数据
                        });
                    }
                    catch (Exception ex)
                    {
                        Utils.LogManager.Log("BinanceExchange", $"处理K线数据项时出错: {ex.Message}");
                        // 继续处理下一条数据
                        continue;
                    }
                }
                
                Utils.LogManager.Log("BinanceExchange", $"成功获取 {klines.Count} 条K线数据");
                return klines;
            }
            catch (BinanceApiException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Utils.LogManager.Log("BinanceExchange", $"获取K线数据失败: {ex.Message}");
                Utils.LogManager.Log("BinanceExchange", $"异常堆栈: {ex.StackTrace}");
                // 返回空列表而不是抛出异常
                return new List<KLineData>();
            }
        }

        public async Task<AccountInfo> GetAccountInfoAsync()
        {
            try
            {
                var parameters = new Dictionary<string, string>();
                var result = await SendRequestAsync<BinanceAccountResponse>("/fapi/v2/account", HttpMethod.Get, parameters, true);
                
                // 检查结果是否为null
                if (result == null)
                {
                    Utils.LogManager.Log("BinanceExchange", "获取账户信息失败: API返回了null");
                    throw new BinanceApiException("获取账户信息失败: API返回了null", "INVALID_RESPONSE", "API返回null");
                }
                
                // 使用TryParse处理每个字符串，避免异常
                decimal totalWalletBalance = 0, totalUnrealizedProfit = 0, availableBalance = 0;
                decimal totalMarginBalance = 0, totalMaintMargin = 0, totalInitialMargin = 0, totalPositionInitialMargin = 0;
                
                decimal.TryParse(result.TotalWalletBalance ?? "0", out totalWalletBalance);
                decimal.TryParse(result.TotalUnrealizedProfit ?? "0", out totalUnrealizedProfit);
                decimal.TryParse(result.AvailableBalance ?? "0", out availableBalance);
                decimal.TryParse(result.TotalMarginBalance ?? "0", out totalMarginBalance);
                decimal.TryParse(result.TotalMaintMargin ?? "0", out totalMaintMargin);
                decimal.TryParse(result.TotalInitialMargin ?? "0", out totalInitialMargin);
                decimal.TryParse(result.TotalPositionInitialMargin ?? "0", out totalPositionInitialMargin);
                
                Utils.LogManager.Log("BinanceExchange", $"账户信息: 钱包余额={totalWalletBalance}, 未实现盈亏={totalUnrealizedProfit}, 可用余额={availableBalance}");
                
                return new AccountInfo
                {
                    TotalEquity = totalWalletBalance + totalUnrealizedProfit,
                    AvailableBalance = availableBalance,
                    UnrealizedPnL = totalUnrealizedProfit,
                    MarginBalance = totalMarginBalance,
                    MaintenanceMargin = totalMaintMargin,
                    InitialMargin = totalInitialMargin,
                    PositionMargin = totalPositionInitialMargin,
                    WalletBalance = totalWalletBalance,
                    UpdateTime = DateTime.Now
                };
            }
            catch (Exception ex)
            {
                Utils.LogManager.Log("BinanceExchange", $"获取账户信息失败: {ex.Message}");
                Utils.LogManager.Log("BinanceExchange", ex.ToString());
                
                // 返回一个默认的AccountInfo对象
                return new AccountInfo
                {
                    TotalEquity = 0,
                    AvailableBalance = 0,
                    UnrealizedPnL = 0,
                    MarginBalance = 0,
                    MaintenanceMargin = 0,
                    InitialMargin = 0,
                    PositionMargin = 0,
                    WalletBalance = 0,
                    UpdateTime = DateTime.Now
                };
            }
        }

        public async Task<bool> PlaceOrderAsync(string symbol, string direction, decimal quantity, decimal price, decimal stopLossPrice, decimal leverage)
        {
            try
            {
                // 设置杠杆
                var leverageParams = new Dictionary<string, string>
                {
                    { "symbol", symbol.ToUpper() },
                    { "leverage", ((int)leverage).ToString() }
                };
                await SendRequestAsync<object>("/fapi/v1/leverage", HttpMethod.Post, leverageParams, true);

                // 下单
                var orderParams = new Dictionary<string, string>
                {
                    { "symbol", symbol.ToUpper() },
                    { "side", direction.ToUpper() },
                    { "type", "LIMIT" },
                    { "quantity", quantity.ToString() },
                    { "price", price.ToString() },
                    { "stopPrice", stopLossPrice.ToString() },
                    { "timeInForce", "GTC" },
                    { "reduceOnly", "false" },
                    { "closePosition", "false" }
                };

                var result = await SendRequestAsync<BinanceOrderResponse>("/fapi/v1/order", HttpMethod.Post, orderParams, true);
                return result != null;
            }
            catch (Exception ex)
            {
                Utils.LogManager.Log("BinanceExchange", $"下单失败: {ex.Message}");
                Utils.LogManager.Log("BinanceExchange", ex.ToString());
                throw;
            }
        }

        public Task<bool> CancelOrderAsync(string orderId)
        {
            throw new NotSupportedException("只读模式下不支持取消订单操作");
        }

        public async Task<TickerInfo> GetTickerAsync(string symbol)
        {
            try
            {
                // 格式化交易对符号
                string formattedSymbol = symbol.ToUpper();
                if (!formattedSymbol.EndsWith("USDT"))
                {
                    formattedSymbol = $"{formattedSymbol}USDT";
                }
                Utils.LogManager.Log("BinanceExchange", $"格式化后的交易对名称: {formattedSymbol}");

                // 获取所有合约的行情数据
                var allTickers = await GetAllTickersAsync();
                if (allTickers == null || !allTickers.Any())
                {
                    Utils.LogManager.Log("BinanceExchange", $"获取所有合约行情数据失败或为空");
                    
                    // 尝试直接获取单个合约的价格
                    try
                    {
                        Utils.LogManager.Log("BinanceExchange", $"尝试直接获取 {formattedSymbol} 的价格");
                        var priceEndpoint = "/fapi/v1/ticker/price";
                        var priceParams = new Dictionary<string, string> { { "symbol", formattedSymbol } };
                        var priceResponse = await SendRequestAsync<BinancePriceResponse>(priceEndpoint, HttpMethod.Get, priceParams);
                        
                        if (priceResponse != null && !string.IsNullOrEmpty(priceResponse.Price))
                        {
                            if (decimal.TryParse(priceResponse.Price, out var price) && price > 0)
                            {
                                Utils.LogManager.Log("BinanceExchange", $"成功获取 {formattedSymbol} 的价格: {price}");
                                return new TickerInfo
                                {
                                    Symbol = formattedSymbol,
                                    LastPrice = price,
                                    BidPrice = price * 0.999m,
                                    AskPrice = price * 1.001m,
                                    Volume = 0,
                                    QuoteVolume = 0,
                                    Timestamp = DateTime.Now,
                                    PriceChangePercent = 0
                                };
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Utils.LogManager.Log("BinanceExchange", $"直接获取价格失败: {ex.Message}");
                        
                        // 检查是否为网络异常，如果是则显示弹窗
                        if (Utils.NetworkExceptionHandler.IsNetworkException(ex))
                        {
                            Utils.NetworkExceptionHandler.ShowTickerFailureDialog();
                        }
                    }
                    
                    // 网络问题时返回null并记录详细信息
                    Utils.LogManager.Log("BinanceExchange", $"**ticker** 是 null。");
                    Utils.LogManager.Log("BinanceExchange", "这通常是由以下原因造成的：");
                    Utils.LogManager.Log("BinanceExchange", "1. 网络连接问题或超时");
                    Utils.LogManager.Log("BinanceExchange", "2. Binance API服务器响应慢");
                    Utils.LogManager.Log("BinanceExchange", "3. 防火墙或代理服务器阻止连接");
                    Utils.LogManager.Log("BinanceExchange", "4. 本地网络环境不稳定");
                    Utils.LogManager.Log("BinanceExchange", "建议检查网络连接或稍后重试");
                    
                    return null;
                }

                // 从所有合约数据中找到目标合约
                var ticker = allTickers.FirstOrDefault(t => 
                    t.Symbol.Equals(formattedSymbol, StringComparison.OrdinalIgnoreCase));

                if (ticker == null)
                {
                    Utils.LogManager.Log("BinanceExchange", $"未找到合约 {formattedSymbol} 的行情数据");
                    
                    // 尝试模糊匹配
                    var similarTicker = allTickers.FirstOrDefault(t => 
                        t.Symbol.Contains(symbol.ToUpper(), StringComparison.OrdinalIgnoreCase));
                    
                    if (similarTicker != null)
                    {
                        Utils.LogManager.Log("BinanceExchange", $"找到相似合约: {similarTicker.Symbol}");
                        return similarTicker;
                    }
                    
                    return null;
                }

                Utils.LogManager.Log("BinanceExchange", $"成功获取合约 {formattedSymbol} 的行情数据");
                return ticker;
            }
            catch (Exception ex)
            {
                Utils.LogManager.Log("BinanceExchange", $"获取 {symbol} 的行情数据失败: {ex.Message}");
                Utils.LogManager.Log("BinanceExchange", $"异常类型: {ex.GetType().Name}");
                
                // 检查是否为网络异常，如果是则显示弹窗
                if (Utils.NetworkExceptionHandler.IsNetworkException(ex))
                {
                    Utils.NetworkExceptionHandler.HandleNetworkException(ex, $"获取{symbol}价格数据");
                }
                
                return null;
            }
        }

        public async Task<List<TickerInfo>> GetAllTickersAsync()
        {
            try
            {
                // 检查缓存
                var now = DateTime.Now;
                if (_cachedTickers != null && (now - _lastTickerCacheTime).TotalMilliseconds < TICKER_CACHE_DURATION_MS)
                {
                    Utils.LogManager.Log("BinanceExchange", $"使用缓存的行情数据，共 {_cachedTickers.Count} 个合约");
                    return _cachedTickers;
                }

                Utils.LogManager.Log("BinanceExchange", "开始获取所有合约行情数据...");
                string endpoint = "/fapi/v1/ticker/24hr";
                
                List<BinanceTickerResponse> response = null;
                try
                {
                    response = await SendRequestAsync<List<BinanceTickerResponse>>(endpoint, HttpMethod.Get);
                }
                catch (TimeoutException ex)
                {
                    Utils.LogManager.Log("BinanceExchange", $"获取行情数据超时: {ex.Message}");
                    // 如果有缓存数据，返回缓存数据
                    if (_cachedTickers != null && _cachedTickers.Count > 0)
                    {
                        Utils.LogManager.Log("BinanceExchange", $"使用过期缓存数据，共 {_cachedTickers.Count} 个合约");
                        return _cachedTickers;
                    }
                    // 否则返回空列表
                    return new List<TickerInfo>();
                }
                catch (Exception ex)
                {
                    Utils.LogManager.Log("BinanceExchange", $"获取行情数据异常: {ex.Message}");
                    // 如果有缓存数据，返回缓存数据
                    if (_cachedTickers != null && _cachedTickers.Count > 0)
                    {
                        Utils.LogManager.Log("BinanceExchange", $"使用过期缓存数据，共 {_cachedTickers.Count} 个合约");
                        return _cachedTickers;
                    }
                    // 否则返回空列表
                    return new List<TickerInfo>();
                }
                
                if (response == null || !response.Any())
                {
                    Utils.LogManager.Log("BinanceExchange", "获取所有合约行情数据失败：响应为空");
                    // 如果有缓存数据，返回缓存数据
                    if (_cachedTickers != null && _cachedTickers.Count > 0)
                    {
                        Utils.LogManager.Log("BinanceExchange", $"使用过期缓存数据，共 {_cachedTickers.Count} 个合约");
                        return _cachedTickers;
                    }
                    return new List<TickerInfo>();
                }

                var tickers = new List<TickerInfo>();
                var successCount = 0;
                var errorCount = 0;
                
                foreach (var item in response)
                {
                    try
                    {
                        // 检查必要字段是否存在
                        if (string.IsNullOrEmpty(item.Symbol) || string.IsNullOrEmpty(item.LastPrice))
                        {
                            errorCount++;
                            continue;
                        }

                        // 使用TryParse替代Parse，并提供默认值
                        decimal lastPrice = 0m, bidPrice = 0m, askPrice = 0m, volume = 0m, quoteVolume = 0m, priceChangePercent = 0m;
                        
                        // 安全地解析每个字段
                        if (!decimal.TryParse(item.LastPrice ?? "0", out lastPrice) || lastPrice <= 0)
                        {
                            errorCount++;
                            continue;
                        }
                        
                        decimal.TryParse(item.BidPrice ?? "0", out bidPrice);
                        decimal.TryParse(item.AskPrice ?? "0", out askPrice);
                        decimal.TryParse(item.Volume ?? "0", out volume);
                        decimal.TryParse(item.QuoteVolume ?? "0", out quoteVolume);
                        decimal.TryParse(item.PriceChangePercent ?? "0", out priceChangePercent);

                        // 创建TickerInfo对象
                        var ticker = new TickerInfo
                        {
                            Symbol = item.Symbol,
                            LastPrice = lastPrice,
                            BidPrice = bidPrice > 0 ? bidPrice : lastPrice * 0.999m,
                            AskPrice = askPrice > 0 ? askPrice : lastPrice * 1.001m,
                            Volume = volume,
                            QuoteVolume = quoteVolume,
                            Timestamp = DateTime.Now,
                            PriceChangePercent = priceChangePercent
                        };
                        
                        tickers.Add(ticker);
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        Utils.LogManager.Log("BinanceExchange", $"解析合约 {item?.Symbol ?? "未知"} 行情数据失败: {ex.Message}");
                        errorCount++;
                        continue;
                    }
                }

                Utils.LogManager.Log("BinanceExchange", $"行情数据处理完成 - 成功: {successCount}, 失败: {errorCount}, 总计: {response.Count}");
                
                // 只有当成功获取到数据时才更新缓存
                if (tickers.Count > 0)
                {
                    _cachedTickers = tickers;
                    _lastTickerCacheTime = DateTime.Now;
                    Utils.LogManager.Log("BinanceExchange", $"成功获取并缓存 {tickers.Count} 个合约的行情数据");
                }
                else
                {
                    Utils.LogManager.Log("BinanceExchange", "未获取到有效的行情数据");
                    // 如果有缓存数据，返回缓存数据
                    if (_cachedTickers != null && _cachedTickers.Count > 0)
                    {
                        Utils.LogManager.Log("BinanceExchange", $"使用过期缓存数据，共 {_cachedTickers.Count} 个合约");
                        return _cachedTickers;
                    }
                }
                
                return tickers;
            }
            catch (Exception ex)
            {
                Utils.LogManager.Log("BinanceExchange", $"获取所有合约行情数据失败: {ex.Message}");
                Utils.LogManager.Log("BinanceExchange", $"异常类型: {ex.GetType().Name}");
                
                // 如果有缓存数据，返回缓存数据
                if (_cachedTickers != null && _cachedTickers.Count > 0)
                {
                    Utils.LogManager.Log("BinanceExchange", $"使用过期缓存数据，共 {_cachedTickers.Count} 个合约");
                    return _cachedTickers;
                }
                
                // 最后的保底措施：返回空列表而不是null
                return new List<TickerInfo>();
            }
        }

        public async Task<List<string>> GetTradableSymbolsAsync()
        {
            try
            {
                string endpoint = "/fapi/v1/exchangeInfo";
                var response = await SendRequestAsync<BinanceExchangeInfoResponse>(endpoint, HttpMethod.Get);
                
                if (response?.Symbols == null)
                {
                    Utils.LogManager.Log("BinanceExchange", "获取交易所信息失败：响应为空");
                    return new List<string>();
                }

                var tradableSymbols = response.Symbols
                    .Where(s => s.Status == "TRADING" && s.Symbol.EndsWith("USDT"))
                    .Select(s => s.Symbol)
                    .ToList();

                Utils.LogManager.Log("BinanceExchange", $"成功获取 {tradableSymbols.Count} 个可交易的USDT合约");
                
                // 调试：检查特定合约的状态
                var alphca = response.Symbols.FirstOrDefault(s => s.Symbol.ToUpper() == "ALPHCAUSDT");
                if (alphca != null)
                {
                    Utils.LogManager.Log("BinanceExchange", $"ALPHCAUSDT状态: {alphca.Status}");
                }
                var bnx = response.Symbols.FirstOrDefault(s => s.Symbol.ToUpper() == "BNXUSDT");
                if (bnx != null)
                {
                    Utils.LogManager.Log("BinanceExchange", $"BNXUSDT状态: {bnx.Status}");
                }
                
                return tradableSymbols;
            }
            catch (Exception ex)
            {
                Utils.LogManager.Log("BinanceExchange", $"获取可交易合约失败: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// 测试网络连接
        /// </summary>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                Utils.LogManager.Log("BinanceExchange", "开始测试网络连接...");
                
                // 测试ping端点
                var response = await SendRequestAsync<object>("/fapi/v1/ping", HttpMethod.Get);
                
                if (response != null)
                {
                    Utils.LogManager.Log("BinanceExchange", "网络连接测试成功");
                    return true;
                }
                else
                {
                    Utils.LogManager.Log("BinanceExchange", "网络连接测试失败：响应为空");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Utils.LogManager.Log("BinanceExchange", $"网络连接测试失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 强制重试网络连接，清除缓存并重新初始化
        /// </summary>
        public async Task<bool> ForceRetryConnectionAsync()
        {
            try
            {
                Utils.LogManager.Log("BinanceExchange", "🔄 开始强制重试网络连接...");
                
                // 清除缓存
                _cachedTickers = null;
                _lastTickerCacheTime = DateTime.MinValue;
                
                // 尝试切换到下一个API端点
                var switchSuccess = await TryNextBaseUrl();
                if (switchSuccess)
                {
                    Utils.LogManager.Log("BinanceExchange", "✅ 成功切换到备用API端点");
                }
                
                // 测试基本连接
                var pingSuccess = await TestConnectionAsync();
                if (!pingSuccess)
                {
                    Utils.LogManager.Log("BinanceExchange", "❌ Ping测试失败，尝试下一个端点");
                    
                    // 如果当前端点失败，再尝试下一个
                    switchSuccess = await TryNextBaseUrl();
                    if (switchSuccess)
                    {
                        pingSuccess = await TestConnectionAsync();
                    }
                }
                
                if (!pingSuccess)
                {
                    Utils.LogManager.Log("BinanceExchange", "❌ 所有端点Ping测试都失败");
                    return false;
                }
                
                // 测试获取价格数据
                var ticker = await GetTickerAsync("BTCUSDT");
                if (ticker != null && ticker.LastPrice > 0)
                {
                    Utils.LogManager.Log("BinanceExchange", $"✅ 强制重试成功，获取到BTCUSDT价格: {ticker.LastPrice:F2}");
                    Utils.LogManager.Log("BinanceExchange", $"当前使用的API端点: {_alternativeBaseUrls[_currentBaseUrlIndex]}");
                    return true;
                }
                else
                {
                    Utils.LogManager.Log("BinanceExchange", "❌ 无法获取价格数据");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Utils.LogManager.Log("BinanceExchange", $"❌ 强制重试连接失败: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            Utils.LogManager.Log("BinanceExchange", "=== 释放币安交易所服务资源 ===");
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    try
                    {
                        Utils.LogManager.Log("BinanceExchange", "正在取消所有正在进行的请求...");
                        // 在单独的try块中取消请求
                        try
                        {
                            // 取消所有正在进行的请求
                    _globalCts?.Cancel();
                        }
                        catch (Exception cancelEx)
                        {
                            Utils.LogManager.Log("BinanceExchange", $"取消请求时发生错误: {cancelEx.Message}");
                        }
                        
                        // 给请求一些时间响应取消，但使用Task.Delay.Wait可能会阻塞线程
                        // 使用更短的延迟时间
                        try
                        {
                            Task.Delay(50).Wait();
                        }
                        catch (Exception delayEx)
                        {
                            Utils.LogManager.Log("BinanceExchange", $"等待请求取消时发生错误: {delayEx.Message}");
                        }
                        
                        // 在单独的try块中释放CancellationTokenSource
                        try
                        {
                            // 释放CancellationTokenSource
                            if (_globalCts != null)
                            {
                                _globalCts.Dispose();
                            }
                        }
                        catch (Exception ctsEx)
                        {
                            Utils.LogManager.Log("BinanceExchange", $"释放CancellationTokenSource时发生错误: {ctsEx.Message}");
                        }
                        
                        // 在单独的try块中释放HttpClient
                        try
                        {
                            // 释放HttpClient
                            if (_httpClient != null)
                            {
                                _httpClient.Dispose();
                            }
                        }
                        catch (Exception httpEx)
                        {
                            Utils.LogManager.Log("BinanceExchange", $"释放HttpClient时发生错误: {httpEx.Message}");
                        }
                        
                        Utils.LogManager.Log("BinanceExchange", "所有资源已释放");
                    }
                    catch (Exception ex)
                    {
                        Utils.LogManager.Log("BinanceExchange", $"释放资源时发生错误: {ex.Message}");
                    }
                }
                _isDisposed = true;
            }
        }

        private class BinanceOrderResponse
        {
            public string? Symbol { get; set; }
            public string? OrderId { get; set; }
            public string? ClientOrderId { get; set; }
            public string? Status { get; set; }
        }

        private class BinanceTickerResponse
        {
            [JsonPropertyName("symbol")]
            public string Symbol { get; set; } = string.Empty;
            
            [JsonPropertyName("lastPrice")]
            public string LastPrice { get; set; } = string.Empty;
            
            [JsonPropertyName("bidPrice")]
            public string BidPrice { get; set; } = string.Empty;
            
            [JsonPropertyName("askPrice")]
            public string AskPrice { get; set; } = string.Empty;
            
            [JsonPropertyName("volume")]
            public string Volume { get; set; } = string.Empty;
            
            [JsonPropertyName("quoteVolume")]
            public string QuoteVolume { get; set; } = string.Empty;
            
            [JsonPropertyName("closeTime")]
            public long CloseTime { get; set; }
            
            [JsonPropertyName("priceChangePercent")]
            public string PriceChangePercent { get; set; } = string.Empty;
        }

        private class BinanceTicker24hrResponse
        {
            [JsonPropertyName("symbol")]
            public string Symbol { get; set; } = string.Empty;
            
            [JsonPropertyName("lastPrice")]
            public string LastPrice { get; set; } = string.Empty;
            
            [JsonPropertyName("bidPrice")]
            public string BidPrice { get; set; } = string.Empty;
            
            [JsonPropertyName("askPrice")]
            public string AskPrice { get; set; } = string.Empty;
            
            [JsonPropertyName("volume")]
            public string Volume { get; set; } = string.Empty;
            
            [JsonPropertyName("closeTime")]
            public long CloseTime { get; set; }
            
            [JsonPropertyName("priceChangePercent")]
            public string PriceChangePercent { get; set; } = string.Empty;
        }

        private class BinancePriceResponse
        {
            [JsonPropertyName("symbol")]
            public string Symbol { get; set; } = string.Empty;
            
            [JsonPropertyName("price")]
            public string Price { get; set; } = string.Empty;
        }

        private class BinanceAccountResponse
        {
            public string? TotalWalletBalance { get; set; }
            public string? TotalUnrealizedProfit { get; set; }
            public string? TotalMarginBalance { get; set; }
            public string? TotalInitialMargin { get; set; }
            public string? TotalMaintMargin { get; set; }
            public string? TotalPositionInitialMargin { get; set; }
            public string? AvailableBalance { get; set; }
        }

        private class BinanceExchangeInfoResponse
        {
            [JsonPropertyName("symbols")]
            public List<BinanceSymbolInfo> Symbols { get; set; } = new List<BinanceSymbolInfo>();
        }

        private class BinanceSymbolInfo
        {
            [JsonPropertyName("symbol")]
            public string Symbol { get; set; } = string.Empty;
            
            [JsonPropertyName("status")]
            public string Status { get; set; } = string.Empty;
            
            [JsonPropertyName("baseAsset")]
            public string BaseAsset { get; set; } = string.Empty;
            
            [JsonPropertyName("quoteAsset")]
            public string QuoteAsset { get; set; } = string.Empty;
        }
    }
} 