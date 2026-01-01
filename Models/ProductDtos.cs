using System.ComponentModel.DataAnnotations;

namespace OnlineContract.Models
{
    public class ProductCreateDto
    {
        [Required]
        public string? Name { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class ProductUpdateDto
    {
        public int? Stamp { get; set; }
        public string? Name { get; set; }
        public bool? IsActive { get; set; }
    }

    public class ProductVariantDto
    {
        public int? Id { get; set; }
        public int? Stamp { get; set; }
        public string? Size { get; set; }
        public string? Color { get; set; }
        public decimal Amount { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsDeleted { get; set; }
        public string? SizeKey { get; set; }
        public string? ColorKey { get; set; }
        public string? PhotoFileName { get; set; }
        public int QtyStore1 { get; set; }
        public int? QtyStore1Stamp { get; set; }
        public int QtyStore2 { get; set; }
        public int? QtyStore2Stamp { get; set; }
    }

    public class ProductDetailsUpdateDto
    {
        public int? Stamp { get; set; }
        public string? Name { get; set; }
        public bool? IsActive { get; set; }
        public List<ProductVariantDto> Variants { get; set; } = new();
        public List<int> DeletedVariantIds { get; set; } = new();
        // Optional notes payload to allow atomic save of notes with product details
        public NotesBulkSaveDto? Notes { get; set; }
    }
}
