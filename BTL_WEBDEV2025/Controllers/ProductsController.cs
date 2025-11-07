using BTL_WEBDEV2025.Models;
using Microsoft.AspNetCore.Mvc;
using BTL_WEBDEV2025.Data;
using Microsoft.EntityFrameworkCore;

namespace BTL_WEBDEV2025.Controllers
{
    public class ProductsController : Controller
    {
        private readonly ILogger<ProductsController> _logger;
        private readonly AppDbContext _db;
        private readonly List<Product> _fallbackProducts;

        public ProductsController(ILogger<ProductsController> logger, AppDbContext db)
        {
            _logger = logger;
            _db = db;
            _fallbackProducts = InitializeProducts();
        }

        public IActionResult Index()
        {
            var all = TryGetProductsFromDb();
            return View(all);
        }

        public IActionResult Men()
        {
            var all = TryGetProductsFromDb();

            List<Product> products;
            List<string> brands;

            try
            {
                if (_db.Database.CanConnect())
                {
                    brands = _db.Brands.Select(b => b.Name).ToList();
                    products = _db.Products
                        .Include(p => p.Brand)
                        .Where(p => p.CategoryId == 1 || p.CategoryId == 4)
                        .ToList();
                    ViewBag.Brands = brands;
                    return View(products);
                }
            }
            catch { }

            brands = all.Select(p => p.Brand?.Name ?? "Others").Distinct().ToList();
            products = all.Where(p => (p.CategoryRef != null && (p.CategoryRef.Name == "Men" || p.CategoryRef.Name == "Unisex"))
                                       || (p.CategoryId == 1 || p.CategoryId == 4))
                          .ToList();

            ViewBag.Brands = brands;
            return View(products);
        }

        public IActionResult Women()
        {
            var all = TryGetProductsFromDb();
            List<Product> products;
            List<string> brands;

            try
            {
                if (_db.Database.CanConnect())
                {
                    brands = _db.Brands.Select(b => b.Name).ToList();
                    products = _db.Products
                        .Include(p => p.Brand)
                        .Where(p => p.CategoryId == 2 || p.CategoryId == 4)
                        .ToList();
                    ViewBag.Brands = brands;
                    return View(products);
                }
            }
            catch { }

            brands = all.Select(p => p.Brand?.Name ?? "Others").Distinct().ToList();
            products = all.Where(p => (p.CategoryRef != null && (p.CategoryRef.Name == "Women" || p.CategoryRef.Name == "Unisex"))
                                       || (p.CategoryId == 2 || p.CategoryId == 4))
                          .ToList();

            ViewBag.Brands = brands;
            return View(products);
        }

        public IActionResult Kid()
        {
            var all = TryGetProductsFromDb();
            List<Product> products;
            List<string> brands;

            try
            {
                if (_db.Database.CanConnect())
                {
                    brands = _db.Brands.Select(b => b.Name).ToList();
                    products = _db.Products
                        .Include(p => p.Brand)
                        .Where(p => p.CategoryId == 3 || p.CategoryId == 4)
                        .ToList();
                    ViewBag.Brands = brands;
                    return View(products);
                }
            }
            catch { }

            brands = all.Select(p => p.Brand?.Name ?? "Others").Distinct().ToList();
            products = all.Where(p => (p.CategoryRef != null && (p.CategoryRef.Name == "Kid" || p.CategoryRef.Name == "Unisex"))
                                       || (p.CategoryId == 3 || p.CategoryId == 4))
                          .ToList();

            ViewBag.Brands = brands;
            return View(products);
        }

        public IActionResult Search(string q)
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return View(new List<Product>());
            }

            List<Product> results;
            List<string> brands;

            try
            {
                if (_db.Database.CanConnect())
                {
                    brands = _db.Brands.Select(b => b.Name).ToList();
                    var query = q.Trim().ToLower();
                    results = _db.Products
                        .Include(p => p.Brand)
                        .Where(p => p.Name.ToLower().Contains(query) || 
                                   (p.Description != null && p.Description.ToLower().Contains(query)) ||
                                   (p.Brand != null && p.Brand.Name.ToLower().Contains(query)))
                        .ToList();
                    ViewBag.Brands = brands;
                    ViewBag.SearchQuery = q;
                    return View(results);
                }
            }
            catch { }

            var all = TryGetProductsFromDb();
            var queryLower = q.Trim().ToLower();
            results = all.Where(p => 
                p.Name.ToLower().Contains(queryLower) ||
                (p.Description != null && p.Description.ToLower().Contains(queryLower)) ||
                (p.Brand != null && p.Brand.Name.ToLower().Contains(queryLower))
            ).ToList();
            
            brands = all.Select(p => p.Brand?.Name ?? "Others").Distinct().ToList();
            ViewBag.Brands = brands;
            ViewBag.SearchQuery = q;
            return View(results);
        }

        private List<Product> InitializeProducts()
        {
            return new List<Product>
            {
                new Product { Id = 1, Name = "Air Max 270", Description = "Premium running shoes with Air Max technology", Price = 150, ImageUrl = "https://via.placeholder.com/300", CategoryId = 1, IsFeatured = true },
                new Product { Id = 2, Name = "Air Force 1", Description = "Classic lifestyle shoes", Price = 90, DiscountPrice = 70, ImageUrl = "https://via.placeholder.com/300", CategoryId = 4, IsFeatured = true, IsSpecialDeal = true },
                new Product { Id = 3, Name = "Zoom Pegasus", Description = "High-performance running shoes", Price = 120, ImageUrl = "https://via.placeholder.com/300", CategoryId = 1, IsFeatured = true },
                new Product { Id = 4, Name = "Revolution 6", Description = "Everyday running for women", Price = 60, ImageUrl = "https://via.placeholder.com/300", CategoryId = 2, IsFeatured = true },
                new Product { Id = 5, Name = "Court Vision", Description = "Basketball lifestyle shoes", Price = 65, DiscountPrice = 45, ImageUrl = "https://via.placeholder.com/300", CategoryId = 1, IsSpecialDeal = true },
                new Product { Id = 6, Name = "React Element", Description = "Futuristic design sneakers", Price = 130, ImageUrl = "https://via.placeholder.com/300", CategoryId = 4, IsFeatured = true },
                new Product { Id = 7, Name = "Free RN", Description = "Natural motion running shoes", Price = 80, DiscountPrice = 60, ImageUrl = "https://via.placeholder.com/300", CategoryId = 2, IsSpecialDeal = true },
                new Product { Id = 8, Name = "Dunk Low", Description = "Skateboarding classic", Price = 100, ImageUrl = "https://via.placeholder.com/300", CategoryId = 4, IsFeatured = true },
                new Product { Id = 9, Name = "Kids Air Max", Description = "Comfortable running for kids", Price = 70, ImageUrl = "https://via.placeholder.com/300", CategoryId = 3, IsFeatured = false },
                new Product { Id = 10, Name = "Kids Basketball", Description = "Basketball shoes for young athletes", Price = 50, DiscountPrice = 35, ImageUrl = "https://via.placeholder.com/300", CategoryId = 3, IsSpecialDeal = true }
            };
        }

        private List<Product> TryGetProductsFromDb()
        {
            try
            {
                if (_db.Database.CanConnect())
                {
                    var list = _db.Products.Include(p => p.Brand).ToList();
                    if (list.Count > 0) return list;
                }
            }
            catch { }
            return _fallbackProducts;
        }

        [HttpGet]
        public async Task<IActionResult> GetProductVariants(int productId)
        {
            try
            {
                var variants = await _db.ProductVariants
                    .Where(v => v.ProductId == productId && v.StockQuantity > 0)
                    .Select(v => new
                    {
                        v.Id,
                        v.Size,
                        v.Color,
                        v.StockQuantity
                    })
                    .OrderBy(v => v.Size)
                    .ThenBy(v => v.Color)
                    .ToListAsync();


                var sizes = variants
                    .Where(v => !string.IsNullOrWhiteSpace(v.Size))
                    .Select(v => v.Size)
                    .Distinct()
                    .OrderBy(s => s)
                    .ToList();

                var colors = variants
                    .Where(v => !string.IsNullOrWhiteSpace(v.Color))
                    .Select(v => v.Color)
                    .Distinct()
                    .OrderBy(c => c)
                    .ToList();

                return Json(new
                {
                    success = true,
                    sizes = sizes,
                    colors = colors,
                    variants = variants
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting product variants for ProductId={ProductId}", productId);
                return Json(new { success = false, sizes = new List<string>(), colors = new List<string>(), variants = new List<object>() });
            }
        }
    }
}

