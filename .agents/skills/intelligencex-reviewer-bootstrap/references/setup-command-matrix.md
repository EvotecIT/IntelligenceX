# Reviewer Bootstrap Command Matrix

## Setup (recommended)
```bash
dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj --framework net8.0 -- \
  setup \
  --repo <owner/name> \
  --github-token <token> \
  --with-config \
  --skip-secret \
  --analysis-enabled true \
  --analysis-packs all-50 \
  --dry-run
```

## Setup (explicit secrets workflow block)
```bash
dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj --framework net8.0 -- \
  setup \
  --repo <owner/name> \
  --github-token <token> \
  --with-config \
  --skip-secret \
  --explicit-secrets \
  --analysis-enabled true \
  --analysis-packs all-50 \
  --dry-run
```

## Update Secret
```bash
dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj --framework net8.0 -- \
  setup \
  --repo <owner/name> \
  --github-token <token> \
  --update-secret \
  --dry-run
```

## Cleanup
```bash
dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj --framework net8.0 -- \
  setup \
  --repo <owner/name> \
  --github-token <token> \
  --cleanup \
  --keep-secret \
  --dry-run
```
