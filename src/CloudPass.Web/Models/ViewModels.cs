using System.ComponentModel.DataAnnotations;
using CloudPass.Application.DTOs;
using CloudPass.Domain.Entities;

namespace CloudPass.Web.Models;

public class HomeViewModel
{
    public List<CloudPass.Domain.Entities.Route> PopularRoutes { get; set; } = new();
    public List<string> AvailableCities { get; set; } = new();
    public int TotalBuses { get; set; }
    public int TotalRoutes { get; set; }
    public int TotalBookings { get; set; }
    public string StorageProvider { get; set; } = "Local";
}

public class RegisterViewModel
{
    [Required, StringLength(120, MinimumLength = 2)]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(180)]
    public string Email { get; set; } = string.Empty;

    [Required, Phone, StringLength(25, MinimumLength = 8)]
    [Display(Name = "Phone Number")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 8)]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[\W_]).{8,}$",
        ErrorMessage = "Password must be at least 8 characters with uppercase, lowercase, number, and special character.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required, Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm Password")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [StringLength(100)]
    public string? City { get; set; }
}

public class LoginViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

public class ForgotPasswordViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Token { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 8)]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[\W_]).{8,}$",
        ErrorMessage = "Password must include uppercase, lowercase, number, and special character.")]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = string.Empty;

    [Required, Compare(nameof(NewPassword))]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class ProfileViewModel
{
    public User User { get; set; } = null!;
    public List<Booking> RecentBookings { get; set; } = new();
    public List<BusPass> ActivePasses { get; set; } = new();
    public List<Notification> Notifications { get; set; } = new();

    [Required, StringLength(120)]
    public string FullName { get; set; } = string.Empty;

    [Required, Phone, StringLength(25)]
    public string PhoneNumber { get; set; } = string.Empty;

    [StringLength(300)]
    public string? Address { get; set; }

    [StringLength(100)]
    public string? City { get; set; }

    [DataType(DataType.Date)]
    public DateTime? DateOfBirth { get; set; }
}

public class ChangePasswordViewModel
{
    [Required, DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 8)]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[\W_]).{8,}$",
        ErrorMessage = "Password must include uppercase, lowercase, number, and special character.")]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = string.Empty;

    [Required, Compare(nameof(NewPassword))]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class BusSearchViewModel
{
    public BusSearchFilterDto Filter { get; set; } = new();
    public List<BusSchedule> Results { get; set; } = new();
    public List<string> AvailableCities { get; set; } = new();
}

public class BusDetailsViewModel
{
    public BusSchedule Schedule { get; set; } = null!;
    public List<Seat> LowerDeckSeats { get; set; } = new();
    public List<Seat> UpperDeckSeats { get; set; } = new();
    public decimal ServiceFeePerSeat { get; set; }
    public decimal TaxPercentage { get; set; }
    public int RequestedPassengers { get; set; } = 1;
}

public class CheckoutViewModel
{
    public BusSchedule Schedule { get; set; } = null!;
    public List<Seat> SelectedSeats { get; set; } = new();
    public FareBreakdownDto FareBreakdown { get; set; } = null!;
    public List<PassengerInputDto> Passengers { get; set; } = new();
    public int ScheduleId { get; set; }
    public string SelectedSeatIdsCsv { get; set; } = string.Empty;
    public decimal? SubmittedTotalAmount { get; set; }
}

public class PaymentPageViewModel
{
    public Booking Booking { get; set; } = null!;
    public Payment Payment { get; set; } = null!;
    public string GatewayKeyId { get; set; } = string.Empty;
    public bool TestMode { get; set; } = true;
}

public class DigitalTicketViewModel
{
    public Booking Booking { get; set; } = null!;
    public Payment? Payment { get; set; }
    public string SignedToken { get; set; } = string.Empty;
    public string VerificationUrl { get; set; } = string.Empty;
    public string QrCodeDataUri { get; set; } = string.Empty;
}

public class MyBookingsViewModel
{
    public List<Booking> Upcoming { get; set; } = new();
    public List<Booking> Completed { get; set; } = new();
    public List<Booking> Cancelled { get; set; } = new();
    public List<Booking> AllBookings { get; set; } = new();
}

public class BusPassIndexViewModel
{
    public List<BusPass> ActivePasses { get; set; } = new();
    public List<BusPass> ExpiredOrRevokedPasses { get; set; } = new();
    public List<BusPassApplication> Applications { get; set; } = new();
    public List<CloudPass.Domain.Entities.Route> AvailableRoutes { get; set; } = new();
}

public class DigitalBusPassViewModel
{
    public BusPass Pass { get; set; } = null!;
    public string SignedToken { get; set; } = string.Empty;
    public string VerificationUrl { get; set; } = string.Empty;
    public string QrCodeDataUri { get; set; } = string.Empty;
}

public class TicketVerifyPageViewModel
{
    public string? TokenInput { get; set; }
    public bool MarkAsUsed { get; set; }
    public TicketVerificationResultDto? Result { get; set; }
    public List<TicketVerification> RecentLogs { get; set; } = new();
}

public class AdminDashboardViewModel
{
    public AdminDashboardStatsDto Stats { get; set; } = new();
    public List<Booking> RecentBookings { get; set; } = new();
    public List<BusPassApplication> PendingApplications { get; set; } = new();
    public List<AuditLog> RecentAuditLogs { get; set; } = new();
    public string StorageProvider { get; set; } = string.Empty;
    public int QueuedBackgroundJobs { get; set; }
}
