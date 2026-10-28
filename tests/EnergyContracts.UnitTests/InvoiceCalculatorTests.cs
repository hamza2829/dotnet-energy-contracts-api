using EnergyContracts.Api.Billing;
using EnergyContracts.Api.Domain;

namespace EnergyContracts.UnitTests;

public sealed class InvoiceCalculatorTests
{
    private static readonly DateOnly October = new(2026, 10, 1);
    private readonly InvoiceCalculator _calculator = new();

    [Fact]
    public void Fixed_tariff_multiplies_consumption_by_fixed_price()
    {
        var result = Preview(Fixed(), [Reading(0, 10m), Reading(1, 5m)]);
        Assert.Equal(3.75m, result.TotalCostEur);
        Assert.Equal(15m, result.TotalConsumptionKwh);
    }

    [Fact]
    public void Dynamic_tariff_matches_each_hour_to_its_price()
    {
        var result = Preview(Dynamic(), [Reading(0, 10m), Reading(1, 5m)],
            [Price(1, 30m), Price(0, 10m)]);
        Assert.Equal(2.50m, result.TotalCostEur);
    }

    [Fact]
    public void Missing_consumption_hours_are_reported_as_partial()
    {
        var result = Preview(Fixed(), [Reading(0, 1m)]);
        Assert.Equal(744, result.ExpectedHours);
        Assert.Equal(743, result.MissingConsumptionHours);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public void No_consumption_costs_zero_and_is_incomplete()
    {
        var result = Preview(Dynamic(), []);
        Assert.Equal(0m, result.TotalCostEur);
        Assert.Equal(0m, result.TotalConsumptionKwh);
        Assert.Equal(744, result.MissingConsumptionHours);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public void Half_a_cent_rounds_away_from_zero()
    {
        var contract = Fixed();
        contract.FixedPriceCtPerKwh = 0.5m;
        Assert.Equal(0.01m, Preview(contract, [Reading(0, 1m)]).TotalCostEur);
    }

    [Fact]
    public void Rounding_happens_after_summing_all_hours()
    {
        var contract = Fixed();
        contract.FixedPriceCtPerKwh = 0.4m;
        Assert.Equal(0.01m, Preview(contract, [Reading(0, 1m), Reading(1, 1m)]).TotalCostEur);
    }

    [Fact]
    public void Missing_dynamic_price_withholds_the_total()
    {
        var result = Preview(Dynamic(), [Reading(0, 1m), Reading(1, 1m)], [Price(0, 10m)]);
        Assert.Null(result.TotalCostEur);
        Assert.Equal(1, result.MissingPriceHours);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public void Negative_dynamic_prices_produce_a_credit()
    {
        Assert.Equal(-0.50m, Preview(Dynamic(), [Reading(0, 5m)], [Price(0, -10m)]).TotalCostEur);
    }

    [Fact]
    public void Readings_outside_the_month_or_for_another_contract_are_ignored()
    {
        var outside = Reading(0, 999m);
        outside.TimestampUtc = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var other = Reading(1, 999m);
        other.ContractId = 2;
        Assert.Equal(0.25m, Preview(Fixed(), [Reading(0, 1m), outside, other]).TotalCostEur);
    }

    [Fact]
    public void Expected_hours_are_clipped_to_contract_dates_with_exclusive_end()
    {
        var contract = Fixed();
        contract.EndDate = October.AddDays(1);
        var result = Preview(contract, [Reading(0, 1m), Reading(24, 100m)]);
        Assert.Equal(24, result.ExpectedHours);
        Assert.Equal(1, result.ConsumptionHours);
        Assert.Equal(0.25m, result.TotalCostEur);
    }

    [Fact]
    public void All_recorded_hours_make_a_complete_preview()
    {
        var result = Preview(Fixed(), Enumerable.Range(0, 744).Select(h => Reading(h, 1m)).ToArray());
        Assert.True(result.IsComplete);
        Assert.Equal(186m, result.TotalCostEur);
    }

    [Fact]
    public void Leap_year_February_has_696_hours()
    {
        var contract = Fixed();
        contract.StartDate = new DateOnly(2024, 1, 1);
        var result = _calculator.Calculate(contract, new DateOnly(2024, 2, 1), [], []);
        Assert.Equal(696, result.ExpectedHours);
    }

    private InvoicePreview Preview(Contract contract, ConsumptionReading[] readings, PricePoint[]? prices = null) =>
        _calculator.Calculate(contract, October, readings, prices ?? []);

    private static Contract Fixed() => new()
    {
        Id = 1, CustomerId = 1, TariffType = TariffType.Fixed,
        StartDate = October, EndDate = new DateOnly(2027, 1, 1),
        AnnualVolumeKwh = 10000m, FixedPriceCtPerKwh = 25m
    };

    private static Contract Dynamic()
    {
        var contract = Fixed();
        contract.TariffType = TariffType.Dynamic;
        contract.FixedPriceCtPerKwh = null;
        return contract;
    }

    private static ConsumptionReading Reading(int hour, decimal kwh) => new()
    {
        ContractId = 1, TimestampUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(hour), Kwh = kwh
    };

    private static PricePoint Price(int hour, decimal price) => new()
    {
        ContractId = 1, TimestampUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(hour),
        PriceCtPerKwh = price
    };
}
