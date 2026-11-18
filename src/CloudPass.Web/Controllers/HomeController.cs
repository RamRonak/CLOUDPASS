using CloudPass.Application.Interfaces;
using CloudPass.Infrastructure.Data;
using CloudPass.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CloudPass.Web.Controllers;

public class HomeController : Controller
{
    private readonly CloudPassDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ITicketStorageService _storage;

    public HomeController(CloudPassDbContext db, IMemoryCache cache, ITicketStorageService storage)
    {
        _db = db;
        _cache = cache;
        _storage = storage;
    }

    public async Task<IActionResult> Index()
    {
        var popularRoutes = await _cache.GetOrCreateAsync("home_popular_routes", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
            var activeRoutes = await _db.Routes
                .Where(r => r.IsActive)
                .AsNoTracking()
                .ToListAsync();
            return activeRoutes
                .OrderBy(r => r.BaseFare)
                .Take(6)
                .ToList();
        }) ?? new();

        var cities = await _cache.GetOrCreateAsync("available_cities", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
            var srcs = await _db.Routes.Where(r => r.IsActive).Select(r => r.SourceCity).Distinct().ToListAsync();
            var dsts = await _db.Routes.Where(r => r.IsActive).Select(r => r.DestinationCity).Distinct().ToListAsync();
            return srcs.Union(dsts).Distinct().OrderBy(c => c).ToList();
        }) ?? new();

        var vm = new HomeViewModel
        {
            PopularRoutes = popularRoutes,
            AvailableCities = cities,
            TotalBuses = await _db.Buses.CountAsync(b => b.IsActive),
            TotalRoutes = await _db.Routes.CountAsync(r => r.IsActive),
            TotalBookings = await _db.Bookings.CountAsync(),
            StorageProvider = _storage.ProviderName
        };

        return View(vm);
    }

    public IActionResult CloudArchitecture()
    {
        ViewBag.StorageProvider = _storage.ProviderName;
        ViewBag.InstanceId = Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID") ?? Environment.MachineName;
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        ViewBag.RequestId = HttpContext.TraceIdentifier;
        return View();
    }
}
