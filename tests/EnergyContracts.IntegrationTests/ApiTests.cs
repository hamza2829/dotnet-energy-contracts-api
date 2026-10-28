using System.Net;
using System.Net.Http.Json;
using System.Text;
using EnergyContracts.Api.Billing;
using EnergyContracts.Api.Domain;

namespace EnergyContracts.IntegrationTests;

public sealed class ApiTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public ApiTests() => _client = _factory.CreateClient();

    [Fact]
    public async Task Can_create_list_and_get_a_customer()
    {
        var response = await _client.PostAsJsonAsync("/customers", new { companyName = "  Example GmbH  ", contactEmail = "office@example.com" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var customer = await response.Content.ReadFromJsonAsync<Customer>();
        Assert.NotNull(customer);
        Assert.Equal("Example GmbH", customer.CompanyName);
        Assert.NotEqual(default, customer.CreatedAtUtc);
        Assert.Equal($"/customers/{customer.Id}", response.Headers.Location!.ToString());
        var fetched = await _client.GetFromJsonAsync<Customer>(response.Headers.Location);
        Assert.Equal(customer.ContactEmail, fetched!.ContactEmail);
        var customers = await _client.GetFromJsonAsync<List<Customer>>("/customers");
        Assert.Single(customers!);
    }

    [Fact]
    public async Task Fixed_tariff_flow_returns_a_hand_calculated_preview()
    {
        var id = await CreateContractAsync();
        var imported = await AddHoursAsync(id, 10m, 5m);
        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        var preview = await _client.GetFromJsonAsync<InvoicePreview>($"/contracts/{id}/invoice-preview?month=2026-10");
        Assert.NotNull(preview);
        Assert.Equal(3.75m, preview.TotalCostEur);
        Assert.Equal(15m, preview.TotalConsumptionKwh);
        Assert.Equal(742, preview.MissingConsumptionHours);
        Assert.False(preview.IsComplete);
        var contract = await _client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/contracts/{id}");
        Assert.Equal("Fixed", contract.GetProperty("tariffType").GetString());
    }

    [Fact]
    public async Task Dynamic_tariff_flow_uses_stored_synthetic_hourly_prices()
    {
        var id = await CreateContractAsync("Dynamic");
        (await AddHoursAsync(id, 10m, 5m)).EnsureSuccessStatusCode();
        var preview = await _client.GetFromJsonAsync<InvoicePreview>($"/contracts/{id}/invoice-preview?month=2026-10");
        // 10 kWh * 8 ct + 5 kWh * 10 ct = 130 ct.
        Assert.Equal(1.30m, preview!.TotalCostEur);
        Assert.Equal(0, preview.MissingPriceHours);
    }

    [Theory]
    [InlineData("", "office@example.com", "companyName")]
    [InlineData("Example GmbH", "invalid", "contactEmail")]
    [InlineData("Example GmbH", "", "contactEmail")]
    public async Task Invalid_customer_returns_clear_validation_errors(string name, string email, string field)
    {
        var response = await _client.PostAsJsonAsync("/customers", new { companyName = name, contactEmail = email });
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, field);
    }

    [Theory]
    [InlineData("/customers/999")]
    [InlineData("/contracts/999")]
    [InlineData("/contracts/999/invoice-preview?month=2026-10")]
    public async Task Missing_items_return_404(string path)
    {
        await AssertProblemAsync(await _client.GetAsync(path), HttpStatusCode.NotFound, "not found");
    }

    [Fact]
    public async Task Cannot_create_a_contract_for_a_missing_customer()
    {
        await AssertProblemAsync(await _client.PostAsJsonAsync("/customers/999/contracts", ContractBody()),
            HttpStatusCode.NotFound, "Customer");
    }

    [Fact]
    public async Task Invalid_dates_and_volume_return_400()
    {
        var customer = await CreateCustomerAsync();
        var response = await _client.PostAsJsonAsync($"/customers/{customer}/contracts", new
        {
            tariffType = "Fixed", startDate = "2026-10-01", endDate = "2026-09-01",
            annualVolumeKwh = 0, fixedPriceCtPerKwh = 25m
        });
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "endDate");
        Assert.Contains("annualVolumeKwh", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Overlapping_contract_returns_409()
    {
        var customer = await CreateCustomerAsync();
        (await _client.PostAsJsonAsync($"/customers/{customer}/contracts", ContractBody())).EnsureSuccessStatusCode();
        await AssertProblemAsync(await _client.PostAsJsonAsync($"/customers/{customer}/contracts", ContractBody()),
            HttpStatusCode.Conflict, "overlapping");
    }

    [Fact]
    public async Task Terminating_twice_returns_409_and_draft_termination_is_allowed()
    {
        var id = await CreateContractAsync();
        var first = await _client.PutAsync($"/contracts/{id}/terminate", null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Contains("Terminated", await first.Content.ReadAsStringAsync());
        await AssertProblemAsync(await _client.PutAsync($"/contracts/{id}/terminate", null),
            HttpStatusCode.Conflict, "already terminated");
    }

    [Fact]
    public async Task Terminating_a_missing_contract_returns_404()
    {
        await AssertProblemAsync(await _client.PutAsync("/contracts/999/terminate", null), HttpStatusCode.NotFound, "Contract");
    }

    [Fact]
    public async Task Consumption_for_a_missing_contract_returns_404()
    {
        await AssertProblemAsync(await AddHoursAsync(999, 1m), HttpStatusCode.NotFound, "Contract");
    }

    [Fact]
    public async Task Importing_an_existing_hour_returns_409_without_partial_insert()
    {
        var id = await CreateContractAsync();
        (await AddHoursAsync(id, 10m)).EnsureSuccessStatusCode();
        await AssertProblemAsync(await AddHoursAsync(id, 20m, 30m), HttpStatusCode.Conflict, "already exists");
        var preview = await _client.GetFromJsonAsync<InvoicePreview>($"/contracts/{id}/invoice-preview?month=2026-10");
        Assert.Equal(1, preview!.ConsumptionHours);
        Assert.Equal(10m, preview.TotalConsumptionKwh);
    }

    [Theory]
    [InlineData("2026-10-01T00:30:00Z", 1)]
    [InlineData("2026-10-01T02:00:00+02:00", 1)]
    [InlineData("2026-11-01T00:00:00Z", 1)]
    [InlineData("2026-10-01T00:00:00Z", -1)]
    public async Task Invalid_consumption_is_rejected_atomically(string timestamp, decimal kwh)
    {
        var id = await CreateContractAsync();
        var response = await _client.PostAsJsonAsync($"/contracts/{id}/consumption", new[]
        {
            new { timestamp = "2026-10-01T03:00:00Z", kwh = 10m }, new { timestamp, kwh }
        });
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "consumption[1]");
        var preview = await _client.GetFromJsonAsync<InvoicePreview>($"/contracts/{id}/invoice-preview?month=2026-10");
        Assert.Equal(0, preview!.ConsumptionHours);
    }

    [Fact]
    public async Task Duplicate_hours_in_a_batch_return_400()
    {
        var id = await CreateContractAsync();
        var reading = new { timestamp = "2026-10-01T00:00:00Z", kwh = 1m };
        await AssertProblemAsync(await _client.PostAsJsonAsync($"/contracts/{id}/consumption", new[] { reading, reading }),
            HttpStatusCode.BadRequest, "Duplicate hours");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[null]")]
    public async Task Empty_or_null_readings_return_400(string body)
    {
        var id = await CreateContractAsync();
        await AssertProblemAsync(await _client.PostAsync($"/contracts/{id}/consumption",
            new StringContent(body, Encoding.UTF8, "application/json")), HttpStatusCode.BadRequest, "consumption");
    }

    [Fact]
    public async Task Cannot_add_consumption_after_termination()
    {
        var id = await CreateContractAsync();
        (await _client.PutAsync($"/contracts/{id}/terminate", null)).EnsureSuccessStatusCode();
        await AssertProblemAsync(await AddHoursAsync(id, 1m), HttpStatusCode.Conflict, "terminated");
    }

    [Theory]
    [InlineData("")]
    [InlineData("2026-13")]
    [InlineData("2026-1")]
    [InlineData("2026-10-01")]
    [InlineData("9999-12")]
    [InlineData("2027-01")]
    public async Task Invalid_or_outside_month_returns_400(string month)
    {
        var id = await CreateContractAsync();
        await AssertProblemAsync(await _client.GetAsync($"/contracts/{id}/invoice-preview?month={month}"),
            HttpStatusCode.BadRequest, "month");
    }

    [Fact]
    public async Task A_month_without_consumption_returns_zero_and_missing_hours()
    {
        var id = await CreateContractAsync();
        var preview = await _client.GetFromJsonAsync<InvoicePreview>($"/contracts/{id}/invoice-preview?month=2026-10");
        Assert.Equal(0m, preview!.TotalCostEur);
        Assert.Equal(744, preview.MissingConsumptionHours);
        Assert.False(preview.IsComplete);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    public async Task Malformed_or_null_JSON_returns_400(string json)
    {
        await AssertProblemAsync(await _client.PostAsync("/customers", new StringContent(json, Encoding.UTF8, "application/json")),
            HttpStatusCode.BadRequest, "400");
    }

    [Fact]
    public async Task Unknown_tariff_returns_400()
    {
        var customer = await CreateCustomerAsync();
        var response = await _client.PostAsJsonAsync($"/customers/{customer}/contracts", ContractBody("Unknown"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_is_available_in_development()
    {
        using var development = _factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Development"));
        using var client = development.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/swagger/index.html")).StatusCode);
        var schema = await client.GetStringAsync("/swagger/v1/swagger.json");
        Assert.Contains("/contracts/{id}/invoice-preview", schema);
    }

    [Fact]
    public async Task Swagger_is_disabled_outside_development()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/swagger/index.html")).StatusCode);
    }

    private async Task<int> CreateCustomerAsync()
    {
        var response = await _client.PostAsJsonAsync("/customers", new { companyName = "Test GmbH", contactEmail = "office@example.com" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Customer>())!.Id;
    }

    private async Task<int> CreateContractAsync(string tariff = "Fixed")
    {
        var customer = await CreateCustomerAsync();
        var response = await _client.PostAsJsonAsync($"/customers/{customer}/contracts", ContractBody(tariff));
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return json.GetProperty("id").GetInt32();
    }

    private static object ContractBody(string tariff = "Fixed") => new
    {
        tariffType = tariff, startDate = "2026-10-01", endDate = "2026-11-01",
        annualVolumeKwh = 10000m, fixedPriceCtPerKwh = tariff == "Dynamic" ? (decimal?)null : 25m
    };

    private Task<HttpResponseMessage> AddHoursAsync(int id, params decimal[] kwh) =>
        _client.PostAsJsonAsync($"/contracts/{id}/consumption", kwh.Select((value, i) => new
        {
            timestamp = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero).AddHours(i), kwh = value
        }));

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string text)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains(text, await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
