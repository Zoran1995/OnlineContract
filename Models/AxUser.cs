using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineContract.Models
{
    [Table("ax_user")]
    public class AxUser
    {
        [Key]
        [Column("ax_user_id")]
        public int Id { get; set; }

        [Column("first_name")]
        [MaxLength(100)]
        public string FirstName { get; set; } = string.Empty;

        [Column("last_name")]
        [MaxLength(100)]
        public string LastName { get; set; } = string.Empty;

        [Column("code")]
        [MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [Column("password")]
        [MaxLength(200)]
        public string Password { get; set; } = string.Empty;

        [Column("is_active")]
        public bool IsActive { get; set; }

        [Column("is_deleted")]
        public bool IsDeleted { get; set; }

        [Column("phone_number")]
        [MaxLength(50)]
        public string Phone { get; set; } = "";

        [Column("email")]
        [MaxLength(150)]
        public string? Email { get; set; }

        [Column("role_id")]
        public int RoleId { get; set; } = 0;

        [Column("city")]
        [MaxLength(100)]
        public string? City { get; set; }

        [Column("street_address")]
        [MaxLength(200)]
        public string? StreetAddress { get; set; }

        [Column("postal_code")]
        [MaxLength(20)]
        public string? PostalCode { get; set; }

        [Column("last_login_dt")]
        public DateTime? LastLoginDt { get; set; }

        [Column("stamp")]
        public int Stamp { get; set; }

        [Column("is_group")]
        public bool IsGroup { get; set; }

        [Column("is_temp_password")]
        public bool IsTempPassword { get; set; }

        [Column("owner_id")]
        public int OwnerId { get; set; }

        [Column("created_dt")]
        public DateTime CreatedDt { get; set; }

        [Column("password_dt")]
        public DateTime PasswordDt { get; set; }

        [Column("input_user_id")]
        public int? InputUserId { get; set; }
    }
}