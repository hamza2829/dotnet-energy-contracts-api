using EnergyContracts.Api.Domain;

namespace EnergyContracts.Api.Contracts;

public sealed record CreateCustomerRequest(string? CompanyName, string? ContactEmail);

public sealed record CreateContractRequest(
    TariffType TariffType,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal AnnualVolumeKwh,
    decimal? FixedPriceCtPerKwh = null,
    ContractStatus Status = ContractStatus.Draft);

public sealed record ConsumptionHourRequest(DateTimeOffset Timestamp, decimal Kwh);

public sealed record ConsumptionImportResult(int AddedHours);
