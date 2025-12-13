namespace OnlineContract.Models
{
    public class Store
    {
        public int StoreId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Phone_Number { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Working_Hours { get; set; }
        public DateTime Created_At { get; set; }
        public DateTime Updated_At { get; set; }
    }
}