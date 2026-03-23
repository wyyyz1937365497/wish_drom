using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using wish_drom.Data;
using wish_drom.Services;
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

            // 聊天服务
            builder.Services.AddSingleton<IChatService, ChatService>();

            // TODO: 课表和活动服务将在后续实现
            // builder.Services.AddSingleton<IScheduleService, ScheduleService>();
            // builder.Services.AddSingleton<IActivityService, ActivityService>();
            // builder.Services.AddSingleton<IDataCaptureService, DataCaptureService>();
            // builder.Services.AddSingleton<IScheduleParser, DefaultScheduleParser>();

            return builder.Build();
        }
    }
}
