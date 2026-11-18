using System.Security.Claims;
using CloudPass.Application.DTOs;
using CloudPass.Application.Interfaces;
using CloudPass.Domain.Entities;
using CloudPass.Domain.Enums;
using CloudPass.Infrastructure.Data;
using CloudPass.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CloudPass.Web.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly CloudPassDbContext _db;
    private readonly IBookingService _bookingService;
    private readonly IBusPassService _passService;
    private readonly IAuditService _audit;
    private readonly ITicketStorageService _storage;
    private readonly IBackgroundTaskQueue _queue;
    private readonly IMemoryCache _cache;

    public AdminController(
        CloudPassDbContext db,
        IBookingService bookingService,
        IBusPassService passService,
        IAuditService audit,
        ITicketStorageService storage,
        IBackgroundTaskQueue queue,
        IMemoryCache cache)
    {
        _db = db;
        _bookingService = bookingService;
        _passService = passService;
        _audit = audit;
        _storage = storage;
        _queue = queue;
        _cache = cache;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var paidBookings = await _db.Bookings
            .Where(b => b.PaymentStatus == PaymentStatus.Paid && b.BookingStatus == BookingStatus.Confirmed)
            .Select(b => new { b.TotalAmount, b.BookingDate })
            .ToListAsync();

        var passAmounts = await _db.BusPasses
            .Where(p => p.Status != BusPassStatus.Revoked)
            .Select(p => p.AmountPaid)
            .ToListAsync();
        var passRevenue = passAmounts.Sum();

        var today = DateTime.UtcNow.Date;
        var trend = Enumerable.Range(0, 7)
            .Select(i => today.AddDays(-6 + i))
            .Select(day =>
            {
                var dayItems = paidBookings.Where(b => b.BookingDate.Date == day).ToList();
                return new DailyRevenuePoint(day.ToString("dd MMM"), dayItems.Sum(x => x.TotalAmount), dayItems.Count);
            })
            .ToList();

        var stats = new AdminDashboardStatsDto
        {
            TotalUsers = await _db.Users.CountAsync(),
            TotalBuses = await _db.Buses.CountAsync(),
            TotalRoutes = await _db.Routes.CountAsync(),
            TotalSchedules = await _db.BusSchedules.CountAsync(),
            TotalBookings = await _db.Bookings.CountAsync(),
            ConfirmedBookings = await _db.Bookings.CountAsync(b => b.BookingStatus == BookingStatus.Confirmed),
            CancelledBookings = await _db.Bookings.CountAsync(b => b.BookingStatus == BookingStatus.Cancelled),
            TotalRevenue = paidBookings.Sum(b => b.TotalAmount) + passRevenue,
            ActiveBusPasses = await _db.BusPasses.CountAsync(p => p.Status == BusPassStatus.Active && p.ValidUntil >= today),
            PendingPassApplications = await _db.BusPassApplications.CountAsync(a => a.Status == PassApplicationStatus.Pending),
            TotalVerifications = await _db.TicketVerifications.CountAsync(),
            RevenueTrend = trend
        };

        var vm = new AdminDashboardViewModel
        {
            Stats = stats,
            RecentBookings = await _db.Bookings
                .Include(b => b.User)
                .Include(b => b.BusSchedule).ThenInclude(s => s.Route)
                .OrderByDescending(b => b.BookingDate)
                .Take(8)
                .AsNoTracking()
                .ToListAsync(),
            PendingApplications = await _db.BusPassApplications
                .Include(a => a.User)
                .Include(a => a.Route)
                .Where(a => a.Status == PassApplicationStatus.Pending)
                .OrderByDescending(a => a.CreatedAt)
                .Take(6)
                .AsNoTracking()
                .ToListAsync(),
            RecentAuditLogs = await _db.AuditLogs
                .OrderByDescending(a => a.Timestamp)
                .Take(10)
                .AsNoTracking()
                .ToListAsync(),
            StorageProvider = _storage.ProviderName,
            QueuedBackgroundJobs = _queue.QueuedCount
        };

        return View(vm);
    }

    // ==================== BUS MANAGEMENT ====================
    [HttpGet]
    public async Task<IActionResult> Buses()
    {
        var buses = await _db.Buses.OrderByDescending(b => b.CreatedAt).AsNoTracking().ToListAsync();
        return View(buses);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveBus(int id, string busName, string busNumber, string operatorName, bool hasAC, bool isSleeper, int totalSeats, string amenities)
    {
        if (string.IsNullOrWhiteSpace(busName) || string.IsNullOrWhiteSpace(busNumber))
        {
            TempData["Error"] = "Bus Name and Bus Number are required.";
            return RedirectToAction(nameof(Buses));
        }

        totalSeats = Math.Clamp(totalSeats, 16, 56);
        var busType = $"{(hasAC ? "AC" : "Non-AC")} {(isSleeper ? "Sleeper" : "Seater")}";
        var normalizedNumber = busNumber.Trim().ToUpperInvariant();

        if (id == 0)
        {
            if (await _db.Buses.AnyAsync(b => b.BusNumber == normalizedNumber))
            {
                TempData["Error"] = $"Bus number {normalizedNumber} already exists.";
                return RedirectToAction(nameof(Buses));
            }

            var bus = new Bus
            {
                BusName = busName.Trim(),
                BusNumber = normalizedNumber,
                OperatorName = string.IsNullOrWhiteSpace(operatorName) ? "CloudPass Transit" : operatorName.Trim(),
                BusType = busType,
                HasAC = hasAC,
                IsSleeper = isSleeper,
                TotalSeats = totalSeats,
                Amenities = string.IsNullOrWhiteSpace(amenities) ? "Live GPS, CCTV, Charging Port" : amenities.Trim(),
                IsActive = true
            };
            _db.Buses.Add(bus);
            await _db.SaveChangesAsync();

            await _audit.LogAsync(AdminId(), AdminEmail(), "ADMIN_BUS_CREATED", "Bus", bus.Id.ToString(),
                $"Added bus {bus.BusName} ({bus.BusNumber}, {bus.BusType}, {bus.TotalSeats} seats).", RemoteIp());
            TempData["Success"] = $"Bus {bus.BusName} added to fleet.";
        }
        else
        {
            var bus = await _db.Buses.FindAsync(id);
            if (bus is null) return NotFound();
            bus.BusName = busName.Trim();
            bus.BusNumber = normalizedNumber;
            bus.OperatorName = string.IsNullOrWhiteSpace(operatorName) ? bus.OperatorName : operatorName.Trim();
            bus.BusType = busType;
            bus.HasAC = hasAC;
            bus.IsSleeper = isSleeper;
            bus.TotalSeats = totalSeats;
            bus.Amenities = amenities?.Trim() ?? bus.Amenities;
            await _db.SaveChangesAsync();

            await _audit.LogAsync(AdminId(), AdminEmail(), "ADMIN_BUS_UPDATED", "Bus", bus.Id.ToString(),
                $"Updated bus {bus.BusName} ({bus.BusNumber}, {bus.BusType}, {bus.TotalSeats} seats).", RemoteIp());
            TempData["Success"] = $"Bus {bus.BusName} updated.";
        }

        return RedirectToAction(nameof(Buses));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteBus(int id)
    {
        var bus = await _db.Buses.Include(b => b.BusSchedules).FirstOrDefaultAsync(b => b.Id == id);
        if (bus is null) return NotFound();

        if (bus.BusSchedules.Any())
        {
            bus.IsActive = !bus.IsActive;
            await _db.SaveChangesAsync();
            await _audit.LogAsync(AdminId(), AdminEmail(), "ADMIN_BUS_TOGGLED", "Bus", bus.Id.ToString(),
                $"Set bus {bus.BusNumber} active status to {bus.IsActive} (has historical schedules).", RemoteIp());
            TempData["Success"] = $"Bus {bus.BusNumber} {(bus.IsActive ? "re-activated" : "deactivated")} (preserved schedule history).";
        }
        else
        {
            _db.Buses.Remove(bus);
            await _db.SaveChangesAsync();
            await _audit.LogAsync(AdminId(), AdminEmail(), "ADMIN_BUS_DELETED", "Bus", id.ToString(),
                $"Permanently removed bus {bus.BusNumber}.", RemoteIp());
            TempData["Success"] = $"Bus {bus.BusNumber} deleted.";
        }

        return RedirectToAction(nameof(Buses));
    }

    // ==================== ROUTE MANAGEMENT ====================
    [HttpGet]
    public async Task<IActionResult> Routes()
    {
        var routes = await _db.Routes.OrderBy(r => r.SourceCity).ThenBy(r => r.DestinationCity).AsNoTracking().ToListAsync();
        return View(routes);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveRoute(int id, string sourceCity, string destinationCity, decimal distanceKm, int estimatedDurationMinutes, decimal baseFare, decimal serviceFee, decimal taxPercentage, decimal monthlyPassBasePrice)
    {
        if (string.IsNullOrWhiteSpace(sourceCity) || string.IsNullOrWhiteSpace(destinationCity) ||
            string.Equals(sourceCity.Trim(), destinationCity.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Source and destination cities must be provided and distinct.";
            return RedirectToAction(nameof(Routes));
        }

        if (id == 0)
        {
            var route = new CloudPass.Domain.Entities.Route
            {
                SourceCity = sourceCity.Trim(),
                DestinationCity = destinationCity.Trim(),
                DistanceKm = Math.Max(10, distanceKm),
                EstimatedDurationMinutes = Math.Max(30, estimatedDurationMinutes),
                BaseFare = Math.Max(50, baseFare),
                ServiceFee = Math.Max(0, serviceFee),
                TaxPercentage = Math.Clamp(taxPercentage, 0, 28),
                MonthlyPassBasePrice = Math.Max(300, monthlyPassBasePrice),
                IsActive = true
            };
            _db.Routes.Add(route);
            await _db.SaveChangesAsync();
            InvalidateRouteCaches();

            await _audit.LogAsync(AdminId(), AdminEmail(), "ADMIN_ROUTE_CREATED", "Route", route.Id.ToString(),
                $"Created route {route.SourceCity} → {route.DestinationCity} ({route.DistanceKm} km, BaseFare INR {route.BaseFare:N2}).", RemoteIp());
            TempData["Success"] = $"Route {route.SourceCity} → {route.DestinationCity} created.";
        }
        else
        {
            var route = await _db.Routes.FindAsync(id);
            if (route is null) return NotFound();
            route.SourceCity = sourceCity.Trim();
            route.DestinationCity = destinationCity.Trim();
            route.DistanceKm = Math.Max(10, distanceKm);
            route.EstimatedDurationMinutes = Math.Max(30, estimatedDurationMinutes);
            route.BaseFare = Math.Max(50, baseFare);
            route.ServiceFee = Math.Max(0, serviceFee);
            route.TaxPercentage = Math.Clamp(taxPercentage, 0, 28);
            route.MonthlyPassBasePrice = Math.Max(300, monthlyPassBasePrice);
            await _db.SaveChangesAsync();
            InvalidateRouteCaches();

            await _audit.LogAsync(AdminId(), AdminEmail(), "ADMIN_ROUTE_UPDATED", "Route", route.Id.ToString(),
                $"Updated route {route.SourceCity} → {route.DestinationCity}.", RemoteIp());
            TempData["Success"] = $"Route {route.SourceCity} → {route.DestinationCity} updated.";
        }

        return RedirectToAction(nameof(Routes));
    }

    // ==================== SCHEDULE MANAGEMENT ====================
    [HttpGet]
    public async Task<IActionResult> Schedules()
    {
        ViewBag.Buses = await _db.Buses.Where(b => b.IsActive).OrderBy(b => b.BusName).AsNoTracking().ToListAsync();
        ViewBag.Routes = await _db.Routes.Where(r => r.IsActive).OrderBy(r => r.SourceCity).AsNoTracking().ToListAsync();

        var schedules = await _db.BusSchedules
            .Include(s => s.Bus)
            .Include(s => s.Route)
            .OrderByDescending(s => s.JourneyDate)
            .ThenBy(s => s.DepartureTime)
            .Take(60)
            .AsNoTracking()
            .ToListAsync();

        return View(schedules);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSchedule(int busId, int routeId, DateTime journeyDate, string departureTime, decimal officialFare, string boardingPoint, string droppingPoint)
    {
        var bus = await _db.Buses.FindAsync(busId);
        var route = await _db.Routes.FindAsync(routeId);
        if (bus is null || route is null)
        {
            TempData["Error"] = "Invalid bus or route selected.";
            return RedirectToAction(nameof(Schedules));
        }

        if (!TimeSpan.TryParse(departureTime, out var depTs))
            depTs = new TimeSpan(9, 0, 0);

        var depDt = journeyDate.Date.Add(depTs);
        var arrDt = depDt.AddMinutes(route.EstimatedDurationMinutes);

        var schedule = new BusSchedule
        {
            BusId = bus.Id,
            RouteId = route.Id,
            JourneyDate = journeyDate.Date,
            DepartureTime = depDt,
            ArrivalTime = arrDt,
            OfficialFare = officialFare > 0 ? officialFare : route.BaseFare,
            AvailableSeats = bus.TotalSeats,
            Status = ScheduleStatus.Scheduled,
            BoardingPoint = string.IsNullOrWhiteSpace(boardingPoint) ? $"{route.SourceCity} Central Hub" : boardingPoint.Trim(),
            DroppingPoint = string.IsNullOrWhiteSpace(droppingPoint) ? $"{route.DestinationCity} Main Terminal" : droppingPoint.Trim()
        };

        _db.BusSchedules.Add(schedule);
        await _db.SaveChangesAsync();

        var seats = DbInitializer.GenerateSeatsForSchedule(schedule.Id, bus);
        _db.Seats.AddRange(seats);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(AdminId(), AdminEmail(), "ADMIN_SCHEDULE_CREATED", "BusSchedule", schedule.Id.ToString(),
            $"Scheduled {bus.BusNumber} on {route.SourceCity} → {route.DestinationCity} ({depDt:dd MMM yyyy HH:mm}) with {seats.Count} seats at INR {schedule.OfficialFare:N2}.", RemoteIp());

        TempData["Success"] = $"Schedule created with {seats.Count} interactive seats generated.";
        return RedirectToAction(nameof(Schedules));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateScheduleStatus(int id, ScheduleStatus status)
    {
        var schedule = await _db.BusSchedules.FindAsync(id);
        if (schedule is null) return NotFound();
        schedule.Status = status;
        await _db.SaveChangesAsync();

        await _audit.LogAsync(AdminId(), AdminEmail(), "ADMIN_SCHEDULE_STATUS", "BusSchedule", id.ToString(),
            $"Changed Schedule #{id} status to {status}.", RemoteIp());
        TempData["Success"] = $"Schedule #{id} updated to {status}.";
        return RedirectToAction(nameof(Schedules));
    }

    // ==================== FARE MANAGEMENT ====================
    [HttpGet]
    public async Task<IActionResult> Fares()
    {
        ViewBag.Schedules = await _db.BusSchedules
            .Include(s => s.Bus)
            .Include(s => s.Route)
            .Where(s => s.Status == ScheduleStatus.Scheduled && s.JourneyDate >= DateTime.UtcNow.Date)
            .OrderBy(s => s.JourneyDate)
            .ThenBy(s => s.DepartureTime)
            .Take(30)
            .AsNoTracking()
            .ToListAsync();

        var routes = await _db.Routes.OrderBy(r => r.SourceCity).AsNoTracking().ToListAsync();
        return View(routes);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateRouteFare(int routeId, decimal baseFare, decimal serviceFee, decimal taxPercentage, decimal monthlyPassBasePrice)
    {
        var route = await _db.Routes.FindAsync(routeId);
        if (route is null) return NotFound();

        var oldSummary = $"Base={route.BaseFare}, Fee={route.ServiceFee}, Tax={route.TaxPercentage}%, Pass={route.MonthlyPassBasePrice}";
        route.BaseFare = Math.Max(50, baseFare);
        route.ServiceFee = Math.Max(0, serviceFee);
        route.TaxPercentage = Math.Clamp(taxPercentage, 0, 28);
        route.MonthlyPassBasePrice = Math.Max(300, monthlyPassBasePrice);
        await _db.SaveChangesAsync();
        InvalidateRouteCaches();

        await _audit.LogAsync(AdminId(), AdminEmail(), "ADMIN_FARE_UPDATED", "Route", route.Id.ToString(),
            $"Updated official fare for {route.SourceCity}→{route.DestinationCity}. Old({oldSummary}) -> New(Base={route.BaseFare}, Fee={route.ServiceFee}, Tax={route.TaxPercentage}%, Pass={route.MonthlyPassBasePrice}).", RemoteIp());

        TempData["Success"] = $"Official centralized fare updated for {route.SourceCity} → {route.DestinationCity}.";
        return RedirectToAction(nameof(Fares));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateScheduleFare(int scheduleId, decimal officialFare)
    {
        var schedule = await _db.BusSchedules.FindAsync(scheduleId);
        if (schedule is null) return NotFound();
        var oldFare = schedule.OfficialFare;
        schedule.OfficialFare = Math.Max(50, officialFare);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(AdminId(), AdminEmail(), "ADMIN_SCHEDULE_FARE_UPDATED", "BusSchedule", scheduleId.ToString(),
            $"Updated Schedule #{scheduleId} official fare from INR {oldFare:N2} to INR {schedule.OfficialFare:N2}.", RemoteIp());

        TempData["Success"] = $"Schedule #{scheduleId} official fare set to INR {schedule.OfficialFare:N2}.";
        return RedirectToAction(nameof(Fares));
    }

    // ==================== BOOKING MANAGEMENT ====================
    [HttpGet]
    public async Task<IActionResult> Bookings(string? search, string? status)
    {
        var query = _db.Bookings
            .Include(b => b.User)
            .Include(b => b.Passengers)
            .Include(b => b.Payments)
            .Include(b => b.BusSchedule).ThenInclude(s => s.Bus)
            .Include(b => b.BusSchedule).ThenInclude(s => s.Route)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(b =>
                b.BookingReference.ToLower().Contains(term) ||
                b.User.FullName.ToLower().Contains(term) ||
                b.User.Email.ToLower().Contains(term) ||
                b.BusSchedule.Route.SourceCity.ToLower().Contains(term) ||
                b.BusSchedule.Route.DestinationCity.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<BookingStatus>(status, out var parsedStatus))
        {
            query = query.Where(b => b.BookingStatus == parsedStatus);
        }

        ViewBag.Search = search;
        ViewBag.Status = status;
        var bookings = await query.OrderByDescending(b => b.BookingDate).Take(100).ToListAsync();
        return View(bookings);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelBooking(string bookingReference, string? reason)
    {
        var res = await _bookingService.CancelBookingAsync(
            bookingReference,
            AdminId(),
            isAdmin: true,
            AdminEmail(),
            string.IsNullOrWhiteSpace(reason) ? "Cancelled by Administrator" : reason,
            RemoteIp());

        TempData[res.Success ? "Success" : "Error"] = res.Message;
        return RedirectToAction(nameof(Bookings));
    }

    // ==================== BUS PASS MANAGEMENT ====================
    [HttpGet]
    public async Task<IActionResult> Passes()
    {
        ViewBag.Passes = await _db.BusPasses
            .Include(p => p.User)
            .Include(p => p.Route)
            .OrderByDescending(p => p.IssuedAt)
            .AsNoTracking()
            .ToListAsync();

        var apps = await _db.BusPassApplications
            .Include(a => a.User)
            .Include(a => a.Route)
            .OrderBy(a => a.Status)
            .ThenByDescending(a => a.CreatedAt)
            .AsNoTracking()
            .ToListAsync();

        return View(apps);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApprovePass(int applicationId, string? remarks)
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var res = await _passService.ApproveApplicationAsync(applicationId, AdminId(), AdminEmail(), remarks, baseUrl, RemoteIp());
        TempData[res.Success ? "Success" : "Error"] = res.Message;
        return RedirectToAction(nameof(Passes));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectPass(int applicationId, string remarks)
    {
        var res = await _passService.RejectApplicationAsync(applicationId, AdminId(), AdminEmail(), remarks, RemoteIp());
        TempData[res.Success ? "Success" : "Error"] = res.Message;
        return RedirectToAction(nameof(Passes));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokePass(int passId, string reason)
    {
        var res = await _passService.RevokePassAsync(passId, AdminId(), AdminEmail(), string.IsNullOrWhiteSpace(reason) ? "Policy violation" : reason, RemoteIp());
        TempData[res.Success ? "Success" : "Error"] = res.Message;
        return RedirectToAction(nameof(Passes));
    }

    // ==================== AUDIT LOGS ====================
    [HttpGet]
    public async Task<IActionResult> AuditLogs(string? search)
    {
        var query = _db.AuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(a =>
                a.Action.ToLower().Contains(term) ||
                a.UserEmail.ToLower().Contains(term) ||
                a.EntityType.ToLower().Contains(term) ||
                a.Details.ToLower().Contains(term));
        }

        ViewBag.Search = search;
        var logs = await query.OrderByDescending(a => a.Timestamp).Take(150).ToListAsync();
        return View(logs);
    }

    private void InvalidateRouteCaches()
    {
        _cache.Remove("home_popular_routes");
        _cache.Remove("available_cities");
    }

    private int AdminId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string AdminEmail() => User.FindFirstValue(ClaimTypes.Email) ?? "admin@cloudpass.io";
    private string? RemoteIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
