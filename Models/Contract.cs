using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using OnlineContract.Helpers;

namespace OnlineContract.Models
{
    [Table("contract", Schema = "dbo")]
    public class Contract
    {
        [Key]
        [Column("contract_id")]
        public int Id { get; set; }

        [NotMapped]
        public string CustomerFullName { get; set; } = string.Empty;

        [Column("input_user_id")]
        public int? InputUserId { get; set; }

        [Column("contract_state")]
        public ContractState ContractState { get; set; }

        [Column("input_dt")]
        public DateTime EntryDate { get; set; }

        [Column("last_modified_by_id")]
        public int? LastModifiedById { get; set; }

        [Column("last_updated_dt")]
        public DateTime? LastUpdatedDt { get; set; }

        [Column("delivered_dt")]
        public DateTime? DeliveredDt { get; set; } = new DateTime(1900, 01, 01);

        [Column("written_off_dt")]
        public DateTime? WrittenOffDt { get; set; } = new DateTime(1900, 01, 01);

        [Column("rejected_dt")]
        public DateTime? RejectedDt { get; set; } = new DateTime(1900, 01, 01);

        [Column("cancelled_dt")]
        public DateTime? CancelledDt { get; set; } = new DateTime(1900, 01, 01);

        [Column("amount")]
        public decimal Amount { get; set; }

        [Column("amt_matched")]
        public decimal AmtMatched { get; set; }

        [Column("stamp")]
        public int Stamp { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("is_deleted")]
        public bool IsDeleted { get; set; } = false;
    }
}