using System.Security.Claims;
using CloudPass.Application.DTOs;
using CloudPass.Application.Interfaces;
using CloudPass.Application.Options;
using CloudPass.Domain.Enums;
using CloudPass.Infrastructure.Data;
using CloudPass.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CloudPass.Web.Controllers;

[Authorize]
public class BookingController : Controller
{
    private readonly CloudPassDbContext _db;
    private readonly IFareCalculationService _fareService;
    private readonly IBookingService _bookingService;
    private readonly PaymentGatewayOptions _paymentOptions;

    public BookingController(
        CloudPassDbContext db,
        IFareCalculationService fareService,
        IBookingService bookingService,
        IOptions<PaymentGatewayOptions> paymentOptions)
    {
        _db = db;
        _fareService = fareService;
        _bookingService = bookingService;
        _paymentOptions = paymentOptions.Value;
    }

    [HttpGet]
    public async Task<IActionResult> Checkout(int scheduleId, string seatIds)
    {
        var parsedIds = ParseSeatIds(seatIds);
        if (parsedIds.Count == 0 || parsedIds.Count > 6)
        {
            TempData["Error"] = "Please select between 1 and 6 seats to continue.";
            return RedirectToAction("Details", "Bus", new { id = scheduleId });
        }

        var schedule = await _db.BusSchedules
            .Include(s => s.Bus)
            .Include(s => s.Route)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == scheduleId && s.Status == ScheduleStatus.Scheduled);

        if (schedule is null) return NotFound();

        var seats = await _db.Seats
            .Where(s => s.BusScheduleId == scheduleId && parsedIds.Contains(s.Id) && !s.IsBooked)
            .OrderBy(s => s.SeatNumber)
            .AsNoTracking()
            .ToListAsync();

        if (seats.Count != parsedIds.Count)
        {
            TempData["Error"] = "One or more selected seats are no longer available. Please choose different seats.";
            return RedirectToAction("Details", "Bus", new { id = scheduleId });
        }

        var currentUser = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == GetCurrentUserId());
        var fare = _fareService.CalculateTicketFare(schedule, schedule.Route, seats.Count);

        var passengers = seats.Select((s, idx) => new PassengerInputDto
        {
            SeatId = s.Id,
            SeatNumber = s.SeatNumber,
            PassengerName = idx == 0 ? currentUser.FullName : string.Empty,
            Age = 24,
            Gender = "Other"
        }).ToList();

        var vm = new CheckoutViewModel
        {
            Schedule = schedule,
            SelectedSeats = seats,
            FareBreakdown = fare,
            Passengers = passengers,
            ScheduleId = scheduleId,
            SelectedSeatIdsCsv = string.Join(",", seats.Select(s => s.Id)),
            SubmittedTotalAmount = fare.TotalAmount
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateBooking(CheckoutViewModel model)
    {
        var seatIds = ParseSeatIds(model.SelectedSeatIdsCsv);
        if (seatIds.Count == 0 || model.Passengers is null || model.Passengers.Count != seatIds.Count)
        {
            TempData["Error"] = "Invalid seat selection or missing passenger details.";
            return RedirectToAction("Details", "Bus", new { id = model.ScheduleId });
        }

        if (model.Passengers.Any(p => string.IsNullOrWhiteSpace(p.PassengerName) || p.Age < 1 || p.Age > 120))
        {
            TempData["Error"] = "Please provide a valid name and age (1-120) for every passenger.";
            return RedirectToAction(nameof(Checkout), new { scheduleId = model.ScheduleId, seatIds = model.SelectedSeatIdsCsv });
        }

        var result = await _bookingService.CreatePendingBookingAsync(new CreateBookingCommand
        {
            UserId = GetCurrentUserId(),
            UserEmail = GetCurrentUserEmail(),
            ScheduleId = model.ScheduleId,
            SeatIds = seatIds,
            Passengers = model.Passengers,
            ClientSubmittedTotal = model.SubmittedTotalAmount,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        if (!result.Success)
        {
            TempData["Error"] = result.ErrorMessage ?? "Could not complete seat reservation.";
            return result.PriceTamperingDetected
                ? RedirectToAction(nameof(Checkout), new { scheduleId = model.ScheduleId, seatIds = model.SelectedSeatIdsCsv })
                : RedirectToAction("Details", "Bus", new { id = model.ScheduleId });
        }

        return RedirectToAction(nameof(Payment), new { bookingRef = result.BookingReference });
    }

    [HttpGet]
    public async Task<IActionResult> Payment(string bookingRef)
    {
        var userId = GetCurrentUserId();
        var booking = await _db.Bookings
            .Include(b => b.Passengers)
            .Include(b => b.Payments)
            .Include(b => b.BusSchedule).ThenInclude(s => s.Bus)
            .Include(b => b.BusSchedule).ThenInclude(s => s.Route)
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.BookingReference == bookingRef);

        if (booking is null) return NotFound();
        if (booking.UserId != userId && !User.IsInRole("Admin")) return Forbid();

        if (booking.BookingStatus == BookingStatus.Confirmed && booking.PaymentStatus == PaymentStatus.Paid)
            return RedirectToAction("ViewTicket", "Ticket", new { bookingRef = booking.BookingReference });

        var payment = booking.Payments.OrderByDescending(p => p.CreatedAt).First();
        var vm = new PaymentPageViewModel
        {
            Booking = booking,
            Payment = payment,
            GatewayKeyId = string.IsNullOrWhiteSpace(_paymentOptions.KeyId) ? "rzp_test_cloudpass_demo" : _paymentOptions.KeyId,
            TestMode = _paymentOptions.TestMode
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmPayment(string bookingReference, string gatewayOrderId, string gatewayPaymentId, string gatewaySignature)
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var res = await _bookingService.ConfirmPaymentAsync(new PaymentConfirmationCommand
        {
            UserId = GetCurrentUserId(),
            UserEmail = GetCurrentUserEmail(),
            BookingReference = bookingReference,
            GatewayOrderId = gatewayOrderId,
            GatewayPaymentId = gatewayPaymentId,
            GatewaySignature = gatewaySignature,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
        }, baseUrl);

        if (!res.Success)
        {
            TempData["Error"] = res.Message;
            return RedirectToAction(nameof(Payment), new { bookingRef = bookingReference });
        }

        TempData["Success"] = "Payment verified securely! Your CloudPass digital ticket is ready.";
        return RedirectToAction("ViewTicket", "Ticket", new { bookingRef = bookingReference });
    }

    [HttpGet]
    public async Task<IActionResult> MyBookings()
    {
        var userId = GetCurrentUserId();
        var all = await _db.Bookings
            .Include(b => b.Passengers)
            .Include(b => b.Payments)
            .Include(b => b.BusSchedule).ThenInclude(s => s.Bus)
            .Include(b => b.BusSchedule).ThenInclude(s => s.Route)
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.BookingDate)
            .AsNoTracking()
            .ToListAsync();

        var now = DateTime.UtcNow;
        var vm = new MyBookingsViewModel
        {
            AllBookings = all,
            Upcoming = all.Where(b => b.BookingStatus != BookingStatus.Cancelled && b.BusSchedule.DepartureTime >= now.AddHours(-2)).ToList(),
            Completed = all.Where(b => b.BookingStatus == BookingStatus.Confirmed && b.BusSchedule.DepartureTime < now.AddHours(-2)).ToList(),
            Cancelled = all.Where(b => b.BookingStatus == BookingStatus.Cancelled).ToList()
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(string bookingReference, string? reason)
    {
        var res = await _bookingService.CancelBookingAsync(
            bookingReference,
            GetCurrentUserId(),
            User.IsInRole("Admin"),
            GetCurrentUserEmail(),
            string.IsNullOrWhiteSpace(reason) ? "Cancelled by passenger" : reason,
            HttpContext.Connection.RemoteIpAddress?.ToString());

        TempData[res.Success ? "Success" : "Error"] = res.Message;
        return RedirectToAction(nameof(MyBookings));
    }

    private static List<int> ParseSeatIds(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return new();
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s.Trim(), out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();
    }

    private int GetCurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string GetCurrentUserEmail() => User.FindFirstValue(ClaimTypes.Email) ?? "user@cloudpass.io";
}
