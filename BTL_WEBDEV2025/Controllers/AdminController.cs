using BTL_WEBDEV2025.Models;
using BTL_WEBDEV2025.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace BTL_WEBDEV2025.Controllers
{
    public class AdminController : Controller
    {
        private readonly ILogger<AdminController> _logger;
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;

        public AdminController(ILogger<AdminController> logger, AppDbContext db, IWebHostEnvironment env)
        {
            _logger = logger;
            _db = db;
            _env = env;
        }

        // GET: Admin
        public IActionResult Index()
        {
            // Simple authentication 
            if (!IsAdmin())
            {
                return RedirectToAction("Login");
            }

            return View();
        }

        // GET: Admin/Login 
        public IActionResult Login()
        {
            return RedirectToAction("Login", "Account");
        }

        // =====================
        // JSON API for Dashboard
        // =====================
        [HttpGet("/admin/api/stats")]
        public async Task<IActionResult> GetStats()
        {
            if (!IsAdmin()) return Unauthorized();

            //Total (USD) = tổng TotalAmount trong Orders
            var totalSales = await _db.Orders.SumAsync(o => (decimal?)o.TotalAmount) ?? 0m;
            var totalOrders = await _db.Orders.CountAsync();
            // Customer (RoleId != 1)
            var totalCustomers = await _db.Users.CountAsync(u => u.RoleId != 1);
            // Profit = Revenue (tạm thời, vì chưa có cost price)
            var profit = totalSales;

            return Ok(new
            {
                totalSales,
                totalOrders,
                totalCustomers,
                profit
            });
        }

        // =====================
        // REPORT (minimal): summary & timeseries by day
        // =====================
        [HttpGet("/admin/api/report/summary")]
        public async Task<IActionResult> ReportSummary([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            if (!IsAdmin()) return Unauthorized();
            var start = (from ?? DateTime.UtcNow.Date.AddDays(-30)).Date;
            var end = (to ?? DateTime.UtcNow.Date).Date.AddDays(1).AddTicks(-1); // inclusive end of day

            var ordersInRange = _db.Orders.Where(o => o.CreatedAt >= start && o.CreatedAt <= end);
            var revenue = await ordersInRange.SumAsync(o => (decimal?)o.TotalAmount) ?? 0m;
            var orders = await ordersInRange.CountAsync();
            var items = await _db.OrderDetails
                .Where(od => ordersInRange.Select(o => o.Id).Contains(od.OrderId))
                .SumAsync(od => (int?)od.Quantity) ?? 0;
            var aov = orders == 0 ? 0 : revenue / orders;

            return Ok(new { revenueUSD = revenue, orders, items, aov });
        }

        [HttpGet("/admin/api/report/timeseries")]
        public async Task<IActionResult> ReportTimeseries([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            if (!IsAdmin()) return Unauthorized();
            var start = (from ?? DateTime.UtcNow.Date.AddDays(-30)).Date;
            var end = (to ?? DateTime.UtcNow.Date).Date.AddDays(1).AddTicks(-1);

            var query = await _db.Orders
                .Where(o => o.CreatedAt >= start && o.CreatedAt <= end)
                .GroupBy(o => o.CreatedAt.Date)
                .Select(g => new { period = g.Key, revenueUSD = g.Sum(x => x.TotalAmount), orders = g.Count() })
                .OrderBy(x => x.period)
                .ToListAsync();

            var result = query.Select(x => new { period = x.period.ToString("yyyy-MM-dd"), x.revenueUSD, x.orders });
            return Ok(result);
        }

        // =====================
        // PRODUCTS CRUD (JSON)
        // =====================
        [HttpGet("/admin/api/products")]
        public async Task<IActionResult> GetProducts()
        {
            if (!IsAdmin()) return Unauthorized();
            var list = await _db.Products
                .Include(p => p.CategoryRef)
                .Include(p => p.Brand)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.Description,
                    p.Price,
                    p.DiscountPrice,
                    Category = p.CategoryRef != null ? p.CategoryRef.Name : "",
                    CategoryId = p.CategoryId,
                    p.ImageUrl,
                    BrandId = p.BrandId,
                    BrandName = p.Brand != null ? p.Brand.Name : ""
                }).ToListAsync();
            return Ok(list);
        }

        [HttpGet("/admin/api/brands")]
        public async Task<IActionResult> GetBrands()
        {
            if (!IsAdmin()) return Unauthorized();
            var brands = await _db.Brands
                .Select(b => new { b.Id, b.Name })
                .OrderBy(b => b.Name)
                .ToListAsync();
            return Ok(brands);
        }

        [HttpGet("/admin/api/categories")]
        public async Task<IActionResult> GetCategories()
        {
            if (!IsAdmin()) return Unauthorized();
            var categories = await _db.Categories
                .Select(c => new { c.Id, c.Name })
                .OrderBy(c => c.Name)
                .ToListAsync();
            return Ok(categories);
        }

        public class ProductUpsertDto
        {
            public int? Id { get; set; }
            [Required]
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            [Range(0, double.MaxValue)]
            public decimal Price { get; set; }
            public string? ImageUrl { get; set; }
        }

        public class InventoryUpdateDto
        {
            public int StockQuantity { get; set; }
            public string? Size { get; set; }
            public string? Color { get; set; }
        }

        public class InventoryCreateDto
        {
            public int ProductId { get; set; }
            public string Size { get; set; } = string.Empty;
            public string Color { get; set; } = string.Empty;
            public int StockQuantity { get; set; }
        }

        // Helper function to generate image file name
        private string GenerateImageFileName(string brandFolder, string extension)
        {
            var webRoot = _env.WebRootPath;
            var brandDir = Path.Combine(webRoot, "media", "images", "products", brandFolder);
            Directory.CreateDirectory(brandDir);

            int nextNumber = 1;
            var escapedBrand = System.Text.RegularExpressions.Regex.Escape(brandFolder);
            var extWithoutDot = extension.TrimStart('.').ToLowerInvariant();
            var pattern = new System.Text.RegularExpressions.Regex(@"^" + escapedBrand + @"(\d+)\." + extWithoutDot + "$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            if (Directory.Exists(brandDir))
            {
                var allFiles = Directory.GetFiles(brandDir);
                var numbers = allFiles
                    .Select(f => Path.GetFileName(f))
                    .Select(fn => pattern.Match(fn))
                    .Where(m => m.Success)
                    .Select(m => int.Parse(m.Groups[1].Value))
                    .ToList();
                
                if (numbers.Any())
                {
                    nextNumber = numbers.Max() + 1;
                }
            }

            return $"{brandFolder}{nextNumber:D2}{extension}";
        }

        [HttpPost("/admin/api/products/create")]
        public async Task<IActionResult> CreateProduct([FromForm] string name, [FromForm] string description, [FromForm] decimal price, [FromForm] decimal? discountPrice, [FromForm] int? categoryId, [FromForm] int? brandId, IFormFile? imageFile)
        {
            if (!IsAdmin()) return Unauthorized();
            
            if (string.IsNullOrWhiteSpace(name))
            {
                return BadRequest("Name is required");
            }

            string imageUrl = string.Empty;

            if (imageFile != null && imageFile.Length > 0)
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
                var ext = Path.GetExtension(imageFile.FileName).ToLowerInvariant();
                
                if (!allowedExtensions.Contains(ext))
                {
                    return BadRequest("Invalid image format. Allowed: jpg, jpeg, png, webp");
                }

                string brandFolder = "general";
                if (brandId.HasValue)
                {
                    var brand = await _db.Brands.FirstOrDefaultAsync(b => b.Id == brandId.Value);
                    if (brand != null)
                    {
                        brandFolder = brand.Name.ToLowerInvariant();
                        foreach (var c in Path.GetInvalidFileNameChars())
                        {
                            brandFolder = brandFolder.Replace(c, '-');
                        }
                        brandFolder = brandFolder.Replace(" ", "-");
                    }
                }

                var fileName = GenerateImageFileName(brandFolder, ext);
                var webRoot = _env.WebRootPath;
                var brandDir = Path.Combine(webRoot, "media", "images", "products", brandFolder);
                Directory.CreateDirectory(brandDir);
                var savePath = Path.Combine(brandDir, fileName);

                using (var stream = System.IO.File.Create(savePath))
                {
                    await imageFile.CopyToAsync(stream);
                }

                imageUrl = $"/media/images/products/{brandFolder}/{fileName}";
            }

            var product = new Product
            {
                Name = name,
                Description = description ?? string.Empty,
                Price = price,
                DiscountPrice = discountPrice,
                ImageUrl = imageUrl,
                CategoryId = categoryId,
                BrandId = brandId
            };
            _db.Products.Add(product);
            await _db.SaveChangesAsync();
            return Ok(new { product.Id });
        }

        [HttpPost("/admin/api/products/update/{id:int}")]
        public async Task<IActionResult> UpdateProduct(int id, [FromForm] string name, [FromForm] string description, [FromForm] decimal price, [FromForm] decimal? discountPrice, [FromForm] int? categoryId, [FromForm] int? brandId, [FromForm] string? imageUrl, IFormFile? imageFile)
        {
            if (!IsAdmin()) return Unauthorized();
            var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id);
            if (product == null) return NotFound();

            product.Name = name ?? product.Name;
            product.Description = description ?? product.Description;
            product.Price = price;
            product.DiscountPrice = discountPrice;
            if (categoryId.HasValue) product.CategoryId = categoryId.Value;
            if (brandId.HasValue) product.BrandId = brandId.Value;

            if (imageFile != null && imageFile.Length > 0)
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
                var ext = Path.GetExtension(imageFile.FileName).ToLowerInvariant();
                
                if (!allowedExtensions.Contains(ext))
                {
                    return BadRequest("Invalid image format. Allowed: jpg, jpeg, png, webp");
                }

                string brandFolder = "general";
                if (brandId.HasValue)
                {
                    var brand = await _db.Brands.FirstOrDefaultAsync(b => b.Id == brandId.Value);
                    if (brand != null)
                    {
                        brandFolder = brand.Name.ToLowerInvariant();
                        foreach (var c in Path.GetInvalidFileNameChars())
                        {
                            brandFolder = brandFolder.Replace(c, '-');
                        }
                        brandFolder = brandFolder.Replace(" ", "-");
                    }
                }
                else if (product.BrandId.HasValue)
                {
                    var brand = await _db.Brands.FirstOrDefaultAsync(b => b.Id == product.BrandId.Value);
                    if (brand != null)
                    {
                        brandFolder = brand.Name.ToLowerInvariant();
                        foreach (var c in Path.GetInvalidFileNameChars())
                        {
                            brandFolder = brandFolder.Replace(c, '-');
                        }
                        brandFolder = brandFolder.Replace(" ", "-");
                    }
                }

                var webRoot = _env.WebRootPath;
                if (!string.IsNullOrEmpty(product.ImageUrl))
                {
                    var oldPath = Path.Combine(webRoot, product.ImageUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                    if (System.IO.File.Exists(oldPath))
                    {
                        try { System.IO.File.Delete(oldPath); } catch { }
                    }
                }

                var fileName = GenerateImageFileName(brandFolder, ext);
                var brandDir = Path.Combine(webRoot, "media", "images", "products", brandFolder);
                Directory.CreateDirectory(brandDir);
                var savePath = Path.Combine(brandDir, fileName);

                using (var stream = System.IO.File.Create(savePath))
                {
                    await imageFile.CopyToAsync(stream);
                }

                product.ImageUrl = $"/media/images/products/{brandFolder}/{fileName}";
            }
            else if (!string.IsNullOrWhiteSpace(imageUrl))
            {
                product.ImageUrl = imageUrl;
            }

            await _db.SaveChangesAsync();
            return Ok();
        }

        [HttpPost("/admin/api/products/delete/{id:int}")]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            if (!IsAdmin()) return Unauthorized();
            var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id);
            if (product == null) return NotFound();
            _db.Products.Remove(product);
            await _db.SaveChangesAsync();
            return Ok();
        }

        // =====================
        // INVENTORY (ProductVariants)
        // =====================
        [HttpGet("/admin/api/inventory")]
        public async Task<IActionResult> GetInventory()
        {
            if (!IsAdmin()) return Unauthorized();
            var rows = await _db.ProductVariants
                .Include(v => v.Product)
                .Select(v => new
                {
                    v.Id,
                    v.ProductId,
                    ProductName = v.Product != null ? v.Product.Name : "",
                    v.Size,
                    v.Color,
                    v.StockQuantity
                })
                .OrderBy(v => v.ProductName)
                .ThenBy(v => v.Size)
                .ThenBy(v => v.Color)
                .ToListAsync();
            return Ok(rows);
        }

        [HttpPost("/admin/api/inventory/update/{id:int}")]
        public async Task<IActionResult> UpdateInventory(int id, [FromBody] InventoryUpdateDto dto)
        {
            if (!IsAdmin()) return Unauthorized();
            var variant = await _db.ProductVariants.FirstOrDefaultAsync(v => v.Id == id);
            if (variant == null) return NotFound();
            variant.StockQuantity = Math.Max(0, dto.StockQuantity);
            if (!string.IsNullOrWhiteSpace(dto.Size)) variant.Size = dto.Size;
            if (!string.IsNullOrWhiteSpace(dto.Color)) variant.Color = dto.Color;
            await _db.SaveChangesAsync();
            return Ok();
        }

        [HttpPost("/admin/api/inventory/create")]
        public async Task<IActionResult> CreateInventory([FromBody] InventoryCreateDto dto)
        {
            if (!IsAdmin()) return Unauthorized();
            if (dto.ProductId <= 0) return BadRequest("ProductId is required");
            if (string.IsNullOrWhiteSpace(dto.Size)) return BadRequest("Size is required");
            if (string.IsNullOrWhiteSpace(dto.Color)) return BadRequest("Color is required");

            // Check if product exists
            var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == dto.ProductId);
            if (product == null) return NotFound("Product not found");

            // Check if variant already exists
            var existing = await _db.ProductVariants
                .FirstOrDefaultAsync(v => v.ProductId == dto.ProductId && v.Size == dto.Size && v.Color == dto.Color);
            if (existing != null) return BadRequest("Variant with this Size and Color already exists");

            var variant = new ProductVariant
            {
                ProductId = dto.ProductId,
                Size = dto.Size,
                Color = dto.Color,
                StockQuantity = Math.Max(0, dto.StockQuantity)
            };
            _db.ProductVariants.Add(variant);
            await _db.SaveChangesAsync();
            return Ok(new { variant.Id });
        }

        [HttpPost("/admin/api/inventory/delete/{id:int}")]
        public async Task<IActionResult> DeleteInventory(int id)
        {
            if (!IsAdmin()) return Unauthorized();
            var variant = await _db.ProductVariants.FirstOrDefaultAsync(v => v.Id == id);
            if (variant == null) return NotFound();
            _db.ProductVariants.Remove(variant);
            await _db.SaveChangesAsync();
            return Ok();
        }

        // =====================
        // CUSTOMERS CRUD (Users)
        // =====================
        [HttpGet("/admin/api/customers")]
        public async Task<IActionResult> GetCustomers()
        {
            if (!IsAdmin()) return Unauthorized();
            var list = await _db.Users
                .Where(u => u.RoleId != 1)
                .Select(u => new
                {
                    u.Id,
                    u.FullName,
                    u.Email,
                    u.PhoneNumber,
                    u.DateOfBirth
                }).ToListAsync();
            return Ok(list);
        }

        public class CustomerUpsertDto
        {
            public int? Id { get; set; }
            [Required, EmailAddress]
            public string Email { get; set; } = string.Empty;
            [Required]
            public string FullName { get; set; } = string.Empty;
            public string? PhoneNumber { get; set; }
            public DateTime? DateOfBirth { get; set; }
        }

        [HttpPost("/admin/api/customers/create")]
        public async Task<IActionResult> CreateCustomer([FromBody] CustomerUpsertDto dto)
        {
            if (!IsAdmin()) return Unauthorized();
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var user = new User
            {
                Email = dto.Email,
                FullName = dto.FullName,
                PhoneNumber = dto.PhoneNumber,
                DateOfBirth = dto.DateOfBirth,
                PasswordHash = "temp", 
                RoleId = 2
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync();
            return Ok(new { user.Id });
        }

        [HttpPost("/admin/api/customers/update/{id:int}")]
        public async Task<IActionResult> UpdateCustomer(int id, [FromBody] CustomerUpsertDto dto)
        {
            if (!IsAdmin()) return Unauthorized();
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.RoleId != 1);
            if (user == null) return NotFound();
            user.Email = dto.Email;
            user.FullName = dto.FullName;
            user.PhoneNumber = dto.PhoneNumber;
            user.DateOfBirth = dto.DateOfBirth;
            await _db.SaveChangesAsync();
            return Ok();
        }

        [HttpPost("/admin/api/customers/delete/{id:int}")]
        public async Task<IActionResult> DeleteCustomer(int id)
        {
            if (!IsAdmin()) return Unauthorized();
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.RoleId != 1);
            if (user == null) return NotFound();
            _db.Users.Remove(user);
            await _db.SaveChangesAsync();
            return Ok();
        }

        // POST: Admin/Login 
        [HttpPost]
        public IActionResult Login(string username, string password)
        {
            return RedirectToAction("Login", "Account");
        }

        // GET: Admin/Logout 
        [HttpGet]
        public IActionResult Logout()
        {
            HttpContext.Session.Remove("IsAdmin");
            HttpContext.Session.Remove("UserId");
            HttpContext.Session.Remove("UserEmail");
            return RedirectToAction("Login", "Account");
        }

        // GET: Admin/BulkImages
        public IActionResult BulkImages()
        {
            if (!IsAdmin()) return RedirectToAction("Login");
            return View();
        }

        // POST: Admin/BulkImages
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkImages(List<IFormFile> files, string? brand, string? category, bool updateOnly = false)
        {
            if (!IsAdmin()) return RedirectToAction("Login");
            if (files == null || files.Count == 0)
            {
                TempData["Success"] = "No files selected.";
                return RedirectToAction("BulkImages");
            }

            var webRoot = _env.WebRootPath;
            var safeSegment = !string.IsNullOrWhiteSpace(brand) ? brand.Trim() : (!string.IsNullOrWhiteSpace(category) ? category.Trim() : "uploads");
            foreach (var c in Path.GetInvalidFileNameChars()) safeSegment = safeSegment.Replace(c, '-');
            var destDir = Path.Combine(webRoot, "media", "products", safeSegment);
            Directory.CreateDirectory(destDir);

            int saved = 0, updated = 0, created = 0;
            foreach (var file in files)
            {
                if (file.Length <= 0) continue;
                var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (ext != ".jpg" && ext != ".jpeg" && ext != ".png" && ext != ".webp") continue;

                var baseName = Path.GetFileNameWithoutExtension(file.FileName);
                // normalize filename
                var normalized = new string(baseName.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-').ToArray());
                normalized = System.Text.RegularExpressions.Regex.Replace(normalized, "-+", "-").Trim('-');
                var fileName = normalized + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ext;
                var savePath = Path.Combine(destDir, fileName);
                using (var stream = System.IO.File.Create(savePath))
                {
                    await file.CopyToAsync(stream);
                }
                saved++;

                var relUrl = "/media/products/" + safeSegment + "/" + fileName;

                // Try map to product by trailing id pattern "...-123"
                int? parsedId = null;
                var m = System.Text.RegularExpressions.Regex.Match(normalized, @"-(\d+)$");
                if (m.Success && int.TryParse(m.Groups[1].Value, out var idVal)) parsedId = idVal;

                Product? product = null;
                if (parsedId.HasValue)
                {
                    product = _db.Products.FirstOrDefault(p => p.Id == parsedId.Value);
                }
                if (product == null)
                {
                    var nameCandidate = baseName;
                    product = _db.Products.FirstOrDefault(p => p.Name == nameCandidate);
                }

                if (product != null)
                {
                    product.ImageUrl = relUrl;
                    _db.SaveChanges();
                    updated++;
                }
                else if (!updateOnly)
                {
                    // Tìm CategoryId từ tên category
                    int? categoryId = null;
                    if (!string.IsNullOrWhiteSpace(category))
                    {
                        var categoryEntity = await _db.Categories.FirstOrDefaultAsync(c => c.Name.Equals(category.Trim(), StringComparison.OrdinalIgnoreCase));
                        if (categoryEntity != null)
                        {
                            categoryId = categoryEntity.Id;
                        }
                    }
                    // Nếu không tìm thấy, mặc định là Unisex (CategoryId = 4)
                    if (!categoryId.HasValue)
                    {
                        var unisexCategory = await _db.Categories.FirstOrDefaultAsync(c => c.Name == "Unisex");
                        categoryId = unisexCategory?.Id ?? 4;
                    }

                    var newProduct = new Product
                    {
                        Name = baseName,
                        Description = "",
                        Price = 0,
                        DiscountPrice = null,
                        ImageUrl = relUrl,
                        CategoryId = categoryId,
                        IsFeatured = false,
                        IsSpecialDeal = false
                    };
                    _db.Products.Add(newProduct);
                    _db.SaveChanges();
                    created++;
                }
            }

            TempData["Success"] = $"Uploaded {saved} files. Updated {updated} products, created {created}.";
            return RedirectToAction("Index");
        }

        private bool IsAdmin()
        {
            // Check UserId and RoleId from Account login
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId.HasValue)
            {
                var user = _db.Users.FirstOrDefault(u => u.Id == userId.Value);
                if (user != null && user.RoleId == 1) // RoleId 1 = Admin
                {
                    return true;
                }
            }
            
            return false;
        }

        // GET: Admin/Settings
        public IActionResult Settings()
        {
            if (!IsAdmin()) return RedirectToAction("Login");
            return View();
        }

        // POST: Admin/ChangeAdminPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ChangeAdminPassword(string newPassword, string confirmPassword)
        {
            if (!IsAdmin()) return RedirectToAction("Login");

            if (string.IsNullOrWhiteSpace(newPassword) || string.IsNullOrWhiteSpace(confirmPassword))
            {
                TempData["ChangePwdError"] = "Please fill in all required fields.";
                return RedirectToAction("Settings");
            }
            if (newPassword != confirmPassword)
            {
                TempData["ChangePwdError"] = "Password confirmation does not match.";
                return RedirectToAction("Settings");
            }
            if (newPassword.Length < 6 || newPassword.Length > 50)
            {
                TempData["ChangePwdError"] = "New password must be 6 to 50 characters.";
                return RedirectToAction("Settings");
            }

            
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId.HasValue)
            {
                var user = _db.Users.FirstOrDefault(u => u.Id == userId.Value && u.RoleId == 1);
                if (user != null)
                {
                    user.PasswordHash = newPassword; // plain per current project setup
                    _db.SaveChanges();
                    TempData["ChangePwdSuccess"] = "Admin password updated successfully.";
                    return RedirectToAction("Settings");
                }
            }

            // Fallback when session has no UserId (should not occur after auth unification)
            TempData["ChangePwdError"] = "Please sign in with an Admin account (RoleId = 1) to change password.";
            return RedirectToAction("Settings");
        }
    }
}

