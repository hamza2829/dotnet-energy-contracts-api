namespace EnergyContracts.Api.Domain;

public enum TariffType { Fixed, Dynamic }
public enum ContractStatus { Draft, Active, Terminated }

public sealed class Contract
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public TariffType TariffType { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public ContractStatus Status { get; set; } = ContractStatus.Draft;
    public decimal AnnualVolumeKwh { get; set; }
    public decimal? FixedPriceCtPerKwh { get; set; }
}
