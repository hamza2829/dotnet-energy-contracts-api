namespace EnergyContracts.Api.Billing;

public sealed record InvoicePreview(
    int ContractId,
    string Month,
    decimal TotalConsumptionKwh,
    decimal? TotalCostEur,
    int ExpectedHours,
    int ConsumptionHours,
    int MissingConsumptionHours,
    int MissingPriceHours,
    bool IsComplete);
