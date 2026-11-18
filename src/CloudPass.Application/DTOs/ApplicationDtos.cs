using CloudPass.Domain.Enums;

namespace CloudPass.Application.DTOs;

public record FareBreakdownDto(
    int ScheduleId,
    int SeatCount,
    decimal UnitOfficialFare,
    decimal BaseFareTotal,
    decimal ServiceFeeAmount,
    decimal TaxPercentage,
    decimal TaxAmount,
    decimal TotalAmount);

public record PassPricingDto(
    int RouteId,
    string SourceCity,
    string DestinationCity,
    int DurationMonths,
    decimal MonthlyBasePrice,
    decimal DiscountPercentage,
    decimal DiscountAmount,
    decimal FinalPrice);

public class BusSearchFilterDto
{
    public string? Source { get; set; }
    public string? Destination { get; set; }
    public DateTime? JourneyDate { get; set; }
    public int Passengers { get; set; } = 1;
    public bool? HasAC { get; set; }
    public bool? IsSleeper { get; set; }
    public string? DepartureWindow { get; set; } // Morning, Afternoon, Evening, Night
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public string? SortBy { get; set; } // price_asc, price_desc, departure_asc, seats_desc
}

public class PassengerInputDto
{
    public int SeatId { get; set; }
    public string SeatNumber { get; set; } = string.Empty;
    public string PassengerName { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Gender { get; set; } = "Male";
    public string? IdProofType { get; set; }
    public string? IdProofNumber { get; set; }
}

public class CreateBookingCommand
{
    public int UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public int ScheduleId { get; set; }
    public List<int> SeatIds { get; set; } = new();
    public List<PassengerInputDto> Passengers { get; set; } = new();
    public decimal? ClientSubmittedTotal { get; set; } // ONLY used to detect tampering! Never trusted.
    public string? IpAddress { get; set; }
}

public class BookingCreationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public bool PriceTamperingDetected { get; set; }
    public bool SeatConflict { get; set; }
    public int? BookingId { get; set; }
    public string? BookingReference { get; set; }
    public string? GatewayOrderId { get; set; }
    public decimal OfficialTotalAmount { get; set; }
}

public class PaymentConfirmationCommand
{
    public int UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string BookingReference { get; set; } = string.Empty;
    public string GatewayOrderId { get; set; } = string.Empty;
    public string GatewayPaymentId { get; set; } = string.Empty;
    public string GatewaySignature { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
}

public class OperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Reference { get; set; }

    public static OperationResult Ok(string message, string? reference = null) =>
        new() { Success = true, Message = message, Reference = reference };

    public static OperationResult Fail(string message) =>
        new() { Success = false, Message = message };
}

public class TicketVerificationResultDto
{
    public bool IsValid { get; set; }
    public string ResultCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public VerificationType VerificationType { get; set; }
    public string? Reference { get; set; }
    public string? HolderName { get; set; }
    public string? RouteLabel { get; set; }
    public string? BusLabel { get; set; }
    public DateTime? JourneyDate { get; set; }
    public DateTime? ValidUntil { get; set; }
    public string? SeatsLabel { get; set; }
    public string? BookingStatus { get; set; }
    public string? PaymentStatus { get; set; }
    public bool AlreadyUsed { get; set; }
    public DateTime? UsedAt { get; set; }
}

public class AdminDashboardStatsDto
{
    public int TotalUsers { get; set; }
    public int TotalBuses { get; set; }
    public int TotalRoutes { get; set; }
    public int TotalSchedules { get; set; }
    public int TotalBookings { get; set; }
    public int ConfirmedBookings { get; set; }
    public int CancelledBookings { get; set; }
    public decimal TotalRevenue { get; set; }
    public int ActiveBusPasses { get; set; }
    public int PendingPassApplications { get; set; }
    public int TotalVerifications { get; set; }
    public List<DailyRevenuePoint> RevenueTrend { get; set; } = new();
}

public record DailyRevenuePoint(string DateLabel, decimal Revenue, int BookingsCount);
