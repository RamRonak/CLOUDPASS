using CloudPass.Application.Interfaces;
using CloudPass.Domain.Entities;
using CloudPass.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CloudPass.Infrastructure.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(
        CloudPassDbContext db,
        IPasswordHasher<User> passwordHasher,
        ITicketSecurityService ticketSecurity)
    {
        // Works seamlessly for both SQL Server (migrations or initial schema) and SQLite (local demo/tests)
        if (db.Database.IsSqlServer())
        {
            if (db.Database.GetMigrations().Any())
                await db.Database.MigrateAsync();
            else
                await db.Database.EnsureCreatedAsync();
        }
        else
        {
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;");
        }

        if (await db.Roles.AnyAsync())
            return;

        // 1. Seed Roles
        var adminRole = new Role { Name = "Admin", Description = "Platform administrator with full fleet, route, fare, pass, and audit access." };
        var userRole = new Role { Name = "User", Description = "Registered passenger and commuter with booking and bus pass privileges." };
        db.Roles.AddRange(adminRole, userRole);
        await db.SaveChangesAsync();

        // 2. Seed Fictional Demo Users (Demo accounts for college presentation; passwords hashed with PBKDF2)
        var adminUser = new User
        {
            FullName = "Aarav Deshmukh",
            Email = "admin@cloudpass.io",
            PhoneNumber = "+91 98200 11001",
            RoleId = adminRole.Id,
            Address = "CloudPass Operations Center, Bandra Kurla Complex",
            City = "Mumbai",
            DateOfBirth = new DateTime(1992, 5, 14)
        };
        adminUser.PasswordHash = passwordHasher.HashPassword(adminUser, "Admin@123!");

        var passengerUser = new User
        {
            FullName = "Priya Sharma",
            Email = "passenger@cloudpass.io",
            PhoneNumber = "+91 98200 22002",
            RoleId = userRole.Id,
            Address = "42 Horizon Park, Kothrud",
            City = "Pune",
            DateOfBirth = new DateTime(2001, 9, 22)
        };
        passengerUser.PasswordHash = passwordHasher.HashPassword(passengerUser, "Pass@123!");

        var secondUser = new User
        {
            FullName = "Rohan Kulkarni",
            Email = "rohan@cloudpass.io",
            PhoneNumber = "+91 98200 33003",
            RoleId = userRole.Id,
            Address = "18 Tech Avenue, Indiranagar",
            City = "Bengaluru",
            DateOfBirth = new DateTime(1999, 2, 10)
        };
        secondUser.PasswordHash = passwordHasher.HashPassword(secondUser, "Pass@123!");

        db.Users.AddRange(adminUser, passengerUser, secondUser);
        await db.SaveChangesAsync();

        // 3. Seed Realistic Fictional Fleet
        var buses = new List<Bus>
        {
            new() { BusName = "CloudLiner Volvo 9600", BusNumber = "MH-12-CP-9601", OperatorName = "CloudPass Premier", BusType = "AC Sleeper", HasAC = true, IsSleeper = true, TotalSeats = 30, Amenities = "High-Speed WiFi, USB-C Fast Charging, Live GPS, CCTV, Blanket, Water Bottle" },
            new() { BusName = "AeroCoach Scania Metrolink", BusNumber = "KA-01-CP-4410", OperatorName = "Skyline Express", BusType = "AC Seater", HasAC = true, IsSleeper = false, TotalSeats = 36, Amenities = "WiFi, Push-Back Recliner, Live GPS, CCTV, Reading Light" },
            new() { BusName = "Deccan Voyager Benz", BusNumber = "MH-01-CP-7720", OperatorName = "Deccan Cloud Mobility", BusType = "AC Sleeper", HasAC = true, IsSleeper = true, TotalSeats = 30, Amenities = "Personal TV, Charging Port, Live GPS, Emergency SOS, Water Bottle" },
            new() { BusName = "MetroSwift Commuter", BusNumber = "MH-14-CP-2108", OperatorName = "Western Transit Corp", BusType = "Non-AC Seater", HasAC = false, IsSleeper = false, TotalSeats = 40, Amenities = "Live GPS, CCTV, Overhead Luggage Rack, First Aid" },
            new() { BusName = "Rajdhani Star Glider", BusNumber = "DL-01-CP-8890", OperatorName = "NorthStar CloudBus", BusType = "AC Sleeper", HasAC = true, IsSleeper = true, TotalSeats = 30, Amenities = "WiFi, Charging Port, Air Suspension, CCTV, Blanket" },
            new() { BusName = "Coastal Breeze Express", BusNumber = "GA-07-CP-5532", OperatorName = "Konkan Cloud Travels", BusType = "Non-AC Sleeper", HasAC = false, IsSleeper = true, TotalSeats = 30, Amenities = "Charging Port, Live GPS, Reading Light, Ventilation" },
            new() { BusName = "Southern Arrow EV", BusNumber = "TN-09-CP-3104", OperatorName = "GreenVolt Mobility", BusType = "AC Seater", HasAC = true, IsSleeper = false, TotalSeats = 36, Amenities = "Zero-Emission Electric, WiFi, USB-C, Live GPS, CCTV" },
            new() { BusName = "Gujarat Horizon Coach", BusNumber = "GJ-01-CP-6402", OperatorName = "WestCoast CloudBus", BusType = "AC Sleeper", HasAC = true, IsSleeper = true, TotalSeats = 30, Amenities = "WiFi, Charging Port, Live GPS, Pillows & Blanket" }
        };
        db.Buses.AddRange(buses);
        await db.SaveChangesAsync();

        // 4. Seed Popular Routes
        var routes = new List<Route>
        {
            new() { SourceCity = "Mumbai", DestinationCity = "Pune", DistanceKm = 152, EstimatedDurationMinutes = 210, BaseFare = 450, ServiceFee = 30, TaxPercentage = 5, MonthlyPassBasePrice = 2400 },
            new() { SourceCity = "Pune", DestinationCity = "Mumbai", DistanceKm = 152, EstimatedDurationMinutes = 215, BaseFare = 450, ServiceFee = 30, TaxPercentage = 5, MonthlyPassBasePrice = 2400 },
            new() { SourceCity = "Bengaluru", DestinationCity = "Chennai", DistanceKm = 346, EstimatedDurationMinutes = 375, BaseFare = 850, ServiceFee = 45, TaxPercentage = 5, MonthlyPassBasePrice = 4200 },
            new() { SourceCity = "Delhi", DestinationCity = "Jaipur", DistanceKm = 281, EstimatedDurationMinutes = 310, BaseFare = 680, ServiceFee = 35, TaxPercentage = 5, MonthlyPassBasePrice = 3400 },
            new() { SourceCity = "Hyderabad", DestinationCity = "Bengaluru", DistanceKm = 574, EstimatedDurationMinutes = 540, BaseFare = 1250, ServiceFee = 55, TaxPercentage = 5, MonthlyPassBasePrice = 5800 },
            new() { SourceCity = "Pune", DestinationCity = "Goa", DistanceKm = 445, EstimatedDurationMinutes = 510, BaseFare = 1100, ServiceFee = 50, TaxPercentage = 5, MonthlyPassBasePrice = 4900 },
            new() { SourceCity = "Ahmedabad", DestinationCity = "Mumbai", DistanceKm = 526, EstimatedDurationMinutes = 525, BaseFare = 1050, ServiceFee = 50, TaxPercentage = 5, MonthlyPassBasePrice = 4800 },
            new() { SourceCity = "Chennai", DestinationCity = "Bengaluru", DistanceKm = 346, EstimatedDurationMinutes = 370, BaseFare = 820, ServiceFee = 45, TaxPercentage = 5, MonthlyPassBasePrice = 4200 }
        };
        db.Routes.AddRange(routes);
        await db.SaveChangesAsync();

        // 5. Seed Schedules for Today + next 6 days across routes
        var today = DateTime.UtcNow.Date;
        var scheduleConfigs = new (int RouteIdx, int BusIdx, int Hour, int Min, decimal FareOverride, string Boarding, string Dropping)[]
        {
            (0, 0, 7, 30, 549, "Dadar TT CloudPass Hub", "Shivajinagar Smart Terminal"),
            (0, 1, 10, 15, 480, "Vashi Highway Plaza", "Wakad Express Bay"),
            (0, 3, 14, 0, 320, "Borivali National Park Gate", "Swargate Main Terminal"),
            (0, 2, 21, 30, 620, "Bandra Kurla Complex Bay 2", "Hinjewadi Phase 2 Hub"),
            (1, 1, 6, 45, 490, "Shivajinagar Smart Terminal", "Dadar TT CloudPass Hub"),
            (1, 0, 18, 30, 560, "Hinjewadi Phase 2 Hub", "Andheri East Metro Bay"),
            (2, 6, 7, 0, 890, "Majestic Cloud Terminal", "Koyambedu Smart Bay 4"),
            (2, 1, 15, 30, 840, "Electronic City Toll Plaza", "Guindy Express Point"),
            (2, 2, 22, 15, 1090, "Silk Board Flyover Hub", "CMBT Chennai Platform 8"),
            (3, 4, 6, 30, 720, "Kashmere Gate ISBT Bay 9", "Sindhi Camp Smart Terminal"),
            (3, 3, 13, 45, 510, "Dhaula Kuan Express Stop", "Narayan Singh Circle"),
            (3, 4, 22, 0, 790, "IFFCO Chowk Gurugram", "Jaipur Central Highway Hub"),
            (4, 0, 20, 30, 1340, "Gachibowli Cloud Hub", "Hebbal Ring Road Bay"),
            (4, 2, 22, 0, 1280, "Miyapur Metro Terminal", "Majestic Cloud Terminal"),
            (5, 5, 20, 0, 960, "Swargate Main Terminal", "Mapusa Bus Stand"),
            (5, 0, 21, 45, 1290, "Wakad Express Bay", "Panjim Kadamba Terminal"),
            (6, 7, 21, 0, 1120, "SG Highway Cloud Hub", "Borivali Express Bay"),
            (7, 6, 8, 15, 860, "Koyambedu Smart Bay 4", "Electronic City Toll Plaza")
        };

        var schedules = new List<BusSchedule>();
        for (int dayOffset = 0; dayOffset < 5; dayOffset++)
        {
            var jDate = today.AddDays(dayOffset);
            foreach (var cfg in scheduleConfigs)
            {
                var route = routes[cfg.RouteIdx];
                var bus = buses[cfg.BusIdx];
                var dep = jDate.AddHours(cfg.Hour).AddMinutes(cfg.Min);
                var arr = dep.AddMinutes(route.EstimatedDurationMinutes);

                schedules.Add(new BusSchedule
                {
                    BusId = bus.Id,
                    RouteId = route.Id,
                    JourneyDate = jDate,
                    DepartureTime = dep,
                    ArrivalTime = arr,
                    OfficialFare = cfg.FareOverride,
                    AvailableSeats = bus.TotalSeats,
                    Status = ScheduleStatus.Scheduled,
                    BoardingPoint = cfg.Boarding,
                    DroppingPoint = cfg.Dropping
                });
            }
        }
        db.BusSchedules.AddRange(schedules);
        await db.SaveChangesAsync();

        // 6. Generate Seats for every seeded schedule
        var busMap = buses.ToDictionary(b => b.Id);
        var allSeats = new List<Seat>();
        foreach (var sch in schedules)
        {
            var bus = busMap[sch.BusId];
            allSeats.AddRange(GenerateSeatsForSchedule(sch.Id, bus));
        }
        db.Seats.AddRange(allSeats);
        await db.SaveChangesAsync();

        // 7. Seed a realistic Confirmed Booking with Cryptographically Signed QR Token for Priya
        var sampleSchedule = schedules.First(s => s.JourneyDate == today.AddDays(1) && s.RouteId == routes[0].Id);
        var sampleSeats = await db.Seats
            .Where(s => s.BusScheduleId == sampleSchedule.Id)
            .OrderBy(s => s.Id)
            .Take(2)
            .ToListAsync();

        var bookingRef = $"CP-{today:yyyyMMdd}-DEMO01";
        var tokenData = ticketSecurity.GenerateSignedTicketToken(bookingRef);
        var unitFare = sampleSchedule.OfficialFare;
        var baseTotal = unitFare * 2;
        var svcFee = routes[0].ServiceFee * 2;
        var taxAmt = decimal.Round((baseTotal + svcFee) * (routes[0].TaxPercentage / 100m), 2);
        var totalAmt = baseTotal + svcFee + taxAmt;

        var demoBooking = new Booking
        {
            BookingReference = bookingRef,
            UserId = passengerUser.Id,
            BusScheduleId = sampleSchedule.Id,
            BookingDate = DateTime.UtcNow.AddHours(-5),
            TotalPassengers = 2,
            UnitOfficialFare = unitFare,
            BaseFareTotal = baseTotal,
            ServiceFeeAmount = svcFee,
            TaxAmount = taxAmt,
            TotalAmount = totalAmt,
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = PaymentStatus.Paid,
            TicketTokenNonce = tokenData.Nonce,
            TicketTokenSignature = tokenData.Signature,
            TicketTokenHash = tokenData.TokenHash
        };
        db.Bookings.Add(demoBooking);
        await db.SaveChangesAsync();

        foreach (var seat in sampleSeats)
        {
            seat.IsBooked = true;
            seat.BookingId = demoBooking.Id;
        }
        sampleSchedule.AvailableSeats -= sampleSeats.Count;

        db.BookingPassengers.AddRange(
            new BookingPassenger { BookingId = demoBooking.Id, SeatId = sampleSeats[0].Id, SeatNumber = sampleSeats[0].SeatNumber, PassengerName = "Priya Sharma", Age = 24, Gender = "Female", IdProofType = "Aadhaar", IdProofNumber = "XXXX-8841" },
            new BookingPassenger { BookingId = demoBooking.Id, SeatId = sampleSeats[1].Id, SeatNumber = sampleSeats[1].SeatNumber, PassengerName = "Ananya Verma", Age = 23, Gender = "Female", IdProofType = "College ID", IdProofNumber = "MIT-2026-109" }
        );

        var orderId = "order_CPDEMO2026001";
        var paymentId = "pay_CPDEMO2026001";
        db.Payments.Add(new Payment
        {
            BookingId = demoBooking.Id,
            GatewayName = "Razorpay-TestMode",
            GatewayOrderId = orderId,
            GatewayPaymentId = paymentId,
            GatewaySignature = ticketSecurity.GeneratePaymentSignature(orderId, paymentId),
            Amount = totalAmt,
            Currency = "INR",
            Status = PaymentStatus.Paid,
            VerifiedServerSide = true,
            WebhookVerified = true,
            PaidAt = DateTime.UtcNow.AddHours(-5),
            VerificationNote = "Verified via server-side HMAC-SHA256 & signed webhook"
        });

        // 8. Seed Approved Active Bus Pass + Pending Bus Pass Application
        var approvedApp = new BusPassApplication
        {
            ApplicationReference = $"BPA-{today:yyyyMMdd}-0001",
            UserId = passengerUser.Id,
            RouteId = routes[0].Id,
            DurationMonths = 1,
            RequestedStartDate = today.AddDays(-5),
            CalculatedPrice = routes[0].MonthlyPassBasePrice,
            IdProofType = "University ID",
            IdProofNumber = "UNI-CS-2026-441",
            Status = PassApplicationStatus.Approved,
            AdminRemarks = "Verified commuter ID. Monthly CloudPass issued.",
            ReviewedByUserId = adminUser.Id,
            ReviewedAt = DateTime.UtcNow.AddDays(-5)
        };
        var pendingApp = new BusPassApplication
        {
            ApplicationReference = $"BPA-{today:yyyyMMdd}-0002",
            UserId = secondUser.Id,
            RouteId = routes[2].Id,
            DurationMonths = 3,
            RequestedStartDate = today.AddDays(2),
            CalculatedPrice = decimal.Round(routes[2].MonthlyPassBasePrice * 3 * 0.95m, 2),
            IdProofType = "Employee ID",
            IdProofNumber = "EMP-BLR-8821",
            Status = PassApplicationStatus.Pending
        };
        db.BusPassApplications.AddRange(approvedApp, pendingApp);
        await db.SaveChangesAsync();

        var passNumber = $"CPP-{today:yyyyMM}-0001";
        var passToken = ticketSecurity.GenerateSignedPassToken(passNumber);
        db.BusPasses.Add(new BusPass
        {
            PassNumber = passNumber,
            ApplicationId = approvedApp.Id,
            UserId = passengerUser.Id,
            RouteId = routes[0].Id,
            ValidFrom = today.AddDays(-5),
            ValidUntil = today.AddDays(25),
            AmountPaid = approvedApp.CalculatedPrice,
            Status = BusPassStatus.Active,
            VerificationTokenNonce = passToken.Nonce,
            VerificationTokenSignature = passToken.Signature,
            VerificationTokenHash = passToken.TokenHash
        });

        // 9. Seed Notifications & Audit Logs
        db.Notifications.AddRange(
            new Notification
            {
                UserId = passengerUser.Id,
                Title = "Digital Ticket Confirmed",
                Message = $"Booking {bookingRef} for Mumbai → Pune is confirmed with tamper-proof QR verification.",
                Category = NotificationCategory.Ticket,
                RelatedReference = bookingRef
            },
            new Notification
            {
                UserId = passengerUser.Id,
                Title = "Monthly Bus Pass Approved",
                Message = $"Your CloudPass {passNumber} (Mumbai → Pune) has been approved and is ready for download.",
                Category = NotificationCategory.BusPass,
                RelatedReference = passNumber
            }
        );

        db.AuditLogs.Add(new AuditLog
        {
            UserId = adminUser.Id,
            UserEmail = adminUser.Email,
            Action = "SYSTEM_SEED",
            EntityType = "System",
            EntityId = "CloudPass",
            Details = "Initialized CloudPass cloud database with fleet, routes, schedules, and sample secured records.",
            IpAddress = "127.0.0.1"
        });

        await db.SaveChangesAsync();
    }

    public static List<Seat> GenerateSeatsForSchedule(int scheduleId, Bus bus)
    {
        var seats = new List<Seat>();
        if (bus.IsSleeper)
        {
            // Lower deck: 15 berths (5 rows x 3 columns: L1..L15), Upper deck: remaining (U1..U15)
            int lowerCount = bus.TotalSeats / 2;
            int upperCount = bus.TotalSeats - lowerCount;

            for (int i = 0; i < lowerCount; i++)
            {
                seats.Add(new Seat
                {
                    BusScheduleId = scheduleId,
                    SeatNumber = $"L{i + 1}",
                    Deck = SeatDeck.Lower,
                    SeatType = SeatType.Sleeper,
                    RowIndex = (i / 3) + 1,
                    ColumnIndex = (i % 3) + 1,
                    IsBooked = false
                });
            }
            for (int i = 0; i < upperCount; i++)
            {
                seats.Add(new Seat
                {
                    BusScheduleId = scheduleId,
                    SeatNumber = $"U{i + 1}",
                    Deck = SeatDeck.Upper,
                    SeatType = SeatType.Sleeper,
                    RowIndex = (i / 3) + 1,
                    ColumnIndex = (i % 3) + 1,
                    IsBooked = false
                });
            }
        }
        else
        {
            // Seater layout: 4 columns (A, B | aisle | C, D)
            string[] cols = ["A", "B", "C", "D"];
            for (int i = 0; i < bus.TotalSeats; i++)
            {
                int row = (i / 4) + 1;
                int colIdx = i % 4;
                seats.Add(new Seat
                {
                    BusScheduleId = scheduleId,
                    SeatNumber = $"{row}{cols[colIdx]}",
                    Deck = SeatDeck.Lower,
                    SeatType = SeatType.Seater,
                    RowIndex = row,
                    ColumnIndex = colIdx + 1,
                    IsBooked = false
                });
            }
        }
        return seats;
    }
}
