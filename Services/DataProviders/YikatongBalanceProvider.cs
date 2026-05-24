using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using wish_drom.Models;
using wish_drom.Data.Entities;
using wish_drom.Services.Interfaces;

namespace wish_drom.Services.DataProviders
{
    /// <summary>
    /// 同济大学一卡通余额数据提供者
    /// 基于 pay-yikatong.tongji.edu.cn，通过 WebView 登录获取凭证，再使用原生 HTTP 请求获取余额。
    /// 鉴权方式：Cookie (JWTUser, TGC) + synjones-auth (Bearer Token)
    /// </summary>
    public class YikatongBalanceProvider : IDataProvider
    {
        private readonly YikatongBalanceDbContext _dbContext;

        public YikatongBalanceProvider(YikatongBalanceDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        private static void Log(string msg)
        {
            Console.WriteLine(msg);
            Debug.WriteLine(msg);
        }

        private const string API_BASE = "https://pay-yikatong.tongji.edu.cn";
        private const string BALANCE_API = "/berserker-app/ykt/tsm/queryCard?synAccessSource=h5";

        private const string JWT_USER_KEY = "yikatong_jwt_user";
        private const string BEARER_TOKEN_KEY = "yikatong_bearer_token";

        public bool IsReadyForExtraction(string currentUrl, string html)
        {
            var urlMatch = currentUrl.StartsWith("https://pay-yikatong.tongji.edu.cn", StringComparison.OrdinalIgnoreCase)
                && (currentUrl.Contains("/plat/wode") || currentUrl.Contains("/berserker-app/"));

            var hasHtml = !string.IsNullOrEmpty(html);
            var htmlLength = html?.Length ?? 0;
            var lengthMatch = htmlLength > 1000;
            // 移除 "403" 检测 - JS 代码中常有状态码检查，会误判
            var hasForbidden = html?.Contains("Forbidden", StringComparison.OrdinalIgnoreCase) == true;
            var hasAuthError = html?.Contains("越权", StringComparison.OrdinalIgnoreCase) == true;

            var contentMatch = hasHtml && lengthMatch && !hasForbidden && !hasAuthError;

            Log($"[YikatongProvider.IsReady] URL: {currentUrl}");
            Log($"[YikatongProvider.IsReady]   urlMatch={urlMatch}, hasHtml={hasHtml}, htmlLength={htmlLength}, lengthMatch={lengthMatch}");
            Log($"[YikatongProvider.IsReady]   hasForbidden={hasForbidden}, hasAuthError={hasAuthError}");
            Log($"[YikatongProvider.IsReady]   contentMatch={contentMatch}");
            Log($"[YikatongProvider.IsReady]   → 返回: {urlMatch && contentMatch}");

            return urlMatch && contentMatch;
        }

        public async Task<string?> ExtractDataAsync(
            string html,
            ISecureDataStorage secureStorage,
            Func<string, Task<string?>>? evaluateJavaScript = null)
        {
            if (evaluateJavaScript == null)
            {
                Log("[YikatongProvider] JS 执行器为空，无法提取凭证");
                return null;
            }

            try
            {
                var cookieString = await TryGetCookieStringAsync(evaluateJavaScript);
                if (!string.IsNullOrEmpty(cookieString))
                {
                    await secureStorage.SetAsync(JWT_USER_KEY, cookieString);
                    Log($"[YikatongProvider] Cookie 已存储 ({cookieString.Length} 字符)");
                }
                else
                {
                    Log("[YikatongProvider] Cookie 提取失败（可能为 HttpOnly），尝试使用 Token");
                }

                // 检查 Token 是否已存在（从 URL 提取的）
                var existingToken = await secureStorage.GetAsync(BEARER_TOKEN_KEY);
                if (!string.IsNullOrEmpty(existingToken))
                {
                    Log("[YikatongProvider] Token 已存在，凭证提取完成");
                    return "CredentialsStored";
                }

                Log("[YikatongProvider] 未找到可用凭证");
                return null;
            }
            catch (Exception ex)
            {
                Log($"[YikatongProvider] ExtractDataAsync 异常: {ex}");
                return null;
            }
        }

        public async Task<string?> FetchDataAsync(ISecureDataStorage secureStorage)
        {
            var cookieString = await secureStorage.GetAsync(JWT_USER_KEY);
            var bearerToken = await secureStorage.GetAsync(BEARER_TOKEN_KEY);

            if (string.IsNullOrEmpty(cookieString))
                throw new AuthExpiredException("未找到一卡通登录凭证，请先完成登录");

            if (string.IsNullOrEmpty(bearerToken))
                throw new AuthExpiredException("未找到一卡通 Bearer Token，请先完成登录");

            try
            {
                using var handler = new HttpClientHandler { UseCookies = false };
                using var client = new HttpClient(handler)
                {
                    BaseAddress = new Uri(API_BASE),
                    Timeout = TimeSpan.FromSeconds(15)
                };

                client.DefaultRequestHeaders.Add("Cookie", cookieString);
                client.DefaultRequestHeaders.Add("synjones-auth", $"bearer {bearerToken}");
                client.DefaultRequestHeaders.Add("synaccesssource", "h5");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                client.DefaultRequestHeaders.Add("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/148.0.0.0 Safari/537.36 Edg/148.0.0.0");

                Log("[YikatongProvider] 请求一卡通余额 API");

                var response = await client.GetAsync(BALANCE_API);

                if (response.StatusCode == HttpStatusCode.Unauthorized ||
                    response.StatusCode == HttpStatusCode.Forbidden)
                {
                    await ClearCredentialsAsync(secureStorage);
                    throw new AuthExpiredException("一卡通凭证已失效，请重新登录");
                }

                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                Log($"[YikatongProvider] API 响应: {content.Length} 字符");

                return content;
            }
            catch (AuthExpiredException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log($"[YikatongProvider] FetchDataAsync 异常: {ex.Message}");
                throw new AuthExpiredException($"获取一卡通余额失败: {ex.Message}");
            }
        }

        public async Task<PersistResult> PersistRawDataAsync(string rawData)
        {
            try
            {
                var balance = ParseBalanceFromRawData(rawData);

                var oldRecords = await _dbContext.Balances.ToListAsync();
                _dbContext.Balances.RemoveRange(oldRecords);

                if (balance != null)
                {
                    await _dbContext.Balances.AddAsync(balance);
                    await _dbContext.SaveChangesAsync();

                    Log($"[YikatongProvider] 余额已保存: {balance.Balance:F2} 元");
                    return new PersistResult
                    {
                        Success = true,
                        SavedRecordCount = 1
                    };
                }

                return new PersistResult
                {
                    Success = false,
                    SavedRecordCount = 0,
                    Error = "无法解析余额数据"
                };
            }
            catch (Exception ex)
            {
                Log($"[YikatongProvider] PersistRawDataAsync 异常: {ex.Message}");
                return new PersistResult
                {
                    Success = false,
                    SavedRecordCount = 0,
                    Error = $"余额保存失败: {ex.Message}"
                };
            }
        }

        private YikatongBalance? ParseBalanceFromRawData(string rawData)
        {
            try
            {
                using var document = JsonDocument.Parse(rawData);
                var root = document.RootElement;

                // 检查 success 标志
                var success = root.TryGetProperty("success", out var successEl) && successEl.GetBoolean();
                if (!success)
                {
                    var msg = root.TryGetProperty("msg", out var msgEl) ? msgEl.GetString() : "未知错误";
                    Log($"[YikatongProvider] API 返回失败: {msg}");
                    return null;
                }

                // 获取 data.card 数组的第一个元素
                if (!root.TryGetProperty("data", out var dataEl))
                {
                    Log($"[YikatongProvider] JSON 缺少 data 属性");
                    return null;
                }

                if (!dataEl.TryGetProperty("card", out var cardEl))
                {
                    Log($"[YikatongProvider] JSON 缺少 data.card 属性");
                    return null;
                }

                if (cardEl.ValueKind != JsonValueKind.Array || cardEl.GetArrayLength() == 0)
                {
                    Log($"[YikatongProvider] data.card 为空或不是数组");
                    return null;
                }

                var card = cardEl[0];

                if (!card.TryGetProperty("elec_accamt", out var balanceElement))
                {
                    Log($"[YikatongProvider] card 对象缺少 elec_accamt 属性");
                    return null;
                }

                var balanceInCents = balanceElement.GetInt64();
                var balanceInYuan = balanceInCents / 100.0m;

                var account = card.TryGetProperty("account", out var accountEl)
                    ? accountEl.GetString() ?? ""
                    : "";

                var name = card.TryGetProperty("name", out var nameEl)
                    ? nameEl.GetString() ?? ""
                    : "";
                var sno = card.TryGetProperty("card_name_en", out var snoEl)
                    ? snoEl.GetString() ?? ""
                    : "";

                Log($"[YikatongProvider] 解析成功: 学号={sno}, 姓名={name}, 余额={balanceInYuan:F2} 元");

                return new YikatongBalance
                {
                    Balance = balanceInYuan,
                    Account = sno,
                    Name = name,
                    UpdatedAt = DateTime.Now
                };
            }
            catch (Exception ex)
            {
                Log($"[YikatongProvider] JSON 解析失败: {ex.Message}");
                Log($"[YikatongProvider] 完整 JSON: {rawData}");
                return null;
            }
        }

        private async Task ClearCredentialsAsync(ISecureDataStorage secureStorage)
        {
            await secureStorage.RemoveAsync(JWT_USER_KEY);
            await secureStorage.RemoveAsync(BEARER_TOKEN_KEY);
        }

        private static async Task<string?> TryGetCookieStringAsync(Func<string, Task<string?>> evaluateJavaScript)
        {
            try
            {
                Log("[YikatongProvider] 使用原生 Cookie API 提取 Cookie");
                var cookies = await EvaluateWithRetryAsync(evaluateJavaScript, "__native_cookies__");

                if (string.IsNullOrEmpty(cookies)) return null;

                var filteredCookies = cookies.Split(';')
                    .Select(c => c.Trim())
                    .Where(c => c.StartsWith("JWTUser=") || c.StartsWith("TGC="))
                    .ToList();

                return filteredCookies.Count > 0 ? string.Join("; ", filteredCookies) : null;
            }
            catch (Exception ex)
            {
                Log($"[YikatongProvider] Cookie 提取失败: {ex.Message}");
                return null;
            }
        }

        private static async Task<string?> EvaluateWithRetryAsync(Func<string, Task<string?>> evaluateJavaScript, string script, int retryCount = 3)
        {
            for (int i = 0; i < retryCount; i++)
            {
                try
                {
                    var result = await evaluateJavaScript(script);
                    return result;
                }
                catch
                {
                    if (i < retryCount - 1)
                        await Task.Delay(300);
                }
            }
            return null;
        }
    }
}