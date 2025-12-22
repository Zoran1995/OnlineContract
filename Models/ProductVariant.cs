using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineContract.Models
{
    [Table("product_variant", Schema = "dbo")]
    public class ProductVariant
    {
        [Key]
        [Column("product_variant_id")]
        public int Id { get; set; }

        [Column("product_id")]
        public int ProductId { get; set; }

        [Column("size")]
        [MaxLength(50)]
        public string? Size { get; set; }

        [Column("color")]
        [MaxLength(50)]
        public string? Color { get; set; }

        [Column("price")]
        public decimal Price { get; set; }

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

        [Column("size_key")]
        [MaxLength(50)]
        public string? SizeKey { get; set; }

        [Column("color_key")]
        [MaxLength(50)]
        public string? ColorKey { get; set; }

        [Column("photo_file_name")]
        [MaxLength(255)]
        public string? PhotoFileName { get; set; }
    }
}
