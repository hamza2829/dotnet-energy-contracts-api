namespace EnergyContracts.Api.Domain;

public sealed class PricePoint
{
    public int ContractId { get; set; }
    public DateTime TimestampUtc { get; set; }
    public decimal PriceCtPerKwh { get; set; }
}
