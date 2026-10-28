using EnergyContracts.Api.Billing;
using EnergyContracts.Api.Contracts;
using EnergyContracts.Api.Domain;
using EnergyContracts.Api.Services;

namespace EnergyContracts.Api.Endpoints;

public static class ContractEndpoints
{
    public static void MapContractEndpoints(this WebApplication app)
    {
        app.MapPost("/customers/{id:int}/contracts", async (
            int id, CreateContractRequest request, ContractService service, CancellationToken ct) =>
            (await service.CreateAsync(id, request, ct)).ToHttp(contract => Results.Created($"/contracts/{contract.Id}", contract)))
            .WithTags("Contracts").Produces<Contract>(201).ProducesValidationProblem().ProducesProblem(404).ProducesProblem(409);

        app.MapGet("/contracts/{id:int}", async (int id, ContractService service, CancellationToken ct) =>
        {
            var contract = await service.GetAsync(id, ct);
            return contract is null
                ? Results.Problem(statusCode: 404, title: "Not found", detail: "Contract was not found.")
                : Results.Ok(contract);
        }).WithTags("Contracts").Produces<Contract>().ProducesProblem(404);

        app.MapPut("/contracts/{id:int}/terminate", async (int id, ContractService service, CancellationToken ct) =>
            (await service.TerminateAsync(id, ct)).ToHttp())
            .WithTags("Contracts").Produces<Contract>().ProducesProblem(404).ProducesProblem(409);

        app.MapPost("/contracts/{id:int}/consumption", async (
            int id, List<ConsumptionHourRequest?> request, ConsumptionService service, CancellationToken ct) =>
            (await service.AddAsync(id, request, ct)).ToHttp())
            .WithTags("Billing").Produces<ConsumptionImportResult>().ProducesValidationProblem().ProducesProblem(404).ProducesProblem(409);

        app.MapGet("/contracts/{id:int}/invoice-preview", async (
            int id, string? month, InvoiceService service, CancellationToken ct) =>
            (await service.PreviewAsync(id, month, ct)).ToHttp())
            .WithTags("Billing").Produces<InvoicePreview>().ProducesValidationProblem().ProducesProblem(404);
    }
}
