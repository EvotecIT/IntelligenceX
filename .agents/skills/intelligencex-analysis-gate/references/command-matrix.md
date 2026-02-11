# Analysis Command Matrix

## Fast
- `dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj --framework net8.0 -- analyze validate-catalog --workspace .`
- `dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj --framework net8.0 -- analyze run --config .intelligencex/reviewer.json --out artifacts --framework net8.0`

## Full
- Fast commands +
- `dotnet build IntelligenceX.sln -c Release`
- `dotnet test IntelligenceX.sln -c Release`
- `dotnet ./IntelligenceX.Tests/bin/Release/net8.0/IntelligenceX.Tests.dll`
- `dotnet ./IntelligenceX.Tests/bin/Release/net10.0/IntelligenceX.Tests.dll`
