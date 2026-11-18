using System.Net;
using System.Text.RegularExpressions;
using CloudPass.Application.DTOs;
using CloudPass.Application.Interfaces;
using CloudPass.Domain.Enums;
using CloudPass.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CloudPass.Tests;

public class FunctionalTests : IClassFixture<CloudPassWebAppFactory>
{
    private readonly CloudPassWebAppFactory _factory;

    public FunctionalTests(CloudPassWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task HomePage_LoadsSuccessfully_WithBrandAndPopularRoutes()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Book Your Bus Journey in Seconds.", html);
        Assert.Contains("CLOUD", html);
        Assert.Contains("Mumbai", html);
    }

    [Fact]
    public async Task BusSearch_FiltersByRouteAndAC_ReturnsDatabaseSchedules()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/Bus/Search?Source=Mumbai&Destination=Pune&HasAC=true");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Mumbai", html);
        Assert.Contains("Pune", html);
        Assert.Contains("AC", html);
    }

    [Fact]
    public async Task HealthEndpoints_ReturnHealthyStatusJson()
    {
        var client = _factory.CreateClient();
        foreach (var endpoint in new[] { "/health", "/health/live", "/health/ready" })
        {
            var res = await client.GetAsync(endpoint);
            var body = await res.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Contains("\"status\": \"Healthy\"", body);
        }
    }

    [Fact]
    public async Task FullEndToEnd_Booking_Payment_TicketPdf_QrVerification_AndCancellation()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudPassDbContext>();
        var bookingService = scope.ServiceProvider.GetRequiredService<IBookingService>();
        var security = scope.ServiceProvider.GetRequiredService<ITicketSecurityService>();
        var pdfService = scope.ServiceProvider.GetRequiredService<IPdfTicketService>();
        var qrService = scope.ServiceProvider.GetRequiredService<IQrCodeService>();

        var user = await db.Users.FirstAsync(u => u.Email == "passenger@cloudpass.io");
        var schedule = await db.BusSchedules
            .Include(s => s.Route)
            .Include(s => s.Bus)
            .Include(s => s.Seats)
            .FirstAsync(s => s.AvailableSeats >= 3);

        var freeSeat = schedule.Seats.First(s => !s.IsBooked);
        var initialAvailable = schedule.AvailableSeats;

        var createRes = await bookingService.CreatePendingBookingAsync(new CreateBookingCommand
        {
            UserId = user.Id,
            UserEmail = user.Email,
            ScheduleId = schedule.Id,
            SeatIds = new List<int> { freeSeat.Id },
            Passengers = new List<PassengerInputDto>
            {
                new() { SeatId = freeSeat.Id, SeatNumber = freeSeat.SeatNumber, PassengerName = "Priya Sharma", Age = 24, Gender = "Female" }
            }
        });

        Assert.True(createRes.Success, createRes.ErrorMessage);
        Assert.NotNull(createRes.BookingReference);
        Assert.NotNull(createRes.GatewayOrderId);

        var paymentId = "pay_FUNC_TEST_001";
        var validSig = security.GeneratePaymentSignature(createRes.GatewayOrderId!, paymentId);
        var confirmRes = await bookingService.ConfirmPaymentAsync(new PaymentConfirmationCommand
        {
            UserId = user.Id,
            UserEmail = user.Email,
            BookingReference = createRes.BookingReference!,
            GatewayOrderId = createRes.GatewayOrderId!,
            GatewayPaymentId = paymentId,
            GatewaySignature = validSig
        }, "https://cloudpass.test");

        Assert.True(confirmRes.Success, confirmRes.Message);

        var booking = await db.Bookings
            .Include(b => b.Passengers)
            .Include(b => b.BusSchedule).ThenInclude(s => s.Bus)
            .Include(b => b.BusSchedule).ThenInclude(s => s.Route)
            .AsNoTracking()
            .FirstAsync(b => b.BookingReference == createRes.BookingReference);

        Assert.Equal(BookingStatus.Confirmed, booking.BookingStatus);
        Assert.Equal(PaymentStatus.Paid, booking.PaymentStatus);

        var fullToken = $"CP1.TKT.{booking.BookingReference}.{booking.TicketTokenNonce}.{booking.TicketTokenSignature}";
        var qrBytes = qrService.GenerateQrPngBytes($"https://cloudpass.test/Ticket/Verify?token={fullToken}");
        var pdfBytes = pdfService.GenerateTicketPdf(booking, $"https://cloudpass.test/Ticket/Verify?token={fullToken}", qrBytes);

        Assert.True(qrBytes.Length > 100);
        Assert.True(pdfBytes.Length > 1000);
        Assert.Equal((byte)'%', pdfBytes[0]);
        Assert.Equal((byte)'P', pdfBytes[1]);
        Assert.Equal((byte)'D', pdfBytes[2]);
        Assert.Equal((byte)'F', pdfBytes[3]);

        var verifyRes = await bookingService.VerifyTokenAsync(fullToken, markAsUsed: false, verifierUserId: null);
        Assert.True(verifyRes.IsValid);
        Assert.Equal("VALID", verifyRes.ResultCode);

        var cancelRes = await bookingService.CancelBookingAsync(booking.BookingReference, user.Id, false, user.Email, "Functional test cancel");
        Assert.True(cancelRes.Success);

        var refreshedSchedule = await db.BusSchedules.AsNoTracking().FirstAsync(s => s.Id == schedule.Id);
        Assert.Equal(initialAvailable, refreshedSchedule.AvailableSeats);
    }

    [Fact]
    public async Task BusPassWorkflow_Apply_AdminApprove_AndVerifySignedQrPass()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudPassDbContext>();
        var passService = scope.ServiceProvider.GetRequiredService<IBusPassService>();
        var bookingService = scope.ServiceProvider.GetRequiredService<IBookingService>();

        var user = await db.Users.FirstAsync(u => u.Email == "rohan@cloudpass.io");
        var admin = await db.Users.FirstAsync(u => u.Email == "admin@cloudpass.io");
        var route = await db.Routes.FirstAsync();

        var applyRes = await passService.SubmitApplicationAsync(
            user.Id, user.Email, route.Id, 3, DateTime.UtcNow.Date, "College ID", "CS-2026-999");
        Assert.True(applyRes.Success);

        var app = await db.BusPassApplications.FirstAsync(a => a.ApplicationReference == applyRes.Reference);
        var expectedPrice = decimal.Round(route.MonthlyPassBasePrice * 3 * 0.95m, 2);
        Assert.Equal(expectedPrice, app.CalculatedPrice);

        var approveRes = await passService.ApproveApplicationAsync(app.Id, admin.Id, admin.Email, "Verified ID", "https://cloudpass.test");
        Assert.True(approveRes.Success);

        var pass = await db.BusPasses.AsNoTracking().FirstAsync(p => p.PassNumber == approveRes.Reference);
        var passToken = $"CP1.PAS.{pass.PassNumber}.{pass.VerificationTokenNonce}.{pass.VerificationTokenSignature}";

        var verify = await bookingService.VerifyTokenAsync(passToken, markAsUsed: false, admin.Id);
        Assert.True(verify.IsValid);
        Assert.Equal("VALID_PASS", verify.ResultCode);
    }

    [Fact]
    public async Task UserLogin_FormSubmission_AuthenticatesSuccessfully()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var loginPage = await client.GetAsync("/Account/Login");
        var html = await loginPage.Content.ReadAsStringAsync();
        var tokenMatch = Regex.Match(html, @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""");
        Assert.True(tokenMatch.Success);

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = tokenMatch.Groups[1].Value,
            ["Email"] = "passenger@cloudpass.io",
            ["Password"] = "Pass@123!",
            ["RememberMe"] = "false"
        });

        var postRes = await client.PostAsync("/Account/Login", form);
        Assert.Equal(HttpStatusCode.Redirect, postRes.StatusCode);
        Assert.Contains("CloudPass.Auth=", string.Join(";", postRes.Headers.GetValues("Set-Cookie")));
    }
}
