using System.Net;
using System.Text;
using System.Text.Json;
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

public class SecurityAndConcurrencyTests : IClassFixture<CloudPassWebAppFactory>
{
    private readonly CloudPassWebAppFactory _factory;

    public SecurityAndConcurrencyTests(CloudPassWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Security_PriceTamperingAttempt_IsBlockedAndAuditLogged()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudPassDbContext>();
        var bookingService = scope.ServiceProvider.GetRequiredService<IBookingService>();

        var user = await db.Users.FirstAsync(u => u.Email == "passenger@cloudpass.io");
        var schedule = await db.BusSchedules.Include(s => s.Seats).FirstAsync(s => s.AvailableSeats > 2);
        var seat = schedule.Seats.First(s => !s.IsBooked);

        var res = await bookingService.CreatePendingBookingAsync(new CreateBookingCommand
        {
            UserId = user.Id,
            UserEmail = user.Email,
            ScheduleId = schedule.Id,
            SeatIds = new List<int> { seat.Id },
            Passengers = new List<PassengerInputDto>
            {
                new() { SeatId = seat.Id, SeatNumber = seat.SeatNumber, PassengerName = "Tamper Attacker", Age = 25, Gender = "Male" }
            },
            ClientSubmittedTotal = 1.00m
        });

        Assert.False(res.Success);
        Assert.True(res.PriceTamperingDetected);
        Assert.True(res.OfficialTotalAmount > 100m);

        var auditExists = await db.AuditLogs.AnyAsync(a => a.Action == "PRICE_TAMPERING_BLOCKED" && a.UserId == user.Id);
        Assert.True(auditExists);
    }

    [Fact]
    public async Task Concurrency_TenSimultaneousBookingsForSameSeat_ExactlyOneSucceeds()
    {
        int scheduleId;
        int targetSeatId;
        string targetSeatNumber;
        int userId;
        string userEmail;

        using (var setupScope = _factory.Services.CreateScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<CloudPassDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == "rohan@cloudpass.io");
            var schedule = await db.BusSchedules.Include(s => s.Seats).OrderByDescending(s => s.Id).FirstAsync(s => s.AvailableSeats > 5);
            var seat = schedule.Seats.First(s => !s.IsBooked);
            scheduleId = schedule.Id;
            targetSeatId = seat.Id;
            targetSeatNumber = seat.SeatNumber;
            userId = user.Id;
            userEmail = user.Email;
        }

        var barrier = new TaskCompletionSource();
        var tasks = Enumerable.Range(1, 10).Select(async idx =>
        {
            await barrier.Task;
            using var scope = _factory.Services.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<IBookingService>();
            return await svc.CreatePendingBookingAsync(new CreateBookingCommand
            {
                UserId = userId,
                UserEmail = userEmail,
                ScheduleId = scheduleId,
                SeatIds = new List<int> { targetSeatId },
                Passengers = new List<PassengerInputDto>
                {
                    new() { SeatId = targetSeatId, SeatNumber = targetSeatNumber, PassengerName = $"Concurrent Rider {idx}", Age = 22, Gender = "Other" }
                }
            });
        }).ToList();

        barrier.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(r => r.Success));
        Assert.Equal(9, results.Count(r => !r.Success && r.SeatConflict));
    }

    [Fact]
    public async Task Security_ForgedQrToken_AndSingleUseDuplicateScan_AreBlocked()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudPassDbContext>();
        var bookingService = scope.ServiceProvider.GetRequiredService<IBookingService>();
        var admin = await db.Users.FirstAsync(u => u.Email == "admin@cloudpass.io");

        var demoBooking = await db.Bookings.FirstAsync(b => b.BookingStatus == BookingStatus.Confirmed && b.PaymentStatus == PaymentStatus.Paid && !b.IsUsed);
        var validToken = $"CP1.TKT.{demoBooking.BookingReference}.{demoBooking.TicketTokenNonce}.{demoBooking.TicketTokenSignature}";

        var forgedToken = validToken[..^1] + (validToken[^1] == 'A' ? 'B' : 'A');
        var forgedRes = await bookingService.VerifyTokenAsync(forgedToken, markAsUsed: false, admin.Id);
        Assert.False(forgedRes.IsValid);
        Assert.Equal("INVALID_SIGNATURE", forgedRes.ResultCode);

        var firstCheckIn = await bookingService.VerifyTokenAsync(validToken, markAsUsed: true, admin.Id);
        Assert.True(firstCheckIn.IsValid);
        Assert.Equal("VERIFIED_BOARDED", firstCheckIn.ResultCode);

        var duplicateScan = await bookingService.VerifyTokenAsync(validToken, markAsUsed: true, admin.Id);
        Assert.False(duplicateScan.IsValid);
        Assert.Equal("ALREADY_USED", duplicateScan.ResultCode);
    }

    [Fact]
    public async Task Security_InvalidPaymentWebhook_IsRejected_AndValidSignedWebhook_ConfirmsBooking()
    {
        string orderId;
        string bookingRef;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CloudPassDbContext>();
            var svc = scope.ServiceProvider.GetRequiredService<IBookingService>();
            var user = await db.Users.FirstAsync(u => u.Email == "passenger@cloudpass.io");
            var schedule = await db.BusSchedules.Include(s => s.Seats).Skip(2).FirstAsync(s => s.AvailableSeats > 2);
            var seat = schedule.Seats.First(s => !s.IsBooked);

            var res = await svc.CreatePendingBookingAsync(new CreateBookingCommand
            {
                UserId = user.Id,
                UserEmail = user.Email,
                ScheduleId = schedule.Id,
                SeatIds = new List<int> { seat.Id },
                Passengers = new List<PassengerInputDto>
                {
                    new() { SeatId = seat.Id, SeatNumber = seat.SeatNumber, PassengerName = "Webhook Rider", Age = 26, Gender = "Female" }
                }
            });
            Assert.True(res.Success);
            orderId = res.GatewayOrderId!;
            bookingRef = res.BookingReference!;
        }

        var client = _factory.CreateClient();
        var payload = JsonSerializer.Serialize(new { @event = "payment.captured", order_id = orderId, payment_id = "pay_WH_999" });

        var badReq = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        badReq.Headers.Add("X-Razorpay-Signature", "forged-invalid-hmac-signature");
        var badRes = await client.SendAsync(badReq);
        Assert.Equal(HttpStatusCode.Unauthorized, badRes.StatusCode);

        string validSignature;
        using (var scope = _factory.Services.CreateScope())
        {
            validSignature = scope.ServiceProvider.GetRequiredService<ITicketSecurityService>().GenerateWebhookSignature(payload);
        }

        var goodReq = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        goodReq.Headers.Add("X-Razorpay-Signature", validSignature);
        var goodRes = await client.SendAsync(goodReq);
        Assert.Equal(HttpStatusCode.OK, goodRes.StatusCode);

        using (var verifyScope = _factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<CloudPassDbContext>();
            var confirmed = await db.Bookings.Include(b => b.Payments).FirstAsync(b => b.BookingReference == bookingRef);
            Assert.Equal(BookingStatus.Confirmed, confirmed.BookingStatus);
            Assert.Equal(PaymentStatus.Paid, confirmed.PaymentStatus);
            Assert.True(confirmed.Payments.First().WebhookVerified);
        }
    }

    [Fact]
    public async Task Security_IDOR_And_RBAC_BlockUnauthorizedTicketAndAdminAccess()
    {
        var client = await CreateAuthenticatedClientAsync("rohan@cloudpass.io", "Pass@123!");

        string priyaBookingRef;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CloudPassDbContext>();
            var priya = await db.Users.FirstAsync(u => u.Email == "passenger@cloudpass.io");
            var priyaBooking = await db.Bookings.FirstAsync(b => b.UserId == priya.Id && b.BookingStatus == BookingStatus.Confirmed);
            priyaBookingRef = priyaBooking.BookingReference;
        }

        var idorTicket = await client.GetAsync($"/Ticket/ViewTicket?bookingRef={priyaBookingRef}");
        Assert.True(
            idorTicket.StatusCode == HttpStatusCode.Forbidden ||
            (idorTicket.StatusCode == HttpStatusCode.Redirect && idorTicket.Headers.Location!.ToString().Contains("AccessDenied")));

        var idorPdf = await client.GetAsync($"/Ticket/DownloadPdf?bookingRef={priyaBookingRef}");
        Assert.True(
            idorPdf.StatusCode == HttpStatusCode.Forbidden ||
            (idorPdf.StatusCode == HttpStatusCode.Redirect && idorPdf.Headers.Location!.ToString().Contains("AccessDenied")));

        var adminRes = await client.GetAsync("/Admin/Index");
        Assert.True(
            adminRes.StatusCode == HttpStatusCode.Forbidden ||
            (adminRes.StatusCode == HttpStatusCode.Redirect && adminRes.Headers.Location!.ToString().Contains("AccessDenied")));
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email, string password)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var getLogin = await client.GetAsync("/Account/Login");
        var html = await getLogin.Content.ReadAsStringAsync();
        var token = Regex.Match(html, @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""").Groups[1].Value;

        var res = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Email"] = email,
            ["Password"] = password
        }));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        return client;
    }
}
