using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineContract.Models
{
    [Table("payment", Schema = "dbo")]
    public class Payment
    {
        [Key]
        [Column("payment_id")]
        public int PaymentId { get; set; }

        [Column("contract_id")]
        public int ContractId { get; set; }

        [Column("provider")]
        public string Provider { get; set; } = "WSPay";

        [Column("external_order_id")]
        public string ExternalOrderId { get; set; } = string.Empty;

        [Column("amt_gross")]
        public decimal AmountGross { get; set; }

        [Column("currency")]
        public string Currency { get; set; } = "RSD";

        [Column("status")]
        public string Status { get; set; } = "Pending"; // Pending|Succeeded|Failed

        [Column("created_dt")]
        public DateTime CreatedDt { get; set; } = DateTime.Now;

        [Column("updated_dt")]
        public DateTime? UpdatedDt { get; set; }

        [Column("stamp")]
        public int Stamp { get; set; } = 0;

        [Column("transaction_id")]
        public string? TransactionId { get; set; }

        [Column("last_callback_dt")]
        public DateTime? LastCallbackDt { get; set; }

        [Column("last_callback_status")]
        public string? LastCallbackStatus { get; set; }
    }
}