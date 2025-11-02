using BTL_WEBDEV2025.Models;
using Microsoft.AspNetCore.Mvc;
using BTL_WEBDEV2025.Data;

namespace BTL_WEBDEV2025.Controllers
{
    public class AccountController : Controller
    {
        private readonly AppDbContext _db;

        public AccountController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public IActionResult Login()
        {
            return View(new LoginViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Login(LoginViewModel model)
        {
            // First step: just email entry - check database and show appropriate form
            if (!model.ShowPassword && !model.IsNewUser)
            {
                // Validate email format
                if (string.IsNullOrWhiteSpace(model.Email) || !model.Email.Contains("@"))
                {
                    if (!ModelState.IsValid)
                    {
                        return View(model);
                    }
                }

                // Check database if email exists
                var existingUser = _db.Users.FirstOrDefault(u => u.Email == model.Email);
                if (existingUser != null)
                {
                    // Email exists in database - show password field for login
                    ModelState.Clear();
                    model.ShowPassword = true;
                    model.IsNewUser = false;
                    return View(model);
                }
                else
                {
                    // Email not in database - show registration form
                    ModelState.Clear();
                    model.IsNewUser = true;
                    model.ShowPassword = true;
                    return View(model);
                }
            }

            // If we're in password step, skip email validation
            if (model.ShowPassword && !model.IsNewUser)
            {
                ModelState.Remove(nameof(LoginViewModel.Email));
                if (string.IsNullOrWhiteSpace(model.Email))
                {
                    return RedirectToAction("Login");
                }
            }
            else if (!ModelState.IsValid && !model.IsNewUser)
            {
                return View(model);
            }

            // Simple user sign-in by email
            var user = _db.Users.FirstOrDefault(u => u.Email == model.Email);
            if (user != null)
            {
                // Step 1: ask for password if not provided yet
                if (string.IsNullOrWhiteSpace(model.Password))
                {
                    ModelState.Clear();
                    model.ShowPassword = true;
                    model.IsNewUser = false;
                    return View(model);
                }

                // Step 2: verify password (plain for testing as requested)
                if (user.PasswordHash == model.Password)
                {
                    HttpContext.Session.SetInt32("UserId", user.Id);
                    HttpContext.Session.SetString("UserEmail", user.Email);
                    if (!string.IsNullOrWhiteSpace(user.FullName))
                    {
                        HttpContext.Session.SetString("UserName", user.FullName);
                    }
                    // redirect by role: 1 = Admin, others = normal user
                    if (user.RoleId == 1)
                    {
                        return RedirectToAction("Index", "Admin");
                    }
                    return RedirectToAction("Index", "User");
                }

                ModelState.Clear();
                ModelState.AddModelError("Password", "Password incorrect");
                model.ShowPassword = true;
                model.IsNewUser = false;
                return View(model);
            }

            // Email not found: inline registration step
            if (!model.IsNewUser)
            {
                ModelState.Clear();
                model.IsNewUser = true;
                model.ShowPassword = true; // reuse password field for account creation
                return View(model);
            }

            // Validate registration fields with strict rules
            ModelState.Remove(nameof(LoginViewModel.Email));
            if (string.IsNullOrWhiteSpace(model.Email) || !model.Email.Contains("@"))
            {
                ModelState.AddModelError("Email", "Valid email is required");
            }
            
            if (string.IsNullOrWhiteSpace(model.FirstName) || model.FirstName.Length < 2 || model.FirstName.Length > 50)
                ModelState.AddModelError("FirstName", "First name must be between 2 and 50 characters");
            else if (!System.Text.RegularExpressions.Regex.IsMatch(model.FirstName, @"^[a-zA-Z\s'-]+$"))
                ModelState.AddModelError("FirstName", "First name can only contain letters, spaces, hyphens, and apostrophes");
            
            if (string.IsNullOrWhiteSpace(model.LastName) || model.LastName.Length < 2 || model.LastName.Length > 50)
                ModelState.AddModelError("LastName", "Last name must be between 2 and 50 characters");
            else if (!System.Text.RegularExpressions.Regex.IsMatch(model.LastName, @"^[a-zA-Z\s'-]+$"))
                ModelState.AddModelError("LastName", "Last name can only contain letters, spaces, hyphens, and apostrophes");
            
            if (string.IsNullOrWhiteSpace(model.Password))
                ModelState.AddModelError("Password", "Password is required");
            else if (model.Password.Length < 6 || model.Password.Length > 50)
                ModelState.AddModelError("Password", "Password must be between 6 and 50 characters");
            else if (!System.Text.RegularExpressions.Regex.IsMatch(model.Password, @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).*$"))
                ModelState.AddModelError("Password", "Password must contain at least one uppercase letter, one lowercase letter, and one number");
            
            if (string.IsNullOrWhiteSpace(model.Preference))
                ModelState.AddModelError("Preference", "Shopping preference is required");
            
            if (!model.AcceptPolicy)
                ModelState.AddModelError("AcceptPolicy", "You must agree to continue");

            // Optional DOB validation if any part is provided
            if (model.BirthDay.HasValue || model.BirthMonth.HasValue || model.BirthYear.HasValue)
            {
                if (!(model.BirthDay.HasValue && model.BirthMonth.HasValue && model.BirthYear.HasValue))
                {
                    ModelState.AddModelError("BirthDay", "Please complete date of birth (DD/MM/YYYY)");
                }
                else
                {
                    try
                    {
                        var testDob = new DateTime(model.BirthYear!.Value, model.BirthMonth!.Value, model.BirthDay!.Value);
                        var today = DateTime.Today;
                        var age = today.Year - testDob.Year;
                        if (testDob.Date > today.AddYears(-age)) age--;
                        // Must be over 13 years old (age must be > 13, not >= 13)
                        if (age <= 13) ModelState.AddModelError("BirthDay", "You must be over 13 years old.");
                    }
                    catch
                    {
                        ModelState.AddModelError("BirthDay", "Invalid date of birth");
                    }
                }
            }

            if (!ModelState.IsValid)
            {
                model.IsNewUser = true;
                model.ShowPassword = true;
                return View(model);
            }

            DateTime? dob = null;
            if (model.BirthDay.HasValue && model.BirthMonth.HasValue && model.BirthYear.HasValue)
            {
                try
                {
                    dob = new DateTime(model.BirthYear!.Value, model.BirthMonth!.Value, model.BirthDay!.Value);
                }
                catch { }
            }

            try
            {
                // Ensure RoleId 2 exists to avoid FK errors on fresh DBs
                if (!_db.Roles.Any(r => r.Id == 2))
                {
                    _db.Roles.Add(new Role { Id = 2, Name = "Customer" });
                    _db.SaveChanges();
                }
                var newUser = new User
                {
                    Email = model.Email,
                    PasswordHash = model.Password!,
                    FullName = ($"{model.FirstName} {model.LastName}").Trim(),
                    ShoppingPreference = model.Preference,
                    DateOfBirth = dob,
                    RoleId = 2
                };
                _db.Users.Add(newUser);
                _db.SaveChanges();

                // Show success notification and redirect to login form (step 1)
                var successMsg = "Account created successfully! You can now sign in.";
                TempData["AuthMessage"] = successMsg; // for popup
                return RedirectToAction("Login", new { success = 1, msg = successMsg });
            }
            catch (Exception ex)
            {
                // Ghi lại lỗi và trả về lại form đăng ký inline với thông báo rõ ràng
                ModelState.AddModelError(string.Empty, "Không thể lưu tài khoản. Vui lòng kiểm tra kết nối CSDL và thử lại.");
                ModelState.AddModelError(string.Empty, ex.GetType().Name + ": " + (ex.InnerException?.Message ?? ex.Message));
                model.IsNewUser = true;
                model.ShowPassword = true;
                return View(model);
            }
        }

        [HttpGet]
        public IActionResult Register(string? email)
        {
            // Redirect to unified Sign In flow; prefill email if provided
            if (!string.IsNullOrWhiteSpace(email))
            {
                return View("Login", new LoginViewModel { Email = email, IsNewUser = true, ShowPassword = true });
            }
            return RedirectToAction("Login");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Register(RegisterViewModel model)
        {
            // additional server-side validation for DOB combination
            if (model.BirthDay.HasValue || model.BirthMonth.HasValue || model.BirthYear.HasValue)
            {
                if (!(model.BirthDay.HasValue && model.BirthMonth.HasValue && model.BirthYear.HasValue))
                {
                    ModelState.AddModelError("BirthDay", "Please complete date of birth (DD/MM/YYYY)");
                }
                else
                {
                    try
                    {
                        var dateOfBirth = new DateTime(model.BirthYear!.Value, model.BirthMonth!.Value, model.BirthDay!.Value);
                        // Must be over 13 years old (age must be > 13, not >= 13)
                        var today = DateTime.Today;
                        var age = today.Year - dateOfBirth.Year;
                        if (dateOfBirth.Date > today.AddYears(-age)) age--;
                        if (age <= 13)
                        {
                            ModelState.AddModelError("BirthDay", "You must be over 13 years old.");
                        }
                    }
                    catch
                    {
                        ModelState.AddModelError("BirthDay", "Invalid date of birth");
                    }
                }
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // Create user in database (plain password for testing as requested)
            var exists = _db.Users.Any(u => u.Email == model.Email);
            if (exists)
            {
                ModelState.AddModelError("Email", "Email already exists");
                return View(model);
            }

            var user = new User
            {
                Email = model.Email,
                PasswordHash = model.Password,
                FullName = ($"{model.FirstName} {model.LastName}").Trim(),
                PhoneNumber = null,
                RoleId = 2 // Customer
            };

            _db.Users.Add(user);
            _db.SaveChanges();

            HttpContext.Session.SetInt32("UserId", user.Id);
            HttpContext.Session.SetString("UserEmail", user.Email);
            if (!string.IsNullOrWhiteSpace(user.FullName))
            {
                HttpContext.Session.SetString("UserName", user.FullName);
            }

            TempData["AuthMessage"] = "Account created.";
            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Logout()
        {
            HttpContext.Session.Remove("UserId");
            HttpContext.Session.Remove("UserEmail");
            return RedirectToAction("Login");
        }
    }
}