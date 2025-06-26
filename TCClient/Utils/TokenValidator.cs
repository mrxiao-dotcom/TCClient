using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TCClient.Utils
{
    /// <summary>
    /// Token验证工具
    /// </summary>
    public static class TokenValidator
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// 验证Token格式和有效性
        /// </summary>
        public static async Task<TokenValidationResult> ValidateTokenAsync(string token)
        {
            var result = new TokenValidationResult
            {
                Token = token,
                ValidationTime = DateTime.Now
            };

            // 1. 基本格式检查
            result.FormatValidation = ValidateTokenFormat(token);

            // 2. 如果格式有问题，直接返回
            if (!result.FormatValidation.IsValid)
            {
                result.IsValid = false;
                result.ErrorMessage = result.FormatValidation.ErrorMessage;
                return result;
            }

            // 3. 网络连接测试
            result.NetworkTest = await TestNetworkConnection();

            // 4. 如果网络有问题，直接返回
            if (!result.NetworkTest.IsSuccess)
            {
                result.IsValid = false;
                result.ErrorMessage = "网络连接失败";
                return result;
            }

            // 5. API有效性测试
            result.ApiTest = await TestTokenApi(token);
            result.IsValid = result.ApiTest.IsSuccess;

            if (!result.IsValid)
            {
                result.ErrorMessage = result.ApiTest.ErrorMessage;
                result.Suggestions = GenerateSuggestions(result);
            }

            return result;
        }

        /// <summary>
        /// 验证Token格式
        /// </summary>
        private static FormatValidationResult ValidateTokenFormat(string token)
        {
            var result = new FormatValidationResult();

            if (string.IsNullOrWhiteSpace(token))
            {
                result.IsValid = false;
                result.ErrorMessage = "Token不能为空";
                return result;
            }

            // 检查是否包含URL
            if (token.Contains("http://") || token.Contains("https://"))
            {
                result.IsValid = false;
                result.ErrorMessage = "Token不应包含完整URL，请只使用Token部分";
                result.SuggestedToken = ExtractTokenFromUrl(token);
                return result;
            }

            // 检查是否包含域名
            if (token.Contains("xtuis.cn") || token.Contains(".cn") || token.Contains(".com"))
            {
                result.IsValid = false;
                result.ErrorMessage = "Token不应包含域名，请只使用Token部分";
                result.SuggestedToken = ExtractTokenFromUrl(token);
                return result;
            }

            // 检查长度
            if (token.Length < 10)
            {
                result.IsValid = false;
                result.ErrorMessage = "Token长度太短，通常应该在10-50个字符之间";
                return result;
            }

            if (token.Length > 100)
            {
                result.IsValid = false;
                result.ErrorMessage = "Token长度太长，通常应该在10-50个字符之间";
                return result;
            }

            // 检查特殊字符
            if (!Regex.IsMatch(token, @"^[a-zA-Z0-9_-]+$"))
            {
                result.IsValid = false;
                result.ErrorMessage = "Token包含无效字符，只能包含字母、数字、下划线和连字符";
                return result;
            }

            result.IsValid = true;
            result.TokenLength = token.Length;
            result.TokenPattern = GetTokenPattern(token);
            return result;
        }

        /// <summary>
        /// 从URL中提取Token
        /// </summary>
        private static string ExtractTokenFromUrl(string input)
        {
            // 移除协议
            input = input.Replace("https://", "").Replace("http://", "");
            
            // 移除域名
            if (input.StartsWith("xtuis.cn/"))
            {
                input = input.Substring("xtuis.cn/".Length);
            }

            // 移除查询参数
            var questionMarkIndex = input.IndexOf('?');
            if (questionMarkIndex > 0)
            {
                input = input.Substring(0, questionMarkIndex);
            }

            return input;
        }

        /// <summary>
        /// 获取Token模式
        /// </summary>
        private static string GetTokenPattern(string token)
        {
            if (token.StartsWith("XT"))
                return "XT开头类型";
            if (Regex.IsMatch(token, @"^[A-Z]{2,4}[0-9]+"))
                return "字母+数字类型";
            if (Regex.IsMatch(token, @"^[a-zA-Z0-9]{20,30}$"))
                return "混合字符类型";
            return "其他类型";
        }

        /// <summary>
        /// 测试网络连接
        /// </summary>
        private static async Task<NetworkTestResult> TestNetworkConnection()
        {
            try
            {
                var response = await _httpClient.GetAsync("https://xtuis.cn/");
                return new NetworkTestResult
                {
                    IsSuccess = response.IsSuccessStatusCode,
                    StatusCode = (int)response.StatusCode,
                    ResponseTime = DateTime.Now
                };
            }
            catch (Exception ex)
            {
                return new NetworkTestResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message,
                    ResponseTime = DateTime.Now
                };
            }
        }

        /// <summary>
        /// 测试Token API
        /// </summary>
        private static async Task<TokenApiTestResult> TestTokenApi(string token)
        {
            try
            {
                var testMessage = "Token验证测试";
                var encodedMessage = Uri.EscapeDataString(testMessage);
                var url = $"https://xtuis.cn/{token}?text={encodedMessage}";

                var response = await _httpClient.GetAsync(url);
                var responseContent = await response.Content.ReadAsStringAsync();

                return new TokenApiTestResult
                {
                    IsSuccess = response.IsSuccessStatusCode,
                    StatusCode = (int)response.StatusCode,
                    ResponseContent = responseContent,
                    ErrorMessage = response.IsSuccessStatusCode ? null : $"HTTP {response.StatusCode}: {response.ReasonPhrase}"
                };
            }
            catch (Exception ex)
            {
                return new TokenApiTestResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// 生成建议
        /// </summary>
        private static string[] GenerateSuggestions(TokenValidationResult result)
        {
            var suggestions = new List<string>();

            if (result.ApiTest.StatusCode == 404)
            {
                suggestions.Add("Token可能不存在或已过期，请检查Token是否正确");
                suggestions.Add("尝试重新从 https://xtuis.cn/ 获取新的Token");
                suggestions.Add("确认账号状态是否正常");
            }
            else if (result.ApiTest.StatusCode == 403)
            {
                suggestions.Add("Token可能被禁用或权限不足");
                suggestions.Add("联系虾推啥客服确认账号状态");
            }
            else if (result.ApiTest.StatusCode == 429)
            {
                suggestions.Add("请求过于频繁，请稍后再试");
                suggestions.Add("检查是否有其他程序在使用同一Token");
            }
            else if (!result.NetworkTest.IsSuccess)
            {
                suggestions.Add("检查网络连接是否正常");
                suggestions.Add("确认防火墙或代理设置");
            }
            else
            {
                suggestions.Add("尝试手动在浏览器中测试Token");
                suggestions.Add("联系虾推啥客服获取技术支持");
            }

            return suggestions.ToArray();
        }
    }

    /// <summary>
    /// Token验证结果
    /// </summary>
    public class TokenValidationResult
    {
        public string Token { get; set; } = string.Empty;
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public DateTime ValidationTime { get; set; }
        public FormatValidationResult FormatValidation { get; set; } = new FormatValidationResult();
        public NetworkTestResult NetworkTest { get; set; } = new NetworkTestResult();
        public TokenApiTestResult ApiTest { get; set; } = new TokenApiTestResult();
        public string[] Suggestions { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// 格式验证结果
    /// </summary>
    public class FormatValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string SuggestedToken { get; set; } = string.Empty;
        public int TokenLength { get; set; }
        public string TokenPattern { get; set; } = string.Empty;
    }

    /// <summary>
    /// 网络测试结果
    /// </summary>
    public class NetworkTestResult
    {
        public bool IsSuccess { get; set; }
        public int StatusCode { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public DateTime ResponseTime { get; set; }
    }

    /// <summary>
    /// Token API测试结果
    /// </summary>
    public class TokenApiTestResult
    {
        public bool IsSuccess { get; set; }
        public int StatusCode { get; set; }
        public string ResponseContent { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }
} 