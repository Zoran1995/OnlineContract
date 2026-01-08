namespace OnlineContract.Dtos;

public class VariantSelectionDto
{
    public int ProductVariantId { get; set; }
    public decimal Amount { get; set; }
    public string? PhotoFileName { get; set; }
    public string? Size { get; set; }
    public string? Color { get; set; }
}