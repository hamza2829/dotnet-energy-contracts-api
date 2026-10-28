using System.Globalization;
using EnergyContracts.Api.Billing;
using EnergyContracts.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EnergyContracts.Api.Services;

public sealed class InvoiceService(EnergyDbContext db, InvoiceCalculator calculator)
{
    public async Task<ServiceResult<InvoicePreview>> PreviewAsync(int contractId, string? month, CancellationToken ct)
    {
        if (month is null || month.Length != 7 || !DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var firstDay)
            || firstDay == new DateOnly(9999, 12, 1))
            return ServiceResult<InvoicePreview>.Invalid(new() { ["month"] = ["Use yyyy-MM, from 0001-01 to 9999-11."] });

        var contract = await db.Contracts.AsNoTracking().SingleOrDefaultAsync(c => c.Id == contractId, ct);
        if (contract is null) return ServiceResult<InvoicePreview>.NotFound("Contract was not found.");
        var nextMonth = firstDay.AddMonths(1);
        if (firstDay >= contract.EndDate || nextMonth <= contract.StartDate)
            return ServiceResult<InvoicePreview>.Invalid(new() { ["month"] = ["Month must intersect the contract's dates."] });

        var start = firstDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = nextMonth.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var consumption = await db.ConsumptionReadings.AsNoTracking().Where(r => r.ContractId == contractId
            && r.TimestampUtc >= start && r.TimestampUtc < end).ToListAsync(ct);
        var prices = await db.PricePoints.AsNoTracking().Where(p => p.ContractId == contractId
            && p.TimestampUtc >= start && p.TimestampUtc < end).ToListAsync(ct);
        return ServiceResult<InvoicePreview>.Success(calculator.Calculate(contract, firstDay, consumption, prices));
    }
}
