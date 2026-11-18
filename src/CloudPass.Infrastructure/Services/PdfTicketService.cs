using CloudPass.Application.Interfaces;
using CloudPass.Domain.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CloudPass.Infrastructure.Services;

public class PdfTicketService : IPdfTicketService
{
    static PdfTicketService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] GenerateTicketPdf(Booking booking, string verificationUrl, byte[] qrPngBytes)
    {
        var navy = "#0B1B3A";
        var cyan = "#06B6D4";
        var slate = "#475569";
        var lightBg = "#F8FAFC";
        var seats = string.Join(", ", booking.Passengers.Select(p => p.SeatNumber));

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(32);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor("#0F172A"));

                page.Header().Background(navy).Padding(18).Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("CLOUDPASS").FontSize(22).Bold().FontColor(Colors.White);
                        col.Item().Text("Smart Cloud-Based Bus Pass & Ticket Booking System").FontSize(9).FontColor("#93C5FD");
                        col.Item().Text("Your Journey. Your Ticket. Always Connected.").FontSize(8).Italic().FontColor(cyan);
                    });

                    row.ConstantItem(170).AlignRight().Column(col =>
                    {
                        col.Item().Text("OFFICIAL E-TICKET").FontSize(11).Bold().FontColor(cyan);
                        col.Item().Text(booking.BookingReference).FontSize(13).Bold().FontColor(Colors.White);
                        col.Item().Text($"Status: {booking.BookingStatus} | {booking.PaymentStatus}").FontSize(8).FontColor("#E2E8F0");
                    });
                });

                page.Content().PaddingVertical(16).Column(col =>
                {
                    col.Spacing(14);

                    // Journey banner
                    col.Item().Border(1).BorderColor("#CBD5E1").Background(lightBg).Padding(14).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("FROM").FontSize(8).Bold().FontColor(slate);
                            c.Item().Text(booking.BusSchedule.Route.SourceCity).FontSize(16).Bold().FontColor(navy);
                            c.Item().Text(booking.BusSchedule.DepartureTime.ToString("dd MMM yyyy, hh:mm tt")).FontSize(10).FontColor("#0284C7");
                            c.Item().Text($"Boarding: {booking.BusSchedule.BoardingPoint}").FontSize(8).FontColor(slate);
                        });

                        row.ConstantItem(60).AlignCenter().AlignMiddle().Text("→").FontSize(22).Bold().FontColor(cyan);

                        row.RelativeItem().AlignRight().Column(c =>
                        {
                            c.Item().Text("TO").FontSize(8).Bold().FontColor(slate);
                            c.Item().Text(booking.BusSchedule.Route.DestinationCity).FontSize(16).Bold().FontColor(navy);
                            c.Item().Text(booking.BusSchedule.ArrivalTime.ToString("dd MMM yyyy, hh:mm tt")).FontSize(10).FontColor("#0284C7");
                            c.Item().Text($"Dropping: {booking.BusSchedule.DroppingPoint}").FontSize(8).FontColor(slate);
                        });
                    });

                    // Bus & QR row
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().PaddingRight(12).Column(c =>
                        {
                            c.Spacing(6);
                            c.Item().Text("BUS & JOURNEY DETAILS").FontSize(10).Bold().FontColor(navy);
                            c.Item().Text($"Bus Name: {booking.BusSchedule.Bus.BusName}");
                            c.Item().Text($"Bus Number: {booking.BusSchedule.Bus.BusNumber}");
                            c.Item().Text($"Operator: {booking.BusSchedule.Bus.OperatorName}");
                            c.Item().Text($"Bus Type: {booking.BusSchedule.Bus.BusType}");
                            c.Item().Text($"Seats: {seats}").Bold().FontColor("#0284C7");
                            c.Item().Text($"Journey Date: {booking.BusSchedule.JourneyDate:dddd, dd MMMM yyyy}");
                        });

                        row.ConstantItem(150).Border(1).BorderColor("#CBD5E1").Padding(8).AlignCenter().Column(c =>
                        {
                            c.Item().AlignCenter().Width(115).Height(115).Image(qrPngBytes);
                            c.Item().PaddingTop(4).AlignCenter().Text("CRYPTOGRAPHIC QR").FontSize(7).Bold().FontColor(navy);
                            c.Item().AlignCenter().Text("Zero-PII Signed Token").FontSize(7).FontColor(slate);
                        });
                    });

                    // Passengers table
                    col.Item().Text("PASSENGER MANIFEST").FontSize(10).Bold().FontColor(navy);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(60);
                            columns.RelativeColumn(3);
                            columns.ConstantColumn(50);
                            columns.ConstantColumn(70);
                            columns.RelativeColumn(2);
                        });

                        table.Header(h =>
                        {
                            h.Cell().Background(navy).Padding(6).Text("Seat").Bold().FontColor(Colors.White);
                            h.Cell().Background(navy).Padding(6).Text("Passenger Name").Bold().FontColor(Colors.White);
                            h.Cell().Background(navy).Padding(6).Text("Age").Bold().FontColor(Colors.White);
                            h.Cell().Background(navy).Padding(6).Text("Gender").Bold().FontColor(Colors.White);
                            h.Cell().Background(navy).Padding(6).Text("ID Reference").Bold().FontColor(Colors.White);
                        });

                        foreach (var p in booking.Passengers)
                        {
                            table.Cell().BorderBottom(1).BorderColor("#E2E8F0").Padding(6).Text(p.SeatNumber).Bold();
                            table.Cell().BorderBottom(1).BorderColor("#E2E8F0").Padding(6).Text(p.PassengerName);
                            table.Cell().BorderBottom(1).BorderColor("#E2E8F0").Padding(6).Text(p.Age.ToString());
                            table.Cell().BorderBottom(1).BorderColor("#E2E8F0").Padding(6).Text(p.Gender);
                            table.Cell().BorderBottom(1).BorderColor("#E2E8F0").Padding(6).Text(string.IsNullOrWhiteSpace(p.IdProofType) ? "—" : $"{p.IdProofType} ({p.IdProofNumber})");
                        }
                    });

                    // Fare Breakdown
                    col.Item().Border(1).BorderColor("#CBD5E1").Background(lightBg).Padding(12).Column(c =>
                    {
                        c.Spacing(4);
                        c.Item().Text("SERVER-VERIFIED FARE SUMMARY").FontSize(10).Bold().FontColor(navy);
                        c.Item().Row(r => { r.RelativeItem().Text($"Base Fare ({booking.TotalPassengers} seat(s) × INR {booking.UnitOfficialFare:N2})"); r.ConstantItem(120).AlignRight().Text($"INR {booking.BaseFareTotal:N2}"); });
                        c.Item().Row(r => { r.RelativeItem().Text("CloudPass Service Fee"); r.ConstantItem(120).AlignRight().Text($"INR {booking.ServiceFeeAmount:N2}"); });
                        c.Item().Row(r => { r.RelativeItem().Text("Applicable GST / Taxes"); r.ConstantItem(120).AlignRight().Text($"INR {booking.TaxAmount:N2}"); });
                        c.Item().LineHorizontal(1).LineColor("#94A3B8");
                        c.Item().Row(r => { r.RelativeItem().Text("TOTAL PAID").Bold().FontSize(12).FontColor(navy); r.ConstantItem(120).AlignRight().Text($"INR {booking.TotalAmount:N2}").Bold().FontSize(12).FontColor("#0284C7"); });
                    });

                    // Security notice
                    col.Item().Background("#EFF6FF").Border(1).BorderColor("#BFDBFE").Padding(10).Column(c =>
                    {
                        c.Item().Text("SECURITY & ANTI-FRAUD NOTICE").FontSize(8).Bold().FontColor(navy);
                        c.Item().Text("This digital ticket is protected by HMAC-SHA256 cryptographic token signing and single-use boarding validation. The QR code contains no personal data and is validated against the centralized CloudPass database.").FontSize(8).FontColor(slate);
                    });
                });

                page.Footer().AlignCenter().Text($"Generated by CloudPass Cloud Platform • {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC • Verify at /Ticket/Verify").FontSize(8).FontColor(slate);
            });
        }).GeneratePdf();
    }

    public byte[] GenerateBusPassPdf(BusPass pass, string verificationUrl, byte[] qrPngBytes)
    {
        var navy = "#0B1B3A";
        var cyan = "#06B6D4";
        var slate = "#475569";

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A5.Landscape());
                page.Margin(24);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor("#0F172A"));

                page.Header().Background(navy).Padding(14).Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("CLOUDPASS COMMUTER PASS").FontSize(18).Bold().FontColor(Colors.White);
                        c.Item().Text("Authorized Cloud-Verified Digital Bus Pass").FontSize(8).FontColor(cyan);
                    });
                    row.ConstantItem(150).AlignRight().Column(c =>
                    {
                        c.Item().Text(pass.PassNumber).FontSize(13).Bold().FontColor(Colors.White);
                        c.Item().Text($"STATUS: {pass.Status.ToString().ToUpperInvariant()}").FontSize(8).Bold().FontColor("#34D399");
                    });
                });

                page.Content().PaddingVertical(14).Row(row =>
                {
                    row.RelativeItem().PaddingRight(12).Column(c =>
                    {
                        c.Spacing(6);
                        c.Item().Text("PASS HOLDER").FontSize(8).Bold().FontColor(slate);
                        c.Item().Text(pass.User.FullName).FontSize(15).Bold().FontColor(navy);
                        c.Item().Text($"Corridor: {pass.Route.SourceCity} ↔ {pass.Route.DestinationCity}").FontSize(12).Bold().FontColor("#0284C7");
                        c.Item().Text($"Valid From: {pass.ValidFrom:dd MMM yyyy}");
                        c.Item().Text($"Valid Until: {pass.ValidUntil:dd MMM yyyy}").Bold();
                        c.Item().Text($"Pass Fee Paid: INR {pass.AmountPaid:N2}");
                        c.Item().PaddingTop(8).Text("Scan QR code on CloudPass Verification Portal to confirm pass authenticity and live validity.").FontSize(8).FontColor(slate);
                    });

                    row.ConstantItem(145).Border(1).BorderColor("#CBD5E1").Padding(8).AlignCenter().Column(c =>
                    {
                        c.Item().AlignCenter().Width(115).Height(115).Image(qrPngBytes);
                        c.Item().PaddingTop(4).AlignCenter().Text("SIGNED PASS QR").FontSize(7).Bold().FontColor(navy);
                    });
                });

                page.Footer().AlignCenter().Text("CloudPass Smart Cloud Mobility • Tamper-Proof Cryptographic Pass").FontSize(8).FontColor(slate);
            });
        }).GeneratePdf();
    }
}
