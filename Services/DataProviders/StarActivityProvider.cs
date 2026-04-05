using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using wish_drom.Models;
using wish_drom.Data.Entities;
using wish_drom.Services.Interfaces;

namespace wish_drom.Services.DataProviders
{
    /// <summary>
    /// 同济大学 STAR 活动平台数据提供者
    /// 基于 star.tongji.edu.cn，通过 WebView 登录获取 Bearer Token，再使用原生 HTTP 请求获取活动数据。
    /// 鉴权方式：Authorization: Bearer {token}，Token 从 WebView localStorage 中提取。
    /// </summary>
    public class StarActivityProvider : IDataProvider, IActivityDataStore
    {
        private readonly StarActivityDbContext _dbContext;

        public StarActivityProvider(StarActivityDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        private static void Log(string msg)
        {
            Console.WriteLine(msg);
            Debug.WriteLine(msg);
        }

        // SecureStorage 键名
        private const string TOKEN_KEY = "star_bearer_token";

        // API 端点
        private const string API_BASE = "https://star.tongji.edu.cn";
        private const string ACTIVITY_LIST_API = "/api/app-api/activity/index/list";
        private const int PAGE_SIZE = 10;

        #region IDataProvider 实现

        /// <summary>
        /// 检测 WebView 是否已到达可提取数据的状态。
        /// 登录成功后应跳转到活动主页（非 login 页面，非 SSO 认证页面）。
        /// </summary>
        public bool IsReadyForExtraction(string currentUrl, string html)
        {
            return currentUrl.StartsWith("https://star.tongji.edu.cn", StringComparison.OrdinalIgnoreCase)
                && !currentUrl.Contains("/login", StringComparison.OrdinalIgnoreCase)
                && !currentUrl.Contains("ids.tongji.edu.cn", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 【阶段一】在 WebView 中提取 Bearer Token 或直接获取活动数据。
        /// 返回 "CredentialsStored" 表示 Token 已存储，等待阶段二获取数据；
        /// 返回原始 JSON 表示已直接获取到数据；
        /// 返回 null 表示全部策略失败。
        /// </summary>
        public async Task<string?> ExtractDataAsync(
            string html,
            ISecureDataStorage secureStorage,
            Func<string, Task<string?>>? evaluateJavaScript = null)
        {
            if (evaluateJavaScript == null)
            {
                Log("[StarActivityProvider] JS 执行器为空，无法提取凭证");
                return null;
            }

            try
            {
                // 尝试多策略提取 Token（传入 HTML 用于策略 5 正则搜索）
                var token = await TryGetTokenAsync(evaluateJavaScript, html);
                if (!string.IsNullOrEmpty(token))
                {
                    await secureStorage.SetAsync(TOKEN_KEY, token);
                    Log($"[StarActivityProvider] Bearer Token 已存储 (len={token.Length})");
                    return "CredentialsStored";
                }

                // Token 提取全部失败，尝试 WebView 内直接获取数据回退
                Log("[StarActivityProvider] Token 提取失败，尝试 WebView 数据获取回退");
                var fallbackData = await TryFetchDataInWebViewAsync(evaluateJavaScript);
                if (!string.IsNullOrEmpty(fallbackData))
                {
                    Log($"[StarActivityProvider] WebView 回退成功，数据长度={fallbackData.Length}");
                    return fallbackData;
                }

                Log("[StarActivityProvider] 所有提取策略和回退均失败");
                return null;
            }
            catch (Exception ex)
            {
                Log($"[StarActivityProvider] ExtractDataAsync 异常: {ex}");
                return null;
            }
        }

        /// <summary>
        /// 【阶段二】使用存储的 Bearer Token 发起 HTTP 请求获取活动数据
        /// 分页拉取所有活动（每页 10 条），直到某页返回不足 PAGE_SIZE 条为止。
        /// </summary>
        public async Task<string?> FetchDataAsync(ISecureDataStorage secureStorage)
        {
            var token = await secureStorage.GetAsync(TOKEN_KEY);
            if (string.IsNullOrEmpty(token))
                throw new AuthExpiredException("未找到 STAR 平台登录凭证，请先完成登录");

            var allActivities = new List<string>();
            var pageNo = 1;
            var hasMore = true;

            while (hasMore)
            {
                using var httpClient = CreateHttpClient(token);
                var requestUrl = $"{ACTIVITY_LIST_API}?pageNo={pageNo}&pageSize={PAGE_SIZE}&recommend=1";

                Log($"[StarActivityProvider] 请求活动列表: pageNo={pageNo}");

                var response = await httpClient.GetAsync(requestUrl);

                if (response.StatusCode == HttpStatusCode.Unauthorized ||
                    response.StatusCode == HttpStatusCode.Forbidden)
                {
                    await secureStorage.RemoveAsync(TOKEN_KEY);
                    throw new AuthExpiredException("STAR 平台凭证已失效，请重新登录");
                }

                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();

                using var document = JsonDocument.Parse(content);
                var root = document.RootElement;

                // 检查返回码
                if (!root.TryGetProperty("code", out var codeEl) || codeEl.GetInt32() != 0)
                {
                    var errorCode = codeEl.GetInt32();
                    Log($"[StarActivityProvider] API 返回错误码: {errorCode}");

                    // 业务层面的鉴权失败（HTTP 200 但 code 为 401/403）
                    if (errorCode == 401 || errorCode == 403)
                    {
                        await secureStorage.RemoveAsync(TOKEN_KEY);
                        throw new AuthExpiredException("STAR 平台凭证已失效，请重新登录");
                    }

                    break;
                }

                if (!root.TryGetProperty("data", out var dataObj) ||
                    !dataObj.TryGetProperty("list", out var listArr) ||
                    listArr.ValueKind != JsonValueKind.Array)
                {
                    Log("[StarActivityProvider] 返回数据中未找到 data.list 数组");
                    break;
                }

                // 在 document 还活着时立即提取原始文本，避免 JsonElement 被 Dispose 后访问
                var pageItems = listArr.EnumerateArray()
                    .Select(item => item.GetRawText())
                    .ToList();

                allActivities.AddRange(pageItems);

                Log($"[StarActivityProvider] 第 {pageNo} 页获取 {pageItems.Count} 条活动");

                // 如果本页不足 PAGE_SIZE 条，说明已到最后一页
                if (pageItems.Count < PAGE_SIZE)
                {
                    hasMore = false;
                }
                else
                {
                    pageNo++;
                }
            }

            Log($"[StarActivityProvider] 活动数据获取完成，共 {allActivities.Count} 条");

            // 将所有活动序列化为 JSON 字符串数组返回
            return JsonSerializer.Serialize(allActivities);
        }

        /// <summary>
        /// 将原始活动数据持久化到数据库
        /// </summary>
        public async Task<PersistResult> PersistRawDataAsync(string rawData)
        {
            try
            {
                var activities = ParseActivitiesFromRawData(rawData);
                await ReplaceActivitiesAsync(activities);

                Log($"[StarActivityProvider] 持久化完成，记录数: {activities.Count}");
                return new PersistResult
                {
                    Success = true,
                    SavedRecordCount = activities.Count
                };
            }
            catch (Exception ex)
            {
                Log($"[StarActivityProvider] PersistRawDataAsync 异常: {ex}");
                return new PersistResult
                {
                    Success = false,
                    SavedRecordCount = 0,
                    Error = $"活动入库失败: {ex.Message}"
                };
            }
        }

        #endregion

        #region IActivityDataStore 实现

        public async Task<List<CampusActivity>> GetAllActivitiesAsync(CancellationToken cancellationToken = default)
        {
            return await _dbContext.Activities
                .AsNoTracking()
                .OrderByDescending(a => a.ActivityDate)
                .ToListAsync(cancellationToken);
        }

        #endregion

        #region Token 提取

        /// <summary>
        /// 多策略从 WebView 中提取 Bearer Token
        /// 策略 1: 直接 localStorage/sessionStorage 键名探测
        /// 策略 2: Vue 运行时状态探索（读取内存中已解密的 token）
        /// 策略 3: Vuex 持久化 state 解码
        /// 策略 4: XHR 拦截器（eval 拆分注入）+ 触发请求捕获
        /// 策略 5: HTML 源码中正则搜索 JWT token
        /// </summary>
        private static async Task<string?> TryGetTokenAsync(
            Func<string, Task<string?>> evaluateJavaScript,
            string html)
        {
            // ─── 策略 1：直接 localStorage/sessionStorage 键名 ───
            var tokenKeys = new[] { "token", "access_token", "auth_token", "Authorization", "bearer_token", "star_token" };

            foreach (var key in tokenKeys)
            {
                var value = NormalizeJavaScriptValue(await evaluateJavaScript($"localStorage.getItem('{key}')"));
                if (!string.IsNullOrEmpty(value))
                {
                    Log($"[StarActivityProvider] 策略1命中: localStorage['{key}'] (len={value.Length})");
                    return StripBearerPrefix(value);
                }
            }

            foreach (var key in tokenKeys)
            {
                var value = NormalizeJavaScriptValue(await evaluateJavaScript($"sessionStorage.getItem('{key}')"));
                if (!string.IsNullOrEmpty(value))
                {
                    Log($"[StarActivityProvider] 策略1命中: sessionStorage['{key}'] (len={value.Length})");
                    return StripBearerPrefix(value);
                }
            }

            Log("[StarActivityProvider] 策略1: 未找到直接 token 键名");

            // ─── 策略 2：Vue 运行时状态探索 ───
            var vueToken = await TryGetTokenFromVueStateAsync(evaluateJavaScript);
            if (!string.IsNullOrEmpty(vueToken))
            {
                Log($"[StarActivityProvider] 策略2命中: Vue 运行时状态 (len={vueToken.Length})");
                return vueToken;
            }

            Log("[StarActivityProvider] 策略2: Vue 状态探测失败");

            // ─── 策略 3：Vuex 持久化 state 解码 ───
            var persistedToken = await TryGetTokenFromPersistedStateAsync(evaluateJavaScript);
            if (!string.IsNullOrEmpty(persistedToken))
            {
                Log($"[StarActivityProvider] 策略3命中: Vuex 持久化 state (len={persistedToken.Length})");
                return persistedToken;
            }

            Log("[StarActivityProvider] 策略3: Vuex 持久化 state 解码失败");

            // ─── 策略 4：XHR 拦截器 + 触发请求 ───
            var interceptedToken = await TryGetTokenViaInterceptionAsync(evaluateJavaScript);
            if (!string.IsNullOrEmpty(interceptedToken))
            {
                Log($"[StarActivityProvider] 策略4命中: XHR 拦截 (len={interceptedToken.Length})");
                return interceptedToken;
            }

            Log("[StarActivityProvider] 策略4: XHR 拦截失败");

            // ─── 策略 5：HTML 源码中搜索 JWT token ───
            var htmlToken = TryExtractTokenFromHtml(html);
            if (!string.IsNullOrEmpty(htmlToken))
            {
                Log($"[StarActivityProvider] 策略5命中: HTML 源码搜索 (len={htmlToken.Length})");
                return htmlToken;
            }

            Log("[StarActivityProvider] 策略5: HTML 源码搜索失败");
            Log("[StarActivityProvider] 所有 Token 提取策略均失败");
            return null;
        }

        /// <summary>
        /// 策略 2：通过 Vue 实例的 $store.state 直接读取内存中已解密的 token
        /// 同时探测 Vue 2 (__vue__) 和 Vue 3 (__vue_app__) 模式
        /// </summary>
        private static async Task<string?> TryGetTokenFromVueStateAsync(Func<string, Task<string?>> evaluateJavaScript)
        {
            // 尝试多种选择器和 Vue 版本
            var selectors = new[] { "#app", "#uni-app", "#vue-app", "body > div" };
            string? foundSelector = null;

            foreach (var sel in selectors)
            {
                // Vue 2: __vue__
                var vue2 = NormalizeJavaScriptValue(await evaluateJavaScript(
                    $"document.querySelector('{sel}').__vue__?1:0"));
                if (vue2 == "1")
                {
                    await evaluateJavaScript(
                        $"window.__v=document.querySelector('{sel}').__vue__");
                    foundSelector = sel;
                    break;
                }

                // Vue 3: __vue_app__
                var vue3 = NormalizeJavaScriptValue(await evaluateJavaScript(
                    $"document.querySelector('{sel}').__vue_app__?1:0"));
                if (vue3 == "1")
                {
                    await evaluateJavaScript(
                        $"window.__v=document.querySelector('{sel}').__vue_app__.config.globalProperties.$store");
                    foundSelector = sel;
                    break;
                }
            }

            if (foundSelector == null) return null;

            // 检查 store 是否存在
            var storeExists = NormalizeJavaScriptValue(await evaluateJavaScript(
                "window.__v&&window.__v.$store?1:0"));
            if (storeExists == "1")
            {
                await evaluateJavaScript("window.__vs=window.__v.$store.state");
            }
            else
            {
                // 可能 state 直接就在 __v 上
                storeExists = NormalizeJavaScriptValue(await evaluateJavaScript(
                    "window.__v&&window.__v.state?1:0"));
                if (storeExists == "1")
                {
                    await evaluateJavaScript("window.__vs=window.__v.state");
                }
                else
                {
                    await evaluateJavaScript("delete window.__v");
                    return null;
                }
            }

            // 探测顶层 token 字段
            var topLevelProbes = new[]
            {
                "window.__vs.token||''",
                "window.__vs.accessToken||''",
                "window.__vs.access_token||''",
                "window.__vs.bearerToken||''",
            };

            foreach (var probe in topLevelProbes)
            {
                var value = NormalizeJavaScriptValue(await evaluateJavaScript(probe));
                if (!string.IsNullOrEmpty(value))
                {
                    await evaluateJavaScript("delete window.__v");
                    await evaluateJavaScript("delete window.__vs");
                    return StripBearerPrefix(value);
                }
            }

            // 探测嵌套模块
            var nestedProbes = new[]
            {
                "window.__vs.user?window.__vs.user.token||'':''",
                "window.__vs.user?window.__vs.user.accessToken||'':''",
                "window.__vs.auth?window.__vs.auth.token||'':''",
                "window.__vs.login?window.__vs.login.token||'':''",
                "window.__vs.account?window.__vs.account.token||'':''",
            };

            foreach (var probe in nestedProbes)
            {
                var value = NormalizeJavaScriptValue(await evaluateJavaScript(probe));
                if (!string.IsNullOrEmpty(value))
                {
                    await evaluateJavaScript("delete window.__v");
                    await evaluateJavaScript("delete window.__vs");
                    return StripBearerPrefix(value);
                }
            }

            // 尝试 getApp() (uni-app 专有)
            var getAppResult = NormalizeJavaScriptValue(await evaluateJavaScript(
                "typeof getApp==='undefined'?'':getApp().$scope.$store.state.token||''"));
            if (!string.IsNullOrEmpty(getAppResult))
            {
                await evaluateJavaScript("delete window.__v");
                await evaluateJavaScript("delete window.__vs");
                return StripBearerPrefix(getAppResult);
            }

            // 清理
            await evaluateJavaScript("delete window.__v");
            await evaluateJavaScript("delete window.__vs");
            return null;
        }

        /// <summary>
        /// 策略 3：尝试从 Vuex 持久化存储键中解码 token
        /// </summary>
        private static async Task<string?> TryGetTokenFromPersistedStateAsync(
            Func<string, Task<string?>> evaluateJavaScript)
        {
            var vuexKeys = new[] { "vuex", "store", "persistedState", "vuexPersist" };

            foreach (var key in vuexKeys)
            {
                var raw = NormalizeJavaScriptValue(await evaluateJavaScript(
                    $"localStorage.getItem('{key}')"));
                if (string.IsNullOrEmpty(raw)) continue;

                // 尝试直接 JSON 解析
                var token = TryExtractTokenFromJson(raw);
                if (!string.IsNullOrEmpty(token))
                {
                    Log($"[StarActivityProvider] 策略3: 从 localStorage['{key}'] JSON 解析成功");
                    return token;
                }

                // 尝试 base64 解码后 JSON 解析
                try
                {
                    var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(raw));
                    token = TryExtractTokenFromJson(decoded);
                    if (!string.IsNullOrEmpty(token))
                    {
                        Log($"[StarActivityProvider] 策略3: 从 localStorage['{key}'] base64 解码成功");
                        return token;
                    }
                }
                catch { /* base64 解码失败，跳过 */ }
            }

            return null;
        }

        /// <summary>
        /// 从 JSON 字符串中搜索 token 相关字段
        /// </summary>
        private static string? TryExtractTokenFromJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // 检查顶层字段
                foreach (var field in new[] { "token", "accessToken", "access_token", "bearerToken" })
                {
                    if (root.TryGetProperty(field, out var prop) && prop.ValueKind == JsonValueKind.String)
                    {
                        var value = prop.GetString();
                        if (!string.IsNullOrEmpty(value)) return StripBearerPrefix(value);
                    }
                }

                // 检查嵌套对象
                foreach (var parentField in new[] { "user", "auth", "login", "account" })
                {
                    if (root.TryGetProperty(parentField, out var parent) && parent.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var field in new[] { "token", "accessToken", "access_token" })
                        {
                            if (parent.TryGetProperty(field, out var prop) && prop.ValueKind == JsonValueKind.String)
                            {
                                var value = prop.GetString();
                                if (!string.IsNullOrEmpty(value)) return StripBearerPrefix(value);
                            }
                        }
                    }
                }
            }
            catch { /* JSON 解析失败，跳过 */ }

            return null;
        }

        /// <summary>
        /// 策略 4：使用 eval 拆分注入 XHR 拦截器，然后触发 SPA 请求来捕获 Authorization header。
        /// 核心改进：用 String.fromCharCode 拼接 "function" 关键字，绕过 JS 超时限制。
        /// 不调用 location.reload()，避免销毁 WebView 上下文。
        /// </summary>
        private static async Task<string?> TryGetTokenViaInterceptionAsync(
            Func<string, Task<string?>> evaluateJavaScript)
        {
            // Step 1: 保存原始 setRequestHeader（72 chars, 简单路径）
            var saveResult = await evaluateJavaScript(
                "XMLHttpRequest.prototype.sH=XMLHttpRequest.prototype.setRequestHeader");
            if (string.IsNullOrEmpty(saveResult))
            {
                Log("[StarActivityProvider] 策略4: 保存原始 setRequestHeader 返回空，可能超时");
            }

            // Step 2: 用 String.fromCharCode 构建 "function" 关键字（66 chars, 简单路径）
            // "function" = f(102) u(117) n(110) c(99) t(116) i(105) o(111) n(110)
            await evaluateJavaScript(
                "window.__fn=String.fromCharCode(102,117,110,99,116,105,111,110)");

            // Step 3: 拼接拦截器函数体（每行 < 80 chars, 简单路径）
            await evaluateJavaScript(
                "window.__xf=window.__fn+'(k,v){'");
            await evaluateJavaScript(
                "window.__xf+='if(k.toLowerCase()==\"authorization\")window.__xhr_t=v;'");
            await evaluateJavaScript(
                "window.__xf+='return this.sH.apply(this,arguments)}'");

            // Step 4: 初始化捕获变量
            await evaluateJavaScript("window.__xhr_t=''");

            // Step 5: 构建完整赋值语句并 eval（简单路径, 不含 trigger words）
            await evaluateJavaScript(
                "window.__xa='XMLHttpRequest.prototype.setRequestHeader='");
            await evaluateJavaScript(
                "eval(window.__xa+window.__xf)");

            Log("[StarActivityProvider] 策略4: XHR 拦截器已注入（eval 拆分模式）");

            // Step 6: 触发 SPA 发起请求
            // 6a: 尝试 uni.request
            var uniAvail = NormalizeJavaScriptValue(await evaluateJavaScript(
                "typeof uni!=='undefined'&&uni.request?1:0"));

            if (uniAvail == "1")
            {
                await evaluateJavaScript("window.__fb=''");
                await evaluateJavaScript("window.__uc={}");
                await evaluateJavaScript(
                    "window.__uc.url='/api/app-api/activity/index/list'");
                await evaluateJavaScript(
                    "window.__uc.data={pageNo:1,pageSize:1,recommend:1}");
                await evaluateJavaScript(
                    "window.__uc.success=window.__fn+'(r){window.__fb=JSON.stringify(r.data)}'");
                // 用 eval 构建 success 回调
                await evaluateJavaScript(
                    "window.__uc.success=eval(window.__fn+'(r){window.__fb=JSON.stringify(r.data)}')");
                await evaluateJavaScript("uni.request(window.__uc)");
                Log("[StarActivityProvider] 策略4: uni.request 已发起");
            }

            // 6b: 触发 hash 变化（可能触发 SPA 路由重载数据）
            await evaluateJavaScript(
                "location.hash='__probe__'+Date.now()");

            // Step 7: 轮询捕获的 token（最多 10 秒）
            for (int attempt = 0; attempt < 10; attempt++)
            {
                await Task.Delay(1000);
                var xhrToken = NormalizeJavaScriptValue(
                    await evaluateJavaScript("window.__xhr_t||''"));
                if (!string.IsNullOrEmpty(xhrToken))
                {
                    Log($"[StarActivityProvider] 策略4: Token 在 {attempt + 1}s 后被拦截");
                    return StripBearerPrefix(xhrToken);
                }
            }

            Log("[StarActivityProvider] 策略4: XHR 拦截轮询超时");
            return null;
        }

        /// <summary>
        /// 策略 5：从 HTML 源码中正则搜索 JWT token（以 eyJ 开头的三段式 base64）
        /// </summary>
        private static string? TryExtractTokenFromHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;

            // JWT token 格式: eyJxxxxx.eyJxxxxx.xxxxx (三段 base64url)
            var jwtMatch = System.Text.RegularExpressions.Regex.Match(
                html, @"eyJ[A-Za-z0-9_-]+\.eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+");

            if (jwtMatch.Success)
            {
                Log($"[StarActivityProvider] 策略5: 在 HTML 中发现 JWT token (len={jwtMatch.Value.Length})");
                return jwtMatch.Value;
            }

            // 也搜索 Bearer xxxxx 模式
            var bearerMatch = System.Text.RegularExpressions.Regex.Match(
                html, @"Bearer\s+(eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+)");
            if (bearerMatch.Success)
            {
                Log($"[StarActivityProvider] 策略5: 在 HTML 中发现 Bearer token (len={bearerMatch.Groups[1].Value.Length})");
                return bearerMatch.Groups[1].Value;
            }

            return null;
        }

        /// <summary>
        /// 回退方案：当 Token 提取全部失败时，尝试直接在 WebView 内通过原生 fetch 获取活动数据。
        /// WebView 中已登录，fetch 可能通过 cookie/session 鉴权成功。
        /// </summary>
        private static async Task<string?> TryFetchDataInWebViewAsync(
            Func<string, Task<string?>> evaluateJavaScript)
        {
            try
            {
                // 先检查策略 4 中是否已有数据
                var existingResult = NormalizeJavaScriptValue(
                    await evaluateJavaScript("window.__fb||''"));
                if (!string.IsNullOrEmpty(existingResult) && !existingResult.StartsWith("ERR:"))
                {
                    Log("[StarActivityProvider] 回退: 使用策略4中已获取的响应数据");
                    return ExtractActivityListFromResponse(existingResult);
                }

                // 尝试用原生 fetch 直接调用 API（可能通过 cookie 鉴权）
                await evaluateJavaScript("window.__fd=''");

                // 使用 eval 构建 fetch 调用（避免 async/await 关键字触发超时）
                await evaluateJavaScript(
                    "window.__fu='/api/app-api/activity/index/list?pageNo=1&pageSize=10&recommend=1'");
                await evaluateJavaScript(
                    "window.__fh={Platform:'h5',Accept:'application/json'}");

                // 用 fetch + then 回调存储结果
                var fetchExpr = "fetch(window.__fu,{headers:window.__fh})" +
                    ".then(function(r){return r.text()})" +
                    ".then(function(d){window.__fd=d})";
                await evaluateJavaScript(fetchExpr);

                Log("[StarActivityProvider] 回退: 原生 fetch 已发起");

                // 轮询响应（最多 15 秒）
                for (int attempt = 0; attempt < 15; attempt++)
                {
                    await Task.Delay(1000);
                    var result = NormalizeJavaScriptValue(
                        await evaluateJavaScript("window.__fd||''"));

                    if (string.IsNullOrEmpty(result)) continue;

                    Log($"[StarActivityProvider] 回退: 获取到响应数据 (len={result.Length})");
                    return ExtractActivityListFromResponse(result);
                }

                Log("[StarActivityProvider] 回退: 原生 fetch 轮询超时");
                return null;
            }
            catch (Exception ex)
            {
                Log($"[StarActivityProvider] 回退异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 从 API 响应 JSON 中提取活动列表数组，序列化为与 FetchDataAsync 相同的格式
        /// </summary>
        private static string? ExtractActivityListFromResponse(string responseJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseJson);
                if (!doc.RootElement.TryGetProperty("data", out var data)) return responseJson;
                if (!data.TryGetProperty("list", out var list)) return responseJson;
                if (list.ValueKind != JsonValueKind.Array) return responseJson;

                var items = list.EnumerateArray()
                    .Select(e => e.GetRawText())
                    .ToList();
                return JsonSerializer.Serialize(items);
            }
            catch
            {
                return responseJson;
            }
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

        /// <summary>
        /// 去除 Token 前的 "Bearer " 前缀（如果存在）
        /// </summary>
        private static string? StripBearerPrefix(string? token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return token.Substring(7);
            return token;
        }

        #endregion

        #region HTTP 工具方法

        private static HttpClient CreateHttpClient(string token)
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

            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
            client.DefaultRequestHeaders.Add("Platform", "h5");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36");

            return client;
        }

        #endregion

        #region 数据解析与持久化

        /// <summary>
        /// 解析活动列表 JSON 为 CampusActivity 列表
        /// JSON 结构: [ "{...}", "{...}" ] (FetchDataAsync 序列化的字符串数组)
        /// 每个活动对象包含: id, title, addr, activityStartTime, activityEndTime,
        /// mainBoardUnit, progress.name, points, photo, pageViews 等
        /// </summary>
        private static List<CampusActivity> ParseActivitiesFromRawData(string rawData)
        {
            var result = new List<CampusActivity>();

            try
            {
                // FetchDataAsync 返回的是字符串数组的 JSON 序列化
                using var document = JsonDocument.Parse(rawData);
                var root = document.RootElement;

                if (root.ValueKind != JsonValueKind.Array)
                {
                    Log("[StarActivityProvider] rawData 不是数组");
                    return result;
                }

                var now = DateTime.Now;

                foreach (var item in root.EnumerateArray())
                {
                    // innerDoc 需要在整个处理逻辑完成后才释放，用 try/finally 控制生命周期
                    JsonDocument? innerDoc = null;
                    try
                    {
                        JsonElement activityObj;
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            innerDoc = JsonDocument.Parse(item.GetString()!);
                            activityObj = innerDoc.RootElement;
                        }
                        else
                        {
                            activityObj = item;
                        }

                        var title = GetString(activityObj, "title");
                        if (string.IsNullOrWhiteSpace(title))
                            continue;

                        var activity = new CampusActivity
                        {
                            Title = title,
                            Source = GetString(activityObj, "mainBoardUnit") ?? "STAR平台",
                            ActivityDate = GetTimestampAsDateTime(activityObj, "activityStartTime") ?? DateTime.MinValue,
                            Location = GetString(activityObj, "addr"),
                            Link = BuildActivityLink(GetLong(activityObj, "id")),
                            SyncTime = now
                        };

                        // 构建描述信息：状态 + 学分 + 浏览量
                        var descParts = new List<string>();
                        var progressName = GetNestedString(activityObj, "progress", "name");
                        if (!string.IsNullOrEmpty(progressName))
                            descParts.Add(progressName);
                        var points = GetDouble(activityObj, "points");
                        if (points > 0)
                            descParts.Add($"学分: {points}");
                        var pageViews = GetInt(activityObj, "pageViews");
                        if (pageViews > 0)
                            descParts.Add($"浏览: {pageViews}");
                        if (descParts.Count > 0)
                            activity.Description = string.Join(" | ", descParts);

                        result.Add(activity);
                    }
                    finally
                    {
                        innerDoc?.Dispose();
                    }
                }
            }
            catch (JsonException ex)
            {
                Log($"[StarActivityProvider] JSON 解析失败: {ex.Message}");
            }

            return result;
        }

        private async Task ReplaceActivitiesAsync(List<CampusActivity> activities)
        {
            var oldRecords = await _dbContext.Activities.ToListAsync();
            _dbContext.Activities.RemoveRange(oldRecords);
            if (activities.Count > 0)
            {
                await _dbContext.Activities.AddRangeAsync(activities);
            }
            await _dbContext.SaveChangesAsync();
        }

        /// <summary>
        /// 根据活动 ID 构造详情页链接
        /// </summary>
        private static string? BuildActivityLink(long? id)
        {
            if (id == null || id <= 0) return null;
            return $"https://star.tongji.edu.cn/app/pages/activity/detail?id={id}";
        }

        private static string? GetString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var value))
                return null;

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => value.ToString()
            };
        }

        private static long? GetLong(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var value))
                return null;

            if (value.ValueKind == JsonValueKind.Number)
                return value.GetInt64();

            if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsed))
                return parsed;

            return null;
        }

        private static int? GetInt(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var value))
                return null;

            if (value.ValueKind == JsonValueKind.Number)
                return value.GetInt32();

            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
                return parsed;

            return null;
        }

        private static double? GetDouble(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var value))
                return null;

            if (value.ValueKind == JsonValueKind.Number)
                return value.GetDouble();

            if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out var parsed))
                return parsed;

            return null;
        }

        /// <summary>
        /// 从嵌套 JSON 对象中提取字符串值，如 progress.name
        /// </summary>
        private static string? GetNestedString(JsonElement element, string level1, string level2)
        {
            if (!element.TryGetProperty(level1, out var l1))
                return null;

            if (l1.ValueKind != JsonValueKind.Object)
                return null;

            if (!l1.TryGetProperty(level2, out var l2))
                return null;

            return l2.ValueKind == JsonValueKind.String ? l2.GetString() : null;
        }

        /// <summary>
        /// 将毫秒时间戳字段转换为 DateTime
        /// </summary>
        private static DateTime? GetTimestampAsDateTime(JsonElement element, string propertyName)
        {
            var ms = GetLong(element, propertyName);
            if (ms == null || ms <= 0)
                return null;

            return DateTimeOffset.FromUnixTimeMilliseconds(ms.Value).ToLocalTime().DateTime;
        }

        #endregion
    }
}
