using System.Security.Claims;
using System.Security.Cryptography;
using CloudPass.Application.Interfaces;
using CloudPass.Application.Options;
using CloudPass.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CloudPass.Web.Controllers;

[ApiController]
[Route("api")]
[EnableRateLimiting("api")]
public class CloudPassApiController : ControllerBase
{
    private readonly CloudPassDbContext _db;
    private readonly IFareCalculationService _fareService;
    private readonly ITicketSecurityService _security;
    private readonly IBookingService _bookingService;
    private readonly INotificationService _notifications;
    private readonly PaymentGatewayOptions _paymentOptions;

    public CloudPassApiController(
        CloudPassDbContext db,
        IFareCalculationService fareService,
        ITicketSecurityService security,
        IBookingService bookingService,
        INotificationService notifications,
        IOptions<PaymentGatewayOptions> paymentOptions)
    {
        _db = db;
        _fareService = fareService;
        _security = security;
        _bookingService = bookingService;
        _notifications = notifications;
        _paymentOptions = paymentOptions.Value;
    }

    /// <summary>
    /// Official server-side fare preview endpoint (read-only; checkout still enforces full server recalculation).
    /// </summary>
    [HttpGet("fares/ticket")]
    public async Task<IActionResult> CalculateTicketFare([FromQuery] int scheduleId, [FromQuery] int seatCount)
    {
        var schedule = await _db.BusSchedules.Include(s => s.Route).AsNoTracking().FirstOrDefaultAsync(s => s.Id == scheduleId);
        if (schedule is null || seatCount < 1 || seatCount > 6)
            return BadRequest(new { error = "Invalid schedule or seat count." });

        return Ok(_fareService.CalculateTicketFare(schedule, schedule.Route, seatCount));
    }

    [HttpGet("fares/pass")]
    public async Task<IActionResult> CalculatePassFare([FromQuery] int routeId, [FromQuery] int durationMonths)
    {
        var route = await _db.Routes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == routeId && r.IsActive);
        if (route is null)
            return NotFound(new { error = "Route not found." });

        return Ok(_fareService.CalculateBusPassPrice(route, durationMonths));
    }

    /// <summary>
    /// Cryptographic QR ticket & bus pass verification REST API.
    /// </summary>
    [HttpPost("tickets/verify")]
    [EnableRateLimiting("verify")]
    public async Task<IActionResult> VerifyTicketApi([FromBody] VerifyTicketApiRequest req)
    {
        int? verifierId = User.Identity?.IsAuthenticated == true
            ? int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)
            : null;

        var canMarkUsed = req.MarkAsUsed && User.IsInRole("Admin");
        var result = await _bookingService.VerifyTokenAsync(
            req.Token ?? string.Empty,
            canMarkUsed,
            verifierId,
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return result.IsValid ? Ok(result) : UnprocessableEntity(result);
    }

    /// <summary>
    /// Razorpay-compatible signed payment webhook endpoint.
    /// Verifies X-Razorpay-Signature (HMAC-SHA256) before confirming any booking.
    /// </summary>
    [HttpPost("payments/webhook")]
    public async Task<IActionResult> PaymentWebhook()
    {
        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync();
        var signature = Request.Headers["X-Razorpay-Signature"].FirstOrDefault() ?? string.Empty;
        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        var result = await _bookingService.ProcessSignedWebhookAsync(rawBody, signature, baseUrl);
        if (!result.Success)
            return Unauthorized(new { status = "rejected", message = result.Message });

        return Ok(new { status = "verified", bookingReference = result.Reference, message = result.Message });
    }

    /// <summary>
    /// Sandbox/Test-Mode gateway helper ONLY active when PaymentGateway:TestMode is true.
    /// Simulates the Razorpay gateway issuing a signed test payment response without exposing secrets to the browser.
    /// Never collects or stores real card data.
    /// </summary>
    [HttpPost("payments/test-authorize")]
    [Authorize]
    public async Task<IActionResult> AuthorizeTestGatewayPayment([FromBody] TestPaymentAuthRequest req)
    {
        if (!_paymentOptions.TestMode)
            return Forbid();

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var payment = await _db.Payments
            .Include(p => p.Booking)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.GatewayOrderId == req.GatewayOrderId && p.Booking.BookingReference == req.BookingReference);

        if (payment is null || (payment.Booking.UserId != userId && !User.IsInRole("Admin")))
            return NotFound(new { error = "Pending payment order not found." });

        var paymentId = $"pay_CPTEST{Convert.ToHexString(RandomNumberGenerator.GetBytes(5))}";
        var signature = _security.GeneratePaymentSignature(payment.GatewayOrderId, paymentId);

        return Ok(new
        {
            gatewayOrderId = payment.GatewayOrderId,
            gatewayPaymentId = paymentId,
            gatewaySignature = signature,
            method = req.PaymentMethod ?? "UPI (Sandbox)",
            testMode = true
        });
    }

    [HttpGet("notifications")]
    [Authorize]
    public async Task<IActionResult> GetNotifications()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var unread = await _notifications.GetUnreadCountAsync(userId);
        var items = await _notifications.GetUserNotificationsAsync(userId, 12);
        return Ok(new { unreadCount = unread, items });
    }

    [HttpPost("notifications/{id:int}/read")]
    [Authorize]
    public async Task<IActionResult> MarkNotificationRead(int id)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _notifications.MarkAsReadAsync(userId, id);
        return Ok(new { ok = true });
    }

    [HttpPost("notifications/read-all")]
    [Authorize]
    public async Task<IActionResult> MarkAllRead()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _notifications.MarkAllAsReadAsync(userId);
        return Ok(new { ok = true });
    }
}

public record VerifyTicketApiRequest(string? Token, bool MarkAsUsed = false);
public record TestPaymentAuthRequest(string BookingReference, string GatewayOrderId, string? PaymentMethod);
