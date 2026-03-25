# 数据源接入指南

本文档说明如何为智能校园助手应用接入新的数据源（如学校教务系统）。

## 架构概述

系统支持两种数据获取模式：

- **HTML 解析模式**：直接解析 WebView 页面的 HTML 内容
- **API 请求模式（双阶段）**：先在 WebView 中登录并提取凭证，再用原生 HTTP 请求调用 API

```
┌─────────────────────────────────────────────────────────────┐
│                    App (已实现)                               │
│  ┌─────────────┐  ┌──────────────┐  ┌─────────────────┐    │
│  │ Sync.razor  │  │DataCaptureSvc│  │ SecureStorage   │    │
│  │  (UI展示)   │◄─│(WebView管理) │◄─│ (Cookie加密存储) │    │
│  └─────────────┘  └───────┬──────┘  └─────────────────┘    │
│                           │                                 │
└───────────────────────────┼─────────────────────────────────┘
                            │ 调用接口
                            ▼
              ┌─────────────────────────────┐
              │   IDataProvider (你需要实现)  │
              ├─────────────────────────────┤
              │ • IsReadyForExtraction()     │
              │ • ExtractDataAsync()        │
              │ • FetchDataAsync()          │
              │ • QueryDataAsync()          │
              └─────────────────────────────┘
```

## 你需要实现的接口

### IDataProvider 接口

命名空间：`wish_drom.Models`

```csharp
public interface IDataProvider
{
    /// <summary>
    /// 检查 WebView 当前页面是否已完成登录/数据加载
    /// </summary>
    bool IsReadyForExtraction(string currentUrl, string html);

    /// <summary>
    /// 【阶段一】在 WebView 上下文中执行。
    /// HTML 解析模式：直接从 html 参数解析数据并返回 JSON。
    /// API 模式：利用 evaluateJavaScript 提取凭证（Cookie 等）并存储。
    /// </summary>
    /// <param name="html">页面 HTML 内容</param>
    /// <param name="secureStorage">安全存储接口</param>
    /// <param name="evaluateJavaScript">
    /// WebView JS 执行委托（可选），支持在 WebView 中运行异步脚本。
    /// 传入 JS 表达式，返回执行结果字符串。
    /// </param>
    /// <returns>
    /// - 业务数据 JSON（HTML 解析模式）
    /// - "CredentialsStored"（API 模式，凭证已存储）
    /// - null（失败）
    /// </returns>
    Task<string?> ExtractDataAsync(
        string html,
        ISecureDataStorage secureStorage,
        Func<string, Task<string?>>? evaluateJavaScript = null
    );

    /// <summary>
    /// 【阶段二·可选】在原生上下文中执行，使用存储的凭证发起 HTTP 请求。
    /// 默认返回 null（HTML 解析模式不需要实现）。
    /// </summary>
    /// <exception cref="AuthExpiredException">凭证过期时抛出</exception>
    Task<string?> FetchDataAsync(ISecureDataStorage secureStorage);

    /// <summary>
    /// 将原始数据转换为标准格式（可选实现，默认返回原始数据）
    /// </summary>
    Task<string?> QueryDataAsync(string rawData);
}
```

### ISecureDataStorage 接口

用于安全存储 Cookie 等敏感信息：

```csharp
public interface ISecureDataStorage
{
    Task SetAsync(string key, string value);      // 保存
    Task<string?> GetAsync(string key);           // 获取
    Task RemoveAsync(string key);                 // 删除
    Task<bool> ContainsKeyAsync(string key);       // 检查是否存在
    Task ClearAsync();                            // 清空
}
```

## 实现方式一：HTML 解析模式

适用于数据直接展示在 HTML 页面中的场景。

### 创建提供者类

```csharp
using wish_drom.Models;
using wish_drom.Services.Interfaces;
using System.Text.Json;

namespace wish_drom.Services.DataProviders
{
    public class MySchoolProvider : IDataProvider
    {
        public bool IsReadyForExtraction(string currentUrl, string html)
        {
            return currentUrl.Contains("/timetable") || html.Contains("课表查询");
        }

        public async Task<string?> ExtractDataAsync(
            string html,
            ISecureDataStorage secureStorage,
            Func<string, Task<string?>>? evaluateJavaScript = null)
        {
            // 直接解析 HTML，不需要 evaluateJavaScript
            var courses = ParseHtml(html);
            return JsonSerializer.Serialize(courses);
        }

        // FetchDataAsync 和 QueryDataAsync 使用默认实现即可
    }
}
```

## 实现方式二：API 请求模式（双阶段）

适用于数据需要通过 API 获取、且鉴权依赖 WebView 登录态的场景（如同济大学教务系统）。

### 工作流程

```
1. 用户点击数据源
2. DataCaptureService 先尝试 FetchDataAsync（使用缓存凭证）
   ├── 成功 → 直接返回数据（静默同步）
   └── 失败/凭证过期 → 进入 WebView 登录流程
3. 用户在 WebView 中完成登录
4. IsReadyForExtraction 检测到登录完成
5. 用户点击"提取数据"
6. ExtractDataAsync 通过 JS 提取 Cookie、sessionid（X-Token）、sessiondata（uid/aesKey/aesIv）
7. C# 端使用 AES-CBC-PKCS7 加密 uid 生成 studentCode 并存储
8. FetchDataAsync 使用存储的 Cookie + X-Token + 加密 studentCode 调用 API
9. 返回原始 JSON 数据
```

### 完整示例：同济大学课表

```csharp
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using wish_drom.Models;
using wish_drom.Services.Interfaces;

namespace wish_drom.Services.DataProviders
{
    public class TongjiScheduleProvider : IDataProvider
    {
        private const string API_BASE = "https://1.tongji.edu.cn";
        private const string CALENDAR_API_PATH = "/api/baseresservice/schoolCalendar/currentTermCalendar";
        private const string TIMETABLE_API_PATH = "/api/electionservice/reportManagement/findStudentTimetab";

        private const string COOKIE_KEY = "tongji_cookies";
        private const string SESSION_ID_KEY = "tongji_session_id";   // X-Token
        private const string STUDENT_CODE_KEY = "tongji_student_code";
        private const string CALENDAR_ID_KEY = "tongji_calendar_id";
        private const string AES_KEY_KEY = "tongji_aes_key";
        private const string AES_IV_KEY = "tongji_aes_iv";
        private const string UID_KEY = "tongji_uid";

        public bool IsReadyForExtraction(string currentUrl, string html)
        {
            return currentUrl.StartsWith("https://1.tongji.edu.cn", StringComparison.OrdinalIgnoreCase)
                && !currentUrl.Contains("ids.tongji.edu.cn", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<string?> ExtractDataAsync(
            string html,
            ISecureDataStorage secureStorage,
            Func<string, Task<string?>>? evaluateJavaScript = null)
        {
            if (evaluateJavaScript == null) return null;

            // 1. 提取 Cookie
            var cookies = await evaluateJavaScript("document.cookie");
            if (string.IsNullOrEmpty(cookies)) return null;
            await secureStorage.SetAsync(COOKIE_KEY, cookies);

            // 2. 提取 sessionid（X-Token 请求头）
            var sessionId = await evaluateJavaScript("sessionStorage.getItem('sessionid')");
            if (!string.IsNullOrEmpty(sessionId))
                await secureStorage.SetAsync(SESSION_ID_KEY, sessionId);

            // 3. 从 localStorage 的 sessiondata 提取 uid / aesKey / aesIv，
            //    然后用 AES-CBC-PKCS7 加密生成 studentCode
            var sessionData = await evaluateJavaScript("localStorage.getItem('sessiondata')");
            // 解析 JSON → 提取 uid, aesKey, aesIv → EncryptStudentCode(uid, aesKey, aesIv) ...

            // 4. 获取当前学期 calendarId
            var calendarJson = await evaluateJavaScript(@"
                fetch('/api/baseresservice/schoolCalendar/currentTermCalendar?_t=' + Date.now(), {
                    credentials: 'include',
                    headers: { 'Accept': 'application/json' }
                }).then(r => r.json()).then(d => JSON.stringify(d))
            ");
            // 解析并缓存 calendarId ...

            return "CredentialsStored";
        }

        public async Task<string?> FetchDataAsync(ISecureDataStorage secureStorage)
        {
            var cookies = await secureStorage.GetAsync(COOKIE_KEY);
            if (string.IsNullOrEmpty(cookies))
                throw new AuthExpiredException("未找到登录凭证");

            var sessionId = await secureStorage.GetAsync(SESSION_ID_KEY) ?? "";
            var studentCode = await secureStorage.GetAsync(STUDENT_CODE_KEY);
            // studentCode 已是 AES 加密后的值，可直接用于 URL 参数

            using var client = new HttpClient { BaseAddress = new Uri(API_BASE) };
            client.DefaultRequestHeaders.Add("Cookie", cookies);
            client.DefaultRequestHeaders.Add("X-Token", sessionId);

            var response = await client.GetAsync(
                $"{TIMETABLE_API_PATH}?calendarId=...&studentCode={studentCode}&_t=..."
            );

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new AuthExpiredException("凭证已失效");

            return await response.Content.ReadAsStringAsync();
        }

        // AES-CBC-PKCS7 加密（逆向自前端 hpw7 模块）：
        // encodeURIComponent(uid) → AES-CBC-PKCS7 → Base64 → encodeURIComponent
        // 密钥经 ParamHandler 相邻字符交换处理
    }
}
```

### 关键技术：JS 执行委托

`evaluateJavaScript` 委托允许 Provider 在 WebView 中执行异步 JavaScript，自动携带页面的 Cookie 和 Session。

底层实现使用全局变量 + 轮询模式，解决 Android WebView 不自动 await Promise 的跨平台问题：

```
JS 表达式 → 包装为 IIFE → 结果写入 window['__key__'] → 轮询读取
```

使用示例：

```csharp
// 获取 Cookie
var cookies = await evaluateJavaScript("document.cookie");

// 调用 API（fetch 自动携带 WebView Cookie）
var data = await evaluateJavaScript(@"
    fetch('/api/some-endpoint', { credentials: 'include' })
        .then(r => r.json())
        .then(d => JSON.stringify(d))
");
```

## 注册数据源

在 `MauiProgram.cs` 的 `CreateMauiApp()` 方法中注册：

```csharp
var app = builder.Build();

var dataCaptureService = app.Services.GetRequiredService<IDataCaptureService>();

// 注册你的数据源
dataCaptureService.RegisterProvider(
    id: "myschool",                                    // 唯一标识
    displayName: "我的学校",                             // 显示名称
    url: "https://myschool.edu.cn/login",              // WebView 打开的 URL
    provider: new MySchoolProvider(),                    // 你的实现
    faviconUrl: "https://myschool.edu.cn/favicon.ico",  // 图标（可选）
    toolDescription: "查询我的学校课表"                   // LLM 工具描述（可选）
);

return app;
```

### 已注册的数据源

| ID | 名称 | URL | 模式 |
|---|---|---|---|
| `tongji-schedule` | 同济大学课表 | `https://1.tongji.edu.cn` | API 请求（双阶段） |

## 工作流程

### HTML 解析模式

1. **用户点击数据源** → App 打开 WebView，加载你提供的 URL
2. **用户登录** → WebView 导航过程中，App 持续调用 `IsReadyForExtraction()`
3. **检测到数据页面** → "提取数据"按钮变为可用状态
4. **用户点击提取** → App 调用 `ExtractDataAsync()`，传入 HTML
5. **返回 JSON** → 数据被保存到本地

### API 请求模式（双阶段）

1. **用户点击数据源** → App 先尝试 `FetchDataAsync()` 静默获取
2. **凭证有效** → 直接返回数据，无需打开 WebView
3. **凭证失效/不存在** → 打开 WebView，用户完成登录
4. **登录完成** → `ExtractDataAsync()` 提取并存储凭证
5. **凭证存储后** → `FetchDataAsync()` 使用凭证调用 API
6. **返回 JSON** → 数据被保存到本地

## 异常处理

### AuthExpiredException

当凭证过期时，`FetchDataAsync` 应抛出 `AuthExpiredException`，`DataCaptureService` 会自动降级到 WebView 登录流程。

```csharp
if (response.StatusCode == HttpStatusCode.Unauthorized)
    throw new AuthExpiredException("Cookie 已失效，请重新登录");
```

## 测试建议

### 1. 单元测试 IsReadyForExtraction

```csharp
[Test]
public void TestIsReadyForExtraction()
{
    var provider = new TongjiScheduleProvider();

    // 统一认证页面 → 未就绪
    Assert.False(provider.IsReadyForExtraction(
        "https://ids.tongji.edu.cn/idp/authcenter/...", "..."));

    // 回到 1.tongji.edu.cn → 就绪
    Assert.True(provider.IsReadyForExtraction(
        "https://1.tongji.edu.cn/index", "..."));
}
```

### 2. 真机测试流程

1. 注册数据源（已在 `MauiProgram.cs` 中完成）
2. 启动应用
3. 进入"数据同步"页面
4. 点击"同济大学课表"
5. 在 WebView 中完成统一身份认证登录
6. 等待重定向回 1.tongji.edu.cn
7. "提取数据"按钮变为可用
8. 点击提取，检查控制台输出
9. 验证返回的 JSON 数据

### 3. API 端点确认

**实际使用前需通过浏览器 DevTools 抓包确认 API 路径**：

1. 在 PC 浏览器登录 [1.tongji.edu.cn](https://1.tongji.edu.cn)
2. 打开 DevTools → Network → Fetch/XHR
3. 进入课表页面，确认以下接口的实际路径和响应格式：
   - 用户信息接口（获取 studentCode）
   - 学期日历接口（获取 calendarId）
   - 课表查询接口
4. 在 `TongjiScheduleProvider.cs` 中更新常量

## 常见问题

### Q: 如何处理需要多次跳转的登录流程？

A: 在 `IsReadyForExtraction` 中检查最终目标页面的特征，而不是中间页面。

### Q: HttpOnly Cookie 无法通过 document.cookie 获取怎么办？

A: 如果关键鉴权 Cookie 是 HttpOnly 的，需要在原生层通过平台 API 提取：
- Android: `Android.Webkit.CookieManager.Instance.GetCookie(url)`
- iOS: `WKWebsiteDataStore.Default.HttpCookieStore`

### Q: 如何处理验证码？

A: WebView 是可见的，用户可以手动完成验证码。你只需要在验证码通过后检测到目标页面。

### Q: 如何处理凭证过期？

A: 在 `FetchDataAsync` 中捕获 401/403 响应，抛出 `AuthExpiredException`。`DataCaptureService` 会自动切换到 WebView 登录流程重新获取凭证。

### Q: Cookie 会自动清除吗？

A: App 提供 `ClearAllDataAsync()` 方法，用户可以手动清除。Cookie 使用 MAUI 的 SecureStorage 加密存储。

---

**最后更新**：2026年3月
