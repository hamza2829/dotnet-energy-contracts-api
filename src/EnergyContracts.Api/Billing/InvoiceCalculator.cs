using System.Globalization;
using EnergyContracts.Api.Domain;

namespace EnergyContracts.Api.Billing;

// Pure calculation: no database, clock, HTTP, or synthetic price generation.
public sealed class InvoiceCalculator
{
    public InvoicePreview Calculate(
        Contract contract,
        DateOnly month,
        IReadOnlyCollection<ConsumptionReading> consumption,
        IReadOnlyCollection<PricePoint> prices)
    {
        if (month.Day != 1 || month.Year == 9999 && month.Month == 12)
        {
            throw new ArgumentException("Use the first day of a month before December 9999.", nameof(month));
        }

        var start = UtcMidnight(month > contract.StartDate ? month : contract.StartDate);
        var nextMonth = month.AddMonths(1);
        var end = UtcMidnight(nextMonth < contract.EndDate ? nextMonth : contract.EndDate);
        var expectedHours = Math.Max(0, (int)(end - start).TotalHours);
        var readings = consumption.Where(r => r.ContractId == contract.Id
            && r.TimestampUtc >= start && r.TimestampUtc < end).ToList();
        var hourlyPrices = prices.Where(p => p.ContractId == contract.Id)
            .ToDictionary(p => p.TimestampUtc, p => p.PriceCtPerKwh);

        decimal totalCt = 0;
        var missingPrices = 0;
        foreach (var reading in readings)
        {
            decimal price;
            if (contract.TariffType == TariffType.Fixed)
            {
                price = contract.FixedPriceCtPerKwh
                    ?? throw new InvalidOperationException("A fixed contract requires a fixed price.");
            }
            else if (!hourlyPrices.TryGetValue(reading.TimestampUtc, out price))
            {
                missingPrices++;
                continue;
            }

            totalCt += reading.Kwh * price;
        }

        // Round once at the end; a missing price must never silently understate the cost.
        decimal? totalEur = missingPrices == 0
            ? decimal.Round(totalCt / 100m, 2, MidpointRounding.AwayFromZero)
            : null;
        var missingConsumption = expectedHours - readings.Count;
        return new InvoicePreview(contract.Id, month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            readings.Sum(r => r.Kwh), totalEur, expectedHours, readings.Count,
            missingConsumption, missingPrices, missingConsumption == 0 && missingPrices == 0);
    }

    private static DateTime UtcMidnight(DateOnly date) =>
        date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
}
