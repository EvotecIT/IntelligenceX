# Onboarding Setup Checklist

## UX Consistency
- Operation selection and onboarding path remain synchronized.
- Defaults (`withConfig`, secret mode, provider) match selected path.
- Preview/apply payload reflects visible state.

## Validation
- `dotnet build IntelligenceX.sln -c Release`
- `dotnet test IntelligenceX.sln -c Release`
- `dotnet ./IntelligenceX.Tests/bin/Release/net8.0/IntelligenceX.Tests.dll`
- `dotnet ./IntelligenceX.Tests/bin/Release/net10.0/IntelligenceX.Tests.dll`

## Docs Alignment
- CLI help examples match actual supported commands.
- Setup docs mention both wizard and web flows when relevant.
