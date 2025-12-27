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
        WrittenOff = 20
    }
}