using CloudPass.Domain.Enums;

namespace CloudPass.Domain.Entities;

public class Bus
{
    public int Id { get; set; }
    public string BusName { get; set; } = string.Empty;
    public string BusNumber { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
    public string BusType { get; set; } = "AC Sleeper";
    public bool HasAC { get; set; } = true;
    public bool IsSleeper { get; set; } = true;
    public int TotalSeats { get; set; } = 32;
    public string Amenities { get; set; } = "WiFi, Charging Port, Live GPS, CCTV, Reading Light";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<BusSchedule> BusSchedules { get; set; } = new List<BusSchedule>();
}

public class Route
{
    public int Id { get; set; }
    public string SourceCity { get; set; } = string.Empty;
    public string DestinationCity { get; set; } = string.Empty;
    public decimal DistanceKm { get; set; }
    public int EstimatedDurationMinutes { get; set; }
    public decimal BaseFare { get; set; }
    public decimal ServiceFee { get; set; } = 35.00m;
    public decimal TaxPercentage { get; set; } = 5.00m;
    public decimal MonthlyPassBasePrice { get; set; } = 1800.00m;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<BusSchedule> BusSchedules { get; set; } = new List<BusSchedule>();
    public ICollection<BusPassApplication> BusPassApplications { get; set; } = new List<BusPassApplication>();
    public ICollection<BusPass> BusPasses { get; set; } = new List<BusPass>();
}

public class BusSchedule
{
    public int Id { get; set; }
    public int BusId { get; set; }
    public int RouteId { get; set; }
    public DateTime JourneyDate { get; set; }
    public DateTime DepartureTime { get; set; }
    public DateTime ArrivalTime { get; set; }
    public decimal OfficialFare { get; set; }
    public int AvailableSeats { get; set; }
    public ScheduleStatus Status { get; set; } = ScheduleStatus.Scheduled;
    public string BoardingPoint { get; set; } = "Central Bus Terminal";
    public string DroppingPoint { get; set; } = "City Highway Terminal";
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Bus Bus { get; set; } = null!;
    public Route Route { get; set; } = null!;
    public ICollection<Seat> Seats { get; set; } = new List<Seat>();
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}

public class Seat
{
    public int Id { get; set; }
    public int BusScheduleId { get; set; }
    public string SeatNumber { get; set; } = string.Empty;
    public SeatDeck Deck { get; set; } = SeatDeck.Lower;
    public SeatType SeatType { get; set; } = SeatType.Seater;
    public int RowIndex { get; set; }
    public int ColumnIndex { get; set; }
    public bool IsBooked { get; set; }
    public int? BookingId { get; set; }
    public DateTime? HeldUntil { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public BusSchedule BusSchedule { get; set; } = null!;
    public Booking? Booking { get; set; }
}
