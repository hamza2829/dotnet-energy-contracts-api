using System.Text.Json.Serialization;
using EnergyContracts.Api.Billing;
using EnergyContracts.Api.Data;
using EnergyContracts.Api.Domain;
using EnergyContracts.Api.Endpoints;
using EnergyContracts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<EnergyDbContext>(options => options.UseSqlite(
    builder.Configuration.GetConnectionString("EnergyContracts") ?? "Data Source=energy-contracts.db"));
builder.Services.AddScoped<CustomerService>();
builder.Services.AddScoped<ContractService>();
builder.Services.AddScoped<ConsumptionService>();
builder.Services.AddScoped<InvoiceService>();
builder.Services.AddSingleton<InvoiceCalculator>();
builder.Services.AddSingleton<SyntheticPriceGenerator>();
builder.Services.AddProblemDetails();
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Swashbuckle 6 needs explicit schemas for minimal API string enums.
    options.MapType<TariffType>(() => new OpenApiSchema
    {
        Type = "string",
        Enum = Enum.GetNames<TariffType>().Select(name => (IOpenApiAny)new OpenApiString(name)).ToList()
    });
    options.MapType<ContractStatus>(() => new OpenApiSchema
    {
        Type = "string",
        Enum = Enum.GetNames<ContractStatus>().Select(name => (IOpenApiAny)new OpenApiString(name)).ToList()
    });
});

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<EnergyDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.MapCustomerEndpoints();
app.MapContractEndpoints();
app.Run();

public partial class Program;
