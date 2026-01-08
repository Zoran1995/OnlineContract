using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using OnlineContract.Helpers;

namespace OnlineContract.Models
{
    [Table("contract_det", Schema = "dbo")]
    public class ContractDet
    {
        [Key]
        [Column("contract_det_id")]
        public int Id { get; set; }

        [Column("contract_id")]
        public int ContractId { get; set; }

        [Column("product_variant_id")]
        public int ProductVariantId { get; set; }

        [Column("quantity")]
        public int Quantity { get; set; }

        [Column("amount")]
        public decimal Amount { get; set; }

        [Column("amt_tax")]
        public decimal AmtTax { get; set; }

        [Column("amt_gross")]
        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        public decimal AmtGross { get; set; }

        [Column("product_name")]
        public string? ProductName { get; set; }

        [Column("size")]
        public string? Size { get; set; }

        [Column("color")]
        public string? Color { get; set; }

        [Column("item_state_id")]
        public ProductStateInOrder ItemStateId { get; set; }

        [Column("input_dt")]
        public DateTime InputDt { get; set; }

        [Column("input_user_id")]
        public int? InputUserId { get; set; }

        [Column("last_modified_by_id")]
        public int? LastModifiedById { get; set; }

        [Column("last_updated_dt")]
        public DateTime? LastUpdatedDt { get; set; }

        [Column("stamp")]
        public int Stamp { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("is_deleted")]
        public bool IsDeleted { get; set; } = false;
    }
}
