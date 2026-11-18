using CloudPass.Application.DTOs;
using CloudPass.Application.Interfaces;
using CloudPass.Domain.Entities;

namespace CloudPass.Application.Services;

public class FareCalculationService : IFareCalculationService
{
    public FareBreakdownDto CalculateTicketFare(BusSchedule schedule, Route route, int seatCount)
    {
        if (seatCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(seatCount), "At least one seat must be selected.");

        // Authoritative server-side fare taken exclusively from database records
        var unitOfficialFare = schedule.OfficialFare > 0 ? schedule.OfficialFare : route.BaseFare;
        var baseFareTotal = decimal.Round(unitOfficialFare * seatCount, 2, MidpointRounding.AwayFromZero);
        var serviceFeeAmount = decimal.Round(route.ServiceFee * seatCount, 2, MidpointRounding.AwayFromZero);
        var taxableAmount = baseFareTotal + serviceFeeAmount;
        var taxAmount = decimal.Round(taxableAmount * (route.TaxPercentage / 100m), 2, MidpointRounding.AwayFromZero);
        var totalAmount = decimal.Round(taxableAmount + taxAmount, 2, MidpointRounding.AwayFromZero);

        return new FareBreakdownDto(
            schedule.Id,
            seatCount,
            unitOfficialFare,
            baseFareTotal,
            serviceFeeAmount,
            route.TaxPercentage,
            taxAmount,
            totalAmount);
    }

    public PassPricingDto CalculateBusPassPrice(Route route, int durationMonths)
    {
        var validMonths = durationMonths is 1 or 3 or 6 or 12 ? durationMonths : 1;
        decimal discountPct = validMonths switch
        {
            3 => 5m,
            6 => 10m,
            12 => 18m,
            _ => 0m
        };

        var gross = decimal.Round(route.MonthlyPassBasePrice * validMonths, 2, MidpointRounding.AwayFromZero);
        var discountAmount = decimal.Round(gross * (discountPct / 100m), 2, MidpointRounding.AwayFromZero);
        var finalPrice = decimal.Round(gross - discountAmount, 2, MidpointRounding.AwayFromZero);

        return new PassPricingDto(
            route.Id,
            route.SourceCity,
            route.DestinationCity,
            validMonths,
            route.MonthlyPassBasePrice,
            discountPct,
            discountAmount,
            finalPrice);
    }
}
