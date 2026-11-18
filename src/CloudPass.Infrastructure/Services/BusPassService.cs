using System.Security.Cryptography;
using CloudPass.Application.DTOs;
using CloudPass.Application.Interfaces;
using CloudPass.Domain.Entities;
using CloudPass.Domain.Enums;
using CloudPass.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CloudPass.Infrastructure.Services;

public class BusPassService : IBusPassService
{
    private readonly CloudPassDbContext _db;
    private readonly IFareCalculationService _fareService;
    private readonly ITicketSecurityService _security;
    private readonly IQrCodeService _qrService;
    private readonly IPdfTicketService _pdfService;
    private readonly ITicketStorageService _storage;
    private readonly INotificationService _notifications;
    private readonly IAuditService _audit;

    public BusPassService(
        CloudPassDbContext db,
        IFareCalculationService fareService,
        ITicketSecurityService security,
        IQrCodeService qrService,
        IPdfTicketService pdfService,
        ITicketStorageService storage,
        INotificationService notifications,
        IAuditService audit)
    {
        _db = db;
        _fareService = fareService;
        _security = security;
        _qrService = qrService;
        _pdfService = pdfService;
        _storage = storage;
        _notifications = notifications;
        _audit = audit;
    }

    public async Task<OperationResult> SubmitApplicationAsync(
        int userId,
        string userEmail,
        int routeId,
        int durationMonths,
        DateTime startDate,
        string idProofType,
        string idProofNumber,
        int? renewedFromPassId = null,
        string? ipAddress = null)
    {
        var route = await _db.Routes.FirstOrDefaultAsync(r => r.Id == routeId && r.IsActive);
        if (route is null)
            return OperationResult.Fail("Selected route is not available for bus passes.");

        var pricing = _fareService.CalculateBusPassPrice(route, durationMonths);
        var appRef = $"BPA-{DateTime.UtcNow:yyyyMMdd}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(3))}";

        var application = new BusPassApplication
        {
            ApplicationReference = appRef,
            UserId = userId,
            RouteId = route.Id,
            DurationMonths = pricing.DurationMonths,
            RequestedStartDate = startDate.Date < DateTime.UtcNow.Date ? DateTime.UtcNow.Date : startDate.Date,
            CalculatedPrice = pricing.FinalPrice,
            IdProofType = string.IsNullOrWhiteSpace(idProofType) ? "Commuter ID" : idProofType.Trim(),
            IdProofNumber = string.IsNullOrWhiteSpace(idProofNumber) ? "N/A" : idProofNumber.Trim(),
            Status = PassApplicationStatus.Pending,
            RenewedFromPassId = renewedFromPassId,
            CreatedAt = DateTime.UtcNow
        };

        _db.BusPassApplications.Add(application);
        await _db.SaveChangesAsync();

        await _notifications.SendAsync(
            userId,
            renewedFromPassId.HasValue ? "Bus Pass Renewal Submitted" : "Bus Pass Application Submitted",
            $"Application {appRef} for {route.SourceCity} ↔ {route.DestinationCity} ({pricing.DurationMonths} month(s), INR {pricing.FinalPrice:N2}) is pending admin approval.",
            NotificationCategory.BusPass,
            appRef);

        await _audit.LogAsync(userId, userEmail, "PASS_APPLICATION_SUBMITTED", "BusPassApplication", appRef,
            $"Submitted {pricing.DurationMonths}-month pass application for {route.SourceCity} ↔ {route.DestinationCity}. Server price: INR {pricing.FinalPrice:N2}.", ipAddress);

        return OperationResult.Ok($"Application {appRef} submitted and awaiting admin review.", appRef);
    }

    public async Task<OperationResult> ApproveApplicationAsync(int applicationId, int adminUserId, string adminEmail, string? remarks, string baseUrl, string? ipAddress = null)
    {
        var app = await _db.BusPassApplications
            .Include(a => a.User)
            .Include(a => a.Route)
            .FirstOrDefaultAsync(a => a.Id == applicationId);

        if (app is null)
            return OperationResult.Fail("Bus pass application not found.");

        if (app.Status != PassApplicationStatus.Pending)
            return OperationResult.Fail("Only pending applications can be approved.");

        var passNumber = $"CPP-{DateTime.UtcNow:yyyyMM}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(3))}";
        var tokenInfo = _security.GenerateSignedPassToken(passNumber);
        var validFrom = app.RequestedStartDate.Date < DateTime.UtcNow.Date ? DateTime.UtcNow.Date : app.RequestedStartDate.Date;
        var validUntil = validFrom.AddMonths(app.DurationMonths);
        var blobName = $"passes/{passNumber}_{tokenInfo.Nonce[..8]}.pdf";

        app.Status = PassApplicationStatus.Approved;
        app.AdminRemarks = string.IsNullOrWhiteSpace(remarks) ? "Approved by CloudPass Admin." : remarks.Trim();
        app.ReviewedByUserId = adminUserId;
        app.ReviewedAt = DateTime.UtcNow;

        var pass = new BusPass
        {
            PassNumber = passNumber,
            ApplicationId = app.Id,
            UserId = app.UserId,
            RouteId = app.RouteId,
            ValidFrom = validFrom,
            ValidUntil = validUntil,
            AmountPaid = app.CalculatedPrice,
            Status = BusPassStatus.Active,
            VerificationTokenNonce = tokenInfo.Nonce,
            VerificationTokenSignature = tokenInfo.Signature,
            VerificationTokenHash = tokenInfo.TokenHash,
            PassPdfBlobName = blobName,
            RenewedFromPassId = app.RenewedFromPassId,
            IssuedAt = DateTime.UtcNow,
            User = app.User,
            Route = app.Route
        };

        _db.BusPasses.Add(pass);
        await _db.SaveChangesAsync();

        // Generate & store pass PDF in cloud storage
        var verifyUrl = $"{baseUrl.TrimEnd('/')}/Ticket/Verify?token={Uri.EscapeDataString(tokenInfo.FullToken)}";
        var qrBytes = _qrService.GenerateQrPngBytes(verifyUrl);
        var pdfBytes = _pdfService.GenerateBusPassPdf(pass, verifyUrl, qrBytes);
        await _storage.UploadAsync(blobName, pdfBytes, "application/pdf");

        await _notifications.SendAsync(
            app.UserId,
            "Bus Pass Approved!",
            $"Your CloudPass {passNumber} ({app.Route.SourceCity} ↔ {app.Route.DestinationCity}) is approved and valid until {validUntil:dd MMM yyyy}. Download your digital QR pass now.",
            NotificationCategory.BusPass,
            passNumber);

        await _audit.LogAsync(adminUserId, adminEmail, "ADMIN_PASS_APPROVED", "BusPass", passNumber,
            $"Approved application {app.ApplicationReference} and issued digital pass {passNumber} valid until {validUntil:yyyy-MM-dd}.", ipAddress);

        return OperationResult.Ok($"Application approved and digital pass {passNumber} issued.", passNumber);
    }

    public async Task<OperationResult> RejectApplicationAsync(int applicationId, int adminUserId, string adminEmail, string remarks, string? ipAddress = null)
    {
        var app = await _db.BusPassApplications
            .Include(a => a.Route)
            .FirstOrDefaultAsync(a => a.Id == applicationId);

        if (app is null)
            return OperationResult.Fail("Bus pass application not found.");

        if (app.Status != PassApplicationStatus.Pending)
            return OperationResult.Fail("Only pending applications can be rejected.");

        app.Status = PassApplicationStatus.Rejected;
        app.AdminRemarks = string.IsNullOrWhiteSpace(remarks) ? "Application did not meet verification requirements." : remarks.Trim();
        app.ReviewedByUserId = adminUserId;
        app.ReviewedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        await _notifications.SendAsync(
            app.UserId,
            "Bus Pass Application Rejected",
            $"Application {app.ApplicationReference} ({app.Route.SourceCity} ↔ {app.Route.DestinationCity}) was rejected. Reason: {app.AdminRemarks}",
            NotificationCategory.BusPass,
            app.ApplicationReference);

        await _audit.LogAsync(adminUserId, adminEmail, "ADMIN_PASS_REJECTED", "BusPassApplication", app.ApplicationReference,
            $"Rejected application {app.ApplicationReference}. Remarks: {app.AdminRemarks}", ipAddress);

        return OperationResult.Ok("Application rejected.", app.ApplicationReference);
    }

    public async Task<OperationResult> RevokePassAsync(int passId, int adminUserId, string adminEmail, string reason, string? ipAddress = null)
    {
        var pass = await _db.BusPasses.FirstOrDefaultAsync(p => p.Id == passId);
        if (pass is null) return OperationResult.Fail("Bus pass not found.");

        pass.Status = BusPassStatus.Revoked;
        await _db.SaveChangesAsync();

        await _notifications.SendAsync(
            pass.UserId,
            "Bus Pass Revoked",
            $"Your CloudPass {pass.PassNumber} has been revoked by administration. Reason: {reason}",
            NotificationCategory.Security,
            pass.PassNumber);

        await _audit.LogAsync(adminUserId, adminEmail, "ADMIN_PASS_REVOKED", "BusPass", pass.PassNumber,
            $"Revoked bus pass {pass.PassNumber}. Reason: {reason}", ipAddress);

        return OperationResult.Ok($"Pass {pass.PassNumber} revoked.", pass.PassNumber);
    }

    public async Task<int> ProcessExpirationRemindersAsync(CancellationToken cancellationToken = default)
    {
        var today = DateTime.UtcNow.Date;
        var expiredPasses = await _db.BusPasses
            .Where(p => p.Status == BusPassStatus.Active && p.ValidUntil < today)
            .ToListAsync(cancellationToken);

        foreach (var p in expiredPasses)
            p.Status = BusPassStatus.Expired;

        var expiringSoon = await _db.BusPasses
            .Include(p => p.Route)
            .Where(p => p.Status == BusPassStatus.Active && p.ValidUntil >= today && p.ValidUntil <= today.AddDays(5))
            .ToListAsync(cancellationToken);

        int sent = 0;
        foreach (var pass in expiringSoon)
        {
            var alreadyNotified = await _db.Notifications.AnyAsync(n =>
                n.UserId == pass.UserId &&
                n.RelatedReference == pass.PassNumber &&
                n.Title == "Bus Pass Expiring Soon" &&
                n.CreatedAt >= DateTime.UtcNow.AddDays(-3), cancellationToken);

            if (!alreadyNotified)
            {
                await _notifications.SendAsync(
                    pass.UserId,
                    "Bus Pass Expiring Soon",
                    $"Your CloudPass {pass.PassNumber} ({pass.Route.SourceCity} ↔ {pass.Route.DestinationCity}) expires on {pass.ValidUntil:dd MMM yyyy}. Renew now for uninterrupted travel.",
                    NotificationCategory.BusPass,
                    pass.PassNumber);
                sent++;
            }
        }

        if (expiredPasses.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);

        return sent;
    }
}
