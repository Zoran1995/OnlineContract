namespace OnlineContract.Models
{
    public class ApprovalRule
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public bool IsDeleted { get; set; }
        public int TaskAssignedToId { get; set; }
        public decimal AmtThreshold { get; set; }
        public decimal PctThreshold { get; set; }
        public int ApprovalRuleContextId { get; set; }
        public int Stamp { get; set; }
    }
}