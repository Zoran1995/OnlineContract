using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineContract.Models
{
    [Table("password_reset_token")]
    public class PasswordResetToken
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("token")]
        [MaxLength(200)]
        public string Token { get; set; } = string.Empty;

        [Column("user_id")]
        public int? UserId { get; set; }

        [Column("code")]
        [MaxLength(50)]
        public string? Code { get; set; }

        [Column("email")]
        [MaxLength(150)]
        public string? Email { get; set; }

        [Column("created_dt")]
        public DateTime CreatedDt { get; set; }

        [Column("expiry_dt")]
        public DateTime ExpiryDt { get; set; }

        [Column("is_used")]
        public bool IsUsed { get; set; }

        [Column("used_dt")]
        public DateTime? UsedDt { get; set; }
    }
}
