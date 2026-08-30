# Show Me The Money

Show Me The Money is a Windows desktop app for understanding spending and,
eventually, planning budgets. It uses Blazor Hybrid inside a WinForms host and
is packaged and updated with VeloPack.

The app imports bank transaction exports in Quicken Interchange Format (QIF),
displays cash-flow summaries, and stores the imported snapshot locally.
Before the first import it displays an empty state with instructions.

Add and select an account in the app, then use **Import QIF** to select the
`.qif` file downloaded from your bank. Accounts can be renamed at any time and
reimporting a file updates matching transactions without replacing other
accounts. Australian `day/month/year` dates, bank, cash, credit card, asset and
liability QIF account types are supported. QIF does not normally contain a
current account balance, so imported accounts show that their balance was not
supplied.

Imported data is stored at:

```text
%LocalAppData%\ShowMeTheMoney\show-me-the-money.db
```

Existing JSON data from earlier versions is migrated into SQLite automatically
and the legacy JSON file is removed after a successful migration.

## Run

The repository uses the .NET 10 SDK selected by `global.json`.

```powershell
dotnet run --project .\src\ShowMeTheMoney.Desktop
```

## Test and build

```powershell
dotnet test .\ShowMeTheMoney.slnx
dotnet build .\ShowMeTheMoney.slnx
```

Future automatic bank connections can be added behind the existing
`IBankingDataSource` abstraction without changing the transaction interface.

## Package

```powershell
dotnet tool restore
dotnet publish .\src\ShowMeTheMoney.Desktop -c Release -r win-x64 --self-contained false `
  -o .\artifacts\publish -p:Version=0.1.0 -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
dotnet vpk pack --packId DavidWengier.ShowMeTheMoney --packVersion 0.1.0 `
  --packDir .\artifacts\publish --mainExe ShowMeTheMoney.exe `
  --packTitle "Show Me The Money" --packAuthors "David Wengier" `
  --outputDir .\artifacts\releases --runtime win-x64 `
  --framework "net10.0-x64-desktop,webview2" --noPortable
```
