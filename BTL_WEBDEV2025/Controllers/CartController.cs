using BTL_WEBDEV2025.Models;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using BTL_WEBDEV2025.Data;
using Microsoft.EntityFrameworkCore;

namespace BTL_WEBDEV2025.Controllers
{
    public class CartController : Controller
    {
        private readonly ILogger<CartController> _logger;
        private const string CartSessionKey = "ShoppingCart";

        private static readonly ConcurrentDictionary<string, bool> _paymentStatus = new ConcurrentDictionary<string, bool>();
        private static readonly ConcurrentDictionary<string, int> _orderTokenMap = new ConcurrentDictionary<string, int>();

        private readonly AppDbContext _db;

        public CartController(ILogger<CartController> logger, AppDbContext db)
        {
            _logger = logger;
            _db = db;
        }

        public IActionResult Index()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
            {
                ViewBag.RequireLogin = true;
                return View(new List<ShoppingCartItem>());
            }

            var cartItems = GetCartItems();
            return View(cartItems);
        }

        [HttpPost]
        public IActionResult AddToCart(int productId, string productName, decimal price, string imageUrl, int quantity = 1, string size = "", string color = "")
        {
            var cartItems = GetCartItems();
            
            var existingItem = cartItems.FirstOrDefault(x => x.ProductId == productId 
                                                            && string.Equals(x.Size ?? string.Empty, size ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                                                            && string.Equals(x.Color ?? string.Empty, color ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            if (existingItem != null)
            {
                existingItem.Quantity += quantity;
            }
            else
            {
                cartItems.Add(new ShoppingCartItem
                {
                    ProductId = productId,
                    ProductName = productName,
                    Price = price,
                    Quantity = quantity,
                    ImageUrl = imageUrl,
                    Size = size ?? string.Empty,
                    Color = color ?? string.Empty
                });
            }

            SaveCartItems(cartItems);
            
            return Json(new { success = true, count = cartItems.Count });
        }

        [HttpPost]
        public IActionResult RemoveFromCart(int productId, string size = "", string color = "")
        {
            var cartItems = GetCartItems();
            cartItems.RemoveAll(x => x.ProductId == productId
                                       && string.Equals(x.Size ?? string.Empty, size ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                                       && string.Equals(x.Color ?? string.Empty, color ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            SaveCartItems(cartItems);
            
            return Json(new { success = true, count = cartItems.Count });
        }

        [HttpPost]
        public IActionResult UpdateCart(int productId, string size = "", string color = "", int quantity = 1)
        {
            var cartItems = GetCartItems();
            var item = cartItems.FirstOrDefault(x => x.ProductId == productId
                                                     && string.Equals(x.Size ?? string.Empty, size ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                                                     && string.Equals(x.Color ?? string.Empty, color ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            
            if (item != null)
            {
                if (quantity > 0)
                {
                    item.Quantity = quantity;
                }
                else
                {
                    cartItems.Remove(item);
                }
            }
            
            SaveCartItems(cartItems);
            return Json(new { success = true, count = cartItems.Count });
        }

        [HttpGet]
        public IActionResult GetCartCount()
        {
            var cartItems = GetCartItems();
            return Json(new { count = cartItems.Sum(x => x.Quantity) });
        }

        [HttpPost]
        public IActionResult ClearCart()
        {
            HttpContext.Session.Remove(CartSessionKey);
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> Checkout([FromForm] CheckoutViewModel model)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
            {
                return Json(new { success = false, needLogin = true });
            }

            if (!ModelState.IsValid)
            {
                var errors = ModelState
                    .Where(x => x.Value?.Errors.Count > 0)
                    .Select(x => new { Field = x.Key, Errors = x.Value?.Errors.Select(e => e.ErrorMessage) })
                    .ToList();
                return Json(new { success = false, message = "Validation failed", errors = errors });
            }

            var cartItems = GetCartItems();
            if (cartItems == null || !cartItems.Any())
            {
                return Json(new { success = false, message = "Cart is empty" });
            }

            var fullName = model.FullName?.Trim() ?? string.Empty;
            var address = model.Address?.Trim() ?? string.Empty;
            var email = model.Email?.Trim() ?? string.Empty;
            var phone = model.Phone?.Trim() ?? string.Empty;
            var paymentMethod = model.PaymentMethod?.Trim() ?? string.Empty;

            var orderGuid = System.Guid.NewGuid().ToString();
            _paymentStatus[orderGuid] = false;

            decimal total = cartItems.Sum(x => x.Price * x.Quantity);
            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var pmNorm = (paymentMethod ?? string.Empty).Trim().ToLowerInvariant();
                var payByValue = pmNorm == "cod" ? "cash" : (pmNorm == "transfer" ? "bank" : pmNorm);

                string initialStatus;
                if (payByValue == "cash") initialStatus = "Unpaid";
                else if (payByValue == "bank" || payByValue == "qr" || payByValue == "transfer" || payByValue == "card") initialStatus = "Paid";
                else initialStatus = "New";

                var order = new Order
                {
                    UserId = userId.Value,
                    CreatedAt = DateTime.UtcNow,
                    TotalAmount = total,
                    PaymentMethod = payByValue,
                    PaymentToken = orderGuid,
                    Status = initialStatus,
                    ShippingAddress = address,
                    NotificationEmail = email
                };

                _db.Orders.Add(order);
                await _db.SaveChangesAsync();

                _logger.LogInformation("Order created: OrderId={OrderId}, UserId={UserId}, Total={Total}, Address={Address}", order.Id, userId.Value, total, address ?? "N/A");

                _orderTokenMap[orderGuid] = order.Id;

                foreach (var it in cartItems)
                {
                    // Tìm ProductVariant từ ProductId, Size, Color
                    ProductVariant? variant = null;
                    if (it.ProductId > 0 && !string.IsNullOrWhiteSpace(it.Size) && !string.IsNullOrWhiteSpace(it.Color))
                    {
                        variant = await _db.ProductVariants
                            .FirstOrDefaultAsync(v => v.ProductId == it.ProductId 
                                && v.Size == it.Size 
                                && v.Color == it.Color);
                        if (variant == null)
                        {
                            // Nếu không tìm thấy variant, dùng variant đầu tiên của product
                            variant = await _db.ProductVariants
                                .FirstOrDefaultAsync(v => v.ProductId == it.ProductId);
                        }
                    }
                    else if (it.ProductId > 0)
                    {
                        // Nếu không có Size/Color, lấy variant đầu tiên
                        variant = await _db.ProductVariants
                            .FirstOrDefaultAsync(v => v.ProductId == it.ProductId);
                    }

                    if (variant == null)
                    {
                        _logger.LogWarning("Could not find ProductVariant for ProductId={ProductId}, Size={Size}, Color={Color}", 
                            it.ProductId, it.Size, it.Color);
                        continue; // Skip this item if no variant found
                    }

                    // Kiểm tra đủ hàng
                    if (variant.StockQuantity < it.Quantity)
                    {
                        _logger.LogWarning("Insufficient stock for ProductVariantId={VariantId}, Requested={Requested}, Available={Available}", 
                            variant.Id, it.Quantity, variant.StockQuantity);
                        throw new Exception($"Insufficient stock for {it.ProductName}. Available: {variant.StockQuantity}, Requested: {it.Quantity}");
                    }

                    // Trừ số lượng tồn kho
                    variant.StockQuantity -= it.Quantity;
                    _db.ProductVariants.Update(variant);

                    var od = new OrderDetail
                    {
                        OrderId = order.Id,
                        ProductVariantId = variant.Id,
                        Quantity = it.Quantity,
                        UnitPrice = it.Price
                    };
                    _db.OrderDetails.Add(od);
                }

                await _db.SaveChangesAsync();
                _logger.LogInformation("OrderDetails saved: OrderId={OrderId}, DetailCount={Count}, NotificationEmail={Email}", order.Id, cartItems.Count, email ?? "N/A");

                await tx.CommitAsync();
                _logger.LogInformation("Transaction committed successfully: OrderId={OrderId}", order.Id);

                var orderDetailCount = await _db.OrderDetails.CountAsync(od => od.OrderId == order.Id);
                if (orderDetailCount != cartItems.Count)
                {
                    _logger.LogError("OrderDetails verification failed: OrderId={OrderId}, Expected={Expected}, Found={Found}", 
                        order.Id, cartItems.Count, orderDetailCount);
                    return Json(new { success = false, message = "Order details were not saved properly" });
                }
                
                _logger.LogInformation("Order verification successful: OrderId={OrderId}, DetailCount={Count}", order.Id, orderDetailCount);

                _paymentStatus[orderGuid] = initialStatus == "Paid";

                SaveCartItems(new List<ShoppingCartItem>());

                string paymentInstructions = string.Empty;
                if (!string.IsNullOrWhiteSpace(pmNorm) && pmNorm.Equals("transfer", StringComparison.OrdinalIgnoreCase))
                {
                    var hostForQr = Request.Host.Host;
                    var port = Request.Host.Port;
                    if (string.IsNullOrEmpty(hostForQr) || hostForQr == "localhost" || hostForQr == "127.0.0.1")
                    {
                        try
                        {
                            var entry = Dns.GetHostEntry(Dns.GetHostName());
                            var lanIp = entry.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))?.ToString();
                            if (!string.IsNullOrEmpty(lanIp)) hostForQr = lanIp;
                        }
                        catch { }
                    }

                    var path = Url.Action("ConfirmPayment", "Cart", new { orderId = orderGuid });
                    var confirmUrl = $"{Request.Scheme}://{hostForQr}{(port.HasValue ? ":" + port.Value : "")}{path}";
                    var qrApi = "https://api.qrserver.com/v1/create-qr-code/?size=200x200&data=" + System.Net.WebUtility.UrlEncode(confirmUrl);

                    paymentInstructions = $"<div style=\"font-weight:600;margin-bottom:8px\">Bank transfer (QR)</div>" +
                                          $"<div style=\"margin-bottom:8px\">Scan this QR with your phone to confirm payment</div>" +
                                          $"<div style=\"margin-bottom:8px\"> <img alt=\"QR\" src=\"{qrApi}\" style=\"width:160px;height:160px;object-fit:contain;border:1px solid #eaeaea;\"/> </div>" +
                                          $"<div style=\"font-size:0.9rem;color:#666\">Or open this link on your phone: <a href=\"{confirmUrl}\" target=\"_blank\">{confirmUrl}</a></div>";
                }
                else if (payByValue == "cash")
                {
                    paymentInstructions = "<div style=\"font-weight:600\">Cash on delivery</div><div>Please pay the delivery person when your order arrives.</div>";
                }
                else
                {
                    paymentInstructions = string.Empty;
                }

                return Json(new { success = true, orderId = orderGuid, paymentInstructions = paymentInstructions });
            }
            catch (Exception ex)
            {
                try
                {
                    await tx.RollbackAsync();
                    _logger.LogError(ex, "Checkout transaction rolled back. UserId={UserId}, Total={Total}, Error={Error}", userId.Value, total, ex.Message);
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogError(rollbackEx, "Failed to rollback transaction");
                }
                
                _logger.LogError(ex, "Checkout save failed. UserId={UserId}, CartItems={Count}, ExceptionType={Type}", 
                    userId.Value, cartItems?.Count ?? 0, ex.GetType().Name);
                _logger.LogError("Stack trace: {StackTrace}", ex.StackTrace);
                
                return Json(new { success = false, message = $"Failed to create order: {ex.Message}" });
            }
        }

        [HttpGet]
        public IActionResult ConfirmPayment(string orderId)
        {
            if (string.IsNullOrEmpty(orderId)) return Content("Invalid order");
            var postUrl = Url.Action("ConfirmPaymentPost", "Cart");
            var html = $"<!doctype html><html><head><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Confirming...</title></head><body style=\"font-family:Arial,Helvetica,sans-serif;padding:20px;text-align:center\">" +
                       $"<h3>Processing payment confirmation...</h3>" +
                       $"<script>" +
                       $"fetch('{postUrl}',{{method:'POST',headers:{{'Content-Type':'application/json'}},body:JSON.stringify({{ orderId: '{System.Net.WebUtility.HtmlEncode(orderId)}' }})}})" +
                       ".then(r=>r.json()).then(j=>{ document.body.innerHTML = '<h2>Payment confirmed</h2><p>Order {System.Net.WebUtility.HtmlEncode(orderId)} marked as paid. You can close this page.</p>'; }).catch(e=>{ document.body.innerHTML = '<h2>Error</h2><p>Could not confirm payment.</p>'; });" +
                       $"</script></body></html>";
            return Content(html, "text/html");
        }

        [HttpPost]
        public async Task<IActionResult> ConfirmPaymentPost([FromBody] ConfirmPaymentRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.OrderId)) return BadRequest(new { success = false });
            _paymentStatus[req.OrderId] = true;

            try
            {
                var order = await _db.Orders.FirstOrDefaultAsync(o => o.PaymentToken == req.OrderId);
                if (order != null)
                {
                    order.Status = "Paid";
                    _db.Orders.Update(order);
                    await _db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ConfirmPaymentPost error");
            }

            if (_orderTokenMap.TryGetValue(req.OrderId, out var numericOrderId))
            {
                _logger.LogInformation("Payment confirmed for order token {Token} -> OrderId {OrderId}", req.OrderId, numericOrderId);
            }

            return Json(new { success = true });
        }

        [HttpGet]
        public IActionResult CheckPaymentStatus(string orderId)
        {
            if (string.IsNullOrEmpty(orderId)) return Json(new { paid = false });
            var paid = _paymentStatus.TryGetValue(orderId, out var v) && v;
            return Json(new { paid = paid });
        }

        private List<ShoppingCartItem> GetCartItems()
        {
            var cartJson = HttpContext.Session.GetString(CartSessionKey);
            if (string.IsNullOrEmpty(cartJson))
            {
                return new List<ShoppingCartItem>();
            }
            
            try
            {
                return JsonSerializer.Deserialize<List<ShoppingCartItem>>(cartJson) ?? new List<ShoppingCartItem>();
            }
            catch
            {
                return new List<ShoppingCartItem>();
            }
        }

        private void SaveCartItems(List<ShoppingCartItem> items)
        {
            var cartJson = JsonSerializer.Serialize(items);
            HttpContext.Session.SetString(CartSessionKey, cartJson);
        }
    }
}

public class ConfirmPaymentRequest { public string? OrderId { get; set; } }

