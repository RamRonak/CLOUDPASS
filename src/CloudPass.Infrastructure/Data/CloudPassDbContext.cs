using CloudPass.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CloudPass.Infrastructure.Data;

public class CloudPassDbContext : DbContext
{
    public CloudPassDbContext(DbContextOptions<CloudPassDbContext> options) : base(options)
    {
    }

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Bus> Buses => Set<Bus>();
    public DbSet<Route> Routes => Set<Route>();
    public DbSet<BusSchedule> BusSchedules => Set<BusSchedule>();
    public DbSet<Seat> Seats => Set<Seat>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingPassenger> BookingPassengers => Set<BookingPassenger>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<BusPassApplication> BusPassApplications => Set<BusPassApplication>();
    public DbSet<BusPass> BusPasses => Set<BusPass>();
    public DbSet<TicketVerification> TicketVerifications => Set<TicketVerification>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 1. Roles
        modelBuilder.Entity<Role>(e =>
        {
            e.ToTable("Roles");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(50).IsRequired();
            e.Property(x => x.Description).HasMaxLength(200);
            e.HasIndex(x => x.Name).IsUnique();
        });

        // 2. Users
        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("Users");
            e.HasKey(x => x.Id);
            e.Property(x => x.FullName).HasMaxLength(120).IsRequired();
            e.Property(x => x.Email).HasMaxLength(180).IsRequired();
            e.Property(x => x.PhoneNumber).HasMaxLength(30).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.City).HasMaxLength(100);
            e.Property(x => x.PasswordResetToken).HasMaxLength(200);
            e.HasIndex(x => x.Email).IsUnique();
            e.HasOne(x => x.Role)
                .WithMany(r => r.Users)
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 3. Buses
        modelBuilder.Entity<Bus>(e =>
        {
            e.ToTable("Buses");
            e.HasKey(x => x.Id);
            e.Property(x => x.BusName).HasMaxLength(120).IsRequired();
            e.Property(x => x.BusNumber).HasMaxLength(40).IsRequired();
            e.Property(x => x.OperatorName).HasMaxLength(120).IsRequired();
            e.Property(x => x.BusType).HasMaxLength(60).IsRequired();
            e.Property(x => x.Amenities).HasMaxLength(400);
            e.HasIndex(x => x.BusNumber).IsUnique();
        });

        // 4. Routes
        modelBuilder.Entity<Route>(e =>
        {
            e.ToTable("Routes");
            e.HasKey(x => x.Id);
            e.Property(x => x.SourceCity).HasMaxLength(100).IsRequired();
            e.Property(x => x.DestinationCity).HasMaxLength(100).IsRequired();
            e.Property(x => x.DistanceKm).HasPrecision(10, 2);
            e.Property(x => x.BaseFare).HasPrecision(10, 2);
            e.Property(x => x.ServiceFee).HasPrecision(10, 2);
            e.Property(x => x.TaxPercentage).HasPrecision(5, 2);
            e.Property(x => x.MonthlyPassBasePrice).HasPrecision(10, 2);
            e.HasIndex(x => new { x.SourceCity, x.DestinationCity });
        });

        // 5. BusSchedules
        modelBuilder.Entity<BusSchedule>(e =>
        {
            e.ToTable("BusSchedules");
            e.HasKey(x => x.Id);
            e.Property(x => x.OfficialFare).HasPrecision(10, 2);
            e.Property(x => x.BoardingPoint).HasMaxLength(200);
            e.Property(x => x.DroppingPoint).HasMaxLength(200);
            e.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
            e.HasIndex(x => new { x.RouteId, x.JourneyDate, x.Status });
            e.HasIndex(x => x.DepartureTime);
            e.HasOne(x => x.Bus)
                .WithMany(b => b.BusSchedules)
                .HasForeignKey(x => x.BusId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Route)
                .WithMany(r => r.BusSchedules)
                .HasForeignKey(x => x.RouteId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 6. Seats
        modelBuilder.Entity<Seat>(e =>
        {
            e.ToTable("Seats");
            e.HasKey(x => x.Id);
            e.Property(x => x.SeatNumber).HasMaxLength(15).IsRequired();
            e.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
            e.HasIndex(x => new { x.BusScheduleId, x.SeatNumber }).IsUnique();
            e.HasIndex(x => new { x.BusScheduleId, x.IsBooked });
            e.HasOne(x => x.BusSchedule)
                .WithMany(s => s.Seats)
                .HasForeignKey(x => x.BusScheduleId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Booking)
                .WithMany(b => b.Seats)
                .HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // 7. Bookings
        modelBuilder.Entity<Booking>(e =>
        {
            e.ToTable("Bookings");
            e.HasKey(x => x.Id);
            e.Property(x => x.BookingReference).HasMaxLength(40).IsRequired();
            e.Property(x => x.UnitOfficialFare).HasPrecision(10, 2);
            e.Property(x => x.BaseFareTotal).HasPrecision(10, 2);
            e.Property(x => x.ServiceFeeAmount).HasPrecision(10, 2);
            e.Property(x => x.TaxAmount).HasPrecision(10, 2);
            e.Property(x => x.TotalAmount).HasPrecision(10, 2);
            e.Property(x => x.TicketTokenNonce).HasMaxLength(100);
            e.Property(x => x.TicketTokenSignature).HasMaxLength(200);
            e.Property(x => x.TicketTokenHash).HasMaxLength(128);
            e.Property(x => x.TicketPdfBlobName).HasMaxLength(260);
            e.Property(x => x.CancellationReason).HasMaxLength(300);
            e.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
            e.HasIndex(x => x.BookingReference).IsUnique();
            e.HasIndex(x => new { x.UserId, x.BookingStatus });
            e.HasIndex(x => x.TicketTokenHash);
            e.HasOne(x => x.User)
                .WithMany(u => u.Bookings)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.BusSchedule)
                .WithMany(s => s.Bookings)
                .HasForeignKey(x => x.BusScheduleId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 8. BookingPassengers
        modelBuilder.Entity<BookingPassenger>(e =>
        {
            e.ToTable("BookingPassengers");
            e.HasKey(x => x.Id);
            e.Property(x => x.SeatNumber).HasMaxLength(15).IsRequired();
            e.Property(x => x.PassengerName).HasMaxLength(120).IsRequired();
            e.Property(x => x.Gender).HasMaxLength(20).IsRequired();
            e.Property(x => x.IdProofType).HasMaxLength(60);
            e.Property(x => x.IdProofNumber).HasMaxLength(80);
            e.HasOne(x => x.Booking)
                .WithMany(b => b.Passengers)
                .HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 9. Payments
        modelBuilder.Entity<Payment>(e =>
        {
            e.ToTable("Payments");
            e.HasKey(x => x.Id);
            e.Property(x => x.GatewayName).HasMaxLength(60).IsRequired();
            e.Property(x => x.GatewayOrderId).HasMaxLength(100).IsRequired();
            e.Property(x => x.GatewayPaymentId).HasMaxLength(100);
            e.Property(x => x.GatewaySignature).HasMaxLength(256);
            e.Property(x => x.Amount).HasPrecision(10, 2);
            e.Property(x => x.Currency).HasMaxLength(10).IsRequired();
            e.Property(x => x.VerificationNote).HasMaxLength(300);
            e.HasIndex(x => x.GatewayOrderId).IsUnique();
            e.HasOne(x => x.Booking)
                .WithMany(b => b.Payments)
                .HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 10. BusPassApplications
        modelBuilder.Entity<BusPassApplication>(e =>
        {
            e.ToTable("BusPassApplications");
            e.HasKey(x => x.Id);
            e.Property(x => x.ApplicationReference).HasMaxLength(40).IsRequired();
            e.Property(x => x.CalculatedPrice).HasPrecision(10, 2);
            e.Property(x => x.IdProofType).HasMaxLength(80).IsRequired();
            e.Property(x => x.IdProofNumber).HasMaxLength(80).IsRequired();
            e.Property(x => x.AdminRemarks).HasMaxLength(400);
            e.HasIndex(x => x.ApplicationReference).IsUnique();
            e.HasIndex(x => new { x.UserId, x.Status });
            e.HasOne(x => x.User)
                .WithMany(u => u.BusPassApplications)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Route)
                .WithMany(r => r.BusPassApplications)
                .HasForeignKey(x => x.RouteId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 11. BusPasses
        modelBuilder.Entity<BusPass>(e =>
        {
            e.ToTable("BusPasses");
            e.HasKey(x => x.Id);
            e.Property(x => x.PassNumber).HasMaxLength(40).IsRequired();
            e.Property(x => x.AmountPaid).HasPrecision(10, 2);
            e.Property(x => x.VerificationTokenNonce).HasMaxLength(100).IsRequired();
            e.Property(x => x.VerificationTokenSignature).HasMaxLength(200).IsRequired();
            e.Property(x => x.VerificationTokenHash).HasMaxLength(128).IsRequired();
            e.Property(x => x.PassPdfBlobName).HasMaxLength(260);
            e.HasIndex(x => x.PassNumber).IsUnique();
            e.HasIndex(x => new { x.UserId, x.Status });
            e.HasIndex(x => x.VerificationTokenHash);
            e.HasOne(x => x.Application)
                .WithOne(a => a.IssuedPass)
                .HasForeignKey<BusPass>(x => x.ApplicationId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.User)
                .WithMany(u => u.BusPasses)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Route)
                .WithMany(r => r.BusPasses)
                .HasForeignKey(x => x.RouteId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 12. TicketVerifications
        modelBuilder.Entity<TicketVerification>(e =>
        {
            e.ToTable("TicketVerifications");
            e.HasKey(x => x.Id);
            e.Property(x => x.TokenSnippet).HasMaxLength(60).IsRequired();
            e.Property(x => x.ResultCode).HasMaxLength(60).IsRequired();
            e.Property(x => x.ResultMessage).HasMaxLength(300).IsRequired();
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.HasIndex(x => x.VerifiedAt);
            e.HasOne(x => x.Booking)
                .WithMany(b => b.TicketVerifications)
                .HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.BusPass)
                .WithMany(p => p.TicketVerifications)
                .HasForeignKey(x => x.BusPassId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // 13. Notifications
        modelBuilder.Entity<Notification>(e =>
        {
            e.ToTable("Notifications");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(160).IsRequired();
            e.Property(x => x.Message).HasMaxLength(600).IsRequired();
            e.Property(x => x.RelatedReference).HasMaxLength(60);
            e.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAt });
            e.HasOne(x => x.User)
                .WithMany(u => u.Notifications)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 14. AuditLogs
        modelBuilder.Entity<AuditLog>(e =>
        {
            e.ToTable("AuditLogs");
            e.HasKey(x => x.Id);
            e.Property(x => x.UserEmail).HasMaxLength(180).IsRequired();
            e.Property(x => x.Action).HasMaxLength(100).IsRequired();
            e.Property(x => x.EntityType).HasMaxLength(80).IsRequired();
            e.Property(x => x.EntityId).HasMaxLength(80).IsRequired();
            e.Property(x => x.Details).HasMaxLength(1000).IsRequired();
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.HasIndex(x => x.Timestamp);
            e.HasOne(x => x.User)
                .WithMany(u => u.AuditLogs)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
