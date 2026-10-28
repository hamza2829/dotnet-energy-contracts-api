using System.ComponentModel.DataAnnotations;
using EnergyContracts.Api.Contracts;
using EnergyContracts.Api.Data;
using EnergyContracts.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace EnergyContracts.Api.Services;

public sealed class CustomerService(EnergyDbContext db)
{
    public Task<List<Customer>> GetAllAsync(CancellationToken ct) =>
        db.Customers.AsNoTracking().OrderBy(c => c.Id).ToListAsync(ct);

    public Task<Customer?> GetAsync(int id, CancellationToken ct) =>
        db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);

    public async Task<ServiceResult<Customer>> CreateAsync(CreateCustomerRequest request, CancellationToken ct)
    {
        var name = request.CompanyName?.Trim();
        var email = request.ContactEmail?.Trim();
        Dictionary<string, string[]> errors = [];
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            errors["companyName"] = ["Company name is required and must be at most 200 characters."];
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254 || !new EmailAddressAttribute().IsValid(email))
            errors["contactEmail"] = ["A valid contact email of at most 254 characters is required."];
        if (errors.Count > 0) return ServiceResult<Customer>.Invalid(errors);

        var customer = new Customer { CompanyName = name!, ContactEmail = email! };
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);
        return ServiceResult<Customer>.Success(customer);
    }
}
