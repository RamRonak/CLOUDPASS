using CloudPass.Application.DTOs;
using CloudPass.Domain.Enums;
using CloudPass.Infrastructure.Data;
using CloudPass.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CloudPass.Web.Controllers;

public class BusController : Controller
{
    private readonly CloudPassDbContext _db;

    public BusController(CloudPassDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] BusSearchFilterDto filter)
    {
        filter.Passengers = Math.Clamp(filter.Passengers <= 0 ? 1 : filter.Passengers, 1, 6);

        var query = _db.BusSchedules
            .Include(s => s.Bus)
            .Include(s => s.Route)
            .Where(s => s.Status == ScheduleStatus.Scheduled && s.Bus.IsActive && s.Route.IsActive && s.AvailableSeats >= filter.Passengers)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Source))
        {
            var src = filter.Source.Trim().ToLower();
            query = query.Where(s => s.Route.SourceCity.ToLower().Contains(src));
        }

        if (!string.IsNullOrWhiteSpace(filter.Destination))
        {
            var dst = filter.Destination.Trim().ToLower();
            query = query.Where(s => s.Route.DestinationCity.ToLower().Contains(dst));
        }

        if (filter.JourneyDate.HasValue)
        {
            var dayStart = filter.JourneyDate.Value.Date;
            var dayEnd = dayStart.AddDays(1);
            query = query.Where(s => s.JourneyDate >= dayStart && s.JourneyDate < dayEnd);
        }
        else
        {
            var today = DateTime.UtcNow.Date;
            query = query.Where(s => s.JourneyDate >= today);
        }

        if (filter.HasAC.HasValue)
            query = query.Where(s => s.Bus.HasAC == filter.HasAC.Value);

        if (filter.IsSleeper.HasValue)
            query = query.Where(s => s.Bus.IsSleeper == filter.IsSleeper.Value);

        var results = await query.ToListAsync();

        if (filter.MinPrice.HasValue)
            results = results.Where(s => s.OfficialFare >= filter.MinPrice.Value).ToList();

        if (filter.MaxPrice.HasValue)
            results = results.Where(s => s.OfficialFare <= filter.MaxPrice.Value).ToList();

        // Departure time window filter (evaluated accurately on local hour)
        if (!string.IsNullOrWhiteSpace(filter.DepartureWindow))
        {
            results = filter.DepartureWindow switch
            {
                "Morning" => results.Where(s => s.DepartureTime.Hour is >= 5 and < 12).ToList(),
                "Afternoon" => results.Where(s => s.DepartureTime.Hour is >= 12 and < 17).ToList(),
                "Evening" => results.Where(s => s.DepartureTime.Hour is >= 17 and < 21).ToList(),
                "Night" => results.Where(s => s.DepartureTime.Hour >= 21 || s.DepartureTime.Hour < 5).ToList(),
                _ => results
            };
        }

        results = filter.SortBy switch
        {
            "price_asc" => results.OrderBy(s => s.OfficialFare).ToList(),
            "price_desc" => results.OrderByDescending(s => s.OfficialFare).ToList(),
            "seats_desc" => results.OrderByDescending(s => s.AvailableSeats).ToList(),
            _ => results.OrderBy(s => s.DepartureTime).ToList()
        };

        var srcs = await _db.Routes.Where(r => r.IsActive).Select(r => r.SourceCity).Distinct().ToListAsync();
        var dsts = await _db.Routes.Where(r => r.IsActive).Select(r => r.DestinationCity).Distinct().ToListAsync();

        var vm = new BusSearchViewModel
        {
            Filter = filter,
            Results = results,
            AvailableCities = srcs.Union(dsts).Distinct().OrderBy(c => c).ToList()
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, int passengers = 1)
    {
        var schedule = await _db.BusSchedules
            .Include(s => s.Bus)
            .Include(s => s.Route)
            .Include(s => s.Seats)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id);

        if (schedule is null)
            return NotFound();

        var orderedSeats = schedule.Seats
            .OrderBy(s => s.RowIndex)
            .ThenBy(s => s.ColumnIndex)
            .ToList();

        var vm = new BusDetailsViewModel
        {
            Schedule = schedule,
            LowerDeckSeats = orderedSeats.Where(s => s.Deck == SeatDeck.Lower).ToList(),
            UpperDeckSeats = orderedSeats.Where(s => s.Deck == SeatDeck.Upper).ToList(),
            ServiceFeePerSeat = schedule.Route.ServiceFee,
            TaxPercentage = schedule.Route.TaxPercentage,
            RequestedPassengers = Math.Clamp(passengers, 1, 6)
        };

        return View(vm);
    }
}
