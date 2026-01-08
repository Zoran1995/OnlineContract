using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineContract.Models
{
    [Table("contract_state", Schema = "dbo")]
    public class ContractStateLookup
    {
        // Use lookup_set_id as key for simplicity
        [Key]
        [Column("lookup_set_id")]
        public int LookupSetId { get; set; }

        [Column("is_start_state")]
        public bool IsStartState { get; set; }

        [Column("is_end_state")]
        public bool IsEndState { get; set; }
    }
}
