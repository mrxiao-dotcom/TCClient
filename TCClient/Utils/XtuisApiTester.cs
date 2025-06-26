using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace TCClient.Utils
{
    /// <summary>
    /// 虾推啥API测试工具
    /// </summary>
    public static class XtuisApiTester
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// 测试虾推啥Token是否有效
        /// </summary>
        public static async Task<TestResult> TestTokenAsync(string token, string testMessage = "API测试消息")
        {
            var result = new TestResult { Token = token };
            
            if (string.IsNullOrWhiteSpace(token))
            {
                result.IsSuccess = false;
                result.ErrorMessage = "Token不能为空";
                return result;
            }

            // 测试网络连接
            result.NetworkTest = await TestNetworkConnection();
            if (!result.NetworkTest.IsSuccess)
            {
                result.IsSuccess = false;
                result.ErrorMessage = "网络连接失败";
                return result;
            }

            // 尝试已验证的API格式
            result.GetTest = await TestGetFormat(token, testMessage);
            result.FormTest = await TestFormFormat(token, testMessage);

            // 任何一种格式成功即认为Token有效
            result.IsSuccess = result.GetTest.IsSuccess || result.FormTest.IsSuccess;
            
            if (!result.IsSuccess)
            {
                result.ErrorMessage = "所有API格式都测试失败，请检查Token是否正确";
            }

            return result;
        }

        /// <summary>
        /// 测试网络连接
        /// </summary>
        private static async Task<ApiTestResult> TestNetworkConnection()
        {
            try
            {
                var response = await _httpClient.GetAsync("https://xtuis.cn/");
                return new ApiTestResult
                {
                    IsSuccess = response.IsSuccessStatusCode,
                    StatusCode = (int)response.StatusCode,
                    ResponseContent = response.IsSuccessStatusCode ? "网络连接正常" : "网络连接异常",
                    Method = "网络测试"
                };
            }
            catch (Exception ex)
            {
                return new ApiTestResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message,
                    Method = "网络测试"
                };
            }
        }



        /// <summary>
        /// 测试官方GET格式API (已验证有效)
        /// </summary>
        private static async Task<ApiTestResult> TestGetFormat(string token, string message)
        {
            try
            {
                // 使用已验证的官方格式
                var title = Uri.EscapeDataString("API测试");
                var content = Uri.EscapeDataString(message);
                var url = $"https://wx.xtuis.cn/{token}.send?text={title}&desp={content}";

                var response = await _httpClient.GetAsync(url);
                var responseContent = await response.Content.ReadAsStringAsync();

                return new ApiTestResult
                {
                    IsSuccess = response.IsSuccessStatusCode,
                    StatusCode = (int)response.StatusCode,
                    ResponseContent = responseContent,
                    Method = "官方GET格式"
                };
            }
            catch (Exception ex)
            {
                return new ApiTestResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message,
                    Method = "官方GET格式"
                };
            }
        }

        /// <summary>
        /// 测试官方POST表单格式API
        /// </summary>
        private static async Task<ApiTestResult> TestFormFormat(string token, string message)
        {
            try
            {
                // 使用官方POST格式
                var url = $"https://wx.xtuis.cn/{token}.send";
                var formData = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("text", "API测试"),
                    new KeyValuePair<string, string>("desp", message)
                };
                var content = new FormUrlEncodedContent(formData);

                var response = await _httpClient.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                return new ApiTestResult
                {
                    IsSuccess = response.IsSuccessStatusCode,
                    StatusCode = (int)response.StatusCode,
                    ResponseContent = responseContent,
                    Method = "官方POST格式"
                };
            }
            catch (Exception ex)
            {
                return new ApiTestResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message,
                    Method = "官方POST格式"
                };
            }
        }

        /// <summary>
        /// 生成测试报告
        /// </summary>
        public static string GenerateTestReport(TestResult result)
        {
            var report = new StringBuilder();
            report.AppendLine("=== 虾推啥API测试报告 ===");
            report.AppendLine($"Token: {result.Token}");
            report.AppendLine($"测试时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine($"总体结果: {(result.IsSuccess ? "✅ 成功" : "❌ 失败")}");
            
            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                report.AppendLine($"错误信息: {result.ErrorMessage}");
            }
            
            report.AppendLine();
            report.AppendLine("详细测试结果:");
            
            AppendTestDetail(report, "网络连接", result.NetworkTest);
            AppendTestDetail(report, "官方GET格式", result.GetTest);
            AppendTestDetail(report, "官方POST格式", result.FormTest);
            
            report.AppendLine();
            report.AppendLine("建议:");
            if (result.IsSuccess)
            {
                if (result.GetTest.IsSuccess)
                    report.AppendLine("✅ 官方GET格式工作正常，将使用此格式");
                else if (result.FormTest.IsSuccess)
                    report.AppendLine("✅ 官方POST格式工作正常，将使用此格式");
            }
            else
            {
                if (!result.NetworkTest.IsSuccess)
                    report.AppendLine("❌ 请检查网络连接或防火墙设置");
                else
                    report.AppendLine("❌ 请确认Token是否正确，或联系虾推啥客服");
            }
            
            return report.ToString();
        }

        private static void AppendTestDetail(StringBuilder sb, string name, ApiTestResult result)
        {
            sb.AppendLine($"  {name}: {(result.IsSuccess ? "✅" : "❌")} {result.Method}");
            if (result.StatusCode.HasValue)
                sb.AppendLine($"    状态码: {result.StatusCode}");
            if (!string.IsNullOrEmpty(result.ResponseContent))
                sb.AppendLine($"    响应: {result.ResponseContent.Substring(0, Math.Min(100, result.ResponseContent.Length))}");
            if (!string.IsNullOrEmpty(result.ErrorMessage))
                sb.AppendLine($"    错误: {result.ErrorMessage}");
        }
    }

    /// <summary>
    /// 测试结果
    /// </summary>
    public class TestResult
    {
        public string Token { get; set; } = string.Empty;
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public ApiTestResult NetworkTest { get; set; } = new ApiTestResult();
        public ApiTestResult GetTest { get; set; } = new ApiTestResult();
        public ApiTestResult FormTest { get; set; } = new ApiTestResult();
    }

    /// <summary>
    /// API测试结果
    /// </summary>
    public class ApiTestResult
    {
        public bool IsSuccess { get; set; }
        public int? StatusCode { get; set; }
        public string ResponseContent { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
    }
} 