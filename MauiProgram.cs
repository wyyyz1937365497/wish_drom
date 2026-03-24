using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using wish_drom.Data;
using wish_drom.Services;
using wish_drom.Services.DataProviders;
using wish_drom.Services.Interfaces;

namespace wish_drom
{
    public static class MauiProgram
    {
        // #region agent log
        private static void AgentDebugLog(string runId, string hypothesisId, string location, string message, object data)
        {
            var payload = JsonSerializer.Serialize(new
            {
                sessionId = "694279",
                runId,
                hypothesisId,
                location,
                message,
                data,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
            File.AppendAllText("/Users/mike/Documents/University/Digital_Twin/wish_drom/.cursor/debug-694279.log", payload + Environment.NewLine);
        }
        // #endregion

        public static MauiApp CreateMauiApp()
        {
            // #region agent log
            AgentDebugLog("pre-fix", "H2", "MauiProgram.cs:CreateMauiApp", "进入 CreateMauiApp", new { phase = "start" });
            // #endregion
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();

#if DEBUG
    		builder.Services.AddBlazorWebViewDeveloperTools();
    		builder.Logging.AddDebug();
#endif

            // 数据库配置
            builder.Services.AddSingleton<AppDbContext>(sp =>
            {
                var dbContext = new AppDbContext();
                dbContext.Database.EnsureCreated();
                return dbContext;
            });

            // 聊天服务
            builder.Services.AddSingleton<IChatService, ChatService>();

            // 数据抓取服务
            builder.Services.AddSingleton<IDataCaptureService, DataCaptureService>();

            var app = builder.Build();

            // 注册数据源
            var dataCaptureService = app.Services.GetRequiredService<IDataCaptureService>();
            // #region agent log
            AgentDebugLog("pre-fix", "H2", "MauiProgram.cs:CreateMauiApp", "已获取 IDataCaptureService", new { serviceType = dataCaptureService.GetType().FullName });
            // #endregion

            dataCaptureService.RegisterProvider(
                id: "tongji-schedule",
                displayName: "同济大学课表",
                url: "https://1.tongji.edu.cn",
                provider: new TongjiScheduleProvider(),
                faviconUrl: "https://1.tongji.edu.cn/favicon.ico",
                toolDescription: "查询同济大学课程表"
            );
            // #region agent log
            AgentDebugLog("pre-fix", "H2", "MauiProgram.cs:CreateMauiApp", "RegisterProvider 调用返回", new { provider = "tongji-schedule" });
            // #endregion

            return app;
        }
    }
}
