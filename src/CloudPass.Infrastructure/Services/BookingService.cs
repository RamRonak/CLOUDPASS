using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using CloudPass.Application.DTOs;
using CloudPass.Application.Interfaces;
using CloudPass.Application.Options;
using CloudPass.Domain.Entities;
using CloudPass.Domain.Enums;
using CloudPass.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloudPass.Infrastructure.Services;

public class BookingService : IBookingService
{
    // SQLite file-level writer coordination (SQL Server / Azure SQL relies purely on Serializable ACID DB transactions)
    private static readonly SemaphoreSlim SqliteWriteGate = new(1, 1);

    private readonly CloudPassDbContext _db;
    private readonly IFareCalculationService _fareService;
    private readonly ITicketSecurityService _securityService;
    private readonly IQrCodeService _qrService;
    private readonly IPdfTicketService _pdfService;
    private readonly ITicketStorageService _storageService;
    private readonly IBackgroundTaskQueue _backgroundQueue;
    private readonly INotificationService _notifications;
    private readonly IAuditService _audit;
    private readonly SecurityOptions _securityOptions;
    private readonly PaymentGatewayOptions _paymentOptions;
    private readonly ILogger<BookingService> _logger;

    public BookingService(
        CloudPassDbContext db,
        IFareCalculationService fareService,
        ITicketSecurityService securityService,
        IQrCodeService qrService,
        IPdfTicketService pdfService,
        ITicketStorageService storageService,
        IBackgroundTaskQueue backgroundQueue,
        INotificationService notifications,
        IAuditService audit,
        IOptions<SecurityOptions> securityOptions,
        IOptions<PaymentGatewayOptions> paymentOptions,
        ILogger<BookingService> logger)
    {
        _db = db;
        _fareService = fareService;
        _securityService = securityService;
        _qrService = qrService;
        _pdfService = pdfService;
        _storageService = storageService;
        _backgroundQueue = backgroundQueue;
        _notifications = notifications;
        _audit = audit;
        _securityOptions = securityOptions.Value;
        _paymentOptions = paymentOptions.Value;
        _logger = logger;
    }

    public async Task<BookingCreationResult> CreatePendingBookingAsync(CreateBookingCommand command, CancellationToken cancellationToken = default)
    {
        var distinctSeatIds = command.SeatIds.Distinct().ToList();
        if (distinctSeatIds.Count == 0 || distinctSeatIds.Count > 6)
        {
            return new BookingCreationResult { Success = false, ErrorMessage = "Please select between 1 and 6 seats." };
        }

        if (command.Passengers.Count != distinctSeatIds.Count)
        {
            return new BookingCreationResult { Success = false, ErrorMessage = "Passenger details must match the exact number of selected seats." };
        }

        var schedule = await _db.BusSchedules
            .Include(s => s.Route)
            .Include(s => s.Bus)
            .FirstOrDefaultAsync(s => s.Id == command.ScheduleId, cancellationToken);

        if (schedule is null || schedule.Status != ScheduleStatus.Scheduled)
        {
            return new BookingCreationResult { Success = false, ErrorMessage = "Selected bus schedule is no longer available." };
        }

        // ZERO-TRUST SERVER-SIDE FARE CALCULATION
        var officialFare = _fareService.CalculateTicketFare(schedule, schedule.Route, distinctSeatIds.Count);

        if (command.ClientSubmittedTotal.HasValue &&
            Math.Abs(command.ClientSubmittedTotal.Value - officialFare.TotalAmount) > 0.01m)
        {
            await _audit.LogAsync(
                command.UserId,
                command.UserEmail,
                "PRICE_TAMPERING_BLOCKED",
                "BusSchedule",
                schedule.Id.ToString(),
                $"Rejected tampered checkout price. Client sent {command.ClientSubmittedTotal.Value:N2}, official server fare is {officialFare.TotalAmount:N2}.",
                command.IpAddress);

            return new BookingCreationResult
            {
                Success = false,
                PriceTamperingDetected = true,
                OfficialTotalAmount = officialFare.TotalAmount,
                ErrorMessage = $"Fare validation failed: submitted amount does not match official server fare (INR {officialFare.TotalAmount:N2})."
            };
        }

        var strategy = _db.Database.CreateExecutionStrategy();
        var useSqliteGate = !_db.Database.IsSqlServer();

        return await strategy.ExecuteAsync(async () =>
        {
            if (useSqliteGate) await SqliteWriteGate.WaitAsync(cancellationToken);
            try
            {
                var isolation = _db.Database.IsSqlServer() ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted;
                await using var tx = await _db.Database.BeginTransactionAsync(isolation, cancellationToken);
                try
                {
                    var requestedSeats = await _db.Seats
                        .Where(s => s.BusScheduleId == schedule.Id && distinctSeatIds.Contains(s.Id))
                        .AsNoTracking()
                        .ToListAsync(cancellationToken);

                    if (requestedSeats.Count != distinctSeatIds.Count || requestedSeats.Any(s => s.IsBooked))
                    {
                        await tx.RollbackAsync(cancellationToken);
                        return new BookingCreationResult
                        {
                            Success = false,
                            SeatConflict = true,
                            ErrorMessage = "One or more selected seats were just booked by another passenger. Please choose different seats."
                        };
                    }

                    var bookingRef = GenerateUniqueReference("CP");
                    var gatewayOrderId = $"order_CP{DateTime.UtcNow:yyyyMMddHHmmss}{RandomHex(4)}";

                    var booking = new Booking
                    {
                        BookingReference = bookingRef,
                        UserId = command.UserId,
                        BusScheduleId = schedule.Id,
                        BookingDate = DateTime.UtcNow,
                        TotalPassengers = distinctSeatIds.Count,
                        UnitOfficialFare = officialFare.UnitOfficialFare,
                        BaseFareTotal = officialFare.BaseFareTotal,
                        ServiceFeeAmount = officialFare.ServiceFeeAmount,
                        TaxAmount = officialFare.TaxAmount,
                        TotalAmount = officialFare.TotalAmount,
                        BookingStatus = BookingStatus.PendingPayment,
                        PaymentStatus = PaymentStatus.Pending
                    };

                    _db.Bookings.Add(booking);
                    await _db.SaveChangesAsync(cancellationToken);

                    // ATOMIC CONCURRENCY-SAFE SEAT CLAIM:
                    // Only claims seats where IsBooked == false; if any row wasn't updated, a race occurred -> rollback!
                    var newStamp = Guid.NewGuid();
                    var claimedRows = await _db.Seats
                        .Where(s => s.BusScheduleId == schedule.Id && distinctSeatIds.Contains(s.Id) && !s.IsBooked)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(s => s.IsBooked, true)
                            .SetProperty(s => s.BookingId, booking.Id)
                            .SetProperty(s => s.ConcurrencyStamp, newStamp), cancellationToken);

                    if (claimedRows != distinctSeatIds.Count)
                    {
                        await tx.RollbackAsync(cancellationToken);
                        return new BookingCreationResult
                        {
                            Success = false,
                            SeatConflict = true,
                            ErrorMessage = "Seat conflict detected during concurrent booking. One or more seats are already reserved."
                        };
                    }

                    var scheduleStamp = Guid.NewGuid();
                    await _db.BusSchedules
                        .Where(s => s.Id == schedule.Id)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(s => s.AvailableSeats, s => s.AvailableSeats - distinctSeatIds.Count)
                            .SetProperty(s => s.ConcurrencyStamp, scheduleStamp), cancellationToken);

                    var seatLookup = requestedSeats.ToDictionary(s => s.Id);
                    foreach (var p in command.Passengers)
                    {
                        var seat = seatLookup.TryGetValue(p.SeatId, out var matched) ? matched : requestedSeats.First();
                        _db.BookingPassengers.Add(new BookingPassenger
                        {
                            BookingId = booking.Id,
                            SeatId = seat.Id,
                            SeatNumber = seat.SeatNumber,
                            PassengerName = p.PassengerName.Trim(),
                            Age = Math.Clamp(p.Age, 1, 120),
                            Gender = string.IsNullOrWhiteSpace(p.Gender) ? "Other" : p.Gender.Trim(),
                            IdProofType = p.IdProofType?.Trim(),
                            IdProofNumber = p.IdProofNumber?.Trim()
                        });
                    }

                    _db.Payments.Add(new Payment
                    {
                        BookingId = booking.Id,
                        GatewayName = _paymentOptions.Provider,
                        GatewayOrderId = gatewayOrderId,
                        Amount = officialFare.TotalAmount,
                        Currency = "INR",
                        Status = PaymentStatus.Pending,
                        VerifiedServerSide = false,
                        WebhookVerified = false
                    });

                    await _db.SaveChangesAsync(cancellationToken);
                    await tx.CommitAsync(cancellationToken);

                    await _audit.LogAsync(
                        command.UserId,
                        command.UserEmail,
                        "BOOKING_INITIATED",
                        "Booking",
                        bookingRef,
                        $"Reserved {distinctSeatIds.Count} seat(s) on Schedule #{schedule.Id}. Official server total: INR {officialFare.TotalAmount:N2}. Awaiting verified payment.",
                        command.IpAddress);

                    return new BookingCreationResult
                    {
                        Success = true,
                        BookingId = booking.Id,
                        BookingReference = bookingRef,
                        GatewayOrderId = gatewayOrderId,
                        OfficialTotalAmount = officialFare.TotalAmount
                    };
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    await tx.RollbackAsync(cancellationToken);
                    _logger.LogWarning(ex, "Concurrency conflict detected while reserving seats on schedule {ScheduleId}.", schedule.Id);
                    return new BookingCreationResult
                    {
                        Success = false,
                        SeatConflict = true,
                        ErrorMessage = "Concurrent seat reservation conflict. Please refresh and select available seats."
                    };
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync(cancellationToken);
                    _logger.LogError(ex, "Error creating booking for schedule {ScheduleId}.", schedule.Id);
                    throw;
                }
            }
            finally
            {
                if (useSqliteGate) SqliteWriteGate.Release();
            }
        });
    }

    public async Task<OperationResult> ConfirmPaymentAsync(PaymentConfirmationCommand command, string baseUrl, CancellationToken cancellationToken = default)
    {
        var booking = await _db.Bookings
            .Include(b => b.Payments)
            .Include(b => b.Passengers)
            .Include(b => b.BusSchedule).ThenInclude(s => s.Bus)
            .Include(b => b.BusSchedule).ThenInclude(s => s.Route)
            .FirstOrDefaultAsync(b => b.BookingReference == command.BookingReference, cancellationToken);

        if (booking is null)
            return OperationResult.Fail("Booking reference not found.");

        if (booking.UserId != command.UserId)
        {
            await _audit.LogAsync(command.UserId, command.UserEmail, "UNAUTHORIZED_PAYMENT_CONFIRM", "Booking", booking.BookingReference,
                "Blocked attempt to confirm payment for another user's booking.", command.IpAddress);
            return OperationResult.Fail("Access denied. You cannot modify another user's booking.");
        }

        if (booking.BookingStatus == BookingStatus.Confirmed && booking.PaymentStatus == PaymentStatus.Paid)
            return OperationResult.Ok("Booking is already confirmed.", booking.BookingReference);

        if (booking.BookingStatus == BookingStatus.Cancelled)
            return OperationResult.Fail("Cannot confirm payment for a cancelled booking.");

        var payment = booking.Payments.FirstOrDefault(p => p.GatewayOrderId == command.GatewayOrderId);
        if (payment is null)
            return OperationResult.Fail("Payment order ID does not match this booking.");

        // Server-side cryptographic payment verification
        if (!_securityService.VerifyPaymentSignature(command.GatewayOrderId, command.GatewayPaymentId, command.GatewaySignature))
        {
            payment.Status = PaymentStatus.Failed;
            payment.VerificationNote = "Rejected: Invalid HMAC-SHA256 gateway payment signature.";
            await _db.SaveChangesAsync(cancellationToken);

            await _audit.LogAsync(command.UserId, command.UserEmail, "INVALID_PAYMENT_SIGNATURE", "Payment", payment.GatewayOrderId,
                "Blocked payment confirmation with invalid cryptographic signature.", command.IpAddress);

            return OperationResult.Fail("Payment signature verification failed. Booking cannot be confirmed.");
        }

        return await FinalizeVerifiedBookingAsync(booking, payment, command.GatewayPaymentId, command.GatewaySignature, webhookVerified: false, baseUrl, command.UserEmail, command.IpAddress, cancellationToken);
    }

    public async Task<OperationResult> ProcessSignedWebhookAsync(string rawPayload, string signatureHeader, string baseUrl, CancellationToken cancellationToken = default)
    {
        if (!_securityService.VerifyWebhookSignature(rawPayload, signatureHeader))
        {
            await _audit.LogAsync(null, "webhook@gateway", "INVALID_WEBHOOK_SIGNATURE", "PaymentWebhook", "unknown",
                "Rejected payment webhook request due to invalid X-Razorpay-Signature HMAC header.");
            return OperationResult.Fail("Invalid webhook signature.");
        }

        try
        {
            using var doc = JsonDocument.Parse(rawPayload);
            var root = doc.RootElement;
            var eventType = root.TryGetProperty("event", out var ev) ? ev.GetString() : "payment.captured";
            var orderId = root.TryGetProperty("order_id", out var oid) ? oid.GetString() : null;
            var paymentId = root.TryGetProperty("payment_id", out var pid) ? pid.GetString() : null;

            if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(paymentId))
                return OperationResult.Fail("Webhook payload missing order_id or payment_id.");

            var payment = await _db.Payments
                .Include(p => p.Booking).ThenInclude(b => b.Passengers)
                .Include(p => p.Booking).ThenInclude(b => b.BusSchedule).ThenInclude(s => s.Bus)
                .Include(p => p.Booking).ThenInclude(b => b.BusSchedule).ThenInclude(s => s.Route)
                .FirstOrDefaultAsync(p => p.GatewayOrderId == orderId, cancellationToken);

            if (payment is null)
                return OperationResult.Fail("Payment order not found for webhook.");

            if (eventType == "payment.failed")
            {
                payment.Status = PaymentStatus.Failed;
                payment.VerificationNote = "Marked failed via signed gateway webhook.";
                await _db.SaveChangesAsync(cancellationToken);
                return OperationResult.Ok("Webhook processed: payment marked failed.", payment.Booking.BookingReference);
            }

            var generatedSig = _securityService.GeneratePaymentSignature(orderId, paymentId);
            return await FinalizeVerifiedBookingAsync(
                payment.Booking,
                payment,
                paymentId,
                generatedSig,
                webhookVerified: true,
                baseUrl,
                "webhook@razorpay-test",
                null,
                cancellationToken);
        }
        catch (JsonException)
        {
            return OperationResult.Fail("Malformed webhook JSON payload.");
        }
    }

    private async Task<OperationResult> FinalizeVerifiedBookingAsync(
        Booking booking,
        Payment payment,
        string gatewayPaymentId,
        string gatewaySignature,
        bool webhookVerified,
        string baseUrl,
        string actorEmail,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        if (booking.BookingStatus == BookingStatus.Confirmed && booking.PaymentStatus == PaymentStatus.Paid)
        {
            if (webhookVerified && !payment.WebhookVerified)
            {
                payment.WebhookVerified = true;
                await _db.SaveChangesAsync(cancellationToken);
            }
            return OperationResult.Ok("Booking already confirmed.", booking.BookingReference);
        }

        // Generate cryptographically signed, non-PII ticket verification token
        var tokenInfo = _securityService.GenerateSignedTicketToken(booking.BookingReference);
        var blobName = $"tickets/{booking.BookingReference}_{tokenInfo.Nonce[..8]}.pdf";

        booking.BookingStatus = BookingStatus.Confirmed;
        booking.PaymentStatus = PaymentStatus.Paid;
        booking.TicketTokenNonce = tokenInfo.Nonce;
        booking.TicketTokenSignature = tokenInfo.Signature;
        booking.TicketTokenHash = tokenInfo.TokenHash;
        booking.TicketPdfBlobName = blobName;

        payment.GatewayPaymentId = gatewayPaymentId;
        payment.GatewaySignature = gatewaySignature;
        payment.Status = PaymentStatus.Paid;
        payment.VerifiedServerSide = true;
        payment.WebhookVerified = webhookVerified || payment.WebhookVerified;
        payment.PaidAt = DateTime.UtcNow;
        payment.VerificationNote = webhookVerified
            ? "Verified via signed HMAC-SHA256 webhook."
            : "Verified via server-side HMAC-SHA256 payment signature.";

        await _db.SaveChangesAsync(cancellationToken);

        // Queue asynchronous PDF generation & cloud storage upload so checkout returns fast under peak load
        var bookingId = booking.Id;
        var verifyUrl = $"{baseUrl.TrimEnd('/')}/Ticket/Verify?token={Uri.EscapeDataString(tokenInfo.FullToken)}";
        await _backgroundQueue.QueueWorkItemAsync(async (sp, ct) =>
        {
            var scopedDb = sp.GetRequiredService<CloudPassDbContext>();
            var qr = sp.GetRequiredService<IQrCodeService>();
            var pdf = sp.GetRequiredService<IPdfTicketService>();
            var storage = sp.GetRequiredService<ITicketStorageService>();

            var fullBooking = await scopedDb.Bookings
                .Include(b => b.Passengers)
                .Include(b => b.BusSchedule).ThenInclude(s => s.Bus)
                .Include(b => b.BusSchedule).ThenInclude(s => s.Route)
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

            if (fullBooking is null) return;
            var qrBytes = qr.GenerateQrPngBytes(verifyUrl);
            var pdfBytes = pdf.GenerateTicketPdf(fullBooking, verifyUrl, qrBytes);
            await storage.UploadAsync(blobName, pdfBytes, "application/pdf", ct);
        });

        // In-app Notifications (Booking + Payment + Digital Ticket)
        await _notifications.SendAsync(
            booking.UserId,
            "Payment Confirmed",
            $"Payment of INR {booking.TotalAmount:N2} ({gatewayPaymentId}) verified for booking {booking.BookingReference}.",
            NotificationCategory.Payment,
            booking.BookingReference);

        await _notifications.SendAsync(
            booking.UserId,
            "Digital Ticket Ready",
            $"Your secured CloudPass e-ticket {booking.BookingReference} ({booking.BusSchedule.Route.SourceCity} → {booking.BusSchedule.Route.DestinationCity}) is generated with QR verification.",
            NotificationCategory.Ticket,
            booking.BookingReference);

        await _audit.LogAsync(
            booking.UserId,
            actorEmail,
            "BOOKING_CONFIRMED",
            "Booking",
            booking.BookingReference,
            $"Payment {gatewayPaymentId} verified (Webhook={webhookVerified}). Cryptographic ticket token issued.",
            ipAddress);

        return OperationResult.Ok("Payment verified and digital ticket issued.", booking.BookingReference);
    }

    public async Task<OperationResult> CancelBookingAsync(
        string bookingReference,
        int requestingUserId,
        bool isAdmin,
        string userEmail,
        string reason,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var booking = await _db.Bookings
            .Include(b => b.Payments)
            .Include(b => b.Seats)
            .FirstOrDefaultAsync(b => b.BookingReference == bookingReference, cancellationToken);

        if (booking is null)
            return OperationResult.Fail("Booking not found.");

        if (!isAdmin && booking.UserId != requestingUserId)
        {
            await _audit.LogAsync(requestingUserId, userEmail, "UNAUTHORIZED_CANCEL_ATTEMPT", "Booking", bookingReference,
                "Blocked unauthorized attempt to cancel another user's booking.", ipAddress);
            return OperationResult.Fail("Access denied. You do not own this booking.");
        }

        if (booking.BookingStatus == BookingStatus.Cancelled)
            return OperationResult.Fail("Booking is already cancelled.");

        if (booking.IsUsed)
            return OperationResult.Fail("Used/boarded tickets cannot be cancelled.");

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var releaseSeatStamp = Guid.NewGuid();
                var releasedSeats = await _db.Seats
                    .Where(s => s.BookingId == booking.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(s => s.IsBooked, false)
                        .SetProperty(s => s.BookingId, (int?)null)
                        .SetProperty(s => s.ConcurrencyStamp, releaseSeatStamp), cancellationToken);

                if (releasedSeats > 0)
                {
                    var releaseSchStamp = Guid.NewGuid();
                    await _db.BusSchedules
                        .Where(s => s.Id == booking.BusScheduleId)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(s => s.AvailableSeats, s => s.AvailableSeats + releasedSeats)
                            .SetProperty(s => s.ConcurrencyStamp, releaseSchStamp), cancellationToken);
                }

                booking.BookingStatus = BookingStatus.Cancelled;
                booking.CancelledAt = DateTime.UtcNow;
                booking.CancellationReason = string.IsNullOrWhiteSpace(reason) ? "Cancelled by user" : reason.Trim();

                if (booking.PaymentStatus == PaymentStatus.Paid)
                {
                    booking.PaymentStatus = PaymentStatus.Refunded;
                    foreach (var p in booking.Payments.Where(x => x.Status == PaymentStatus.Paid))
                        p.Status = PaymentStatus.Refunded;
                }

                await _db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);

                await _notifications.SendAsync(
                    booking.UserId,
                    "Booking Cancelled",
                    $"Booking {booking.BookingReference} has been cancelled. {releasedSeats} seat(s) released back to cloud inventory.",
                    NotificationCategory.Cancellation,
                    booking.BookingReference);

                await _audit.LogAsync(
                    requestingUserId,
                    userEmail,
                    isAdmin ? "ADMIN_BOOKING_CANCELLED" : "USER_BOOKING_CANCELLED",
                    "Booking",
                    booking.BookingReference,
                    $"Cancelled booking and released {releasedSeats} seat(s). Reason: {booking.CancellationReason}",
                    ipAddress);

                return OperationResult.Ok("Booking cancelled and seats released successfully.", booking.BookingReference);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    public async Task<TicketVerificationResultDto> VerifyTokenAsync(
        string rawToken,
        bool markAsUsed,
        int? verifierUserId,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var snippet = string.IsNullOrWhiteSpace(rawToken)
            ? "EMPTY"
            : (rawToken.Length > 36 ? rawToken[..36] + "..." : rawToken);

        if (!_securityService.ValidateTokenStructureAndSignature(rawToken, out var tokenType, out var reference, out var nonce, out var signature))
        {
            await RecordVerificationAsync(null, null, VerificationType.Ticket, snippet, verifierUserId, false, "INVALID_SIGNATURE",
                "Cryptographic signature validation failed. Ticket may be forged or tampered.", ipAddress, cancellationToken);

            return new TicketVerificationResultDto
            {
                IsValid = false,
                ResultCode = "INVALID_SIGNATURE",
                Message = "INVALID / FORGED TOKEN: Cryptographic HMAC-SHA256 verification failed."
            };
        }

        var canonicalToken = $"CP1.{tokenType}.{reference}.{nonce}.{signature}";
        var tokenHash = _securityService.ComputeSha256Hex(canonicalToken);

        if (tokenType == "TKT")
        {
            var booking = await _db.Bookings
                .Include(b => b.User)
                .Include(b => b.Passengers)
                .Include(b => b.BusSchedule).ThenInclude(s => s.Bus)
                .Include(b => b.BusSchedule).ThenInclude(s => s.Route)
                .FirstOrDefaultAsync(b => b.BookingReference == reference, cancellationToken);

            if (booking is null || booking.TicketTokenHash != tokenHash)
            {
                await RecordVerificationAsync(booking?.Id, null, VerificationType.Ticket, snippet, verifierUserId, false, "TOKEN_MISMATCH",
                    "Ticket reference does not match the signed database record.", ipAddress, cancellationToken);

                return new TicketVerificationResultDto
                {
                    IsValid = false,
                    VerificationType = VerificationType.Ticket,
                    ResultCode = "TOKEN_MISMATCH",
                    Message = "Ticket not found or token does not match database hash."
                };
            }

            var seatsLabel = string.Join(", ", booking.Passengers.Select(p => p.SeatNumber));
            var holderName = booking.Passengers.FirstOrDefault()?.PassengerName ?? booking.User.FullName;

            if (booking.BookingStatus == BookingStatus.Cancelled)
            {
                await RecordVerificationAsync(booking.Id, null, VerificationType.Ticket, snippet, verifierUserId, false, "BOOKING_CANCELLED",
                    "Ticket rejected: Booking has been cancelled.", ipAddress, cancellationToken);

                return BuildTicketResult(false, "BOOKING_CANCELLED", "INVALID: This booking was cancelled.", booking, holderName, seatsLabel);
            }

            if (booking.PaymentStatus != PaymentStatus.Paid || booking.BookingStatus != BookingStatus.Confirmed)
            {
                await RecordVerificationAsync(booking.Id, null, VerificationType.Ticket, snippet, verifierUserId, false, "UNPAID_BOOKING",
                    "Ticket rejected: Payment is not verified/completed.", ipAddress, cancellationToken);

                return BuildTicketResult(false, "UNPAID_BOOKING", "INVALID: Payment has not been verified for this booking.", booking, holderName, seatsLabel);
            }

            if (booking.BusSchedule.JourneyDate.Date < DateTime.UtcNow.Date.AddDays(-1))
            {
                await RecordVerificationAsync(booking.Id, null, VerificationType.Ticket, snippet, verifierUserId, false, "TICKET_EXPIRED",
                    "Ticket rejected: Journey date has already passed.", ipAddress, cancellationToken);

                return BuildTicketResult(false, "TICKET_EXPIRED", "EXPIRED: Journey date has passed.", booking, holderName, seatsLabel);
            }

            if (_securityOptions.EnforceSingleUseTicketValidation && booking.IsUsed)
            {
                await RecordVerificationAsync(booking.Id, null, VerificationType.Ticket, snippet, verifierUserId, false, "ALREADY_USED",
                    $"Duplicate scan blocked: Ticket already boarded at {booking.UsedAt:yyyy-MM-dd HH:mm} UTC.", ipAddress, cancellationToken);

                return BuildTicketResult(false, "ALREADY_USED", $"DUPLICATE / REUSED TICKET BLOCKED: Already checked in at {booking.UsedAt:dd MMM yyyy HH:mm} UTC.", booking, holderName, seatsLabel);
            }

            if (markAsUsed)
            {
                booking.IsUsed = true;
                booking.UsedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
            }

            var okMsg = markAsUsed
                ? "AUTHENTIC & CHECKED IN: Ticket verified and marked as boarded (single-use locked)."
                : "AUTHENTIC & VALID: Cryptographic signature, payment, and booking status verified.";

            await RecordVerificationAsync(booking.Id, null, VerificationType.Ticket, snippet, verifierUserId, true, markAsUsed ? "VERIFIED_BOARDED" : "VALID",
                okMsg, ipAddress, cancellationToken);

            return BuildTicketResult(true, markAsUsed ? "VERIFIED_BOARDED" : "VALID", okMsg, booking, holderName, seatsLabel);
        }
        else // Bus Pass Verification ("PAS")
        {
            var pass = await _db.BusPasses
                .Include(p => p.User)
                .Include(p => p.Route)
                .FirstOrDefaultAsync(p => p.PassNumber == reference, cancellationToken);

            if (pass is null || pass.VerificationTokenHash != tokenHash)
            {
                await RecordVerificationAsync(null, pass?.Id, VerificationType.BusPass, snippet, verifierUserId, false, "PASS_MISMATCH",
                    "Bus pass token does not match database record.", ipAddress, cancellationToken);

                return new TicketVerificationResultDto
                {
                    IsValid = false,
                    VerificationType = VerificationType.BusPass,
                    ResultCode = "PASS_MISMATCH",
                    Message = "Bus Pass not found or cryptographic hash mismatch."
                };
            }

            var now = DateTime.UtcNow.Date;
            var isValid = pass.Status == BusPassStatus.Active && now >= pass.ValidFrom.Date && now <= pass.ValidUntil.Date;
            var code = isValid ? "VALID_PASS" : (pass.Status == BusPassStatus.Revoked ? "PASS_REVOKED" : "PASS_EXPIRED");
            var msg = isValid
                ? $"AUTHENTIC BUS PASS: Active commuter pass valid through {pass.ValidUntil:dd MMM yyyy}."
                : $"INVALID BUS PASS: Status is {pass.Status} (Valid {pass.ValidFrom:dd MMM} – {pass.ValidUntil:dd MMM yyyy}).";

            await RecordVerificationAsync(null, pass.Id, VerificationType.BusPass, snippet, verifierUserId, isValid, code, msg, ipAddress, cancellationToken);

            return new TicketVerificationResultDto
            {
                IsValid = isValid,
                VerificationType = VerificationType.BusPass,
                ResultCode = code,
                Message = msg,
                Reference = pass.PassNumber,
                HolderName = pass.User.FullName,
                RouteLabel = $"{pass.Route.SourceCity} ↔ {pass.Route.DestinationCity}",
                BusLabel = "All CloudPass Corridor Buses",
                ValidUntil = pass.ValidUntil,
                BookingStatus = pass.Status.ToString(),
                PaymentStatus = "Paid"
            };
        }
    }

    private static TicketVerificationResultDto BuildTicketResult(bool isValid, string code, string msg, Booking booking, string holderName, string seatsLabel) =>
        new()
        {
            IsValid = isValid,
            VerificationType = VerificationType.Ticket,
            ResultCode = code,
            Message = msg,
            Reference = booking.BookingReference,
            HolderName = holderName,
            RouteLabel = $"{booking.BusSchedule.Route.SourceCity} → {booking.BusSchedule.Route.DestinationCity}",
            BusLabel = $"{booking.BusSchedule.Bus.BusName} ({booking.BusSchedule.Bus.BusNumber})",
            JourneyDate = booking.BusSchedule.DepartureTime,
            SeatsLabel = seatsLabel,
            BookingStatus = booking.BookingStatus.ToString(),
            PaymentStatus = booking.PaymentStatus.ToString(),
            AlreadyUsed = booking.IsUsed,
            UsedAt = booking.UsedAt
        };

    private async Task RecordVerificationAsync(
        int? bookingId,
        int? passId,
        VerificationType type,
        string snippet,
        int? verifierUserId,
        bool isValid,
        string code,
        string message,
        string? ipAddress,
        CancellationToken ct)
    {
        _db.TicketVerifications.Add(new TicketVerification
        {
            BookingId = bookingId,
            BusPassId = passId,
            VerificationType = type,
            TokenSnippet = snippet,
            VerifiedByUserId = verifierUserId,
            IsValid = isValid,
            ResultCode = code,
            ResultMessage = message,
            IpAddress = ipAddress,
            VerifiedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
    }

    private static string GenerateUniqueReference(string prefix)
        => $"{prefix}-{DateTime.UtcNow:yyyyMMdd}-{RandomHex(3)}";

    private static string RandomHex(int bytes)
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes));
}
