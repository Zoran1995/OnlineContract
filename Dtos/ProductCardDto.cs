namespace OnlineContract.Dtos;

public class ProductCardDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? PhotoFileName { get; set; }
    public string? MainComment { get; set; }
    public decimal MinAmount { get; set; }
}