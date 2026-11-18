using System.Security.Claims;
using System.Security.Cryptography;
using CloudPass.Application.Interfaces;
using CloudPass.Domain.Entities;
using CloudPass.Domain.Enums;
using CloudPass.Infrastructure.Data;
using CloudPass.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CloudPass.Web.Controllers;

public class AccountController : Controller
{
    private readonly CloudPassDbContext _db;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly IAuditService _audit;
    private readonly INotificationService _notifications;

    public AccountController(
        CloudPassDbContext db,
        IPasswordHasher<User> passwordHasher,
        IAuditService audit,
        INotificationService notifications)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _audit = audit;
        _notifications = notifications;
    }

    [HttpGet]
    public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var normalizedEmail = model.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Email.ToLower() == normalizedEmail))
        {
            ModelState.AddModelError(nameof(model.Email), "An account with this email address already exists.");
            return View(model);
        }

        var userRole = await _db.Roles.FirstAsync(r => r.Name == "User");
        var user = new User
        {
            FullName = model.FullName.Trim(),
            Email = normalizedEmail,
            PhoneNumber = model.PhoneNumber.Trim(),
            City = model.City?.Trim(),
            RoleId = userRole.Id,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, model.Password);

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        await _notifications.SendAsync(user.Id, "Welcome to CloudPass!",
            "Your account is secured with cloud-backed authentication. Book bus tickets or apply for digital commuter passes anytime.",
            NotificationCategory.System);

        await _audit.LogAsync(user.Id, user.Email, "USER_REGISTERED", "User", user.Id.ToString(),
            "New commuter account registered.", HttpContext.Connection.RemoteIpAddress?.ToString());

        await SignInUserAsync(user, userRole.Name, rememberMe: false);
        TempData["Success"] = $"Welcome to CloudPass, {user.FullName}!";
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
        => View(new LoginViewModel { ReturnUrl = returnUrl });

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var email = model.Email.Trim().ToLowerInvariant();
        var user = await _db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email && u.IsActive);

        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
            return View(model);
        }

        var verifyResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, model.Password);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            await _audit.LogAsync(user.Id, user.Email, "LOGIN_FAILED", "User", user.Id.ToString(),
                "Failed password verification attempt.", HttpContext.Connection.RemoteIpAddress?.ToString());
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
            return View(model);
        }

        if (verifyResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, model.Password);
            await _db.SaveChangesAsync();
        }

        await SignInUserAsync(user, user.Role.Name, model.RememberMe);
        await _audit.LogAsync(user.Id, user.Email, "USER_LOGIN", "User", user.Id.ToString(),
            $"Authenticated with role {user.Role.Name}.", HttpContext.Connection.RemoteIpAddress?.ToString());

        if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            return Redirect(model.ReturnUrl);

        return user.Role.Name == "Admin"
            ? RedirectToAction("Index", "Admin")
            : RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["Success"] = "You have been signed out securely.";
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var email = model.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email && u.IsActive);

        if (user is not null)
        {
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
            user.PasswordResetToken = token;
            user.PasswordResetExpiry = DateTime.UtcNow.AddMinutes(30);
            await _db.SaveChangesAsync();

            await _audit.LogAsync(user.Id, user.Email, "PASSWORD_RESET_REQUESTED", "User", user.Id.ToString(),
                "Generated time-limited password reset token (30 min TTL).", HttpContext.Connection.RemoteIpAddress?.ToString());

            // In local/college demo mode without SMTP configured, display the secure reset link cleanly
            ViewBag.DemoResetUrl = Url.Action("ResetPassword", "Account", new { email = user.Email, token }, Request.Scheme);
        }

        ViewBag.Submitted = true;
        return View(model);
    }

    [HttpGet]
    public IActionResult ResetPassword(string? email, string? token)
        => View(new ResetPasswordViewModel { Email = email ?? string.Empty, Token = token ?? string.Empty });

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var email = model.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email && u.IsActive);

        if (user is null ||
            string.IsNullOrWhiteSpace(user.PasswordResetToken) ||
            !string.Equals(user.PasswordResetToken, model.Token.Trim(), StringComparison.Ordinal) ||
            user.PasswordResetExpiry is null ||
            user.PasswordResetExpiry < DateTime.UtcNow)
        {
            ModelState.AddModelError(string.Empty, "Invalid or expired password reset token.");
            return View(model);
        }

        user.PasswordHash = _passwordHasher.HashPassword(user, model.NewPassword);
        user.PasswordResetToken = null;
        user.PasswordResetExpiry = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _audit.LogAsync(user.Id, user.Email, "PASSWORD_RESET_COMPLETED", "User", user.Id.ToString(),
            "User completed secure password reset.", HttpContext.Connection.RemoteIpAddress?.ToString());

        TempData["Success"] = "Your password has been reset. You can now sign in.";
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Profile()
    {
        var userId = GetCurrentUserId();
        var user = await _db.Users.Include(u => u.Role).FirstAsync(u => u.Id == userId);

        var vm = new ProfileViewModel
        {
            User = user,
            FullName = user.FullName,
            PhoneNumber = user.PhoneNumber,
            Address = user.Address,
            City = user.City,
            DateOfBirth = user.DateOfBirth,
            RecentBookings = await _db.Bookings
                .Include(b => b.BusSchedule).ThenInclude(s => s.Route)
                .Include(b => b.BusSchedule).ThenInclude(s => s.Bus)
                .Where(b => b.UserId == userId)
                .OrderByDescending(b => b.BookingDate)
                .Take(6)
                .AsNoTracking()
                .ToListAsync(),
            ActivePasses = await _db.BusPasses
                .Include(p => p.Route)
                .Where(p => p.UserId == userId && p.Status == BusPassStatus.Active)
                .OrderByDescending(p => p.ValidUntil)
                .AsNoTracking()
                .ToListAsync(),
            Notifications = await _notifications.GetUserNotificationsAsync(userId, 15)
        };

        return View(vm);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateProfile(ProfileViewModel model)
    {
        var userId = GetCurrentUserId();
        var user = await _db.Users.FirstAsync(u => u.Id == userId);

        if (string.IsNullOrWhiteSpace(model.FullName) || string.IsNullOrWhiteSpace(model.PhoneNumber))
        {
            TempData["Error"] = "Full name and phone number are required.";
            return RedirectToAction(nameof(Profile));
        }

        user.FullName = model.FullName.Trim();
        user.PhoneNumber = model.PhoneNumber.Trim();
        user.Address = model.Address?.Trim();
        user.City = model.City?.Trim();
        user.DateOfBirth = model.DateOfBirth;
        user.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var roleName = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        await SignInUserAsync(user, roleName, rememberMe: false);

        await _audit.LogAsync(user.Id, user.Email, "PROFILE_UPDATED", "User", user.Id.ToString(),
            "Updated personal contact details.", HttpContext.Connection.RemoteIpAddress?.ToString());

        TempData["Success"] = "Profile updated securely.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Password change failed. Ensure your new password meets security rules and matches confirmation.";
            return RedirectToAction(nameof(Profile));
        }

        var userId = GetCurrentUserId();
        var user = await _db.Users.FirstAsync(u => u.Id == userId);

        if (_passwordHasher.VerifyHashedPassword(user, user.PasswordHash, model.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            TempData["Error"] = "Current password is incorrect.";
            return RedirectToAction(nameof(Profile));
        }

        user.PasswordHash = _passwordHasher.HashPassword(user, model.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _audit.LogAsync(user.Id, user.Email, "PASSWORD_CHANGED", "User", user.Id.ToString(),
            "User changed account password.", HttpContext.Connection.RemoteIpAddress?.ToString());

        TempData["Success"] = "Password updated securely.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();

    private async Task SignInUserAsync(User user, string roleName, bool rememberMe)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, roleName)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = rememberMe });
    }

    private int GetCurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
