# 数据源接入指南

本文档说明如何为智能校园助手应用接入新的数据源（如学校教务系统）。

## 架构概述

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
    /// <param name="currentUrl">当前 WebView URL</param>
    /// <param name="html">当前页面 HTML 内容</param>
    /// <returns>true 表示可以开始提取数据（启用"提取数据"按钮）</returns>
    bool IsReadyForExtraction(string currentUrl, string html);

    /// <summary>
    /// 从页面中提取数据并返回 JSON
    /// </summary>
    /// <param name="html">页面 HTML 内容</param>
    /// <param name="secureStorage">安全存储接口，可用于保存 Cookie 等敏感信息</param>
    /// <returns>JSON 字符串</returns>
    Task<string> ExtractDataAsync(string html, ISecureDataStorage secureStorage);

    /// <summary>
    /// 解析返回的 JSON 数据，用于 LLM 工具调用（可选实现）
    /// </summary>
    /// <param name="jsonData">之前保存的 JSON 数据</param>
    /// <param name="query">用户查询（如"今天有什么课？"）</param>
    /// <returns>自然语言回复</returns>
    Task<string> QueryDataAsync(string jsonData, string query);
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

## 实现步骤

### 第一步：创建提供者类

```csharp
using wish_drom.Models;
using wish_drom.Services.Interfaces;
using System.Text.Json;

namespace MySchool.DataProvider
{
    public class MySchoolProvider : IDataProvider
    {
        // 你的实现...
    }
}
```

### 第二步：实现 IsReadyForExtraction

判断用户是否已经到达可以提取数据的页面：

```csharp
public bool IsReadyForExtraction(string currentUrl, string html)
{
    // 方法1: 检查 URL
    if (currentUrl.Contains("/timetable") || currentUrl.Contains("/schedule"))
        return true;

    // 方法2: 检查页面内容
    if (html.Contains("课表查询") || html.Contains("我的课程"))
        return true;

    // 方法3: 检查特定的 HTML 元素
    if (html.Contains("<table class=\"course-list\">"))
        return true;

    return false;
}
```

### 第三步：实现 ExtractDataAsync

从 HTML 中提取数据并返回 JSON：

```csharp
public async Task<string> ExtractDataAsync(string html, ISecureDataStorage secureStorage)
{
    // 1. 保存 Cookie（如果需要）
    await secureStorage.SetAsync("my_school_cookies", "your_cookies");

    // 2. 解析 HTML 数据
    var courses = ParseHtml(html);

    // 3. 返回 JSON
    return JsonSerializer.Serialize(courses);
}

private List<CourseData> ParseHtml(string html)
{
    var courses = new List<CourseData>();

    // 使用 HtmlAgilityPack 或正则表达式解析 HTML
    // 示例：使用正则
    var pattern = @"<td class=""course-name"">([^<]+)</td>";
    foreach (Match match in Regex.Matches(html, pattern))
    {
        courses.Add(new CourseData
        {
            Name = match.Groups[1].Value,
            // 其他字段...
        });
    }

    return courses;
}

// 定义你的数据结构
public class CourseData
{
    public string Name { get; set; }
    public string Location { get; set; }
    public string Time { get; set; }
    // 更多字段...
}
```

### 第四步：（可选）实现 QueryDataAsync

用于 LLM 工具直接查询已保存的数据：

```csharp
public async Task<string> QueryDataAsync(string jsonData, string query)
{
    // 1. 反序列化保存的 JSON
    var courses = JsonSerializer.Deserialize<List<CourseData>>(jsonData);

    // 2. 根据查询处理
    if (query.Contains("今天") || query.Contains("课表"))
    {
        var today = DateTime.Now.DayOfWeek;
        var todayCourses = courses.Where(c => IsToday(c, today)).ToList();

        if (todayCourses.Count == 0)
            return "今天没有课程。";

        return $"今天有 {todayCourses.Count} 门课：" +
               string.Join("、", todayCourses.Select(c => c.Name));
    }

    return "抱歉，我不理解这个问题。";
}
```

## 注册数据源

### 方法一：在 MauiProgram.cs 中注册

```csharp
// 在 MauiProgram.cs 的 CreateMauiApp() 方法中
var dataCaptureService = builder.Services.GetRequiredService<IDataCaptureService>();

// 注册你的数据源
dataCaptureService.RegisterProvider(
    id: "myschool",                                    // 唯一标识
    displayName: "我的学校",                             // 显示名称
    url: "https://myschool.edu.cn/timetable",          // 登录 URL
    provider: new MySchoolProvider(),                    // 你的实现
    faviconUrl: "https://myschool.edu.cn/favicon.ico",  // 图标（可选）
    toolDescription: "查询我的学校课表"                   // LLM 工具描述（可选）
);
```

### 方法二：创建独立的注册类

```csharp
// DataProviders/MySchoolRegistration.cs
public static class MySchoolRegistration
{
    public static void Register(IDataCaptureService service)
    {
        service.RegisterProvider(
            id: "myschool",
            displayName: "我的学校",
            url: "https://myschool.edu.cn/login",
            provider: new MySchoolProvider(),
            faviconUrl: "https://myschool.edu.cn/favicon.ico",
            toolDescription: "查询我的学校课表"
        );
    }
}
```

## JSON 数据格式建议

为了方便后续处理，建议使用统一的 JSON 格式：

```json
{
    "source": "myschool",
    "syncTime": "2024-03-23T10:30:00Z",
    "data": {
        "courses": [
            {
                "name": "高等数学",
                "location": "教学楼101",
                "dayOfWeek": 1,
                "startTime": "08:00",
                "endTime": "09:40",
                "teacher": "张老师",
                "weeks": "1-16"
            }
        ]
    }
}
```

## 工作流程

1. **用户点击数据源** → App 打开 WebView，加载你提供的 URL
2. **用户登录** → WebView 导航过程中，App 持续调用 `IsReadyForExtraction()`
3. **检测到数据页面** → "提取数据"按钮变为可用状态
4. **用户点击提取** → App 调用 `ExtractDataAsync()`，传入 HTML 和安全存储接口
5. **返回 JSON** → 数据被保存到本地数据库
6. **用户可以在聊天中查询** → LLM 通过 `QueryDataAsync()` 查询数据

## 测试建议

### 1. 单元测试 IsReadyForExtraction

```csharp
[Test]
public void TestIsReadyForExtraction()
{
    var provider = new MySchoolProvider();

    // 测试登录页
    Assert.False(provider.IsReadyForExtraction("https://myschool.edu.cn/login", "..."));

    // 测试课表页
    Assert.True(provider.IsReadyForExtraction("https://myschool.edu.cn/timetable", "..."));
}
```

### 2. 测试 HTML 解析

```csharp
[Test]
public void TestExtractData()
{
    var provider = new MySchoolProvider();
    var mockHtml = "<td class='course-name'>高等数学</td>";

    var json = provider.ExtractDataAsync(mockHtml, null).Result;

    Assert.NotNull(json);
    Assert.Contains("高等数学", json);
}
```

### 3. 真机测试流程

1. 注册数据源
2. 启动应用
3. 进入"数据同步"页面
4. 点击你的数据源
5. 在 WebView 中完成登录
6. 等待"提取数据"按钮可用
7. 点击提取，检查控制台输出
8. 验证数据是否正确保存

## 常见问题

### Q: 如何处理需要多次跳转的登录流程？

A: 在 `IsReadyForExtraction` 中检查最终目标页面的特征，而不是中间页面。

### Q: 数据在多个页面中，需要抓取多次怎么办？

A: 目前接口设计为单次提取。如果需要多次抓取，可以在 `ExtractDataAsync` 中：
- 保存必要的 Cookie
- 返回需要继续抓取的提示
- 或者合并所有数据后一次性返回

### Q: 如何处理验证码？

A: WebView 是可见的，用户可以手动完成验证码。你只需要在验证码通过后检测到目标页面。

### Q: JSON 格式有强制要求吗？

A: 没有强制要求，只要返回有效的 JSON 字符串即可。但建议使用结构化格式，方便后续处理。

### Q: Cookie 会自动清除吗？

A: App 提供 `ClearAllDataAsync()` 方法，用户可以手动清除。Cookie 使用 MAUI 的 SecureStorage 加密存储。

## 联系方式

如有问题，请联系：
- 开发负责人：[你的名字]
- 项目仓库：[GitHub 仓库地址]

---

**最后更新**：2024年3月
