namespace OnlineContract.Dtos;

public class AvailabilityDto
{
    public int StoreId { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public int QtyOnHand { get; set; }
}