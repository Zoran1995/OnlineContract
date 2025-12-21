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
        Accepted = 10,
        PartiallyAccepted = 11,
        Rejected = 12,
        InProgress = 13,
        Completed = 14,
        Dispatched = 15,
        Delivered = 16,
        Returned = 17,
        Cancelled = 18,
        WrittenOff = 19
    }
}