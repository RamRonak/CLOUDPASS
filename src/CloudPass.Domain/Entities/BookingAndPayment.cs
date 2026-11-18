using CloudPass.Domain.Enums;

namespace CloudPass.Domain.Entities;

public class Booking
{
    public int Id { get; set; }
    public string BookingReference { get; set; } = string.Empty;
    public int UserId { get; set; }
    public int BusScheduleId { get; set; }
    public DateTime BookingDate { get; set; } = DateTime.UtcNow;
    public int TotalPassengers { get; set; }
    public decimal UnitOfficialFare { get; set; }
    public decimal BaseFareTotal { get; set; }
    public decimal ServiceFeeAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public BookingStatus BookingStatus { get; set; } = BookingStatus.PendingPayment;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public string? TicketTokenNonce { get; set; }
    public string? TicketTokenSignature { get; set; }
    public string? TicketTokenHash { get; set; }
    public string? TicketPdfBlobName { get; set; }
    public bool IsUsed { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public User User { get; set; } = null!;
    public BusSchedule BusSchedule { get; set; } = null!;
    public ICollection<BookingPassenger> Passengers { get; set; } = new List<BookingPassenger>();
    public ICollection<Seat> Seats { get; set; } = new List<Seat>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<TicketVerification> TicketVerifications { get; set; } = new List<TicketVerification>();
}

public class BookingPassenger
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public int SeatId { get; set; }
    public string SeatNumber { get; set; } = string.Empty;
    public string PassengerName { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Gender { get; set; } = "Other";
    public string? IdProofType { get; set; }
    public string? IdProofNumber { get; set; }

    public Booking Booking { get; set; } = null!;
}

public class Payment
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public string GatewayName { get; set; } = "Razorpay-TestMode";
    public string GatewayOrderId { get; set; } = string.Empty;
    public string? GatewayPaymentId { get; set; }
    public string? GatewaySignature { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "INR";
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public bool VerifiedServerSide { get; set; }
    public bool WebhookVerified { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? VerificationNote { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Booking Booking { get; set; } = null!;
}
