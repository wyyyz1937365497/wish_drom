using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using wish_drom.Data;
using wish_drom.Services;
using wish_drom.Services.DataProviders;
using wish_drom.Services.Interfaces;

namespace wish_drom
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
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

            // Provider 路由与注册中心
            builder.Services.AddSingleton<IProviderRegistry, ProviderRegistry>();
            builder.Services.AddSingleton<IScheduleDataReader, ScheduleDataReader>();
            builder.Services.AddSingleton<IActivityDataReader, ActivityDataReader>();

            // 安全存储服务
            builder.Services.AddSingleton<ISecureDataStorage, AppSecureDataStorage>();

            // 校历服务
            builder.Services.AddSingleton<ISchoolCalendarService, SchoolCalendarService>();

            // 课表服务
            builder.Services.AddSingleton<IScheduleService, ScheduleService>();

            // 活动服务
            builder.Services.AddSingleton<IActivityService, ActivityService>();

            // 聊天服务
            builder.Services.AddSingleton<IChatService, ChatService>();

            // 数据抓取服务
            builder.Services.AddSingleton<IDataCaptureService, DataCaptureService>();

            // 同济课表 Provider 私有数据库与实现
            builder.Services.AddSingleton<TongjiScheduleDbContext>(sp =>
            {
                var dbContext = new TongjiScheduleDbContext();
                dbContext.Database.EnsureCreated();
                return dbContext;
            });
            builder.Services.AddSingleton<TongjiScheduleProvider>();

            var app = builder.Build();

            // 注册数据源
            var dataCaptureService = app.Services.GetRequiredService<IDataCaptureService>();

            dataCaptureService.RegisterProvider(
                id: "tongji-schedule",
                displayName: "同济大学课表",
                url: "https://1.tongji.edu.cn/workbench",
                provider: app.Services.GetRequiredService<TongjiScheduleProvider>(),
                faviconUrl: "https://1.tongji.edu.cn/favicon.ico",
                toolDescription: "查询同济大学课程表"
            );

            return app;
        }
    }
}
