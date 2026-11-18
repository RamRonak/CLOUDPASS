using System.Security.Claims;
using CloudPass.Application.Interfaces;
using CloudPass.Domain.Enums;
using CloudPass.Infrastructure.Data;
using CloudPass.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CloudPass.Web.Controllers;

[Authorize]
public class PassController : Controller
{
    private readonly CloudPassDbContext _db;
    private readonly IBusPassService _passService;
    private readonly IQrCodeService _qrService;
    private readonly IPdfTicketService _pdfService;
    private readonly ITicketStorageService _storage;
    private readonly IAuditService _audit;

    public PassController(
        CloudPassDbContext db,
        IBusPassService passService,
        IQrCodeService qrService,
        IPdfTicketService pdfService,
        ITicketStorageService storage,
        IAuditService audit)
    {
        _db = db;
        _passService = passService;
        _qrService = qrService;
        _pdfService = pdfService;
        _storage = storage;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = GetCurrentUserId();
        var passes = await _db.BusPasses
            .Include(p => p.Route)
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.IssuedAt)
            .AsNoTracking()
            .ToListAsync();

        var apps = await _db.BusPassApplications
            .Include(a => a.Route)
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .AsNoTracking()
            .ToListAsync();

        var routes = await _db.Routes
            .Where(r => r.IsActive)
            .OrderBy(r => r.SourceCity)
            .AsNoTracking()
            .ToListAsync();

        var vm = new BusPassIndexViewModel
        {
            ActivePasses = passes.Where(p => p.Status == BusPassStatus.Active && p.ValidUntil >= DateTime.UtcNow.Date).ToList(),
            ExpiredOrRevokedPasses = passes.Where(p => p.Status != BusPassStatus.Active || p.ValidUntil < DateTime.UtcNow.Date).ToList(),
            Applications = apps,
            AvailableRoutes = routes
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(int routeId, int durationMonths, DateTime requestedStartDate, string idProofType, string idProofNumber, int? renewedFromPassId = null)
    {
        var res = await _passService.SubmitApplicationAsync(
            GetCurrentUserId(),
            GetCurrentUserEmail(),
            routeId,
            durationMonths,
            requestedStartDate,
            idProofType,
            idProofNumber,
            renewedFromPassId,
            HttpContext.Connection.RemoteIpAddress?.ToString());

        TempData[res.Success ? "Success" : "Error"] = res.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Renew(int passId, int durationMonths = 1)
    {
        var userId = GetCurrentUserId();
        var oldPass = await _db.BusPasses
            .Include(p => p.Application)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == passId && p.UserId == userId);

        if (oldPass is null) return NotFound();

        var startDate = oldPass.ValidUntil >= DateTime.UtcNow.Date ? oldPass.ValidUntil.AddDays(1) : DateTime.UtcNow.Date;
        var res = await _passService.SubmitApplicationAsync(
            userId,
            GetCurrentUserEmail(),
            oldPass.RouteId,
            durationMonths,
            startDate,
            oldPass.Application.IdProofType,
            oldPass.Application.IdProofNumber,
            renewedFromPassId: oldPass.Id,
            HttpContext.Connection.RemoteIpAddress?.ToString());

        TempData[res.Success ? "Success" : "Error"] = res.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> ViewPass(string passNumber)
    {
        var pass = await _db.BusPasses
            .Include(p => p.User)
            .Include(p => p.Route)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PassNumber == passNumber);

        if (pass is null) return NotFound();

        var userId = GetCurrentUserId();
        if (pass.UserId != userId && !User.IsInRole("Admin"))
        {
            await _audit.LogAsync(userId, GetCurrentUserEmail(), "IDOR_PASS_ACCESS_BLOCKED", "BusPass", passNumber,
                "Blocked unauthorized attempt to access another user's bus pass.", HttpContext.Connection.RemoteIpAddress?.ToString());
            return Forbid();
        }

        var fullToken = $"CP1.PAS.{pass.PassNumber}.{pass.VerificationTokenNonce}.{pass.VerificationTokenSignature}";
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var verifyUrl = $"{baseUrl}/Ticket/Verify?token={Uri.EscapeDataString(fullToken)}";

        var vm = new DigitalBusPassViewModel
        {
            Pass = pass,
            SignedToken = fullToken,
            VerificationUrl = verifyUrl,
            QrCodeDataUri = _qrService.GenerateQrDataUri(verifyUrl, 7)
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> DownloadPdf(string passNumber)
    {
        var pass = await _db.BusPasses
            .Include(p => p.User)
            .Include(p => p.Route)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PassNumber == passNumber);

        if (pass is null) return NotFound();

        var userId = GetCurrentUserId();
        if (pass.UserId != userId && !User.IsInRole("Admin"))
            return Forbid();

        byte[]? pdfBytes = null;
        if (!string.IsNullOrWhiteSpace(pass.PassPdfBlobName))
            pdfBytes = await _storage.DownloadAsync(pass.PassPdfBlobName);

        if (pdfBytes is null)
        {
            var fullToken = $"CP1.PAS.{pass.PassNumber}.{pass.VerificationTokenNonce}.{pass.VerificationTokenSignature}";
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var verifyUrl = $"{baseUrl}/Ticket/Verify?token={Uri.EscapeDataString(fullToken)}";
            var qrBytes = _qrService.GenerateQrPngBytes(verifyUrl);
            pdfBytes = _pdfService.GenerateBusPassPdf(pass, verifyUrl, qrBytes);

            var blobName = pass.PassPdfBlobName ?? $"passes/{pass.PassNumber}.pdf";
            await _storage.UploadAsync(blobName, pdfBytes, "application/pdf");
        }

        return File(pdfBytes, "application/pdf", $"CloudPass-BusPass-{pass.PassNumber}.pdf");
    }

    private int GetCurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string GetCurrentUserEmail() => User.FindFirstValue(ClaimTypes.Email) ?? "user@cloudpass.io";
}
