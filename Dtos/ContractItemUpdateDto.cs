namespace OnlineContract.Dtos
{
    public class ContractItemUpdateDto
    {
        public int ItemId { get; set; }
        public int Quantity { get; set; }
        public string? Size { get; set; }
        public string? Color { get; set; }
        // Concurrency
        public int ItemStamp { get; set; }
        public int ContractStamp { get; set; }
        // Optional preferred store
        public int? StoreId { get; set; }
    }
}
