using Microsoft.Maui.Controls;
using System.Diagnostics;
using Microsoft.Maui.Graphics;
using wish_drom.Models;
using wish_drom.Services.Interfaces;

namespace wish_drom.Services
{
    /// <summary>
    /// WebView 数据抓取服务实现
    /// App 只提供 WebView 和安全存储，数据提取逻辑由提供者实现
    /// </summary>
    public class DataCaptureService : IDataCaptureService
    {
        private readonly Dictionary<string, DataSourceConfig> _sources = new();
        private Action<CaptureResult>? _onResult;
        private WebView? _currentWebView;
        private ContentPage? _webViewPage;
        private CancellationTokenSource? _captureCts;
        private readonly ISecureDataStorage _secureStorage;

        public DataCaptureService()
        {
            _secureStorage = new AppSecureDataStorage();
        }

        public void RegisterProvider(string id, string displayName, string url, IDataProvider provider, string? faviconUrl = null, string? toolDescription = null)
        {
            _sources[id] = new DataSourceConfig
            {
                Id = id,
                DisplayName = displayName,
                Url = url,
                Provider = provider,
                FaviconUrl = faviconUrl,
                ToolDescription = toolDescription ?? $"从 {displayName} 获取数据"
            };

            Debug.WriteLine($"[DataCapture] 已注册数据源: {displayName} ({id})");
        }

        public List<DataSourceConfig> GetRegisteredSources()
        {
            return _sources.Values.ToList();
        }

        public void StartCapture(string sourceId, Action<CaptureResult> onResult)
        {
            if (!_sources.TryGetValue(sourceId, out var source))
            {
                onResult(new CaptureResult
                {
                    Success = false,
                    Error = $"未找到数据源: {sourceId}"
                });
                return;
            }

            _onResult = onResult;
            _captureCts = new CancellationTokenSource();

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await RunWebViewCaptureAsync(source, _captureCts.Token);
            });
        }

        private async Task RunWebViewCaptureAsync(DataSourceConfig source, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<CaptureResult>();

            try
            {
                // 创建 WebView 页面
                _webViewPage = new ContentPage
                {
                    Title = source.DisplayName,
                    BackgroundColor = Colors.White
                };

                _currentWebView = new WebView
                {
                    Source = source.Url
                };

                var statusLabel = new Label
                {
                    Text = $"正在访问 {source.DisplayName}...",
                    HorizontalTextAlignment = TextAlignment.Center,
                    Margin = new Thickness(16, 8),
                    FontSize = 14,
                    TextColor = Colors.Gray
                };

                var extractButton = new Button
                {
                    Text = "提取数据",
                    IsEnabled = false,
                    Margin = new Thickness(16, 8),
                    BackgroundColor = Color.FromArgb("#4CAF50"),
                    TextColor = Colors.White,
                    CornerRadius = 8
                };

                var cancelButton = new Button
                {
                    Text = "取消",
                    Margin = new Thickness(16, 0, 16, 16),
                    BackgroundColor = Colors.LightGray,
                    TextColor = Colors.DarkGray,
                    CornerRadius = 8
                };

                // 提取按钮点击
                extractButton.Clicked += async (sender, e) =>
                {
                    extractButton.IsEnabled = false;
                    cancelButton.IsEnabled = false;
                    statusLabel.Text = "正在提取数据...";

                    try
                    {
                        var html = await _currentWebView.EvaluateJavaScriptAsync("document.documentElement.outerHTML");
                        var currentUrl = _currentWebView.Source?.ToString() ?? source.Url;

                        if (string.IsNullOrEmpty(html))
                        {
                            tcs.SetResult(new CaptureResult
                            {
                                Success = false,
                                Error = "无法获取页面内容"
                            });
                            return;
                        }

                        // 调用提供者的提取方法
                        var jsonData = await source.Provider.ExtractDataAsync(html, _secureStorage);

                        tcs.SetResult(new CaptureResult
                        {
                            Success = true,
                            JsonData = jsonData
                        });
                    }
                    catch (Exception ex)
                    {
                        tcs.SetResult(new CaptureResult
                        {
                            Success = false,
                            Error = ex.Message
                        });
                    }
                };

                cancelButton.Clicked += (sender, e) =>
                {
                    tcs.SetResult(new CaptureResult
                    {
                        Success = false,
                        Error = "用户取消"
                    });
                };

                // 监听导航事件
                _currentWebView.Navigated += async (sender, e) =>
                {
                    Debug.WriteLine($"[DataCapture] 导航到: {e.Url}");

                    try
                    {
                        var html = await _currentWebView.EvaluateJavaScriptAsync("document.documentElement.outerHTML");

                        // 询问提供者是否可以提取数据
                        if (source.Provider.IsReadyForExtraction(e.Url, html))
                        {
                            statusLabel.Text = "检测到数据页面，可以提取数据";
                            extractButton.IsEnabled = true;
                        }
                        else
                        {
                            statusLabel.Text = "请登录或导航到数据页面...";
                            extractButton.IsEnabled = false;
                        }
                    }
                    catch
                    {
                        // 页面可能还在加载，忽略错误
                    }
                };

                // 页面布局
                _webViewPage.Content = new StackLayout
                {
                    VerticalOptions = LayoutOptions.FillAndExpand,
                    Children =
                    {
                        statusLabel,
                        new Frame
                        {
                            HeightRequest = DeviceDisplay.MainDisplayInfo.Height / 2,
                            CornerRadius = 0,
                            Padding = 0,
                            Content = _currentWebView,
                            HasShadow = false
                        },
                        extractButton,
                        cancelButton
                    }
                };

                // 显示页面
                var mainPage = Application.Current?.MainPage;
                if (mainPage != null)
                {
                    await mainPage.Navigation.PushAsync(_webViewPage);
                }

                // 等待结果
                var result = await tcs.Task;

                // 关闭 WebView
                await CloseWebViewAsync();

                // 回调结果
                _onResult?.Invoke(result);
            }
            catch (Exception ex)
            {
                await CloseWebViewAsync();
                _onResult?.Invoke(new CaptureResult
                {
                    Success = false,
                    Error = ex.Message
                });
            }
        }

        private async Task CloseWebViewAsync()
        {
            try
            {
                var mainPage = Application.Current?.MainPage;
                if (mainPage != null && _webViewPage != null && mainPage.Navigation.NavigationStack.Contains(_webViewPage))
                {
                    await mainPage.Navigation.PopAsync();
                }
            }
            catch { }

            _currentWebView = null;
            _webViewPage = null;
        }

        public void CancelCapture()
        {
            _captureCts?.Cancel();
            _currentWebView = null;
        }

        public async Task ClearAllDataAsync()
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
    }
}
