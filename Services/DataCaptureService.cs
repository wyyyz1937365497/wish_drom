using Microsoft.Maui.Controls;
using wish_drom.Data.Entities;
using wish_drom.Services.Interfaces;
using wish_drom.Services.HtmlParsers;

namespace wish_drom.Services
{
    /// <summary>
    /// WebView 数据抓取服务实现
    /// </summary>
    public class DataCaptureService : IDataCaptureService
    {
        private readonly IScheduleService _scheduleService;
        private readonly IActivityService _activityService;
        private readonly IScheduleParser _scheduleParser;
        private string? _currentTargetUrl;

        public event EventHandler<string>? CaptureProgress;

        public DataCaptureService(
            IScheduleService scheduleService,
            IActivityService activityService,
            IScheduleParser scheduleParser)
        {
            _scheduleService = scheduleService;
            _activityService = activityService;
            _scheduleParser = scheduleParser;
        }

        public async Task<int> CaptureScheduleDataAsync(string targetUrl, CancellationToken cancellationToken = default)
        {
            _currentTargetUrl = targetUrl;
            CaptureProgress?.Invoke(this, "正在启动数据抓取...");

            // 创建 WebView 页面
            var capturedHtml = await ShowWebViewAndCaptureAsync(targetUrl, cancellationToken);
            if (string.IsNullOrEmpty(capturedHtml))
            {
                CaptureProgress?.Invoke(this, "未能获取到页面数据");
                return 0;
            }

            CaptureProgress?.Invoke(this, "正在解析数据...");

            // 解析 HTML
            var semester = GetCurrentSemester();
            var schedules = _scheduleParser.ParseScheduleHtml(capturedHtml, semester);

            if (schedules.Count == 0)
            {
                CaptureProgress?.Invoke(this, "未解析到课程数据，可能需要手动调整解析规则");
                return 0;
            }

            // 保存到数据库
            CaptureProgress?.Invoke(this, $"正在保存 {schedules.Count} 条课程数据...");
            var savedCount = await _scheduleService.SaveSchedulesAsync(schedules, cancellationToken);

            // 清除 Cookie
            await ClearCookiesAsync();

            CaptureProgress?.Invoke(this, $"数据同步完成，共保存 {savedCount} 条课程");
            return savedCount;
        }

        public async Task<int> CaptureActivityDataAsync(string targetUrl, CancellationToken cancellationToken = default)
        {
            _currentTargetUrl = targetUrl;
            CaptureProgress?.Invoke(this, "正在启动活动数据抓取...");

            var capturedHtml = await ShowWebViewAndCaptureAsync(targetUrl, cancellationToken);
            if (string.IsNullOrEmpty(capturedHtml))
            {
                CaptureProgress?.Invoke(this, "未能获取到页面数据");
                return 0;
            }

            CaptureProgress?.Invoke(this, "正在解析活动数据...");

            // 解析活动 (简化实现)
            var activities = ParseActivityHtml(capturedHtml);

            if (activities.Count == 0)
            {
                CaptureProgress?.Invoke(this, "未解析到活动数据");
                return 0;
            }

            // 保存到数据库
            CaptureProgress?.Invoke(this, $"正在保存 {activities.Count} 条活动数据...");
            var savedCount = await _activityService.SaveActivitiesAsync(activities, cancellationToken);

            await ClearCookiesAsync();

            CaptureProgress?.Invoke(this, $"活动同步完成，共保存 {savedCount} 条");
            return savedCount;
        }

        public async Task ClearCookiesAsync()
        {
#if ANDROID
            var cookieManager = Android.Webkit.CookieManager.Instance;
            cookieManager.RemoveAllCookies(null);
#endif
#if IOS
            Foundation.NSHttpCookieStorage storage = Foundation.NSHttpCookieStorage.SharedStorage;
            foreach (var cookie in storage.Cookies)
            {
                storage.DeleteCookie(cookie);
            }
#endif
            await Task.CompletedTask;
        }

        private async Task<string?> ShowWebViewAndCaptureAsync(string url, CancellationToken cancellationToken)
        {
            // 使用 TaskCompletionSource 等待 WebView 完成加载
            var tcs = new TaskCompletionSource<string?>();

            // 创建包含 WebView 的页面
            var webViewPage = new ContentPage
            {
                Title = "数据同步",
                Content = new WebView
                {
                    Source = url,
                    HeightRequest = 800
                }
            };

            var webView = (WebView)webViewPage.Content;

            // 监听导航完成事件
            webView.Navigated += async (sender, e) =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    tcs.SetCanceled(cancellationToken);
                    return;
                }

                // 等待页面完全加载
                await Task.Delay(2000, cancellationToken);

                try
                {
                    // 获取页面 HTML
                    var html = await webView.EvaluateJavaScriptAsync("document.documentElement.outerHTML");

                    // 检查是否是目标页面 (可能需要根据具体系统调整)
                    if (!string.IsNullOrEmpty(html) && html.Length > 1000)
                    {
                        tcs.SetResult(html);
                    }
                    else
                    {
                        // 如果页面内容不足，可能是在登录页，等待用户操作
                        CaptureProgress?.Invoke(this, "请在WebView中登录，完成后点击返回");
                    }
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            };

            // 添加一个按钮用于手动完成抓取
            var doneButton = new Button
            {
                Text = "完成抓取",
                VerticalOptions = LayoutOptions.End
            };
            doneButton.Clicked += async (sender, e) =>
            {
                try
                {
                    var html = await webView.EvaluateJavaScriptAsync("document.documentElement.outerHTML");
                    tcs.SetResult(html);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            };

            webViewPage.Content = new StackLayout
            {
                Children =
                {
                    new Label { Text = "请在下方登录并导航到课表页面，完成后点击下方按钮", Margin = 10 },
                    webView,
                    doneButton
                }
            };

            // 显示页面 (需要从主线程调用)
            var mainPage = Application.Current?.MainPage;
            if (mainPage != null)
            {
                await mainPage.Navigation.PushAsync(webViewPage);
            }

            // 等待结果
            var result = await tcs.Task;

            // 关闭页面
            if (mainPage != null)
            {
                await mainPage.Navigation.PopAsync();
            }

            return result;
        }

        private List<CampusActivity> ParseActivityHtml(string html)
        {
            var activities = new List<CampusActivity>();

            // 简化的活动解析逻辑
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);

            var activityNodes = doc.DocumentNode.SelectNodes("//div[@class='activity'] | //div[@class='event'] | //li[@class='notice']");
            if (activityNodes != null)
            {
                foreach (var node in activityNodes)
                {
                    var title = node.SelectSingleNode(".//*[@class='title'] | .//h3 | .//h4")?.InnerText.Trim();
                    var dateText = node.SelectSingleNode(".//*[@class='date'] | .//*[@class='time']")?.InnerText.Trim();
                    var source = node.SelectSingleNode(".//*[@class='source'] | .//*[@class='org']")?.InnerText.Trim() ?? "未知来源";

                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        activities.Add(new CampusActivity
                        {
                            Title = title,
                            Source = source,
                            ActivityDate = ParseActivityDate(dateText),
                            SyncTime = DateTime.Now
                        });
                    }
                }
            }

            return activities;
        }

        private DateTime ParseActivityDate(string? dateText)
        {
            if (string.IsNullOrWhiteSpace(dateText))
                return DateTime.Today;

            // 尝试解析日期
            if (DateTime.TryParse(dateText, out var date))
                return date;

            return DateTime.Today;
        }

        private string GetCurrentSemester()
        {
            var year = DateTime.Now.Year;
            if (DateTime.Now.Month >= 9)
            {
                return $"{year}-{year + 1}第一学期";
            }
            else
            {
                return $"{year - 1}-{year}第二学期";
            }
        }
    }
}
