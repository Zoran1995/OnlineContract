using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineContract.Models
{
    [Table("product_inventory", Schema = "dbo")]
    public class ProductInventory
    {
        [Key]
        [Column("product_inventory_id")]
        public int Id { get; set; }

        [Column("product_variant_id")]
        public int ProductVariantId { get; set; }

        [Column("store_id")]
        public int StoreId { get; set; }

        [Column("qty_on_hand")]
        public int QtyOnHand { get; set; }

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