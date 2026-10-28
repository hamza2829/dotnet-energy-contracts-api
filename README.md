# dotnet-energy-contracts-api

[![CI](https://github.com/hamza2829/dotnet-energy-contracts-api/actions/workflows/ci.yml/badge.svg)](https://github.com/hamza2829/dotnet-energy-contracts-api/actions/workflows/ci.yml)

A small B2B electricity contracts backend for commercial customers. Built as an interview project with C# 12, .NET 8 minimal APIs, EF Core, SQLite, and xUnit. No Docker or external database is needed.

It stores customers, contracts, hourly consumption, and synthetic hourly prices, then calculates an energy-only monthly invoice preview. This is a learning project, not production billing software.

## Domain

| Model | Fields and purpose |
| --- | --- |
| Customer | ID, company name, contact email, UTC creation date |
| Contract | ID, customer ID, Fixed/Dynamic tariff, start/end dates, Draft/Active/Terminated status, annual kWh volume, optional fixed ct/kWh price |
| Consumption reading | Contract ID + UTC hour (composite key), actual kWh |
| Price point | Contract ID + UTC hour (composite key), decimal price in ct/kWh |
| Invoice preview | Month, consumed kWh, EUR cost, expected/recorded/missing hours, completeness flag; calculated rather than stored |

Annual volume is the customer's forecast. Billing uses submitted actual consumption; the forecast does not generate or cap readings.

## Run on Windows

Install the [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0), not just the runtime. Run these commands from the repository root:

```powershell
dotnet restore
dotnet build --configuration Release --no-restore --warnaserror
dotnet run --project src/EnergyContracts.Api --launch-profile http
```

The API listens at `http://localhost:5080`. Open [Swagger UI](http://localhost:5080/swagger/index.html). The launch profile selects Development; Swagger is disabled in other environments. SQLite creates `energy-contracts.db` in the API's working directory (normally `src/EnergyContracts.Api`). Stopping and restarting preserves data.

To use another database file in PowerShell:

```powershell
$env:ConnectionStrings__EnergyContracts = 'Data Source=C:/temp/energy-contracts.db'
dotnet run --project src/EnergyContracts.Api --launch-profile http
```

The containing directory must already exist. Delete only the demo database file when you deliberately want to reset its data.

## Endpoints

| Method | Path | Success |
| --- | --- | --- |
| POST | `/customers` | 201 + customer and Location header |
| GET | `/customers` | 200 + customer list |
| GET | `/customers/{id}` | 200 + customer |
| POST | `/customers/{id}/contracts` | 201 + contract and Location header |
| GET | `/contracts/{id}` | 200 + contract |
| PUT | `/contracts/{id}/terminate` | 200 + terminated contract |
| POST | `/contracts/{id}/consumption` | 200 + addedHours; body is an array of hourly readings |
| GET | `/contracts/{id}/invoice-preview?month=2026-10` | 200 + preview |

Validation returns 400 with Problem Details and field-specific `errors`. Missing records return 404. Overlapping contracts, repeated termination, imports to terminated contracts, and reimporting a stored hour return 409 with a clear explanation. Malformed JSON and unknown enum names return 400. Enums use string names in JSON.

### Example using curl on Windows

In a second PowerShell window, from the repository root, against a fresh database:

```powershell
curl.exe --json "@examples/customer.json" http://localhost:5080/customers
curl.exe --json "@examples/fixed-contract.json" http://localhost:5080/customers/1/contracts
curl.exe --json "@examples/consumption.json" http://localhost:5080/contracts/1/consumption
curl.exe "http://localhost:5080/contracts/1/invoice-preview?month=2026-10"
curl.exe -X PUT http://localhost:5080/contracts/1/terminate
```

On Linux/macOS use `curl` instead of `curl.exe`. Replace IDs with those returned by your requests if the database already contains records. Repeating the consumption request intentionally returns 409.

The fixed preview includes:

```json
{
  "contractId": 1,
  "month": "2026-10",
  "totalConsumptionKwh": 15,
  "totalCostEur": 3.75,
  "expectedHours": 744,
  "consumptionHours": 2,
  "missingConsumptionHours": 742,
  "missingPriceHours": 0,
  "isComplete": false
}
```

For Dynamic, create another customer and submit `examples/dynamic-contract.json`, then import readings and preview using the returned contract ID. That same two-hour consumption costs EUR 1.30 at the synthetic prices of 8 and 10 ct/kWh.

## Rules and invoice calculation

- Dates use `[startDate, endDate)`: start inclusive, end exclusive. Adjacent contracts are allowed. Draft and Active contracts reserve dates for a customer; Terminated contracts do not. Different customers can use the same dates.
- A new contract defaults to Draft, or can explicitly start Active. Termination changes only status, including for a draft. Repeating it is a conflict. Historical previews remain available. There is no activation endpoint or automatic status scheduler.
- Readings must be whole UTC hours (`Z` or `+00:00`) within the contract dates. UTC calendar months have no daylight-saving adjustment. October 2026 has 744 hours. One customer's sites/meters are outside this model.
- Import validates the entire batch before saving. Existing hours are rejected rather than overwritten. A transaction saves readings and synthetic prices together.
- Synthetic Dynamic prices are generated on import and persisted per contract/hour: `8 + (UTC hour % 6) * 2` ct/kWh. This repeatable 8–18 ct pattern is invented data, with no market feed or price-upload endpoint. The calculator itself also supports negative Dynamic prices, covered by a unit test.
- Fixed: sum `kWh * fixed ct/kWh`. Dynamic: match each recorded hour to its stored price and sum `kWh * hourly ct/kWh`. Divide the sum by 100 for EUR, then round **once** to two decimals with `MidpointRounding.AwayFromZero`.
- Missing consumption is not estimated or treated as a measured zero. Cost covers recorded hours only; missing-hour counts and `isComplete: false` make this explicit. No consumption returns EUR 0 and an incomplete preview. Expected hours are clipped to the contract dates.
- A recorded Dynamic hour without a price produces `totalCostEur: null` and a missing-price count, avoiding an understated cost. Normal API imports always create the corresponding synthetic price.
- A preview month must intersect the contract. It uses strict `yyyy-MM` format; December 9999 is excluded to avoid overflowing the next-month calculation.
- Limits keep this demo bounded: up to 10,000 readings per import; 0–1,000,000 kWh per reading; annual volume above 0 and at most 1,000,000,000 kWh; fixed price 0–10,000 ct/kWh. Decimal inputs allow at most four decimal places. Zero consumption and zero fixed price are valid.

## Architecture

```text
HTTP request -> minimal API endpoint -> service -> EF Core DbContext -> SQLite
                                          |
                                    InvoiceCalculator
                                    (plain C# logic)
```

`Domain/` holds the four data models and enums. `Billing/` contains the pure calculator and its result. `Services/` performs validation, overlap/status rules, imports, and database reads/writes. `Endpoints/` only maps routes and service results to HTTP responses. `Data/` defines tables, keys, relationships, and decimal precision.

Dependency injection supplies scoped services and a scoped DbContext per request. The stateless calculator and synthetic generator are singletons. Database calls use async/await and cancellation tokens; the calculation is synchronous because it is in-memory arithmetic. SQLite's underlying provider still performs much I/O synchronously. There is no repository abstraction because EF Core already supplies querying and a unit of work.

SQLite stores decimals as text, so sums and multiplications happen in C# using `decimal`, not SQL or floating point. Date/time range queries use UTC `DateTime` columns; SQLite has limitations around querying `DateTimeOffset`. See [EF Core's SQLite limitations](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations).

### Schema creation and SQL Server

This demo uses **EnsureCreatedAsync**, keeping setup to one command and making isolated test databases easy. It creates a missing schema but does not evolve an existing schema. There are no migrations. After a model change, a disposable local database must be reset. A real service should use reviewed migrations and backups.

To switch to SQL Server:

1. Add `Microsoft.EntityFrameworkCore.SqlServer` at the same 8.0.x version as the SQLite provider. Install the matching `dotnet-ef` tool and add `Microsoft.EntityFrameworkCore.Design` with `PrivateAssets=all` for migration tooling.
2. Change `UseSqlite(...)` in `Program.cs` to `UseSqlServer(...)`, and set `ConnectionStrings__EnergyContracts` to a SQL Server connection string outside source control. The configured `(18,4)` decimal precision matches the API's four-decimal validation.
3. Replace startup `EnsureCreatedAsync` with a migration deployment process, create an initial SQL Server migration (`dotnet ef migrations add InitialCreate --project src/EnergyContracts.Api`), then apply it (`dotnet ef database update --project src/EnergyContracts.Api`). Use a fresh database or an explicit data migration; EnsureCreated databases cannot simply become migration-managed databases.
4. Retest concurrency/overlap enforcement and run integration tests against SQL Server as well. SQLite tests do not prove SQL Server behavior. The current test factory deliberately stays on SQLite.

## Tests and CI

```powershell
dotnet test --configuration Release
```

There are **67 test cases**: 12 pure calculator cases, 19 SQLite-backed service/rule cases, and 36 HTTP integration cases. Theory data rows count as separate cases. The two test projects keep HTTP hosting separate from calculator/rule testing.

Tests cover hand-calculated fixed/dynamic totals, missing hours/prices, no consumption, rounding, credits, leap years, contract boundaries, validation, overlap directions, draft cancellation, repeated termination, the complete HTTP flow, atomic imports, 400/404/409 errors, and Swagger's environment setting. `WebApplicationFactory` hosts the real pipeline with a fresh open in-memory **SQLite relational database per test**, not EF's InMemory provider. No Docker is required.

[CI](https://github.com/hamza2829/dotnet-energy-contracts-api/actions/workflows/ci.yml) runs on `ubuntu-latest`: setup .NET 8, restore, Release build with warnings as errors, and both xUnit suites. A PowerShell script reads TRX results into the run's test summary; the `test-results` artifact retains raw TRX files for 14 days. Summary/upload steps run even when an earlier step fails. Project settings also treat compiler and package warnings as errors locally.

## How I used AI and how I checked it

- AI helped draft the models, services, endpoints, tests, and documentation. **This implementation was assembled with OpenAI Codex.** No Claude Code contribution is claimed without a record of that work.
- Automated checks compare the invoice logic against explicit hand-calculated examples: `15 kWh * 25 ct = EUR 3.75`, and `10 kWh * 8 ct + 5 kWh * 10 ct = EUR 1.30`.
- Tests exercise rounding after the sum, negative prices, missing data, date boundaries, conflicts, and full HTTP requests against SQLite. The project is built with warnings as errors.
- These are checks performed during implementation, not a claim that the project author has already reviewed every line. Before an interview I should run the examples myself, trace a request, and explain or change the code without AI. [Interview notes](docs/interview-notes.md) provide a starting point.

## Honest limits

- Synthetic prices and sample consumption only; no real exchange/market data, meter integration, forecasting, or settlement.
- No authentication, authorization, customer isolation, pagination, rate limiting, or audit trail. Do not expose this API to untrusted users.
- SQLite is convenient for a small local demo. Transactions and unique hourly keys protect ordinary imports; the status concurrency token detects competing terminations. Production concurrency, transient-error retries, and race handling across imports/termination or multiple instances need further work.
- One commercial customer has one non-terminated contract at a time. Real customers may have multiple sites/meters and concurrent supply products. Termination is a simplified status change with no effective timestamp, notice period, or partial-period proration.
- UTC-only hourly intervals. Real energy billing may require local market time, daylight-saving transitions, and sub-hourly readings.
- The result is an energy-only preview in EUR: no VAT, taxes, grid fees, fixed fees, currency conversion, official invoice numbering, PDF invoice, or payment processing. Missing readings are never extrapolated.
- No migrations, deployment, production monitoring, or production security hardening. This is not production code.
