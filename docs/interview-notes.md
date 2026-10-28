# Interview walkthrough

## Trace one request

Start at `Program.cs`: it configures dependency injection, JSON string enums, errors, SQLite, development-only Swagger, schema creation, and the routes.

For `POST /customers/1/contracts`, `ContractEndpoints` passes the request to `ContractService`. The service checks that the customer exists, validates dates/volume/tariff, checks for overlapping non-terminated contracts, and saves in a transaction. It returns either a value or a service error. `ResultMapping` translates that into 201, 400, 404, or 409.

For consumption, the service validates every entry first. A composite database key makes each contract/hour unique. Dynamic imports save a synthetic price in the same transaction. Existing hours return a conflict so accidental retries cannot double bill.

For a preview, `InvoiceService` parses the month, loads matching readings/prices, and calls `InvoiceCalculator`. The calculator knows nothing about EF Core or HTTP, so its arithmetic can be tested directly.

## Explain the calculation on paper

Fixed: 10 kWh plus 5 kWh at 25 ct/kWh is 375 ct, or EUR 3.75.

Dynamic: 10 kWh at 8 ct plus 5 kWh at 10 ct is 130 ct, or EUR 1.30. The price must match the consumption timestamp, not the position in a list.

Rounding: two hourly costs of 0.4 ct sum to 0.8 ct = EUR 0.008, rounded to EUR 0.01. Rounding each hour first would incorrectly give zero. `decimal` avoids the binary representation errors associated with monetary `double` arithmetic.

Missing hours: two readings in October 2026 leave 742 missing UTC hours. The preview reports the recorded cost with `isComplete = false`. A missing price for a recorded hour withholds the total entirely.

## Explain the design choices

| Choice | Reason and tradeoff |
| --- | --- |
| Minimal API | A small route set stays readable; business rules live in services, not route handlers. |
| One API project with folders | Keeps navigation simple; separate assemblies would add ceremony at this size. |
| EF Core directly | DbContext handles queries and save operations; a second repository layer adds little here. |
| SQLite | Real relational constraints without an external server; production provider behavior still needs testing. |
| EnsureCreated | Easy demo/test startup; schema evolution needs migrations later. |
| Decimal | Exact base-10 arithmetic for kWh/prices; SQLite arithmetic stays in C#. |
| End-exclusive dates | The overlap rule is `old.Start < new.End && new.Start < old.End`, allowing adjacency. |
| UTC hours | Removes ambiguous timestamps; real market calendars are more involved. |
| Scoped DbContext | One unit of work per request; DbContext is not safe for concurrent operations. |
| Async database calls | Standard await/cancellation flow; SQLite's underlying async support is limited. |
| ServiceResult | Expected validation/not-found/conflict outcomes are explicit; exceptions remain for unexpected failures. |

## Before discussing the project

1. Run all tests and the README curl commands.
2. Change a fixed price and predict the preview before requesting it.
3. Find one unit test and one integration test that protect different things.
4. Explain why a draft reserves dates, why terminating a draft is allowed, and how a real supplier might choose different rules.
5. Describe the remaining concurrency race between import and termination, and why switching database providers needs new tests.
6. Be clear that AI generated substantial code. Only claim personal review and understanding once you have done that work.
