using Microsoft.SemanticKernel;
using System.ComponentModel;
using wish_drom.Data.Entities;
using wish_drom.Services.Interfaces;

namespace wish_drom.Plugins
{
    /// <summary>
    /// 课表查询插件 - Semantic Kernel
    /// </summary>
    public class SchedulePlugin
    {
        private readonly IScheduleService _scheduleService;

        public SchedulePlugin(IScheduleService scheduleService)
        {
            _scheduleService = scheduleService;
        }

        [KernelFunction("get_today_schedule")]
        [Description("获取用户今天的课程安排，包含时间、地点和课程名")]
        public async Task<string> GetTodaySchedule()
        {
            var schedules = await _scheduleService.GetTodayScheduleAsync();
            return FormatSchedules(schedules, "今天");
        }

        [KernelFunction("get_week_schedule")]
        [Description("获取指定周次的课程安排")]
        public async Task<string> GetWeekSchedule(int weekNumber)
        {
            var schedules = await _scheduleService.GetWeekScheduleAsync(weekNumber);
            return FormatSchedules(schedules, $"第{weekNumber}周");
        }

        [KernelFunction("get_day_schedule")]
        [Description("获取指定星期的课程安排 (1-7代表周一到周日)")]
        public async Task<string> GetDaySchedule(int dayOfWeek)
        {
            var dayNames = new[] { "", "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
            var schedules = await _scheduleService.GetDayScheduleAsync(dayOfWeek);
            return FormatSchedules(schedules, dayNames[dayOfWeek]);
        }

        [KernelFunction("get_current_week")]
        [Description("获取当前是第几周")]
        public string GetCurrentWeek()
        {
            var week = _scheduleService.GetCurrentWeekNumber();
            return $"当前是第 {week} 周";
        }

        [KernelFunction("get_semester_schedule")]
        [Description("获取整个学期的课程概览")]
        public async Task<string> GetSemesterSchedule()
        {
            var schedules = await _scheduleService.GetSemesterScheduleAsync();

            if (schedules.Count == 0)
                return "当前学期没有课程数据，请先进行数据同步。";

            var summary = new System.Text.StringBuilder();
            summary.AppendLine($"📚 本学期共有 {schedules.Count} 门课程:");

            var groupedByDay = schedules.GroupBy(s => s.DayOfWeek).OrderBy(g => g.Key);
            var dayNames = new[] { "", "周一", "周二", "周三", "周四", "周五", "周六", "周日" };

            foreach (var dayGroup in groupedByDay)
            {
                summary.AppendLine($"\n{dayNames[dayGroup.Key]}:");
                foreach (var schedule in dayGroup.OrderBy(s => s.StartPeriod))
                {
                    summary.AppendLine($"  {schedule.StartPeriod}-{schedule.EndPeriod}节: {schedule.CourseName} @ {schedule.Location}");
                }
            }

            return summary.ToString();
        }

        [KernelFunction("search_course")]
        [Description("根据课程名称搜索课程")]
        public async Task<string> SearchCourse(string courseName)
        {
            var allSchedules = await _scheduleService.GetSemesterScheduleAsync();
            var matched = allSchedules
                .Where(s => s.CourseName.Contains(courseName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matched.Count == 0)
                return $"未找到包含 '{courseName}' 的课程。";

            return FormatSchedules(matched, $"包含'{courseName}'的课程");
        }

        private string FormatSchedules(List<CourseSchedule> schedules, string title)
        {
            if (schedules.Count == 0)
                return $"{title}没有课程安排。";

            var result = new System.Text.StringBuilder();
            var dayNames = new[] { "", "周一", "周二", "周三", "周四", "周五", "周六", "周日" };

            result.AppendLine($"📅 {title}共有 {schedules.Count} 节课:\n");

            foreach (var schedule in schedules.OrderBy(s => s.StartPeriod))
            {
                result.AppendLine($"📖 {schedule.CourseName}");
                result.AppendLine($"   ⏰ {schedule.StartPeriod}-{schedule.EndPeriod}节");
                result.AppendLine($"   📍 {schedule.Location}");
                if (!string.IsNullOrEmpty(schedule.Teacher))
                    result.AppendLine($"   👨‍🏫 {schedule.Teacher}");
                result.AppendLine($"   📅 第{schedule.StartWeek}-{schedule.EndWeek}周 {schedule.WeekType ?? ""}");
                result.AppendLine();
            }

            return result.ToString();
        }
    }
}
