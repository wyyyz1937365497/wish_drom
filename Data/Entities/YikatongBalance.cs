using System.ComponentModel.DataAnnotations;

namespace wish_drom.Data.Entities
{
    public class YikatongBalance
    {
        [Key]
        public int Id { get; set; }

        public decimal Balance { get; set; }

        public string Account { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}