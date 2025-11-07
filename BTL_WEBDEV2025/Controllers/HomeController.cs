using System.Diagnostics;
using BTL_WEBDEV2025.Models;
using Microsoft.AspNetCore.Mvc;
using BTL_WEBDEV2025.Data;
using Microsoft.AspNetCore.Hosting;

namespace BTL_WEBDEV2025.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly List<Product> _fallbackProducts;

        public HomeController(ILogger<HomeController> logger, AppDbContext db, IWebHostEnvironment env)
        {
            _logger = logger;
            _db = db;
            _env = env;
            _fallbackProducts = InitializeProducts();
        }

        public IActionResult Index()
        {
            var products = TryGetProductsFromDb();
            var featured = products.Where(p => p.IsFeatured).ToList();
            if (featured == null || featured.Count == 0)
            {
                featured = products.Take(8).ToList();
            }
            var deals = products.Where(p => p.IsSpecialDeal || p.DiscountPrice.HasValue).ToList();
            if (deals == null || deals.Count == 0)
            {
                deals = products.Take(8).ToList();
            }
            ViewBag.Featured = featured;
            ViewBag.Deals = deals;
            var wanted = new[] { "Nike", "Adidas", "Balenciaga" };
            var rnd = new Random();
            var brandIcons = new List<(string Brand, string ImageUrl)>();
            foreach (var b in wanted)
            {
                var candidates = products
                    .Where(p => p.Brand != null && p.Brand.Name.Equals(b, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(p.ImageUrl))
                    .Select(p => p.ImageUrl!)
                    .Distinct()
                    .ToList();

                if (candidates.Count == 0)
                {
                    candidates = GetMediaImagesForBrand(b);
                }

                var shuffled = candidates.OrderBy(_ => rnd.Next()).Take(Math.Min(4, candidates.Count)).ToList();
                foreach (var url in shuffled)
                {
                    brandIcons.Add((b, url));
                }
            }
            ViewBag.BrandIcons = brandIcons;
            return View();
        }

        private List<string> GetMediaImagesForBrand(string brand)
        {
            try
            {
                var list = new List<string>();
                var root = _env.WebRootPath;
                if (string.IsNullOrWhiteSpace(root)) return list;
                var mediaDirs = new[]
                {
                    System.IO.Path.Combine(root, "media", "images"),
                    System.IO.Path.Combine(root, "media", "products")
                };
                foreach (var dir in mediaDirs)
                {
                    if (!Directory.Exists(dir)) continue;
                    var files = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                        .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                                 || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                                 || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                                 || f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                        .Where(f => System.IO.Path.GetFileName(f).Contains(brand, StringComparison.OrdinalIgnoreCase)
                                 || System.IO.Path.GetDirectoryName(f)!.Contains(brand, StringComparison.OrdinalIgnoreCase))
                        .Select(f => f.Replace(root, "").Replace("\\", "/"))
                        .Select(rel => rel.StartsWith("/") ? rel : "/" + rel)
                        .ToList();
                    list.AddRange(files);
                }
                return list.Distinct().ToList();
            }
            catch { return new List<string>(); }
        }

        public IActionResult Privacy()
        {
            return View();
        }


        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        private List<Product> InitializeProducts()
        {
            return new List<Product>
            {
                new Product { Id = 1, Name = "Air Max 270", Description = "Premium running shoes", Price = 150, ImageUrl = "https://via.placeholder.com/300", CategoryId = 1, IsFeatured = true },
                new Product { Id = 2, Name = "Air Force 1", Description = "Classic lifestyle shoes", Price = 90, DiscountPrice = 70, ImageUrl = "https://via.placeholder.com/300", CategoryId = 4, IsFeatured = true, IsSpecialDeal = true },
                new Product { Id = 3, Name = "Zoom Pegasus", Description = "High-performance running", Price = 120, ImageUrl = "https://via.placeholder.com/300", CategoryId = 1, IsFeatured = true },
                new Product { Id = 4, Name = "Revolution 6", Description = "Everyday running", Price = 60, ImageUrl = "https://via.placeholder.com/300", CategoryId = 2, IsFeatured = true },
                new Product { Id = 5, Name = "Court Vision", Description = "Basketball lifestyle", Price = 65, DiscountPrice = 45, ImageUrl = "https://via.placeholder.com/300", CategoryId = 1, IsSpecialDeal = true },
                new Product { Id = 6, Name = "React Element", Description = "Futuristic design", Price = 130, ImageUrl = "https://via.placeholder.com/300", CategoryId = 4, IsFeatured = true },
                new Product { Id = 7, Name = "Free RN", Description = "Natural motion", Price = 80, DiscountPrice = 60, ImageUrl = "https://via.placeholder.com/300", CategoryId = 2, IsSpecialDeal = true },
                new Product { Id = 8, Name = "Dunk Low", Description = "Skateboarding classic", Price = 100, ImageUrl = "https://via.placeholder.com/300", CategoryId = 4, IsFeatured = true }
            };
        }

        private List<Product> TryGetProductsFromDb()
        {
            try
            {
                if (_db.Database.CanConnect())
                {
                    var list = _db.Products.ToList();
                    if (list.Count > 0) return list;
                }
            }
            catch { }
            return _fallbackProducts;
        }
    }
}
