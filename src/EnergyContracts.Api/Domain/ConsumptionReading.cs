namespace EnergyContracts.Api.Domain;

public sealed class ConsumptionReading
{
    public int ContractId { get; set; }
    public DateTime TimestampUtc { get; set; }
    public decimal Kwh { get; set; }
}
