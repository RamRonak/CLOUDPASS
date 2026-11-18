using System.Security.Claims;
using CloudPass.Application.Interfaces;
using CloudPass.Domain.Enums;
using CloudPass.Infrastructure.Data;
using CloudPass.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CloudPass.Web.Controllers;

public class TicketController : Controller
{
    private readonly CloudPassDbContext _db;
    private readonly IQrCodeService _qrService;
    private readonly IPdfTicketService _pdfService;
    private readonly ITicketStorageService _storage;
    private readonly IBookingService _bookingService;
    private readonly IAuditService _audit;

    public TicketController(
        CloudPassDbContext db,
        IQrCodeService qrService,
        IPdfTicketService pdfService,
        ITicketStorageService storage,
        IBookingService bookingService,
        IAuditService audit)
    {
        _db = db;
        _qrService = qrService;
        _pdfService = pdfService;
        _storage = storage;
        _bookingService = bookingService;
        _audit = audit;
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> ViewTicket(string bookingRef)
    {
        var booking = await _db.Bookings
            .Include(b => b.User)
            .Include(b => b.Passengers)
            .Include(b => b.Payments)
            .Include(b => b.BusSchedule).ThenInclude(s => s.Bus)
            .Include(b => b.BusSchedule).ThenInclude(s => s.Route)
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.BookingReference == bookingRef);

        if (booking is null) return NotFound();

        var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var isAdmin = User.IsInRole("Admin");
        if (booking.UserId != currentUserId && !isAdmin)
        {
            await _audit.LogAsync(currentUserId, User.FindFirstValue(ClaimTypes.Email) ?? "", "IDOR_TICKET_ACCESS_BLOCKED",
                "Booking", bookingRef, "Blocked unauthorized attempt to access another user's digital ticket via URL.",
                HttpContext.Connection.RemoteIpAddress?.ToString());
            return Forbid();
        }

        if (booking.BookingStatus != BookingStatus.Confirmed || booking.PaymentStatus != PaymentStatus.Paid)
        {
            TempData["Error"] = "Digital ticket is only generated after verified payment confirmation.";
            return RedirectToAction("Payment", "Booking", new { bookingRef });
        }

        var fullToken = $"CP1.TKT.{booking.BookingReference}.{booking.TicketTokenNonce}.{booking.TicketTokenSignature}";
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var verifyUrl = $"{baseUrl}/Ticket/Verify?token={Uri.EscapeDataString(fullToken)}";

        var vm = new DigitalTicketViewModel
        {
            Booking = booking,
            Payment = booking.Payments.OrderByDescending(p => p.CreatedAt).FirstOrDefault(),
            SignedToken = fullToken,
            VerificationUrl = verifyUrl,
            QrCodeDataUri = _qrService.GenerateQrDataUri(verifyUrl, 7)
        };

        return View(vm);
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> DownloadPdf(string bookingRef)
    {
        var booking = await _db.Bookings
            .Include(b => b.Passengers)
            .Include(b => b.BusSchedule).ThenInclude(s => s.Bus)
            .Include(b => b.BusSchedule).ThenInclude(s => s.Route)
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.BookingReference == bookingRef);

        if (booking is null) return NotFound();

        var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (booking.UserId != currentUserId && !User.IsInRole("Admin"))
        {
            await _audit.LogAsync(currentUserId, User.FindFirstValue(ClaimTypes.Email) ?? "", "IDOR_PDF_DOWNLOAD_BLOCKED",
                "Booking", bookingRef, "Blocked unauthorized attempt to download another user's ticket PDF.",
                HttpContext.Connection.RemoteIpAddress?.ToString());
            return Forbid();
        }

        if (booking.BookingStatus != BookingStatus.Confirmed || booking.PaymentStatus != PaymentStatus.Paid)
            return BadRequest("Ticket PDF is unavailable for unpaid or unconfirmed bookings.");

        byte[]? pdfBytes = null;
        if (!string.IsNullOrWhiteSpace(booking.TicketPdfBlobName))
        {
            pdfBytes = await _storage.DownloadAsync(booking.TicketPdfBlobName);
        }

        if (pdfBytes is null)
        {
            var fullToken = $"CP1.TKT.{booking.BookingReference}.{booking.TicketTokenNonce}.{booking.TicketTokenSignature}";
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var verifyUrl = $"{baseUrl}/Ticket/Verify?token={Uri.EscapeDataString(fullToken)}";
            var qrBytes = _qrService.GenerateQrPngBytes(verifyUrl);
            pdfBytes = _pdfService.GenerateTicketPdf(booking, verifyUrl, qrBytes);

            var blobName = booking.TicketPdfBlobName ?? $"tickets/{booking.BookingReference}.pdf";
            await _storage.UploadAsync(blobName, pdfBytes, "application/pdf");
        }

        return File(pdfBytes, "application/pdf", $"CloudPass-Ticket-{booking.BookingReference}.pdf");
    }

    [HttpGet]
    [EnableRateLimiting("verify")]
    public async Task<IActionResult> Verify(string? token = null)
    {
        var vm = new TicketVerifyPageViewModel { TokenInput = token };
        if (!string.IsNullOrWhiteSpace(token))
        {
            int? verifierId = User.Identity?.IsAuthenticated == true
                ? int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)
                : null;
            vm.Result = await _bookingService.VerifyTokenAsync(token, markAsUsed: false, verifierId, HttpContext.Connection.RemoteIpAddress?.ToString());
        }

        if (User.IsInRole("Admin"))
        {
            vm.RecentLogs = await _db.TicketVerifications
                .OrderByDescending(v => v.VerifiedAt)
                .Take(10)
                .AsNoTracking()
                .ToListAsync();
        }

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("verify")]
    public async Task<IActionResult> Verify(TicketVerifyPageViewModel model)
    {
        int? verifierId = User.Identity?.IsAuthenticated == true
            ? int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)
            : null;

        // Only Admins/Conductors can mark a ticket as single-use boarded
        var canMarkUsed = model.MarkAsUsed && User.IsInRole("Admin");

        model.Result = await _bookingService.VerifyTokenAsync(
            model.TokenInput ?? string.Empty,
            canMarkUsed,
            verifierId,
            HttpContext.Connection.RemoteIpAddress?.ToString());

        if (User.IsInRole("Admin"))
        {
            model.RecentLogs = await _db.TicketVerifications
                .OrderByDescending(v => v.VerifiedAt)
                .Take(10)
                .AsNoTracking()
                .ToListAsync();
        }

        return View(model);
    }
}
