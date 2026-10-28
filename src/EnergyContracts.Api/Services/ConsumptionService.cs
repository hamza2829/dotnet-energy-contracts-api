using EnergyContracts.Api.Contracts;
using EnergyContracts.Api.Data;
using EnergyContracts.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace EnergyContracts.Api.Services;

public sealed class ConsumptionService(EnergyDbContext db, SyntheticPriceGenerator syntheticPrices)
{
    public async Task<ServiceResult<ConsumptionImportResult>> AddAsync(
        int contractId, IReadOnlyList<ConsumptionHourRequest?> requests, CancellationToken ct)
    {
        var contract = await db.Contracts.AsNoTracking().SingleOrDefaultAsync(c => c.Id == contractId, ct);
        if (contract is null) return ServiceResult<ConsumptionImportResult>.NotFound("Contract was not found.");
        if (contract.Status == ContractStatus.Terminated)
            return ServiceResult<ConsumptionImportResult>.Conflict("Cannot add consumption to a terminated contract.");

        var errors = Validate(contract, requests);
        if (errors.Count > 0) return ServiceResult<ConsumptionImportResult>.Invalid(errors);

        var timestamps = requests.Select(r => r!.Timestamp.UtcDateTime).ToArray();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await db.ConsumptionReadings.AnyAsync(r => r.ContractId == contractId && timestamps.Contains(r.TimestampUtc), ct))
            return ServiceResult<ConsumptionImportResult>.Conflict("Consumption for at least one submitted hour already exists.");

        foreach (var request in requests)
        {
            var timestamp = request!.Timestamp.UtcDateTime;
            db.ConsumptionReadings.Add(new ConsumptionReading
            {
                ContractId = contractId, TimestampUtc = timestamp, Kwh = request.Kwh
            });
            if (contract.TariffType == TariffType.Dynamic)
            {
                db.PricePoints.Add(new PricePoint
                {
                    ContractId = contractId, TimestampUtc = timestamp,
                    PriceCtPerKwh = syntheticPrices.PriceCtPerKwh(timestamp)
                });
            }
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ServiceResult<ConsumptionImportResult>.Success(new(requests.Count));
    }

    private static Dictionary<string, string[]> Validate(Contract contract, IReadOnlyList<ConsumptionHourRequest?> requests)
    {
        Dictionary<string, string[]> errors = [];
        if (requests.Count is 0 or > 10_000)
        {
            errors["consumption"] = ["Submit between 1 and 10,000 hourly readings."];
            return errors;
        }

        HashSet<DateTimeOffset> hours = [];
        var start = contract.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = contract.EndDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        for (var i = 0; i < requests.Count; i++)
        {
            var request = requests[i];
            if (request is null)
            {
                errors[$"consumption[{i}]"] = ["Reading must not be null."];
                continue;
            }
            List<string> messages = [];
            if (request.Timestamp.Offset != TimeSpan.Zero || request.Timestamp.Ticks % TimeSpan.TicksPerHour != 0)
                messages.Add("Timestamp must be aligned to a UTC hour (Z or +00:00).");
            if (request.Timestamp.UtcDateTime < start || request.Timestamp.UtcDateTime >= end)
                messages.Add("Timestamp must be within the contract's start-inclusive, end-exclusive dates.");
            if (request.Kwh < 0 || request.Kwh > 1_000_000m || decimal.Round(request.Kwh, 4) != request.Kwh)
                messages.Add("Consumption must be between 0 and 1,000,000 kWh with at most four decimal places.");
            if (!hours.Add(request.Timestamp)) messages.Add("Duplicate hours in the same request are not allowed.");
            if (messages.Count > 0) errors[$"consumption[{i}]"] = messages.ToArray();
        }
        return errors;
    }
}
