namespace EnergyContracts.Api.Domain;

public sealed class Customer
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = "";
    public string ContactEmail { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
