using System.ComponentModel;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using wish_drom.Data;
using wish_drom.Data.Entities;
using System.Globalization;

namespace wish_drom.Services.Plugins
{
    /// <summary>
    /// 课表插件 - 为 LLM 提供课表查询功能
    /// 使用绝对日期（几月几日）与 LLM 交互，服务内部处理日期到周次的转换
    /// </summary>
    public class SchedulePlugin
    {
        private readonly AppDbContext _dbContext;
        private static readonly DateTime _semesterStart = new DateTime(2024, 9, 2); // 秋季学期开学日期

        public SchedulePlugin(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        #region 私有辅助方法

        /// <summary>
        /// 获取指定日期的周次（基于秋季学期开学日期）
        /// </summary>
        private static int GetWeek(DateTime date)
        {
            var days = (date - _semesterStart).Days;
            return Math.Max(1, (days / 7) + 1);
        }

        /// <summary>
        /// 格式化日期为中文格式
        /// </summary>
        private static string FormatDate(DateTime date)
        {
            return date.ToString("M月d日", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 格式化星期
        /// </summary>
        private static string FormatWeekday(int dayOfWeek)
        {
            var dayNames = new[] { "", "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
            return dayNames[dayOfWeek];
        }

        /// <summary>
        /// 格式化节次时间
        /// </summary>
        private static string FormatPeriodTime(int startPeriod, int endPeriod)
        {
            var startTimes = new[] { "", "08:00", "08:55", "10:00", "10:55", "14:00", "14:55", "16:00", "16:55", "19:00", "19:55" };
            var endTimes = new[] { "", "08:45", "09:40", "10:45", "11:40", "14:45", "15:40", "16:45", "17:40", "19:45", "20:40" };

            if (startPeriod >= 1 && startPeriod <= 10 && endPeriod >= 1 && endPeriod <= 10)
            {
                return $"{startTimes[startPeriod]}-{endTimes[endPeriod]}";
            }
            return $"第{startPeriod}-{endPeriod}节";
        }

        /// <summary>
        /// 解析日期字符串
        /// </summary>
        private static DateTime ParseDate(string dateStr)
        {
            dateStr = dateStr.Trim();

            // 相对日期处理
            var today = DateTime.Today;
            var lower = dateStr.ToLower();

            if (lower == "今天" || lower == "今日")
                return today;
            if (lower == "明天" || lower == "明日")
                return today.AddDays(1);
            if (lower == "后天")
                return today.AddDays(2);
            if (lower == "昨天")
                return today.AddDays(-1);

            // 处理"周X"/"星期X"
            int targetDayOfWeek = -1;
            if (lower.Contains("周一") || lower == "周一" || lower == "下周一") targetDayOfWeek = 1;
            else if (lower.Contains("周二") || lower == "周二" || lower == "下周二") targetDayOfWeek = 2;
            else if (lower.Contains("周三") || lower == "周三" || lower == "下周三") targetDayOfWeek = 3;
            else if (lower.Contains("周四") || lower == "周四" || lower == "下周四") targetDayOfWeek = 4;
            else if (lower.Contains("周五") || lower == "周五" || lower == "下周五") targetDayOfWeek = 5;
            else if (lower.Contains("周六") || lower == "周六" || lower == "下周六") targetDayOfWeek = 6;
            else if (lower.Contains("周日") || lower == "周日" || lower == "下周日") targetDayOfWeek = 7;

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

            // 解析绝对日期格式
            string[] formats = { "M月d日", "M-d", "yyyy-M-d", "yyyy/M/d", "yyyy-MM-dd" };
            if (DateTime.TryParseExact(dateStr, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
            {
                return result;
            }

            return DateTime.MinValue;
        }

        /// <summary>
        /// 内部方法：查询指定日期的课程
        /// </summary>
        private async Task<string> GetCoursesByDateInternalAsync(DateTime date)
        {
            var dayOfWeek = (int)date.DayOfWeek;
            if (dayOfWeek == 0) dayOfWeek = 7;

            var week = GetWeek(date);

            try
            {
                var courses = await _dbContext.CourseSchedules
                    .Where(c => c.DayOfWeek == dayOfWeek && c.StartWeek <= week && c.EndWeek >= week)
                    .OrderBy(c => c.StartPeriod)
                    .ToListAsync();

                if (courses.Count == 0)
                {
                    return $"{FormatDate(date)}（{FormatWeekday(dayOfWeek)}）没有课程安排。";
                }

                var result = new System.Text.StringBuilder();
                result.AppendLine($"{FormatDate(date)}（{FormatWeekday(dayOfWeek)}）共有 {courses.Count} 门课程：");

                foreach (var course in courses)
                {
                    result.AppendLine($"📚 {course.CourseName}");
                    result.AppendLine($"   📍 {course.Location}");
                    result.AppendLine($"   👨‍🏫 {course.Teacher}");
                    result.AppendLine($"   ⏰ {FormatPeriodTime(course.StartPeriod, course.EndPeriod)}");
                    result.AppendLine();
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchedulePlugin] 查询课程失败: {ex.Message}");
                return $"查询失败: {ex.Message}。请先在【数据同步】页面同步课表数据。";
            }
        }

        #endregion

        #region 公开接口（供 LLM 调用）

        /// <summary>
        /// 查询今天的课程
        /// </summary>
        [KernelFunction("查询今天的课程")]
        [Description("查询今天有什么课程，返回课程名称、地点、教师和具体时间。")]
        public async Task<string> GetTodayCoursesAsync()
        {
            return await GetCoursesByDateInternalAsync(DateTime.Today);
        }

        /// <summary>
        /// 查询指定日期的课程
        /// </summary>
        [KernelFunction("查询指定日期的课程")]
        [Description("查询指定日期的课程安排。支持格式：3月25日、明天、后天、下周一等。")]
        public async Task<string> GetCoursesByDateAsync(
            [Description("要查询的日期，如：3月25日、明天、后天、下周一")]
            string dateStr)
        {
            DateTime targetDate = ParseDate(dateStr);
            if (targetDate == DateTime.MinValue)
            {
                return $"无法解析日期【{dateStr}】，请使用类似【3月25日】、【明天】、【下周一】的格式。";
            }

            return await GetCoursesByDateInternalAsync(targetDate);
        }

        /// <summary>
        /// 查询日期范围内的课程
        /// </summary>
        [KernelFunction("查询日期范围内的课程")]
        [Description("查询一个日期范围内的所有课程，如查询本周、下周的课程。")]
        public async Task<string> GetCoursesByDateRangeAsync(
            [Description("开始日期，如：今天、本周一、3月25日")]
            string startDateStr,
            [Description("结束日期，如：周日、3月31日")]
            string endDateStr)
        {
            DateTime startDate = ParseDate(startDateStr);
            DateTime endDate = ParseDate(endDateStr);

            if (startDate == DateTime.MinValue || endDate == DateTime.MinValue)
            {
                return $"无法解析日期，请使用类似【3月25日】、【今天】、【本周一】的格式。";
            }

            if (startDate > endDate)
            {
                return $"开始日期不能晚于结束日期。";
            }

            var startWeek = GetWeek(startDate);
            var endWeek = GetWeek(endDate);

            try
            {
                var courses = await _dbContext.CourseSchedules
                    .Where(c => c.StartWeek <= endWeek && c.EndWeek >= startWeek)
                    .OrderBy(c => c.DayOfWeek)
                    .ThenBy(c => c.StartPeriod)
                    .ToListAsync();

                // 筛选在日期范围内的课程
                var filteredCourses = new List<Data.Entities.CourseSchedule>();
                foreach (var course in courses)
                {
                    var courseDate = startDate.AddDays((course.DayOfWeek - 1 - (int)startDate.DayOfWeek + 7) % 7);
                    var courseWeek = GetWeek(courseDate);
                    if (course.StartWeek <= courseWeek && course.EndWeek >= courseWeek)
                    {
                        filteredCourses.Add(course);
                    }
                }

                if (filteredCourses.Count == 0)
                {
                    return $"{FormatDate(startDate)} 至 {FormatDate(endDate)} 没有课程安排。";
                }

                var result = new System.Text.StringBuilder();
                result.AppendLine($"{FormatDate(startDate)} 至 {FormatDate(endDate)} 共有 {filteredCourses.Count} 门课程：");
                result.AppendLine();

                var grouped = filteredCourses.GroupBy(c => c.DayOfWeek);
                foreach (var group in grouped)
                {
                    result.AppendLine($"【{FormatWeekday(group.Key)}】");
                    foreach (var course in group)
                    {
                        result.AppendLine($"  📚 {course.CourseName}");
                        result.AppendLine($"     📍 {course.Location} | ⏰ {FormatPeriodTime(course.StartPeriod, course.EndPeriod)}");
                    }
                    result.AppendLine();
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchedulePlugin] 查询日期范围课程失败: {ex.Message}");
                return $"查询失败: {ex.Message}。请先在【数据同步】页面同步课表数据。";
            }
        }

        /// <summary>
        /// 查询本周课程
        /// </summary>
        [KernelFunction("查询本周课程")]
        [Description("查询本周所有课程，按每天列出课程安排。")]
        public async Task<string> GetThisWeekCoursesAsync()
        {
            var today = DateTime.Today;
            var dayOfWeek = (int)today.DayOfWeek;
            if (dayOfWeek == 0) dayOfWeek = 7;

            var monday = today.AddDays(-(dayOfWeek - 1));
            var sunday = monday.AddDays(6);

            return await GetCoursesByDateRangeAsync("本周一", FormatDate(sunday));
        }

        /// <summary>
        /// 查询课程详细信息
        /// </summary>
        [KernelFunction("查询课程详细信息")]
        [Description("根据课程名称查询课程的详细信息，包括所有上课时间、地点、教师等。")]
        public async Task<string> GetCourseDetailsAsync(
            [Description("课程名称或关键词")]
            string courseName)
        {
            try
            {
                var courses = await _dbContext.CourseSchedules
                    .Where(c => c.CourseName.Contains(courseName))
                    .OrderBy(c => c.DayOfWeek)
                    .ThenBy(c => c.StartPeriod)
                    .ToListAsync();

                if (courses.Count == 0)
                {
                    return $"未找到包含【{courseName}】的课程。";
                }

                var result = new System.Text.StringBuilder();
                result.AppendLine($"找到 {courses.Count} 门相关课程：");
                result.AppendLine();

                foreach (var course in courses)
                {
                    result.AppendLine($"📚 {course.CourseName}");
                    result.AppendLine($"   📍 地点: {course.Location}");
                    result.AppendLine($"   👨‍🏫 教师: {course.Teacher}");
                    result.AppendLine($"   📅 时间: {FormatWeekday(course.DayOfWeek)} {FormatPeriodTime(course.StartPeriod, course.EndPeriod)}");
                    result.AppendLine($"   🗓️ 周次: 第 {course.StartWeek}-{course.EndWeek} 周");
                    if (!string.IsNullOrEmpty(course.WeekType))
                    {
                        result.AppendLine($"   📝 备注: {course.WeekType}");
                    }
                    result.AppendLine();
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchedulePlugin] 查询课程详情失败: {ex.Message}");
                return $"查询失败: {ex.Message}。请先在【数据同步】页面同步课表数据。";
            }
        }

        /// <summary>
        /// 获取课表统计
        /// </summary>
        [KernelFunction("获取课表统计")]
        [Description("获取课表的统计信息，包括总课程数、本周课程数等。")]
        public async Task<string> GetScheduleStatisticsAsync()
        {
            try
            {
                var totalCourses = await _dbContext.CourseSchedules.CountAsync();

                if (totalCourses == 0)
                {
                    return "当前没有同步任何课表数据。请先在【数据同步】页面同步课表数据。";
                }

                var today = DateTime.Today;
                var currentWeek = GetWeek(today);

                var thisWeekCourses = await _dbContext.CourseSchedules
                    .Where(c => c.StartWeek <= currentWeek && c.EndWeek >= currentWeek)
                    .CountAsync();

                var todayDayOfWeek = (int)today.DayOfWeek;
                if (todayDayOfWeek == 0) todayDayOfWeek = 7;

                var todayCourses = await _dbContext.CourseSchedules
                    .Where(c => c.DayOfWeek == todayDayOfWeek && c.StartWeek <= currentWeek && c.EndWeek >= currentWeek)
                    .CountAsync();

                var result = new System.Text.StringBuilder();
                result.AppendLine("📊 课表统计：");
                result.AppendLine($"   学期开始: {_semesterStart.ToString("yyyy年M月d日")}");
                result.AppendLine($"   当前日期: {FormatDate(today)} ({FormatWeekday(todayDayOfWeek)})");
                result.AppendLine($"   当前周次: 第 {currentWeek} 周");
                result.AppendLine($"   总课程数: {totalCourses} 门");
                result.AppendLine($"   本周课程: {thisWeekCourses} 门");
                result.AppendLine($"   今日课程: {todayCourses} 门");

                return result.ToString();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchedulePlugin] 获取统计失败: {ex.Message}");
                return $"查询失败: {ex.Message}。请先在【数据同步】页面同步课表数据。";
            }
        }

        #endregion
    }
}
