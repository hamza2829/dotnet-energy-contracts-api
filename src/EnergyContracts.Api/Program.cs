using System.Text.Json.Serialization;
using EnergyContracts.Api.Billing;
using EnergyContracts.Api.Data;
using EnergyContracts.Api.Endpoints;
using EnergyContracts.Api.Services;
using Microsoft.EntityFrameworkCore;

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
builder.Services.AddSwaggerGen(options => options.UseInlineDefinitionsForEnums());

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
