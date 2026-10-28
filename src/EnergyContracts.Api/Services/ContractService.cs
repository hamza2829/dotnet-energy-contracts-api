using EnergyContracts.Api.Contracts;
using EnergyContracts.Api.Data;
using EnergyContracts.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace EnergyContracts.Api.Services;

public sealed class ContractService(EnergyDbContext db)
{
    public Task<Contract?> GetAsync(int id, CancellationToken ct) =>
        db.Contracts.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);

    public async Task<ServiceResult<Contract>> CreateAsync(int customerId, CreateContractRequest request, CancellationToken ct)
    {
        if (!await db.Customers.AnyAsync(c => c.Id == customerId, ct))
            return ServiceResult<Contract>.NotFound("Customer was not found.");

        var errors = Validate(request);
        if (errors.Count > 0) return ServiceResult<Contract>.Invalid(errors);

        // Keep the overlap check and insert in the same SQLite write transaction.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var overlaps = await db.Contracts.AnyAsync(c => c.CustomerId == customerId
            && c.Status != ContractStatus.Terminated
            && c.StartDate < request.EndDate && request.StartDate < c.EndDate, ct);
        if (overlaps) return ServiceResult<Contract>.Conflict("The customer already has an overlapping contract.");

        var contract = new Contract
        {
            CustomerId = customerId, TariffType = request.TariffType,
            StartDate = request.StartDate, EndDate = request.EndDate,
            AnnualVolumeKwh = request.AnnualVolumeKwh,
            FixedPriceCtPerKwh = request.FixedPriceCtPerKwh, Status = request.Status
        };
        db.Contracts.Add(contract);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ServiceResult<Contract>.Success(contract);
    }

    public async Task<ServiceResult<Contract>> TerminateAsync(int id, CancellationToken ct)
    {
        var contract = await db.Contracts.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (contract is null) return ServiceResult<Contract>.NotFound("Contract was not found.");
        if (contract.Status == ContractStatus.Terminated)
            return ServiceResult<Contract>.Conflict("Contract is already terminated.");

        // A draft can be cancelled too. Dates and historical readings stay available for previews.
        contract.Status = ContractStatus.Terminated;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult<Contract>.Conflict("Contract was changed by another request. Reload it and retry.");
        }
        return ServiceResult<Contract>.Success(contract);
    }

    private static Dictionary<string, string[]> Validate(CreateContractRequest request)
    {
        Dictionary<string, string[]> errors = [];
        if (request.StartDate == default)
            errors["startDate"] = ["Start date is required."];
        if (request.EndDate <= request.StartDate)
            errors["endDate"] = ["End date must be after start date (the end is exclusive)."];
        if (request.AnnualVolumeKwh <= 0 || request.AnnualVolumeKwh > 1_000_000_000m)
            errors["annualVolumeKwh"] = ["Annual volume must be above 0 and at most 1,000,000,000 kWh."];
        if (!Enum.IsDefined(request.TariffType))
            errors["tariffType"] = ["Tariff type must be Fixed or Dynamic."];
        if (request.Status is not (ContractStatus.Draft or ContractStatus.Active))
            errors["status"] = ["A new contract must be Draft or Active."];
        if (request.TariffType == TariffType.Fixed
            && (request.FixedPriceCtPerKwh is null or < 0 or > 10_000m))
            errors["fixedPriceCtPerKwh"] = ["Fixed tariffs require a price between 0 and 10,000 ct/kWh."];
        if (request.TariffType == TariffType.Dynamic && request.FixedPriceCtPerKwh is not null)
            errors["fixedPriceCtPerKwh"] = ["Dynamic tariffs must not specify a fixed price."];
        if (decimal.Round(request.AnnualVolumeKwh, 4) != request.AnnualVolumeKwh)
            errors["annualVolumeKwh"] = ["Use at most four decimal places."];
        if (request.FixedPriceCtPerKwh is { } price && decimal.Round(price, 4) != price)
            errors["fixedPriceCtPerKwh"] = ["Use at most four decimal places."];
        return errors;
    }
}
