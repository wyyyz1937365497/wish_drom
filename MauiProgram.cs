using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using wish_drom.Data;
using wish_drom.Services;
using wish_drom.Services.Interfaces;
using wish_drom.Services.HtmlParsers;

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

            // HTML 解析器
            builder.Services.AddSingleton<IScheduleParser, DefaultScheduleParser>();

            // 数据服务
            builder.Services.AddSingleton<IScheduleService, ScheduleService>();
            builder.Services.AddSingleton<IActivityService, ActivityService>();

            // 数据抓取服务
            builder.Services.AddSingleton<IDataCaptureService, DataCaptureService>();

            // 聊天服务 (延迟初始化，因为需要 API Key)
            builder.Services.AddSingleton<IChatService, ChatService>();

            return builder.Build();
        }
    }
}
