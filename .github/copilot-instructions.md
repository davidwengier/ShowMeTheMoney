# Repository instructions

## Build and test

This repository requires the .NET SDK selected by `global.json` (`10.0.400`,
rolling forward only to a later patch).

```powershell
# Run the Windows desktop app
dotnet run --project .\src\ShowMeTheMoney.Desktop

# Build everything; all projects treat warnings as errors
dotnet build .\ShowMeTheMoney.slnx

# Run all tests
dotnet test .\ShowMeTheMoney.slnx

# Run one test project
dotnet test .\tests\ShowMeTheMoney.Storage.Sqlite.Tests\ShowMeTheMoney.Storage.Sqlite.Tests.csproj

# Run one xUnit test by fully qualified name
dotnet test .\tests\ShowMeTheMoney.Core.Tests\ShowMeTheMoney.Core.Tests.csproj `
  --filter "FullyQualifiedName~CategoryFlowSummarizerTests.Summarize_GroupsIncomeAndSpendingByCategory"
```

There is no separate lint command. Use the solution build as the compile,
nullable, and warning validation step.

For release packaging, restore the repository-local VeloPack tool with
`dotnet tool restore`; the complete publish and `dotnet vpk pack` commands are
in `README.md`. A push to `main` runs `.github/workflows/windows-release.yml`,
tests in Release configuration, creates a single-file `win-x64` build, packages
it with VeloPack, and publishes a GitHub release.

## Architecture

- `ShowMeTheMoney.Desktop` is the Windows entry point. It hosts the Razor UI in
  a WinForms `BlazorWebView`, wires all services in `Program.cs`, persists window
  placement, integrates VeloPack updates, and embeds the UI static assets into
  the desktop executable. Changes to UI assets may require checking the
  embedded-resource entries in the desktop project.
- `ShowMeTheMoney.UI` contains routed Razor pages and the shared CSS. Pages
  depend on `IBankingDataSource` for reads and `IBankingDataStore` for
  mutations; Windows-specific operations are behind UI service interfaces such
  as `IQifFilePicker` and `IApplicationUpdateService`.
- `ShowMeTheMoney.Core` owns immutable banking records, QIF parsing,
  categorization rules, transaction summaries, and other storage-independent
  logic. Keep reusable calculations and policies here so they can be tested
  without the desktop or SQLite projects.
- `ShowMeTheMoney.Storage.Sqlite` is the local persistence implementation for
  both banking interfaces. The database normally lives at
  `%LocalAppData%\ShowMeTheMoney\show-me-the-money.db`; tests pass unique
  temporary paths. `SHOW_ME_THE_MONEY_DATABASE` overrides the production path.
- `IBankingDataSource.GetOverviewAsync` is the lightweight account-screen read.
  Transaction rows use `GetTransactionPageAsync` and cached running balances.
  `GetSnapshotAsync` intentionally materializes all transactions and is used by
  whole-dataset features such as cash-flow aggregation and legacy migration.
  Do not switch paged UI back to full snapshots.

## Repository conventions

- SQLite access is serialized through the store's `SemaphoreSlim`. Multi-step
  mutations use one SQLite transaction and update all related data atomically.
- Schema evolution is implemented inside `EnsureSchemaAsync`, guarded by
  `PRAGMA user_version`, and must upgrade existing databases in place. The
  schema initialization flag avoids repeating migration work on every query.
  Add migration coverage that constructs the previous schema when changing it.
- Dates and decimal amounts are stored as invariant-culture text
  (`yyyy-MM-dd` for dates); use the existing `FormatDecimal` and `ParseDecimal`
  helpers rather than culture-sensitive conversion.
- Running balances are a database cache. Rebuild them when transaction amounts,
  ordering, account assignment, imports, or an account's current balance
  changes. Category-only changes must not trigger a balance rebuild.
- QIF imports accept Australian day/month/year dates and the supported account
  types listed in `QifParser`. Transaction IDs are deterministic hashes of the
  parsed data, then prefixed with the selected account ID in the UI. Preserve
  this identity scheme so re-importing updates rows instead of duplicating them.
- `Uncategorised` is the canonical empty transaction state and is not stored as
  an editable category. Default categories and rules are seeded only for an
  empty database; afterward every category and rule is database-driven and
  editable. Category rename/delete operations must update affected transactions
  and rules atomically. Manual categorization previews matching payee counts and
  lets the user update only the selected transaction, matching uncategorised
  transactions, or all exact matches. Selected-only changes create a
  `NoAutomaticMatch` exact rule so broader rules cannot overwrite them.
- Razor pages keep operation state explicitly (`isWorking`, loading flags,
  action/error messages), disable conflicting controls while work is running,
  and surface `InvalidOperationException` messages from expected store
  validation. Preserve the current page when refreshing after edits; reset to
  the newest transaction page after account changes or imports.
- UI styling is centralized in
  `src\ShowMeTheMoney.UI\wwwroot\css\app.css`. Reuse the existing panel, button,
  message, responsive breakpoint, and dark native `select`/`option` patterns
  rather than introducing page-local styles.
- Tests use xUnit v3. Core policy and calculations belong in
  `ShowMeTheMoney.Core.Tests`; persistence, migrations, atomic side effects, and
  paging belong in `ShowMeTheMoney.Storage.Sqlite.Tests`. SQLite tests create a
  unique temporary database and delete the database, WAL, and SHM files in
  `finally`; raw fixture connections should use `Pooling=False`.
