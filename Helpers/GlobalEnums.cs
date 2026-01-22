namespace OnlineContract.Helpers
{
    public enum EventType
    {
        Information = 2,
        Warning = 3,
        Error = 4
    }

    public enum UserRole
    {
        Customer = 5,
        Worker = 6,
        Manager = 7,
        Administrator = 8
    }
    
    public enum ContractState
    {
        Draft = 9,
        Submitted = 10,
        Accepted = 11,
        PartiallyAccepted = 12,
        Rejected = 13,
        InProgress = 14,
        Completed = 15,
        Dispatched = 16,
        Delivered = 17,
        Returned = 18,
        Cancelled = 19,
        WrittenOff = 20,
        Refunded = 21
    }

    public enum ProductStateInOrder
    {
        Draft = 22,
        Submitted = 23,
        Accepted = 24,       
        Rejected = 25
    }

    public enum TaskPriority
    {
        Urgent = 26,
        High = 27,
        Normal = 28,
        Low = 29
    }

    public enum TaskStatus
    {
        NotStarted = 30,
        Started= 31,
        Approved = 32,
        Rejected = 33,
        Cancelled = 34,
        Completed = 35,
        Failed = 36,
        Successful = 37,
        Warning = 38,
        SuccessfulNothingProcessed = 39
    }

    public enum ApprovalRuleContext
    {
        RefundPayment = 40,
        ContractWriteOff = 41
    }
}