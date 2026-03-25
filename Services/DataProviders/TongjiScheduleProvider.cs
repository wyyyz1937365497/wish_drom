using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using wish_drom.Models;
using wish_drom.Services.Interfaces;

namespace wish_drom.Services.DataProviders
{
    /// <summary>
    /// 同济大学课表数据提供者
    /// 基于 1.tongji.edu.cn 统一门户，通过 WebView 登录获取凭证，再使用原生 HTTP 请求获取课表数据。
    /// 加密算法逆向自前端 webpack 模块 hpw7：AES-CBC-PKCS7，密钥经 paramHandler 字符交换处理。
    /// </summary>
    public class TongjiScheduleProvider : IDataProvider
    {
        private static void Log(string msg)
        {
            Console.WriteLine(msg);
            Debug.WriteLine(msg);
        }

        private const string API_BASE = "https://1.tongji.edu.cn/workbench";

        private const string CALENDAR_API_PATH = "/api/baseresservice/schoolCalendar/currentTermCalendar";
        private const string TIMETABLE_API_PATH = "/api/electionservice/reportManagement/findStudentTimetab";

        // SecureStorage 键名
        private const string COOKIE_KEY = "tongji_cookies";
        private const string SESSION_ID_KEY = "tongji_session_id";
        private const string STUDENT_CODE_KEY = "tongji_student_code";
        private const string CALENDAR_ID_KEY = "tongji_calendar_id";
        private const string CALENDAR_BEGIN_DAY_MS_KEY = "tongji_calendar_begin_day_ms";
        private const string TEACHING_WEEK_START_KEY = "tongji_teaching_week_start";
        private const string TEACHING_WEEK_END_KEY = "tongji_teaching_week_end";
        private const string AES_KEY_KEY = "tongji_aes_key";
        private const string AES_IV_KEY = "tongji_aes_iv";
        private const string UID_KEY = "tongji_uid";

        public bool IsReadyForExtraction(string currentUrl, string html)
        {
            return currentUrl.StartsWith("https://1.tongji.edu.cn/workbench", StringComparison.OrdinalIgnoreCase)
                && !currentUrl.Contains("ids.tongji.edu.cn", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<string?> ExtractDataAsync(
            string html,
            ISecureDataStorage secureStorage,
            Func<string, Task<string?>>? evaluateJavaScript = null)
        {
            if (evaluateJavaScript == null)
            {
                Log("[TongjiProvider] JS 执行器为空，无法提取凭证");
                return null;
            }

            try
            {
                // 步骤 1：从 WebView 中提取 Cookie（JS + cookieStore + 原生回退）
                var cookieString = await TryGetCookieStringAsync(evaluateJavaScript);
                if (string.IsNullOrEmpty(cookieString))
                {
                    Log("[TongjiProvider] 无法获取 Cookie");
                    return null;
                }
                await secureStorage.SetAsync(COOKIE_KEY, cookieString);
                Log($"[TongjiProvider] Cookie 已存储 ({cookieString.Length} 字符)");

                // 步骤 2：从 sessionStorage 提取 sessionid（用于 X-Token 请求头）
                var sessionId = await TryGetSessionIdAsync(evaluateJavaScript);
                if (!string.IsNullOrEmpty(sessionId))
                {
                    await secureStorage.SetAsync(SESSION_ID_KEY, sessionId);
                    Log("[TongjiProvider] sessionId (X-Token) 已存储");
                }

                // 步骤 3：从 localStorage 提取 sessiondata，解析 uid / aesKey / aesIv
                var sessionDataRaw = NormalizeJavaScriptValue(await evaluateJavaScript("localStorage.getItem('sessiondata')"));
                if (!string.IsNullOrEmpty(sessionDataRaw))
                {
                    var uid = TryExtractTopLevelField(sessionDataRaw, "uid");
                    var aesKey = TryExtractTopLevelField(sessionDataRaw, "aesKey");
                    var aesIv = TryExtractTopLevelField(sessionDataRaw, "aesIv");

                    if (!string.IsNullOrEmpty(uid))
                        await secureStorage.SetAsync(UID_KEY, uid);
                    if (!string.IsNullOrEmpty(aesKey))
                        await secureStorage.SetAsync(AES_KEY_KEY, aesKey);
                    if (!string.IsNullOrEmpty(aesIv))
                        await secureStorage.SetAsync(AES_IV_KEY, aesIv);

                    // 使用 AES-CBC 加密生成 studentCode
                    if (!string.IsNullOrEmpty(uid) && !string.IsNullOrEmpty(aesKey) && !string.IsNullOrEmpty(aesIv))
                    {
                        var encryptedStudentCode = EncryptStudentCode(uid, aesKey, aesIv);
                        await secureStorage.SetAsync(STUDENT_CODE_KEY, encryptedStudentCode);
                        Log("[TongjiProvider] 加密 studentCode 已生成并存储");
                    }
                    else
                    {
                        Log($"[TongjiProvider] sessiondata 缺少必要字段 uid={uid != null} aesKey={aesKey != null} aesIv={aesIv != null}");
                    }
                }
                else
                {
                    Log("[TongjiProvider] 无法从 localStorage 读取 sessiondata");
                }

                // 步骤 4：calendarId 统一在阶段二通过原生 HTTP 获取，避免 WebView JS fetch 超时噪声。
                Log("[TongjiProvider] 跳过阶段一 calendarId 提取，将在阶段二通过 HTTP 获取");

                await LogCredentialSummaryAsync(secureStorage);

                return "CredentialsStored";
            }
            catch (Exception ex)
            {
                Log($"[TongjiProvider] ExtractDataAsync 异常: {ex}");
                return null;
            }
        }

        public async Task<string?> FetchDataAsync(ISecureDataStorage secureStorage)
        {
            var cookieString = await secureStorage.GetAsync(COOKIE_KEY);
            if (string.IsNullOrEmpty(cookieString))
                throw new AuthExpiredException("未找到登录凭证，请先完成登录");

            var sessionId = await secureStorage.GetAsync(SESSION_ID_KEY) ?? "";
            var studentCode = await secureStorage.GetAsync(STUDENT_CODE_KEY);
            var calendarId = await secureStorage.GetAsync(CALENDAR_ID_KEY);

            // 如果缺少加密 studentCode，尝试用存储的原始数据重新生成
            if (string.IsNullOrEmpty(studentCode))
            {
                studentCode = await TryReEncryptStudentCodeAsync(secureStorage);
                if (string.IsNullOrEmpty(studentCode))
                    throw new AuthExpiredException("缺少 studentCode，请重新登录以获取加密参数");
            }

            using var httpClient = CreateHttpClient(cookieString, sessionId);

            if (string.IsNullOrEmpty(calendarId))
            {
                calendarId = await FetchCalendarIdAsync(httpClient, secureStorage);
            }

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var timetableUrl = $"{TIMETABLE_API_PATH}?calendarId={Uri.EscapeDataString(calendarId ?? "")}" +
                               $"&studentCode={studentCode}" +
                               $"&_t={timestamp}";

            Log($"[TongjiProvider] 请求课表: {timetableUrl}");

            var response = await httpClient.GetAsync(timetableUrl);

            if (response.StatusCode == HttpStatusCode.Unauthorized ||
                response.StatusCode == HttpStatusCode.Forbidden)
            {
                await secureStorage.RemoveAsync(COOKIE_KEY);
                await secureStorage.RemoveAsync(SESSION_ID_KEY);
                throw new AuthExpiredException("凭证已失效，请重新登录");
            }

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            Log($"[TongjiProvider] 课表数据获取成功 ({content.Length} 字符)");

            return content;
        }

        /// <summary>
        /// 尝试用安全存储中缓存的 uid/aesKey/aesIv 重新生成加密 studentCode
        /// </summary>
        private static async Task<string?> TryReEncryptStudentCodeAsync(ISecureDataStorage secureStorage)
        {
            var uid = await secureStorage.GetAsync(UID_KEY);
            var aesKey = await secureStorage.GetAsync(AES_KEY_KEY);
            var aesIv = await secureStorage.GetAsync(AES_IV_KEY);

            if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(aesKey) || string.IsNullOrEmpty(aesIv))
                return null;

            var encrypted = EncryptStudentCode(uid, aesKey, aesIv);
            await secureStorage.SetAsync(STUDENT_CODE_KEY, encrypted);
            Log("[TongjiProvider] studentCode 已从缓存参数重新生成");
            return encrypted;
        }

        /// <summary>
        /// 从 API 获取当前学期 calendarId
        /// </summary>
        private async Task<string?> FetchCalendarIdAsync(HttpClient httpClient, ISecureDataStorage secureStorage)
        {
            try
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var response = await httpClient.GetAsync($"{CALENDAR_API_PATH}?_t={timestamp}");
                if (!response.IsSuccessStatusCode)
                {
                    HandleAuthError(response.StatusCode);
                    Log($"[TongjiProvider] 获取日历失败: {response.StatusCode}");
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync();
                await CacheCalendarMetadataAsync(content, secureStorage);
                var calendarId = TryExtractNestedField(content, "data", "schoolCalendar", "id");

                if (!string.IsNullOrEmpty(calendarId))
                {
                    await secureStorage.SetAsync(CALENDAR_ID_KEY, calendarId);
                    Log($"[TongjiProvider] calendarId 已获取并缓存: {calendarId}");
                }

                return calendarId;
            }
            catch (AuthExpiredException) { throw; }
            catch (Exception ex)
            {
                Log($"[TongjiProvider] 获取 calendarId 异常: {ex.Message}");
                return null;
            }
        }

        // ──────────────── AES-CBC-PKCS7 加密（逆向自前端 hpw7 模块） ────────────────

        /// <summary>
        /// 复现前端加密逻辑：encodeURIComponent(uid) → AES-CBC-PKCS7 → Base64 → encodeURIComponent
        /// </summary>
        private static string EncryptStudentCode(string uid, string aesKey, string aesIv)
        {
            var processedKey = ParamHandler(aesKey);
            var processedIv = ParamHandler(aesIv);

            var keyBytes = Encoding.UTF8.GetBytes(processedKey);
            var ivBytes = Encoding.UTF8.GetBytes(processedIv);
            var plainBytes = Encoding.UTF8.GetBytes(Uri.EscapeDataString(uid));

            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = keyBytes;
            aes.IV = ivBytes;

            using var encryptor = aes.CreateEncryptor();
            var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            return Uri.EscapeDataString(Convert.ToBase64String(cipherBytes));
        }

        /// <summary>
        /// 前端 paramHandler：相邻字符两两交换。"abcd" → "badc"
        /// </summary>
        private static string ParamHandler(string input)
        {
            var chars = input.ToCharArray();
            var result = new char[chars.Length];
            for (int i = 0; i < chars.Length; i += 2)
            {
                if (i + 1 < chars.Length)
                {
                    result[i] = chars[i + 1];
                    result[i + 1] = chars[i];
                }
                else
                {
                    result[i] = chars[i];
                }
            }
            return new string(result);
        }

        // ──────────────── HTTP 与 JSON 工具方法 ────────────────

        private static HttpClient CreateHttpClient(string cookieString, string sessionId)
        {
            var handler = new HttpClientHandler
            {
                UseCookies = false
            };

            var client = new HttpClient(handler)
            {
                BaseAddress = new Uri(API_BASE),
                Timeout = TimeSpan.FromSeconds(15)
            };

            client.DefaultRequestHeaders.Add("Cookie", cookieString);
            if (!string.IsNullOrEmpty(sessionId))
                client.DefaultRequestHeaders.Add("X-Token", sessionId);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36");

            return client;
        }

        private static async Task<string?> TryGetCookieStringAsync(Func<string, Task<string?>> evaluateJavaScript)
        {
            var cookie = NormalizeJavaScriptValue(await evaluateJavaScript("document.cookie"));
            if (!string.IsNullOrEmpty(cookie))
            {
                Log($"[TongjiProvider] Cookie 来源: document.cookie (len={cookie.Length}, items={CountCookieItems(cookie)})");
                return cookie;
            }

            var cookieStoreScript = @"
                (async () => {
                    if (window.cookieStore && window.cookieStore.getAll) {
                        const all = await window.cookieStore.getAll();
                        return all.map(c => `${c.name}=${c.value}`).join('; ');
                    }
                    return '';
                })()
            ";

            cookie = NormalizeJavaScriptValue(await evaluateJavaScript(cookieStoreScript));
            if (!string.IsNullOrEmpty(cookie))
            {
                Log($"[TongjiProvider] Cookie 来源: cookieStore.getAll (len={cookie.Length}, items={CountCookieItems(cookie)})");
                return cookie;
            }

            // 由 DataCaptureService 提供平台层 Cookie 回退（适配 HttpOnly 场景）
            cookie = NormalizeJavaScriptValue(await evaluateJavaScript("__native_cookies__"));
            if (!string.IsNullOrEmpty(cookie))
            {
                Log($"[TongjiProvider] Cookie 来源: native cookie manager (len={cookie.Length}, items={CountCookieItems(cookie)})");
                return cookie;
            }

            Log("[TongjiProvider] Cookie 来源: 全部失败 (document.cookie/cookieStore/native)");
            return null;
        }

        private static async Task<string?> TryGetSessionIdAsync(Func<string, Task<string?>> evaluateJavaScript)
        {
            var probes = new[]
            {
                "sessionStorage.getItem('sessionid')",
                "sessionStorage.getItem('sessionId')",
                "localStorage.getItem('sessionid')",
                "localStorage.getItem('sessionId')"
            };

            foreach (var probe in probes)
            {
                var value = NormalizeJavaScriptValue(await evaluateJavaScript(probe));
                if (!string.IsNullOrEmpty(value))
                    return value;
            }

            return null;
        }

        private static string? NormalizeJavaScriptValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var trimmed = value.Trim();
            if (trimmed == "null" || trimmed == "undefined")
                return null;

            if (trimmed.Length >= 2 && trimmed.StartsWith('"') && trimmed.EndsWith('"'))
            {
                try
                {
                    return JsonSerializer.Deserialize<string>(trimmed) ?? trimmed.Trim('"');
                }
                catch
                {
                    return trimmed.Trim('"');
                }
            }

            return trimmed;
        }

        private static async Task LogCredentialSummaryAsync(ISecureDataStorage secureStorage)
        {
            var cookieString = await secureStorage.GetAsync(COOKIE_KEY);
            var sessionId = await secureStorage.GetAsync(SESSION_ID_KEY);
            var uid = await secureStorage.GetAsync(UID_KEY);
            var aesKey = await secureStorage.GetAsync(AES_KEY_KEY);
            var aesIv = await secureStorage.GetAsync(AES_IV_KEY);
            var studentCode = await secureStorage.GetAsync(STUDENT_CODE_KEY);
            var calendarId = await secureStorage.GetAsync(CALENDAR_ID_KEY);

            var summary = string.Join(", ",
                $"{COOKIE_KEY}={FormatPresence(cookieString, CountCookieItems(cookieString))}",
                $"{SESSION_ID_KEY}={FormatPresence(sessionId)}",
                $"{UID_KEY}={FormatPresence(uid)}",
                $"{AES_KEY_KEY}={FormatPresence(aesKey)}",
                $"{AES_IV_KEY}={FormatPresence(aesIv)}",
                $"{STUDENT_CODE_KEY}={FormatPresence(studentCode)}",
                $"{CALENDAR_ID_KEY}={FormatPresence(calendarId)}");

            Log($"[TongjiProvider] 凭证摘要: {summary}");
        }

        private static string FormatPresence(string? value, int cookieItems = 0)
        {
            if (string.IsNullOrEmpty(value))
                return "missing";

            if (cookieItems > 0)
                return $"present(len={value.Length}, items={cookieItems})";

            return $"present(len={value.Length})";
        }

        private static int CountCookieItems(string? cookieString)
        {
            if (string.IsNullOrWhiteSpace(cookieString))
                return 0;

            return cookieString
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Count(part => part.Contains('='));
        }

        /// <summary>
        /// 从 JSON 顶层安全提取字段值。支持 { "fieldName": "value" } 结构。
        /// </summary>
        private static string? TryExtractTopLevelField(string json, string fieldName)
        {
            try
            {
                using var doc = JsonDocument.Parse(NormalizeJsonPayload(json));
                if (doc.RootElement.TryGetProperty(fieldName, out var prop))
                {
                    return prop.ValueKind switch
                    {
                        JsonValueKind.String => prop.GetString(),
                        JsonValueKind.Number => prop.GetRawText(),
                        _ => prop.GetRawText()
                    };
                }
            }
            catch (JsonException ex)
            {
                Log($"[TongjiProvider] JSON 解析失败: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// 从三层嵌套 JSON 中安全提取字段值。
        /// 支持 { "data": { "schoolCalendar": { "id": 121 } } } 结构。
        /// </summary>
        private static string? TryExtractNestedField(string json, string level1, string level2, string level3)
        {
            try
            {
                using var doc = JsonDocument.Parse(NormalizeJsonPayload(json));
                var root = doc.RootElement;

                if (root.TryGetProperty(level1, out var l1) &&
                    l1.TryGetProperty(level2, out var l2) &&
                    l2.TryGetProperty(level3, out var l3))
                {
                    return l3.ValueKind switch
                    {
                        JsonValueKind.String => l3.GetString(),
                        JsonValueKind.Number => l3.GetRawText(),
                        _ => l3.GetRawText()
                    };
                }
            }
            catch (JsonException ex)
            {
                Log($"[TongjiProvider] JSON 解析失败: {ex.Message}");
            }

            return null;
        }

        private static string NormalizeJsonPayload(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return raw;

            var current = raw.Trim();

            // 最多尝试 3 轮解包，覆盖 "{\"k\":\"v\"}"、{\"k\":\"v\"} 等场景。
            for (int i = 0; i < 3; i++)
            {
                var changed = false;

                if (current.Length >= 2 && current.StartsWith('"') && current.EndsWith('"'))
                {
                    try
                    {
                        var decoded = JsonSerializer.Deserialize<string>(current);
                        if (!string.IsNullOrEmpty(decoded))
                        {
                            current = decoded.Trim();
                            changed = true;
                        }
                    }
                    catch
                    {
                        current = current.Trim('"').Trim();
                        changed = true;
                    }
                }

                if (current.Contains("\\\"", StringComparison.Ordinal))
                {
                    current = current.Replace("\\\"", "\"", StringComparison.Ordinal);
                    changed = true;
                }

                if (current.Contains("\\/", StringComparison.Ordinal))
                {
                    current = current.Replace("\\/", "/", StringComparison.Ordinal);
                    changed = true;
                }

                if (!changed)
                    break;
            }

            return current;
        }

        private static async Task CacheCalendarMetadataAsync(string calendarJson, ISecureDataStorage secureStorage)
        {
            try
            {
                using var doc = JsonDocument.Parse(NormalizeJsonPayload(calendarJson));
                if (!doc.RootElement.TryGetProperty("data", out var data))
                    return;

                if (!data.TryGetProperty("schoolCalendar", out var schoolCalendar))
                    return;

                if (schoolCalendar.TryGetProperty("beginDay", out var beginDay) && beginDay.ValueKind == JsonValueKind.Number)
                {
                    var beginDayMs = beginDay.GetInt64().ToString();
                    await secureStorage.SetAsync(CALENDAR_BEGIN_DAY_MS_KEY, beginDayMs);
                }

                if (schoolCalendar.TryGetProperty("teachingWeekStart", out var teachingWeekStart) && teachingWeekStart.ValueKind == JsonValueKind.Number)
                {
                    await secureStorage.SetAsync(TEACHING_WEEK_START_KEY, teachingWeekStart.GetInt32().ToString());
                }

                if (schoolCalendar.TryGetProperty("teachingWeekEnd", out var teachingWeekEnd) && teachingWeekEnd.ValueKind == JsonValueKind.Number)
                {
                    await secureStorage.SetAsync(TEACHING_WEEK_END_KEY, teachingWeekEnd.GetInt32().ToString());
                }

                Log("[TongjiProvider] 学期元数据已缓存 (beginDay/teachingWeekStart/teachingWeekEnd)");
            }
            catch (Exception ex)
            {
                Log($"[TongjiProvider] 缓存学期元数据失败: {ex.Message}");
            }
        }

        private static void HandleAuthError(HttpStatusCode statusCode)
        {
            if (statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden)
                throw new AuthExpiredException("凭证已失效，请重新登录");
        }
    }
}
