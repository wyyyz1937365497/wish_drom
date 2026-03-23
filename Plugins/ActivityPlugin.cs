using Microsoft.SemanticKernel;
using System.ComponentModel;
using wish_drom.Data.Entities;
using wish_drom.Services.Interfaces;

namespace wish_drom.Plugins
{
    /// <summary>
    /// 校园活动查询插件 - Semantic Kernel
    /// </summary>
    public class ActivityPlugin
    {
        private readonly IActivityService _activityService;

        public ActivityPlugin(IActivityService activityService)
        {
            _activityService = activityService;
        }

        [KernelFunction("get_upcoming_activities")]
        [Description("获取近期校园活动列表，默认7天内")]
        public async Task<string> GetUpcomingActivities(int days = 7)
        {
            var activities = await _activityService.GetUpcomingActivitiesAsync(days);
            return FormatActivities(activities, $"近{days}天");
        }

        [KernelFunction("get_activities_by_date_range")]
        [Description("获取指定日期范围内的活动")]
        public async Task<string> GetActivitiesByDateRange(string startDate, string endDate)
        {
            if (!DateTime.TryParse(startDate, out var start) || !DateTime.TryParse(endDate, out var end))
            {
                return "日期格式不正确，请使用如: 2024-01-01 的格式。";
            }

            var activities = await _activityService.GetActivitiesByDateRangeAsync(start, end);
            return FormatActivities(activities, $"{startDate} 至 {endDate}");
        }

        [KernelFunction("get_activity_sources")]
        [Description("获取校园活动来源列表")]
        public async Task<string> GetActivitySources()
        {
            // 这里简化实现，实际可能需要从数据库统计
            return "常见活动来源包括: 学生会、各学院社团、教务处、图书馆、就业指导中心等。";
        }

        [KernelFunction("get_activities_by_source")]
        [Description("根据来源获取活动列表")]
        public async Task<string> GetActivitiesBySource(string source)
        {
            var activities = await _activityService.GetActivitiesBySourceAsync(source);
            return FormatActivities(activities, source);
        }

        [KernelFunction("get_unread_activities_count")]
        [Description("获取未读活动数量")]
        public async Task<string> GetUnreadActivitiesCount()
        {
            var count = await _activityService.GetUnreadCountAsync();
            return count > 0 ? $"您有 {count} 条未读活动。" : "暂无未读活动。";
        }

        private string FormatActivities(List<CampusActivity> activities, string title)
        {
            if (activities.Count == 0)
                return $"{title}没有相关活动。";

            var result = new System.Text.StringBuilder();
            result.AppendLine($"🎉 {title}共有 {activities.Count} 个活动:\n");

            foreach (var activity in activities.OrderBy(a => a.ActivityDate))
            {
                result.AppendLine($"📌 {activity.Title}");
                result.AppendLine($"   📅 {activity.ActivityDate:yyyy-MM-dd}");
                if (!string.IsNullOrEmpty(activity.Location))
                    result.AppendLine($"   📍 {activity.Location}");
                if (!string.IsNullOrEmpty(activity.Description))
                    result.AppendLine($"   📝 {activity.Description}");
                result.AppendLine($"   🏢 {activity.Source}");
                result.AppendLine();
            }

            return result.ToString();
        }
    }
}
