using CloudPass.Application.DTOs;
using CloudPass.Domain.Entities;
using CloudPass.Domain.Enums;

namespace CloudPass.Application.Interfaces;

public interface IFareCalculationService
{
    FareBreakdownDto CalculateTicketFare(BusSchedule schedule, Route route, int seatCount);
    PassPricingDto CalculateBusPassPrice(Route route, int durationMonths);
}

public interface ITicketSecurityService
{
    (string FullToken, string Nonce, string Signature, string TokenHash) GenerateSignedTicketToken(string bookingReference);
    (string FullToken, string Nonce, string Signature, string TokenHash) GenerateSignedPassToken(string passNumber);
    bool ValidateTokenStructureAndSignature(string token, out string tokenType, out string reference, out string nonce, out string signature);
    string ComputeSha256Hex(string input);
    string GeneratePaymentSignature(string gatewayOrderId, string gatewayPaymentId);
    bool VerifyPaymentSignature(string gatewayOrderId, string gatewayPaymentId, string providedSignature);
    string GenerateWebhookSignature(string rawPayload);
    bool VerifyWebhookSignature(string rawPayload, string providedSignature);
}

public interface IQrCodeService
{
    byte[] GenerateQrPngBytes(string payload, int pixelsPerModule = 8);
    string GenerateQrDataUri(string payload, int pixelsPerModule = 8);
}

public interface IPdfTicketService
{
    byte[] GenerateTicketPdf(Booking booking, string verificationUrl, byte[] qrPngBytes);
    byte[] GenerateBusPassPdf(BusPass pass, string verificationUrl, byte[] qrPngBytes);
}

public interface ITicketStorageService
{
    string ProviderName { get; }
    Task<string> UploadAsync(string blobName, byte[] content, string contentType, CancellationToken cancellationToken = default);
    Task<byte[]?> DownloadAsync(string blobName, CancellationToken cancellationToken = default);
    Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default);
}

public interface IBackgroundTaskQueue
{
    ValueTask QueueWorkItemAsync(Func<IServiceProvider, CancellationToken, Task> workItem);
    ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken);
    int QueuedCount { get; }
}

public interface IAuditService
{
    Task LogAsync(int? userId, string userEmail, string action, string entityType, string entityId, string details, string? ipAddress = null);
}

public interface INotificationService
{
    Task SendAsync(int userId, string title, string message, NotificationCategory category, string? relatedReference = null);
    Task<int> GetUnreadCountAsync(int userId);
    Task<List<Notification>> GetUserNotificationsAsync(int userId, int take = 20);
    Task MarkAsReadAsync(int userId, int notificationId);
    Task MarkAllAsReadAsync(int userId);
}

public interface IBookingService
{
    Task<BookingCreationResult> CreatePendingBookingAsync(CreateBookingCommand command, CancellationToken cancellationToken = default);
    Task<OperationResult> ConfirmPaymentAsync(PaymentConfirmationCommand command, string baseUrl, CancellationToken cancellationToken = default);
    Task<OperationResult> ProcessSignedWebhookAsync(string rawPayload, string signatureHeader, string baseUrl, CancellationToken cancellationToken = default);
    Task<OperationResult> CancelBookingAsync(string bookingReference, int requestingUserId, bool isAdmin, string userEmail, string reason, string? ipAddress = null, CancellationToken cancellationToken = default);
    Task<TicketVerificationResultDto> VerifyTokenAsync(string rawToken, bool markAsUsed, int? verifierUserId, string? ipAddress = null, CancellationToken cancellationToken = default);
}

public interface IBusPassService
{
    Task<OperationResult> SubmitApplicationAsync(int userId, string userEmail, int routeId, int durationMonths, DateTime startDate, string idProofType, string idProofNumber, int? renewedFromPassId = null, string? ipAddress = null);
    Task<OperationResult> ApproveApplicationAsync(int applicationId, int adminUserId, string adminEmail, string? remarks, string baseUrl, string? ipAddress = null);
    Task<OperationResult> RejectApplicationAsync(int applicationId, int adminUserId, string adminEmail, string remarks, string? ipAddress = null);
    Task<OperationResult> RevokePassAsync(int passId, int adminUserId, string adminEmail, string reason, string? ipAddress = null);
    Task<int> ProcessExpirationRemindersAsync(CancellationToken cancellationToken = default);
}
