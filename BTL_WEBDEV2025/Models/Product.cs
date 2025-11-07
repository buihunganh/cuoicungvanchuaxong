namespace BTL_WEBDEV2025.Models
{
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public decimal? DiscountPrice { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public int? CategoryId { get; set; }
        public Category? CategoryRef { get; set; }
        public int? BrandId { get; set; }
        public Brand? Brand { get; set; }
        public bool IsFeatured { get; set; }
        public bool IsSpecialDeal { get; set; }

        public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();
    }
}

