using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace OnlineContract.Models
{
    public class ProductCreateDto
    {
        [Required]
        public string? Name { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class ProductUpdateDto
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public bool? IsActive { get; set; }
    }

    public class ProductVariantDto
    {
        public int? Id { get; set; }
        public string? Size { get; set; }
        public string? Color { get; set; }
        public decimal Price { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsDeleted { get; set; }
        public string? SizeKey { get; set; }
        public string? ColorKey { get; set; }
        public string? PhotoFileName { get; set; }
        public int QtyStore1 { get; set; }
        public int QtyStore2 { get; set; }
    }

    public class ProductDetailsUpdateDto
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public bool? IsActive { get; set; }
        public List<ProductVariantDto> Variants { get; set; } = new();
        public List<int> DeletedVariantIds { get; set; } = new();
    }
}
