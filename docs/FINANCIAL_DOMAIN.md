# Financial Domain

The rules for money in Melamu Holdings OS. This file records what is **implemented**. The design for the ledger, payouts and allocation, which are not built yet, is in [`../PLAN.md`](../PLAN.md) §3; it moves here as each part is implemented.

## Scope and limits

Melamu Holdings OS is an operational management system, **not accounting software**.
- It records cash movements and allocations so that every balance can be traced to its records.
- It does not produce statutory financial statements, accrual-based profit or tax figures.
- Net cash movement is never labelled as profit.

## Money and currency (Phase 3: implemented)

Code: `MelamuHoldings.Domain/Finance/Currency.cs`, `Money.cs`. Tests: `tests/MelamuHoldings.UnitTests/Finance/MoneyTests.cs`.

| Rule | Behaviour |
|---|---|
| Currency is always explicit | Every `Money` carries a `Currency`. There is no default currency. |
| Only supported currencies | `ZAR`, `USD`, `EUR` and `GBP` (ISO 4217, 2 minor units each). An unknown or mistyped code is rejected rather than creating a new currency. |
| Whole minor units | `Money.Of` rejects amounts finer than the currency allows: `10.005 USD` throws instead of being silently rounded. |
| Rounding is explicit | Calculated amounts go through `Money.Round(amount, currency, mode)`, so the rounding mode is visible where it happens. The allocation engine (Phase 5) will truncate with `ToZero`, then distribute the leftover minor units by largest remainder (PLAN §3.4). |
| No implicit conversion | Adding, subtracting, summing or comparing different currencies throws `CurrencyMismatchException`. Converting requires a recorded `CurrencyExchange` transaction with its rate (Phase 4, decision D3). |
| Value equality | `10.5 USD == 10.50 USD`, but `10.5 USD != 10.5 ZAR`. |
| Decimal arithmetic only | `decimal` throughout. No `float` or `double` touches money. |
| Culture-independent text | `ToString()` gives `USD 1350.00` regardless of machine locale. Display formatting is the frontend's job. |

Business-rule violations derive from `DomainException` (`MelamuHoldings.Domain/Common`). Once finance endpoints exist (Phase 4), the API will map them to HTTP 422 ProblemDetails.

## Storage conventions (apply from Phase 4)

- Finance tables live in a separate SQL schema, `finance`. Trading tables stay in `dbo`. EF creates the schema with the first finance migration, so no empty schema is created in advance.
- Amounts: `decimal(19,4)`. Percentages and exchange rates: `decimal(9,6)`. Currency codes: `char(3)`.
- Foreign keys from finance tables use `Restrict`. Deleting a login must never cascade into financial history.
- Posted ledger records are immutable. Corrections are reversal transactions.
