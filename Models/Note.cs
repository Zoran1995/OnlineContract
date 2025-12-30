using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineContract.Models
{
    [Table("note", Schema = "dbo")]
    public class Note
    {
        [Key]
        [Column("note_id")]
        public int Id { get; set; }

        [Column("product_id")]
        public int? ProductId { get; set; }

        [Column("contract_id")]
        public int? ContractId { get; set; }

        [Column("comment")]
        [MaxLength(4000)]
        public string? Comment { get; set; }

        [Column("subject")]
        [MaxLength(250)]
        public string Subject { get; set; } = "";

        [Column("is_main")]
        public bool IsMain { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("is_deleted")]
        public bool IsDeleted { get; set; }

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
    }
}
