using CloudPass.Domain.Enums;

namespace CloudPass.Domain.Entities;

public class BusPassApplication
{
    public int Id { get; set; }
    public string ApplicationReference { get; set; } = string.Empty;
    public int UserId { get; set; }
    public int RouteId { get; set; }
    public int DurationMonths { get; set; } = 1;
    public DateTime RequestedStartDate { get; set; }
    public decimal CalculatedPrice { get; set; }
    public string IdProofType { get; set; } = "Student / Commuter ID";
    public string IdProofNumber { get; set; } = string.Empty;
    public PassApplicationStatus Status { get; set; } = PassApplicationStatus.Pending;
    public string? AdminRemarks { get; set; }
    public int? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public int? RenewedFromPassId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Route Route { get; set; } = null!;
    public BusPass? IssuedPass { get; set; }
}

public class BusPass
{
    public int Id { get; set; }
    public string PassNumber { get; set; } = string.Empty;
    public int ApplicationId { get; set; }
    public int UserId { get; set; }
    public int RouteId { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime ValidUntil { get; set; }
    public decimal AmountPaid { get; set; }
    public BusPassStatus Status { get; set; } = BusPassStatus.Active;
    public string VerificationTokenNonce { get; set; } = string.Empty;
    public string VerificationTokenSignature { get; set; } = string.Empty;
    public string VerificationTokenHash { get; set; } = string.Empty;
    public string? PassPdfBlobName { get; set; }
    public int? RenewedFromPassId { get; set; }
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    public BusPassApplication Application { get; set; } = null!;
    public User User { get; set; } = null!;
    public Route Route { get; set; } = null!;
    public ICollection<TicketVerification> TicketVerifications { get; set; } = new List<TicketVerification>();
}

public class TicketVerification
{
    public int Id { get; set; }
    public int? BookingId { get; set; }
    public int? BusPassId { get; set; }
    public VerificationType VerificationType { get; set; }
    public string TokenSnippet { get; set; } = string.Empty;
    public int? VerifiedByUserId { get; set; }
    public bool IsValid { get; set; }
    public string ResultCode { get; set; } = string.Empty;
    public string ResultMessage { get; set; } = string.Empty;
    public DateTime VerifiedAt { get; set; } = DateTime.UtcNow;
    public string? IpAddress { get; set; }

    public Booking? Booking { get; set; }
    public BusPass? BusPass { get; set; }
}

public class Notification
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public NotificationCategory Category { get; set; } = NotificationCategory.System;
    public string? RelatedReference { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}

public class AuditLog
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string UserEmail { get; set; } = "system@cloudpass.io";
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
}
