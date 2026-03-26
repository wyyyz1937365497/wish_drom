using System.Diagnostics;
using wish_drom.Services.Interfaces;

namespace wish_drom.Services;

/// <summary>
/// 校历服务实现 - 优先使用同济校历元数据进行周次计算，失败时回退到默认算法
/// </summary>
public class SchoolCalendarService : ISchoolCalendarService
{
    private static readonly DateTime _defaultSemesterStart = new DateTime(2024, 9, 2); // 默认秋季学期开学日期
    private readonly ISecureDataStorage _secureStorage;

    private const string CALENDAR_BEGIN_DAY_MS_KEY = "tongji_calendar_begin_day_ms";
    private const string TEACHING_WEEK_START_KEY = "tongji_teaching_week_start";
    private const string TEACHING_WEEK_END_KEY = "tongji_teaching_week_end";

    private static void Log(string message)
    {
        Debug.WriteLine(message);
        Console.WriteLine(message);
    }

    public SchoolCalendarService(ISecureDataStorage secureStorage)
    {
        _secureStorage = secureStorage;
    }

    /// <summary>
    /// 根据日期计算所在周次（优先使用同济校历元数据，失败时回退到默认算法）
    /// </summary>
    public async Task<int> GetWeekNumberFromDateAsync(DateTime date)
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
                Log($"[SchoolCalendarService] 周次计算(元数据): date={date:yyyy-MM-dd}, begin={beginDate:yyyy-MM-dd}, startWeek={teachingWeekStart}, endWeek={teachingWeekEnd}, result={week}");
                return Math.Clamp(week, 1, Math.Max(teachingWeekEnd, 20));
            }
        }
        catch (Exception ex)
        {
            Log($"[SchoolCalendarService] 读取学期元数据失败，回退默认算法: {ex.Message}");
        }

        // 回退算法：使用默认的学期开始日期
        var days = (date - _defaultSemesterStart).Days;
        var fallbackWeek = Math.Max(1, (days / 7) + 1);
        Log($"[SchoolCalendarService] 周次计算(回退): date={date:yyyy-MM-dd}, semesterStart={_defaultSemesterStart:yyyy-MM-dd}, result={fallbackWeek}");
        return fallbackWeek;
    }

    /// <summary>
    /// 获取当前周次
    /// </summary>
    public Task<int> GetCurrentWeekNumberAsync()
    {
        return GetWeekNumberFromDateAsync(DateTime.Now);
    }

    /// <summary>
    /// 获取学期开始日期（如果有校历元数据）
    /// </summary>
    public async Task<DateTime?> GetSemesterStartDateAsync()
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
            Log($"[SchoolCalendarService] 读取学期开始日期失败: {ex.Message}");
        }

        return null;
    }
}
