using Microsoft.Maui.Controls;
using System.Diagnostics;
using Microsoft.Maui.Graphics;
using System.Text.Json;
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
        private bool _webViewPresentedModally;
        private CancellationTokenSource? _captureCts;
        private readonly ISecureDataStorage _secureStorage;
        private string _lastPolledUrl = "";

        private static void Log(string msg)
        {
            Console.WriteLine(msg);
            Debug.WriteLine(msg);
        }

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

            Log($"[DataCapture] 已注册数据源: {displayName} ({id})");
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
                try
                {
                    var cachedData = await source.Provider.FetchDataAsync(_secureStorage);
                    if (!string.IsNullOrEmpty(cachedData))
                    {
                        Log("[DataCapture] 使用缓存凭证静默获取数据成功");
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
                    Log("[DataCapture] 凭证已过期，进入登录流程");
                }
                catch (Exception ex)
                {
                    Log($"[DataCapture] 静默获取失败: {ex.Message}，进入登录流程");
                }

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

                Log($"[WebView] ====== 开始 WebView 会话 ======");
                Log($"[WebView] 初始 URL: {source.Url}");

                var statusLabel = new Label
                {
                    Text = $"正在访问 {source.DisplayName}...\nURL: {source.Url}",
                    HorizontalTextAlignment = TextAlignment.Center,
                    Margin = new Thickness(16, 8),
                    FontSize = 11,
                    TextColor = Colors.Gray,
                    LineBreakMode = LineBreakMode.TailTruncation,
                    MaxLines = 3
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

                // ──────── Navigating 事件：导航发生前 ────────
                _currentWebView.Navigating += (sender, e) =>
                {
                    Log($"[WebView] >>> NAVIGATING to: {e.Url}");
                    Log($"[WebView]     NavigationEvent: {e.NavigationEvent}");
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        statusLabel.Text = $"正在跳转...\nURL: {e.Url}";
                    });
                };

                // ──────── Navigated 事件：导航完成后（增强日志） ────────
                _currentWebView.Navigated += async (sender, e) =>
                {
                    Log($"[WebView] <<< NAVIGATED");
                    Log($"[WebView]     Event URL: {e.Url}");
                    Log($"[WebView]     Result: {e.Result}");
                    Log($"[WebView]     NavigationEvent: {e.NavigationEvent}");

                    try
                    {
                        var sourceUrl = _currentWebView?.Source?.ToString() ?? "(null)";
                        Log($"[WebView]     WebView.Source: {sourceUrl}");

                        if (_currentWebView == null) return;

                        var title = await _currentWebView.EvaluateJavaScriptAsync("document.title");
                        Log($"[WebView]     Page Title: {title}");

                        var jsUrl = await _currentWebView.EvaluateJavaScriptAsync("window.location.href");
                        Log($"[WebView]     JS location.href: {jsUrl}");

                        var cookies = await _currentWebView.EvaluateJavaScriptAsync("document.cookie");
                        var cookiePreview = string.IsNullOrEmpty(cookies)
                            ? "(empty)"
                            : (cookies.Length > 120 ? cookies[..120] + "..." : cookies);
                        Log($"[WebView]     Cookies ({cookies?.Length ?? 0} chars): {cookiePreview}");

                        var html = await _currentWebView.EvaluateJavaScriptAsync("document.documentElement.outerHTML");
                        Log($"[WebView]     HTML length: {html?.Length ?? 0}");

                        var urlToCheck = jsUrl ?? e.Url;
                        var isReady = source.Provider.IsReadyForExtraction(urlToCheck, html ?? "");
                        Log($"[WebView]     IsReadyForExtraction: {isReady}");

                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            if (isReady)
                            {
                                statusLabel.Text = $"登录成功！可以提取数据\nURL: {urlToCheck}";
                                extractButton.IsEnabled = true;
                            }
                            else
                            {
                                statusLabel.Text = $"请登录...\nURL: {urlToCheck}";
                                extractButton.IsEnabled = false;
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Log($"[WebView]     Navigated handler error: {ex.Message}");
                    }
                };

                // ──────── 提取数据按钮 ────────
                extractButton.Clicked += async (sender, e) =>
                {
                    extractButton.IsEnabled = false;
                    cancelButton.IsEnabled = false;
                    statusLabel.Text = "正在提取数据...";

                    try
                    {
                        var html = await _currentWebView.EvaluateJavaScriptAsync("document.documentElement.outerHTML");
                        var currentUrl = _currentWebView.Source?.ToString() ?? source.Url;

                        Log($"[DataCapture] 开始提取，当前 URL: {currentUrl}，HTML 长度: {html?.Length ?? 0}");

                        if (string.IsNullOrEmpty(html))
                        {
                            tcs.TrySetResult(new CaptureResult
                            {
                                Success = false,
                                Error = "无法获取页面内容"
                            });
                            return;
                        }

                        Func<string, Task<string?>> jsExecutor = ExecuteJavaScriptForProviderAsync;

                        statusLabel.Text = "正在提取凭证...";
                        Log("[DataCapture] 阶段一：开始 ExtractDataAsync");
                        var extractResult = await source.Provider.ExtractDataAsync(html, _secureStorage, jsExecutor);
                        Log($"[DataCapture] 阶段一结果: {extractResult}");

                        if (extractResult == "CredentialsStored")
                        {
                            statusLabel.Text = "正在获取课表数据...";
                            Log("[DataCapture] 阶段二：开始 FetchDataAsync");
                            var businessData = await source.Provider.FetchDataAsync(_secureStorage);
                            Log($"[DataCapture] 阶段二结果: {(businessData != null ? $"{businessData.Length} chars" : "null")}");

                            if (!string.IsNullOrEmpty(businessData))
                            {
                                Log($"[DataCapture] 数据预览: {businessData[..Math.Min(300, businessData.Length)]}");
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
                        Log($"[DataCapture] 提取异常: {ex}");
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

                var layoutGrid = new Grid
                {
                    RowDefinitions =
                    {
                        new RowDefinition { Height = GridLength.Auto },
                        new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                        new RowDefinition { Height = GridLength.Auto },
                        new RowDefinition { Height = GridLength.Auto }
                    }
                };

                var webViewContainer = new Border
                {
                    StrokeThickness = 0,
                    Stroke = Colors.Transparent,
                    Content = _currentWebView
                };

                Grid.SetRow(statusLabel, 0);
                Grid.SetRow(webViewContainer, 1);
                Grid.SetRow(extractButton, 2);
                Grid.SetRow(cancelButton, 3);

                layoutGrid.Children.Add(statusLabel);
                layoutGrid.Children.Add(webViewContainer);
                layoutGrid.Children.Add(extractButton);
                layoutGrid.Children.Add(cancelButton);

                _webViewPage.Content = layoutGrid;

                var rootPage = GetRootPage();
                if (rootPage != null)
                {
                    await rootPage.Navigation.PushModalAsync(_webViewPage);
                    _webViewPresentedModally = true;
                }

                // ──────── URL 变化轮询（兜底：JS 跳转不触发 Navigating/Navigated） ────────
                _lastPolledUrl = source.Url;
                _ = Task.Run(async () =>
                {
                    while (!cancellationToken.IsCancellationRequested && !tcs.Task.IsCompleted)
                    {
                        await Task.Delay(2000, cancellationToken);

                        try
                        {
                            if (_currentWebView == null) break;

                            var currentHref = await MainThread.InvokeOnMainThreadAsync(async () =>
                                await _currentWebView.EvaluateJavaScriptAsync("window.location.href"));

                            if (!string.IsNullOrEmpty(currentHref) && currentHref != _lastPolledUrl)
                            {
                                Log($"[WebView-Poll] URL 变化: {_lastPolledUrl} -> {currentHref}");
                                _lastPolledUrl = currentHref;

                                var isReady = source.Provider.IsReadyForExtraction(currentHref, "");
                                if (isReady)
                                {
                                    Log($"[WebView-Poll] IsReadyForExtraction=true，启用提取按钮");
                                    MainThread.BeginInvokeOnMainThread(() =>
                                    {
                                        statusLabel.Text = $"登录成功！可以提取数据\nURL: {currentHref}";
                                        extractButton.IsEnabled = true;
                                    });
                                }
                            }
                        }
                        catch (TaskCanceledException) { break; }
                        catch (Exception ex)
                        {
                            Log($"[WebView-Poll] 轮询异常: {ex.Message}");
                        }
                    }

                    Log("[WebView-Poll] 轮询结束");
                }, cancellationToken);

                var result = await tcs.Task;

                _captureCts?.Cancel();
                await CloseWebViewAsync();

                _onResult?.Invoke(result);
            }
            catch (Exception ex)
            {
                _captureCts?.Cancel();
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
        private async Task<string?> ExecuteJavaScriptForProviderAsync(string expression)
        {
            if (_currentWebView == null || string.IsNullOrWhiteSpace(expression))
                return null;

            if (string.Equals(expression, "__native_cookies__", StringComparison.Ordinal))
            {
                return await TryGetNativeCookiesAsync();
            }

            // 简单表达式优先直接执行，避免额外包装带来的不确定性。
            if (IsSimpleExpression(expression))
            {
                var directResult = await EvaluateJavaScriptWithTimeoutAsync(expression, 8000);
                var normalizedDirect = NormalizeJavaScriptResult(directResult);
                if (!string.IsNullOrEmpty(normalizedDirect) ||
                    !expression.Contains("document.cookie", StringComparison.OrdinalIgnoreCase))
                {
                    return normalizedDirect;
                }
            }

            var wrappedResult = await EvaluateAsyncJavaScriptAsync(expression);
            return NormalizeJavaScriptResult(wrappedResult);
        }

        private async Task<string?> EvaluateJavaScriptWithTimeoutAsync(string script, int timeoutMs)
        {
            if (_currentWebView == null) return null;

            try
            {
                var evalTask = MainThread.InvokeOnMainThreadAsync(() => _currentWebView.EvaluateJavaScriptAsync(script));
                var completedTask = await Task.WhenAny(evalTask, Task.Delay(timeoutMs));

                if (completedTask != evalTask)
                {
                    Log($"[DataCapture] JS 直接执行超时: {script[..Math.Min(100, script.Length)]}");
                    return null;
                }

                return await evalTask;
            }
            catch (Exception ex)
            {
                Log($"[DataCapture] JS 直接执行失败: {ex.Message}");
                return null;
            }
        }

        private static bool IsSimpleExpression(string expression)
        {
            return !expression.Contains('\n')
                && !expression.Contains('\r')
                && !expression.Contains("=>", StringComparison.Ordinal)
                && !expression.Contains("fetch(", StringComparison.OrdinalIgnoreCase)
                && !expression.Contains("await ", StringComparison.OrdinalIgnoreCase)
                && !expression.Contains("function", StringComparison.OrdinalIgnoreCase);
        }

        private static string? NormalizeJavaScriptResult(string? result)
        {
            if (string.IsNullOrWhiteSpace(result))
                return null;

            var trimmed = result.Trim();
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

        private async Task<string?> TryGetNativeCookiesAsync()
        {
            var currentUrl = await EvaluateJavaScriptWithTimeoutAsync("window.location.href", 5000);
            currentUrl = NormalizeJavaScriptResult(currentUrl) ?? _currentWebView?.Source?.ToString();

            if (string.IsNullOrWhiteSpace(currentUrl))
                return null;

            try
            {
#if ANDROID
                var cookies = Android.Webkit.CookieManager.Instance.GetCookie(currentUrl);
                return NormalizeJavaScriptResult(cookies);
#elif WINDOWS
                if (_currentWebView?.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 webView2 &&
                    webView2.CoreWebView2 != null)
                {
                    var cookieItems = await webView2.CoreWebView2.CookieManager.GetCookiesAsync(currentUrl);
                    if (cookieItems == null || cookieItems.Count == 0)
                        return null;

                    var parts = new List<string>(cookieItems.Count);
                    foreach (var cookie in cookieItems)
                    {
                        parts.Add($"{cookie.Name}={cookie.Value}");
                    }

                    return string.Join("; ", parts);
                }

                return null;
#elif IOS || MACCATALYST
                var nsUrl = new Foundation.NSUrl(currentUrl);
                var storage = Foundation.NSHttpCookieStorage.SharedStorage;
                var cookies = storage.CookiesForUrl(nsUrl);
                if (cookies == null || cookies.Length == 0)
                    return null;

                var parts = new List<string>(cookies.Length);
                foreach (var cookie in cookies)
                {
                    parts.Add($"{cookie.Name}={cookie.Value}");
                }
                return string.Join("; ", parts);
#else
                return null;
#endif
            }
            catch (Exception ex)
            {
                Log($"[DataCapture] 获取原生 Cookie 失败: {ex.Message}");
                return null;
            }
        }

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

            await MainThread.InvokeOnMainThreadAsync(() => _currentWebView.EvaluateJavaScriptAsync(wrappedScript));

            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(500);
                var result = await MainThread.InvokeOnMainThreadAsync(() => _currentWebView.EvaluateJavaScriptAsync($"window['{resultKey}']"));

                if (result != null && result != "null" && result != "undefined")
                {
                    await MainThread.InvokeOnMainThreadAsync(() => _currentWebView.EvaluateJavaScriptAsync($"delete window['{resultKey}']"));

                    if (result.Contains("\"__error\""))
                    {
                        Log($"[DataCapture] JS 执行错误: {result}");
                        return null;
                    }
                    return result;
                }
            }

            Log($"[DataCapture] JS 执行超时: {asyncExpression[..Math.Min(100, asyncExpression.Length)]}");
            return null;
        }

        private async Task CloseWebViewAsync()
        {
            try
            {
                var rootPage = GetRootPage();
                if (rootPage != null && _webViewPage != null)
                {
                    if (_webViewPresentedModally && rootPage.Navigation.ModalStack.Contains(_webViewPage))
                    {
                        await rootPage.Navigation.PopModalAsync();
                    }
                    else if (rootPage.Navigation.NavigationStack.Contains(_webViewPage))
                    {
                        await rootPage.Navigation.PopAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[DataCapture] 关闭 WebView 页面失败: {ex.Message}");
            }

            _currentWebView = null;
            _webViewPage = null;
            _webViewPresentedModally = false;
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
#if IOS || MACCATALYST
            Foundation.NSHttpCookieStorage storage = Foundation.NSHttpCookieStorage.SharedStorage;
            foreach (var cookie in storage.Cookies)
            {
                storage.DeleteCookie(cookie);
            }

            var dataStore = WebKit.WKWebsiteDataStore.DefaultDataStore;
            var dataTypes = WebKit.WKWebsiteDataStore.AllWebsiteDataTypes;
            var dateFrom = Foundation.NSDate.FromTimeIntervalSinceReferenceDate(0);
            await dataStore.RemoveDataOfTypesAsync(dataTypes, dateFrom);
            Log("[DataCapture] Mac Catalyst / iOS WebKit 数据已清理");
#endif
            await _secureStorage.ClearAsync();
        }

        private static Page? GetRootPage()
        {
            return Application.Current?.Windows
                .FirstOrDefault(window => window.Page != null)
                ?.Page;
        }
    }
}
