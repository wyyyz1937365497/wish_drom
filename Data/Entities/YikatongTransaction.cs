using System.ComponentModel.DataAnnotations;

namespace wish_drom.Data.Entities
{
    public class YikatongTransaction
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string OrderId { get; set; } = string.Empty;

        public DateTime TransactionDateTime { get; set; }

        public decimal Amount { get; set; }

        public decimal Balance { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        [MaxLength(50)]
        public string? TurnoverType { get; set; }

        [MaxLength(100)]
        public string? LocationName { get; set; }

        [MaxLength(100)]
        public string? PayName { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}