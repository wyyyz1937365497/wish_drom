using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using wish_drom.Data;
using wish_drom.Data.Entities;
using System.Globalization;
using wish_drom.Services.Interfaces;

namespace wish_drom.Services.Plugins
{
    /// <summary>
    /// 课表插件 - 为 LLM 提供课表查询功能
    /// 使用绝对日期（几月几日）与 LLM 交互，服务内部处理日期到周次的转换
    /// </summary>
    public class SchedulePlugin
    {
        private static void Log(string message)
        {
            Debug.WriteLine(message);
            Console.WriteLine(message);
        }

        private readonly AppDbContext _dbContext;
        private readonly ISecureDataStorage _secureStorage;
        private static readonly DateTime _semesterStart = new DateTime(2024, 9, 2); // 秋季学期开学日期
        private const string API_BASE = "https://1.tongji.edu.cn/workbench";
        private const string ATTEND_CLASS_API_PATH = "/api/electionservice/reportManagement/queryAttendClassContent";

        private const string COOKIE_KEY = "tongji_cookies";
        private const string SESSION_ID_KEY = "tongji_session_id";
        private const string CALENDAR_BEGIN_DAY_MS_KEY = "tongji_calendar_begin_day_ms";
        private const string TEACHING_WEEK_START_KEY = "tongji_teaching_week_start";
        private const string TEACHING_WEEK_END_KEY = "tongji_teaching_week_end";

        public SchedulePlugin(AppDbContext dbContext)
        {
            _dbContext = dbContext;
            _secureStorage = new AppSecureDataStorage();
        }

        #region 私有辅助方法

        /// <summary>
        /// 获取指定日期的周次（优先使用同济学期元数据，失败时回退到本地默认）
        /// </summary>
        private async Task<int> GetWeekAsync(DateTime date)
        {
            try
            {
                var beginDayMsRaw = await _secureStorage.GetAsync(CALENDAR_BEGIN_DAY_MS_KEY);
                var teachingWeekStartRaw = await _secureStorage.GetAsync(TEACHING_WEEK_START_KEY);
                var teachingWeekEndRaw = await _secureStorage.GetAsync(TEACHING_WEEK_END_KEY);

                if (long.TryParse(beginDayMsRaw, out var beginDayMs))
                {
                    var beginDate = DateTimeOffset.FromUnixTimeMilliseconds(beginDayMs).LocalDateTime.Date;
                    var teachingWeekStart = int.TryParse(teachingWeekStartRaw, out var startWeek) ? startWeek : 1;
                    var teachingWeekEnd = int.TryParse(teachingWeekEndRaw, out var endWeek) ? endWeek : 20;

                    var offsetWeeks = (int)Math.Floor((date.Date - beginDate).TotalDays / 7.0);
                    var week = teachingWeekStart + offsetWeeks;
                    Log($"[SchedulePlugin] 周次计算(元数据): date={date:yyyy-MM-dd}, begin={beginDate:yyyy-MM-dd}, startWeek={teachingWeekStart}, endWeek={teachingWeekEnd}, result={week}");
                    return Math.Clamp(week, 1, Math.Max(teachingWeekEnd, 20));
                }
            }
            catch (Exception ex)
            {
                Log($"[SchedulePlugin] 读取学期元数据失败，回退默认算法: {ex.Message}");
            }

            var days = (date - _semesterStart).Days;
            var fallbackWeek = Math.Max(1, (days / 7) + 1);
            Log($"[SchedulePlugin] 周次计算(回退): date={date:yyyy-MM-dd}, semesterStart={_semesterStart:yyyy-MM-dd}, result={fallbackWeek}");
            return fallbackWeek;
        }

        private async Task<DateTime?> TryGetSemesterStartDateAsync()
        {
            try
            {
                var beginDayMsRaw = await _secureStorage.GetAsync(CALENDAR_BEGIN_DAY_MS_KEY);
                if (long.TryParse(beginDayMsRaw, out var beginDayMs))
                {
                    return DateTimeOffset.FromUnixTimeMilliseconds(beginDayMs).LocalDateTime.Date;
                }
            }
            catch (Exception ex)
            {
                Log($"[SchedulePlugin] 读取学期开始日期失败: {ex.Message}");
            }

            return null;
        }

        private async Task<HttpClient?> CreateTongjiHttpClientAsync()
        {
            var cookie = await _secureStorage.GetAsync(COOKIE_KEY);
            if (string.IsNullOrWhiteSpace(cookie))
                return null;

            var sessionId = await _secureStorage.GetAsync(SESSION_ID_KEY);

            var handler = new HttpClientHandler
            {
                UseCookies = false
            };

            var client = new HttpClient(handler)
            {
                BaseAddress = new Uri(API_BASE),
                Timeout = TimeSpan.FromSeconds(15)
            };

            client.DefaultRequestHeaders.Add("Cookie", cookie);
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                client.DefaultRequestHeaders.Add("X-Token", sessionId);
            }
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            return client;
        }

        private static bool TryParseSection(string section, out int startPeriod, out int endPeriod)
        {
            startPeriod = 0;
            endPeriod = 0;
            if (string.IsNullOrWhiteSpace(section)) return false;

            var cleaned = section.Replace("[", "").Replace("]", "").Replace("节", "");
            var parts = cleaned.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return false;

            if (int.TryParse(parts[0], out startPeriod) && int.TryParse(parts[1], out endPeriod))
                return true;

            return false;
        }

        private static string BuildDailyApiResult(DateTime date, List<(string CourseName, string Campus, string RoomName, string TimeNode, string Section)> items)
        {
            var dayOfWeek = (int)date.DayOfWeek;
            if (dayOfWeek == 0) dayOfWeek = 7;

            if (items.Count == 0)
                return $"{FormatDate(date)}（{FormatWeekday(dayOfWeek)}）没有课程安排。";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{FormatDate(date)}（{FormatWeekday(dayOfWeek)}）共有 {items.Count} 门课程：");
            foreach (var item in items)
            {
                sb.AppendLine($"📚 {item.CourseName}");
                var location = string.IsNullOrWhiteSpace(item.RoomName) ? item.Campus : $"{item.Campus} {item.RoomName}";
                sb.AppendLine($"   📍 {location}");
                sb.AppendLine($"   ⏰ {item.TimeNode} {item.Section}");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private async Task<string?> TryGetCoursesByDateFromTongjiApiAsync(DateTime date)
        {
            try
            {
                using var client = await CreateTongjiHttpClientAsync();
                if (client == null)
                {
                    Log("[SchedulePlugin] 同济接口未调用: 缺少 Cookie 凭证");
                    return null;
                }

                var chooseDate = $"{date.Year}-{date.Month}-{date.Day}";
                var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var url = $"{ATTEND_CLASS_API_PATH}?chooseDate={Uri.EscapeDataString(chooseDate)}&_t={ts}";
                Log($"[SchedulePlugin] 同济接口请求: {url}");

                var response = await client.GetAsync(url);
                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                {
                    Log($"[SchedulePlugin] 同济接口鉴权失败: {(int)response.StatusCode}");
                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    Log($"[SchedulePlugin] 同济接口响应异常: {(int)response.StatusCode}");
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);

                if (!doc.RootElement.TryGetProperty("code", out var codeEl) || codeEl.GetInt32() != 200)
                    return null;
                if (!doc.RootElement.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Array)
                    return BuildDailyApiResult(date, new List<(string, string, string, string, string)>());

                var items = new List<(string CourseName, string Campus, string RoomName, string TimeNode, string Section)>();
                foreach (var item in dataEl.EnumerateArray())
                {
                    var courseName = item.TryGetProperty("courseName", out var c) ? (c.GetString() ?? "未知课程") : "未知课程";
                    var campus = item.TryGetProperty("campus", out var cp) ? (cp.GetString() ?? "") : "";
                    var roomName = item.TryGetProperty("roomName", out var r) ? (r.GetString() ?? "") : "";
                    var timeNode = item.TryGetProperty("timeNode", out var t) ? (t.GetString() ?? "") : "";
                    var section = item.TryGetProperty("section", out var s) ? (s.GetString() ?? "") : "";

                    items.Add((courseName, campus, roomName, timeNode, section));
                }

                Log($"[SchedulePlugin] 同济接口命中: date={date:yyyy-MM-dd}, count={items.Count}");

                return BuildDailyApiResult(date, items);
            }
            catch (Exception ex)
            {
                Log($"[SchedulePlugin] 同济单日课表接口调用失败: {ex.Message}");
                return null;
            }
        }

        private static bool IsExplicitRealtimeRequest(string? requestText)
        {
            if (string.IsNullOrWhiteSpace(requestText))
                return false;

            var text = requestText.ToLowerInvariant();
            return text.Contains("实时")
                || text.Contains("在线")
                || text.Contains("同济接口")
                || text.Contains("门户")
                || text.Contains("官网")
                || text.Contains("最新");
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

            if (lower == "今天" || lower == "今日") return today;
            if (lower == "明天" || lower == "明日") return today.AddDays(1);
            if (lower == "后天") return today.AddDays(2);
            if (lower == "昨天") return today.AddDays(-1);

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

            var week = await GetWeekAsync(date);
            Log($"[SchedulePlugin] 本地日查询开始: date={date:yyyy-MM-dd}, dayOfWeek={dayOfWeek}, week={week}");

            try
            {
                var courses = await _dbContext.CourseSchedules
                    .Where(c => c.DayOfWeek == dayOfWeek && c.StartWeek <= week && c.EndWeek >= week)
                    .OrderBy(c => c.StartPeriod)
                    .ToListAsync();

                Log($"[SchedulePlugin] 本地日查询结果: date={date:yyyy-MM-dd}, count={courses.Count}");

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
                Log($"[SchedulePlugin] 查询课程失败: {ex.Message}");
                return $"查询失败: {ex.Message}。请先在【数据同步】页面同步课表数据。";
            }
        }

        #endregion

        #region 公开接口（供 LLM 调用）

        /// <summary>
        /// 查询今天的课程
        /// </summary>
        [KernelFunction("get_today_courses")]
        [Description("查询今天有什么课程，返回课程名称、地点、教师和具体时间。")]
        public async Task<string> GetTodayCoursesAsync()
        {
            // 默认使用本地课表数据，避免高频调用远程接口。
            return await GetCoursesByDateInternalAsync(DateTime.Today);
        }

        /// <summary>
        /// 查询指定日期的课程
        /// </summary>
        [KernelFunction("get_courses_by_date")]
        [Description("查询指定日期的课程安排。支持格式：3月25日、明天、后天、下周一等。")]
        public async Task<string> GetCoursesByDateAsync(
            [Description("要查询的日期，如：3月25日、明天、后天、下周一")]
            string dateStr)
        {
            DateTime targetDate = ParseDate(dateStr);
            Log($"[SchedulePlugin] 解析日期输入: raw='{dateStr}', parsed={(targetDate == DateTime.MinValue ? "invalid" : targetDate.ToString("yyyy-MM-dd"))}");
            if (targetDate == DateTime.MinValue)
            {
                return $"无法解析日期【{dateStr}】，请使用类似【3月25日】、【明天】、【下周一】的格式。";
            }

            // 默认使用本地课表数据，避免高频调用远程接口。
            return await GetCoursesByDateInternalAsync(targetDate);
        }

        /// <summary>
        /// 查询日期范围内的课程
        /// </summary>
        [KernelFunction("get_courses_by_date_range")]
        [Description("查询一个日期范围内的所有课程，如查询本周、下周的课程。")]
        public async Task<string> GetCoursesByDateRangeAsync(
            [Description("开始日期，如：今天、本周一、3月25日")]
            string startDateStr,
            [Description("结束日期，如：周日、3月31日")]
            string endDateStr)
        {
            DateTime startDate = ParseDate(startDateStr);
            DateTime endDate = ParseDate(endDateStr);
            Log($"[SchedulePlugin] 范围查询输入: startRaw='{startDateStr}', endRaw='{endDateStr}', start={(startDate == DateTime.MinValue ? "invalid" : startDate.ToString("yyyy-MM-dd"))}, end={(endDate == DateTime.MinValue ? "invalid" : endDate.ToString("yyyy-MM-dd"))}");

            if (startDate == DateTime.MinValue || endDate == DateTime.MinValue)
            {
                return $"无法解析日期，请使用类似【3月25日】、【今天】、【本周一】的格式。";
            }

            if (startDate > endDate)
            {
                return $"开始日期不能晚于结束日期。";
            }

            var startWeek = await GetWeekAsync(startDate);
            var endWeek = await GetWeekAsync(endDate);
            Log($"[SchedulePlugin] 范围查询周次: startWeek={startWeek}, endWeek={endWeek}");

            try
            {
                var courses = await _dbContext.CourseSchedules
                    .Where(c => c.StartWeek <= endWeek && c.EndWeek >= startWeek)
                    .OrderBy(c => c.DayOfWeek)
                    .ThenBy(c => c.StartPeriod)
                    .ToListAsync();
                Log($"[SchedulePlugin] 范围查询候选课程数: {courses.Count}");

                // 筛选在日期范围内的课程
                var filteredCourses = new List<Data.Entities.CourseSchedule>();
                foreach (var course in courses)
                {
                    var courseDate = startDate.AddDays((course.DayOfWeek - 1 - (int)startDate.DayOfWeek + 7) % 7);
                    var courseWeek = await GetWeekAsync(courseDate);
                    if (course.StartWeek <= courseWeek && course.EndWeek >= courseWeek)
                    {
                        filteredCourses.Add(course);
                    }
                }

                Log($"[SchedulePlugin] 范围查询过滤后课程数: {filteredCourses.Count}");

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
                Log($"[SchedulePlugin] 查询日期范围课程失败: {ex.Message}");
                return $"查询失败: {ex.Message}。请先在【数据同步】页面同步课表数据。";
            }
        }

        [KernelFunction("get_today_courses_from_tongji")]
        [Description("直接通过同济 queryAttendClassContent 接口查询当天课表，返回与门户一致的结果。")]
        public async Task<string> GetTodayCoursesFromTongjiServiceAsync(
            [Description("用户原始需求文本。仅当用户明确要求实时同济接口时才调用本接口。")]
            string requestText = "")
        {
            if (!IsExplicitRealtimeRequest(requestText))
            {
                Log("[SchedulePlugin] 同济当天接口被拦截: 未检测到明确实时需求，回退本地查询");
                return await GetCoursesByDateInternalAsync(DateTime.Today);
            }

            var apiResult = await TryGetCoursesByDateFromTongjiApiAsync(DateTime.Today);
            if (!string.IsNullOrWhiteSpace(apiResult))
                return apiResult;

            Log("[SchedulePlugin] 同济当天接口失败，回退本地查询");
            return await GetCoursesByDateInternalAsync(DateTime.Today);
        }

        [KernelFunction("get_courses_by_date_range_from_tongji")]
        [Description("直接通过同济 queryAttendClassContent 接口按日期范围逐日查询课表。")]
        public async Task<string> GetCoursesByDateRangeFromTongjiServiceAsync(
            [Description("开始日期，如：今天、3月25日")]
            string startDateStr,
            [Description("结束日期，如：3月31日、下周一")]
            string endDateStr,
            [Description("用户原始需求文本。仅当用户明确要求实时同济接口时才调用本接口。")]
            string requestText = "")
        {
            var startDate = ParseDate(startDateStr);
            var endDate = ParseDate(endDateStr);

            if (!IsExplicitRealtimeRequest(requestText))
            {
                Log("[SchedulePlugin] 同济范围接口被拦截: 未检测到明确实时需求，回退本地查询");
                return await GetCoursesByDateRangeAsync(startDateStr, endDateStr);
            }

            if (startDate == DateTime.MinValue || endDate == DateTime.MinValue)
                return "无法解析日期，请使用类似【3月25日】、【今天】、【本周一】的格式。";
            if (startDate > endDate)
                return "开始日期不能晚于结束日期。";
            if ((endDate - startDate).TotalDays > 31)
                return "日期范围过大，请将查询范围控制在 31 天内。";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{FormatDate(startDate)} 至 {FormatDate(endDate)} 的课程安排：");
            sb.AppendLine();

            var hasAny = false;
            for (var d = startDate.Date; d <= endDate.Date; d = d.AddDays(1))
            {
                var dayResult = await TryGetCoursesByDateFromTongjiApiAsync(d);
                if (!string.IsNullOrWhiteSpace(dayResult))
                {
                    hasAny = true;
                    sb.AppendLine(dayResult.TrimEnd());
                    sb.AppendLine();
                }
            }

            if (!hasAny)
            {
                Log("[SchedulePlugin] 同济范围接口无结果，回退本地查询");
                return await GetCoursesByDateRangeAsync(startDateStr, endDateStr);
            }

            return sb.ToString();
        }

        /// <summary>
        /// 查询本周课程
        /// </summary>
        [KernelFunction("get_this_week_courses")]
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
        [KernelFunction("get_course_details")]
        [Description("根据课程名称查询课程的详细信息，包括所有上课时间、地点、教师等。")]
        public async Task<string> GetCourseDetailsAsync(
            [Description("课程名称或关键词")]
            string courseName)
        {
            try
            {
                Log($"[SchedulePlugin] 课程详情查询: keyword='{courseName}'");
                var courses = await _dbContext.CourseSchedules
                    .Where(c => c.CourseName.Contains(courseName))
                    .OrderBy(c => c.DayOfWeek)
                    .ThenBy(c => c.StartPeriod)
                    .ToListAsync();
                Log($"[SchedulePlugin] 课程详情查询结果数: {courses.Count}");

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
                Log($"[SchedulePlugin] 查询课程详情失败: {ex.Message}");
                return $"查询失败: {ex.Message}。请先在【数据同步】页面同步课表数据。";
            }
        }

        /// <summary>
        /// 获取课表统计
        /// </summary>
        [KernelFunction("get_schedule_statistics")]
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
                var currentWeek = await GetWeekAsync(today);

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
                var semesterStartDate = await TryGetSemesterStartDateAsync() ?? _semesterStart;
                result.AppendLine($"   学期开始: {semesterStartDate:yyyy年M月d日}");
                result.AppendLine($"   当前日期: {FormatDate(today)} ({FormatWeekday(todayDayOfWeek)})");
                result.AppendLine($"   当前周次: 第 {currentWeek} 周");
                result.AppendLine($"   总课程数: {totalCourses} 门");
                result.AppendLine($"   本周课程: {thisWeekCourses} 门");
                result.AppendLine($"   今日课程: {todayCourses} 门");

                return result.ToString();
            }
            catch (Exception ex)
            {
                Log($"[SchedulePlugin] 获取统计失败: {ex.Message}");
                return $"查询失败: {ex.Message}。请先在【数据同步】页面同步课表数据。";
            }
        }

        #endregion
    }
}
