using Microsoft.Maui.Controls;
using System.Diagnostics;
using Microsoft.Maui.Graphics;
using wish_drom.Models;
using wish_drom.Services.Interfaces;

namespace wish_drom.Services
{
    /// <summary>
    /// WebView 数据抓取服务实现 - 支持双阶段架构：
    /// 阶段一：WebView 登录 + 凭证提取
    /// 阶段二：原生 HTTP 数据获取（利用缓存凭证）
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
                // 尝试使用已缓存的凭证静默获取数据
                try
                {
                    var cachedData = await source.Provider.FetchDataAsync(_secureStorage);
                    if (!string.IsNullOrEmpty(cachedData))
                    {
                        Debug.WriteLine("[DataCapture] 使用缓存凭证静默获取数据成功");
                        _onResult?.Invoke(new CaptureResult
                        {
                            Success = true,
                            JsonData = cachedData
                        });
                        return;
                    }
                }
                catch (AuthExpiredException)
                {
                    Debug.WriteLine("[DataCapture] 凭证已过期，进入登录流程");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DataCapture] 静默获取失败: {ex.Message}，进入登录流程");
                }

                // 凭证不可用，走 WebView 登录流程
                await RunWebViewCaptureAsync(source, _captureCts.Token);
            });
        }

        private async Task RunWebViewCaptureAsync(DataSourceConfig source, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<CaptureResult>();

            try
            {
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
                            tcs.TrySetResult(new CaptureResult
                            {
                                Success = false,
                                Error = "无法获取页面内容"
                            });
                            return;
                        }

                        // 构建跨平台异步 JS 执行器委托
                        Func<string, Task<string?>> jsExecutor = EvaluateAsyncJavaScriptAsync;

                        // 阶段一：凭证提取（在 WebView 上下文中执行）
                        statusLabel.Text = "正在提取凭证...";
                        var extractResult = await source.Provider.ExtractDataAsync(html, _secureStorage, jsExecutor);

                        if (extractResult == "CredentialsStored")
                        {
                            // 阶段二：使用提取的凭证获取业务数据
                            statusLabel.Text = "正在获取课表数据...";
                            var businessData = await source.Provider.FetchDataAsync(_secureStorage);

                            if (!string.IsNullOrEmpty(businessData))
                            {
                                tcs.TrySetResult(new CaptureResult
                                {
                                    Success = true,
                                    JsonData = businessData
                                });
                            }
                            else
                            {
                                tcs.TrySetResult(new CaptureResult
                                {
                                    Success = false,
                                    Error = "凭证已存储但获取数据失败"
                                });
                            }
                        }
                        else if (!string.IsNullOrEmpty(extractResult))
                        {
                            // Provider 直接返回了业务数据（传统 HTML 解析模式）
                            tcs.TrySetResult(new CaptureResult
                            {
                                Success = true,
                                JsonData = extractResult
                            });
                        }
                        else
                        {
                            tcs.TrySetResult(new CaptureResult
                            {
                                Success = false,
                                Error = "数据提取失败"
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[DataCapture] 提取异常: {ex}");
                        tcs.TrySetResult(new CaptureResult
                        {
                            Success = false,
                            Error = ex.Message
                        });
                    }
                };

                cancelButton.Clicked += (sender, e) =>
                {
                    tcs.TrySetResult(new CaptureResult
                    {
                        Success = false,
                        Error = "用户取消"
                    });
                };

                _currentWebView.Navigated += async (sender, e) =>
                {
                    Debug.WriteLine($"[DataCapture] 导航到: {e.Url}");

                    try
                    {
                        var html = await _currentWebView.EvaluateJavaScriptAsync("document.documentElement.outerHTML");

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
                        // 页面可能还在加载
                    }
                };

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

                var mainPage = Application.Current?.MainPage;
                if (mainPage != null)
                {
                    await mainPage.Navigation.PushAsync(_webViewPage);
                }

                var result = await tcs.Task;

                await CloseWebViewAsync();

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

        /// <summary>
        /// 跨平台异步 JavaScript 执行器。
        /// Android WebView 的 EvaluateJavaScriptAsync 不会自动 await Promise，
        /// 因此使用全局变量 + 轮询模式确保跨平台一致行为。
        /// </summary>
        private async Task<string?> EvaluateAsyncJavaScriptAsync(string asyncExpression)
        {
            if (_currentWebView == null) return null;

            var resultKey = $"__extractResult_{Guid.NewGuid():N}";

            var wrappedScript = $@"
                (async () => {{
                    try {{
                        const result = await ({asyncExpression});
                        window['{resultKey}'] = typeof result === 'object'
                            ? JSON.stringify(result)
                            : String(result);
                    }} catch(e) {{
                        window['{resultKey}'] = JSON.stringify({{__error: e.message}});
                    }}
                }})();
            ";

            await _currentWebView.EvaluateJavaScriptAsync(wrappedScript);

            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(500);
                var result = await _currentWebView.EvaluateJavaScriptAsync($"window['{resultKey}']");

                if (result != null && result != "null" && result != "undefined")
                {
                    await _currentWebView.EvaluateJavaScriptAsync($"delete window['{resultKey}']");

                    if (result.Contains("\"__error\""))
                    {
                        Debug.WriteLine($"[DataCapture] JS 执行错误: {result}");
                        return null;
                    }
                    return result;
                }
            }

            Debug.WriteLine($"[DataCapture] JS 执行超时: {asyncExpression[..Math.Min(100, asyncExpression.Length)]}");
            return null;
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
            await _secureStorage.ClearAsync();
        }
    }
}
