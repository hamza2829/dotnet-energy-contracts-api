using EnergyContracts.Api.Contracts;
using EnergyContracts.Api.Data;
using EnergyContracts.Api.Domain;
using EnergyContracts.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EnergyContracts.UnitTests;

public sealed class ContractServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly EnergyDbContext _db;
    private readonly ContractService _service;
    private static readonly CreateContractRequest Valid = new(TariffType.Fixed,
        new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1), 10000m, 25m);

    public ContractServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = new EnergyDbContext(new DbContextOptionsBuilder<EnergyDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _db.Customers.Add(new Customer { Id = 1, CompanyName = "Test GmbH", ContactEmail = "test@example.com" });
        _db.SaveChanges();
        _service = new ContractService(_db);
    }

    [Fact]
    public async Task Creates_a_draft_by_default()
    {
        var result = await _service.CreateAsync(1, Valid, default);
        Assert.Null(result.Error);
        Assert.Equal(ContractStatus.Draft, result.Value!.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Volume_must_be_positive(int volume)
    {
        var result = await _service.CreateAsync(1, Valid with { AnnualVolumeKwh = volume }, default);
        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        Assert.Contains("annualVolumeKwh", result.Error.ValidationErrors!.Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task End_must_be_after_start(int offset)
    {
        var result = await _service.CreateAsync(1, Valid with { EndDate = Valid.StartDate.AddDays(offset) }, default);
        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
    }

    [Fact]
    public async Task Fixed_tariff_requires_a_price()
    {
        var result = await _service.CreateAsync(1, Valid with { FixedPriceCtPerKwh = null }, default);
        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
    }

    [Fact]
    public async Task Dynamic_tariff_rejects_a_fixed_price()
    {
        var result = await _service.CreateAsync(1, Valid with { TariffType = TariffType.Dynamic }, default);
        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
    }

    [Fact]
    public async Task Cannot_create_a_terminated_contract()
    {
        var result = await _service.CreateAsync(1, Valid with { Status = ContractStatus.Terminated }, default);
        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
    }

    [Fact]
    public async Task Cannot_create_for_a_missing_customer()
    {
        var result = await _service.CreateAsync(999, Valid, default);
        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 40)]
    [InlineData(5, 20)]
    [InlineData(-5, 5)]
    public async Task Overlap_is_rejected_including_containment(int startOffset, int endOffset)
    {
        await _service.CreateAsync(1, Valid, default);
        var result = await _service.CreateAsync(1, Valid with
        {
            StartDate = Valid.StartDate.AddDays(startOffset), EndDate = Valid.StartDate.AddDays(endOffset)
        }, default);
        Assert.Equal(ErrorKind.Conflict, result.Error!.Kind);
        Assert.Equal(1, await _db.Contracts.CountAsync());
    }

    [Fact]
    public async Task Adjacent_contracts_do_not_overlap()
    {
        await _service.CreateAsync(1, Valid, default);
        var result = await _service.CreateAsync(1, Valid with
        {
            StartDate = Valid.EndDate, EndDate = Valid.EndDate.AddMonths(1)
        }, default);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Another_customer_can_have_the_same_dates()
    {
        _db.Customers.Add(new Customer { Id = 2, CompanyName = "Other", ContactEmail = "other@example.com" });
        await _db.SaveChangesAsync();
        await _service.CreateAsync(1, Valid, default);
        Assert.Null((await _service.CreateAsync(2, Valid, default)).Error);
    }

    [Theory]
    [InlineData(ContractStatus.Draft)]
    [InlineData(ContractStatus.Active)]
    public async Task Can_terminate_a_draft_or_active_contract(ContractStatus status)
    {
        var created = await _service.CreateAsync(1, Valid with { Status = status }, default);
        var result = await _service.TerminateAsync(created.Value!.Id, default);
        Assert.Null(result.Error);
        Assert.Equal(ContractStatus.Terminated, result.Value!.Status);
        Assert.Equal(Valid.EndDate, result.Value.EndDate);
    }

    [Fact]
    public async Task Terminating_twice_returns_conflict()
    {
        var created = await _service.CreateAsync(1, Valid, default);
        await _service.TerminateAsync(created.Value!.Id, default);
        Assert.Equal(ErrorKind.Conflict, (await _service.TerminateAsync(created.Value.Id, default)).Error!.Kind);
    }

    [Fact]
    public async Task Terminated_contract_does_not_reserve_the_dates()
    {
        var created = await _service.CreateAsync(1, Valid, default);
        await _service.TerminateAsync(created.Value!.Id, default);
        Assert.Null((await _service.CreateAsync(1, Valid, default)).Error);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
