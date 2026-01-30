using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineContract.Models
{
    [Table("review", Schema = "dbo")]
    public class Review
    {
        [Key]
        [Column("review_id")]
        public int Id { get; set; }

        [Column("input_dt")]
        public DateTime InputDt { get; set; }

        [Column("input_user_id")]
        public int InputUserId { get; set; }

        [Column("comment")]
        [MaxLength(1000)]
        public string? Comment { get; set; }

        [Column("mark")]
        public int Mark { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("is_deleted")]
        public bool IsDeleted { get; set; }

        [Column("stamp")]
        public int Stamp { get; set; }
    }
}
