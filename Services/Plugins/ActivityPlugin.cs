using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.SemanticKernel;
using wish_drom.Data.Entities;
using wish_drom.Services.Interfaces;

namespace wish_drom.Services.Plugins
{
    /// <summary>
    /// 校园活动插件 - 为 LLM 提供活动查询功能
    /// 通过 IActivityDataReader 聚合所有已注册 Provider 的活动数据
    /// </summary>
    public class ActivityPlugin
    {
        private static void Log(string message)
        {
            Debug.WriteLine(message);
            Console.WriteLine(message);
        }

        private readonly IActivityDataReader _activityDataReader;

        public ActivityPlugin(IActivityDataReader activityDataReader)
        {
            _activityDataReader = activityDataReader;
        }

        #region 私有辅助方法

        private static string FormatDate(DateTime date)
        {
            return date.ToString("M月d日");
        }

        private static string FormatWeekday(int dayOfWeek)
        {
            var dayNames = new[] { "", "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
            return dayOfWeek >= 1 && dayOfWeek <= 7 ? dayNames[dayOfWeek] : "";
        }

        private static string FormatActivity(CampusActivity activity)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"  📌 {activity.Title}");
            sb.AppendLine($"     📍 {activity.Location ?? "地点未定"}");
            sb.AppendLine($"     📅 {FormatDate(activity.ActivityDate)}（{FormatWeekday((int)activity.ActivityDate.DayOfWeek == 0 ? 7 : (int)activity.ActivityDate.DayOfWeek)}）");
            if (!string.IsNullOrWhiteSpace(activity.Source))
                sb.AppendLine($"     🏷️ {activity.Source}");
            if (!string.IsNullOrWhiteSpace(activity.Description))
                sb.AppendLine($"     📝 {activity.Description}");
            return sb.ToString();
        }

        #endregion

        #region 公开接口（供 LLM 调用）

        /// <summary>
        /// 查询即将到来的校园活动
        /// </summary>
        [KernelFunction("get_upcoming_activities")]
        [Description("查询未来几天的校园活动，返回活动标题、时间、地点等信息。")]
        public async Task<string> GetUpcomingActivitiesAsync(
            [Description("查询未来多少天的活动，默认7天")]
            int days = 7)
        {
            try
            {
                Log($"[ActivityPlugin] 查询未来 {days} 天活动");

                var startDate = DateTime.Today;
                var endDate = startDate.AddDays(days);
                var allActivities = await _activityDataReader.GetAllActivitiesAsync();

                var upcoming = allActivities
                    .Where(a => a.ActivityDate >= startDate && a.ActivityDate <= endDate)
                    .OrderBy(a => a.ActivityDate)
                    .ToList();

                Log($"[ActivityPlugin] 查询结果: {upcoming.Count} 个活动");

                if (upcoming.Count == 0)
                {
                    return $"未来 {days} 天内没有校园活动。";
                }

                var result = new StringBuilder();
                result.AppendLine($"未来 {days} 天共有 {upcoming.Count} 个校园活动：");
                result.AppendLine();

                foreach (var activity in upcoming)
                {
                    result.AppendLine(FormatActivity(activity));
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                Log($"[ActivityPlugin] 查询活动失败: {ex.Message}");
                return $"查询活动失败: {ex.Message}。请先在【数据同步】页面同步活动数据。";
            }
        }

        /// <summary>
        /// 按日期范围查询活动
        /// </summary>
        [KernelFunction("get_activities_by_date_range")]
        [Description("查询指定日期范围内的校园活动。支持如：今天、明天、3月25日等格式。")]
        public async Task<string> GetActivitiesByDateRangeAsync(
            [Description("开始日期，如：今天、3月25日")]
            string startDateStr,
            [Description("结束日期，如：3月31日")]
            string endDateStr)
        {
            try
            {
                var startDate = ParseDate(startDateStr);
                var endDate = ParseDate(endDateStr);

                Log($"[ActivityPlugin] 日期范围查询: {startDate:yyyy-MM-dd} 至 {endDate:yyyy-MM-dd}");

                if (startDate == DateTime.MinValue || endDate == DateTime.MinValue)
                {
                    return "无法解析日期，请使用类似【3月25日】、【今天】的格式。";
                }

                if (startDate > endDate)
                {
                    return "开始日期不能晚于结束日期。";
                }

                var allActivities = await _activityDataReader.GetAllActivitiesAsync();

                var filtered = allActivities
                    .Where(a => a.ActivityDate >= startDate && a.ActivityDate <= endDate)
                    .OrderBy(a => a.ActivityDate)
                    .ToList();

                Log($"[ActivityPlugin] 范围查询结果: {filtered.Count} 个活动");

                if (filtered.Count == 0)
                {
                    return $"{FormatDate(startDate)} 至 {FormatDate(endDate)} 没有校园活动。";
                }

                var result = new StringBuilder();
                result.AppendLine($"{FormatDate(startDate)} 至 {FormatDate(endDate)} 共有 {filtered.Count} 个校园活动：");
                result.AppendLine();

                foreach (var activity in filtered)
                {
                    result.AppendLine(FormatActivity(activity));
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                Log($"[ActivityPlugin] 日期范围查询失败: {ex.Message}");
                return $"查询活动失败: {ex.Message}。请先在【数据同步】页面同步活动数据。";
            }
        }

        /// <summary>
        /// 按关键词搜索活动
        /// </summary>
        [KernelFunction("search_activities")]
        [Description("根据关键词搜索校园活动标题或描述，返回匹配的活动列表。")]
        public async Task<string> SearchActivitiesAsync(
            [Description("搜索关键词，如：讲座、比赛、社团等")]
            string keyword)
        {
            try
            {
                Log($"[ActivityPlugin] 关键词搜索: '{keyword}'");

                var allActivities = await _activityDataReader.GetAllActivitiesAsync();

                var matched = allActivities
                    .Where(a =>
                        (a.Title?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true) ||
                        (a.Description?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true) ||
                        (a.Source?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true))
                    .OrderByDescending(a => a.ActivityDate)
                    .ToList();

                Log($"[ActivityPlugin] 搜索结果: {matched.Count} 个活动");

                if (matched.Count == 0)
                {
                    return $"未找到包含【{keyword}】的校园活动。";
                }

                var result = new StringBuilder();
                result.AppendLine($"找到 {matched.Count} 个相关活动：");
                result.AppendLine();

                foreach (var activity in matched)
                {
                    result.AppendLine(FormatActivity(activity));
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                Log($"[ActivityPlugin] 搜索活动失败: {ex.Message}");
                return $"搜索活动失败: {ex.Message}。请先在【数据同步】页面同步活动数据。";
            }
        }

        #endregion

        #region 日期解析

        private static DateTime ParseDate(string dateStr)
        {
            dateStr = dateStr.Trim();
            var today = DateTime.Today;
            var lower = dateStr.ToLower();

            if (lower == "今天" || lower == "今日") return today;
            if (lower == "明天" || lower == "明日") return today.AddDays(1);
            if (lower == "后天") return today.AddDays(2);
            if (lower == "昨天") return today.AddDays(-1);

            // 处理 "周X" / "星期X"
            int targetDayOfWeek = lower switch
            {
                _ when lower.Contains("周一") => 1,
                _ when lower.Contains("周二") => 2,
                _ when lower.Contains("周三") => 3,
                _ when lower.Contains("周四") => 4,
                _ when lower.Contains("周五") => 5,
                _ when lower.Contains("周六") => 6,
                _ when lower.Contains("周日") || lower.Contains("周天") => 7,
                _ => -1
            };

            if (targetDayOfWeek != -1)
            {
                int currentDayOfWeek = (int)today.DayOfWeek;
                if (currentDayOfWeek == 0) currentDayOfWeek = 7;

                int daysToAdd = targetDayOfWeek - currentDayOfWeek;
                if (lower.Contains("下周") && daysToAdd <= 0)
                    daysToAdd += 7;
                else if (daysToAdd <= 0)
                    daysToAdd += 7;

                return today.AddDays(daysToAdd);
            }

            string[] formats = { "M月d日", "M-d", "yyyy-M-d", "yyyy/M/d", "yyyy-MM-dd" };
            if (DateTime.TryParseExact(dateStr, formats, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var result))
            {
                return result;
            }

            return DateTime.MinValue;
        }

        #endregion
    }
}
