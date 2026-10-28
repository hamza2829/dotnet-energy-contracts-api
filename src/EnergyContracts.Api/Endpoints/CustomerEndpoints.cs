using EnergyContracts.Api.Contracts;
using EnergyContracts.Api.Domain;
using EnergyContracts.Api.Services;

namespace EnergyContracts.Api.Endpoints;

public static class CustomerEndpoints
{
    public static void MapCustomerEndpoints(this WebApplication app)
    {
        app.MapPost("/customers", async (CreateCustomerRequest request, CustomerService service, CancellationToken ct) =>
            (await service.CreateAsync(request, ct)).ToHttp(customer => Results.Created($"/customers/{customer.Id}", customer)))
            .WithTags("Customers").Produces<Customer>(201).ProducesValidationProblem();

        app.MapGet("/customers", async (CustomerService service, CancellationToken ct) =>
            Results.Ok(await service.GetAllAsync(ct)))
            .WithTags("Customers").Produces<List<Customer>>();

        app.MapGet("/customers/{id:int}", async (int id, CustomerService service, CancellationToken ct) =>
        {
            var customer = await service.GetAsync(id, ct);
            return customer is null
                ? Results.Problem(statusCode: 404, title: "Not found", detail: "Customer was not found.")
                : Results.Ok(customer);
        }).WithTags("Customers").Produces<Customer>().ProducesProblem(404);
    }
}
