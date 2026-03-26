namespace wish_drom.Services.Interfaces;

/// <summary>
/// 校历服务接口 - 提供基于校历元数据的周次计算功能
/// </summary>
public interface ISchoolCalendarService
{
    /// <summary>
    /// 根据日期计算所在周次（优先使用同济校历元数据，失败时回退到默认算法）
    /// </summary>
    /// <param name="date">要计算周次的日期</param>
    /// <returns>周次（1-20）</returns>
    Task<int> GetWeekNumberFromDateAsync(DateTime date);

    /// <summary>
    /// 获取当前周次
    /// </summary>
    /// <returns>当前周次（1-20）</returns>
    Task<int> GetCurrentWeekNumberAsync();

    /// <summary>
    /// 获取学期开始日期（如果有校历元数据）
    /// </summary>
    /// <returns>学期开始日期，如果没有校历元数据则返回 null</returns>
    Task<DateTime?> GetSemesterStartDateAsync();
}
