using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineContract.Models
{
    [Table("product", Schema = "dbo")]
    public class Product
    {
        [Key]
        [Column("product_id")]
        public int Id { get; set; }

        [Column("name")]
        [MaxLength(200)]
        public string? Name { get; set; }

        [Column("description")]
        public string? Description { get; set; }

        [Column("input_dt")]
        public DateTime InputDt { get; set; }

        [Column("input_user_id")]
        public int? InputUserId { get; set; }

        [Column("last_modified_by_id")]
        public int? LastModifiedById { get; set; }

        [Column("last_updated_dt")]
        public DateTime? LastUpdatedDt { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; }

        [Column("is_deleted")]
        public bool IsDeleted { get; set; }

        [Column("stamp")]
        public int Stamp { get; set; }
    }
}
