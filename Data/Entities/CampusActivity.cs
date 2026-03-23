namespace wish_drom.Data.Entities
{
    /// <summary>
    /// 校园活动实体
    /// </summary>
    public class CampusActivity
    {
        public int Id { get; set; }

        /// <summary>
        /// 活动标题
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// 活动来源 (如: 学生会、社团、教务处)
        /// </summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>
        /// 活动描述
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 活动日期
        /// </summary>
        public DateTime ActivityDate { get; set; }

        /// <summary>
        /// 活动地点
        /// </summary>
        public string? Location { get; set; }

        /// <summary>
        /// 活动链接
        /// </summary>
        public string? Link { get; set; }

        /// <summary>
        /// 数据同步时间
        /// </summary>
        public DateTime SyncTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 是否已读
        /// </summary>
        public bool IsRead { get; set; } = false;
    }
}
