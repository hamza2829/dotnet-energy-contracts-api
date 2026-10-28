namespace EnergyContracts.Api.Services;

// Repeatable demonstration data, unrelated to any real market feed.
public sealed class SyntheticPriceGenerator
{
    public decimal PriceCtPerKwh(DateTime timestampUtc) => 8m + timestampUtc.Hour % 6 * 2m;
}
