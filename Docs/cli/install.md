# CLI Install

The CLI package-manager channels are not published yet.
Use source-based execution for now.

## Run from source (recommended)

```powershell
git clone https://github.com/EvotecIT/IntelligenceX.git
cd IntelligenceX
dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj -c Release -- setup wizard
```

## Build a local CLI binary

```powershell
pwsh ./Build/Publish-Cli.ps1 -Runtime win-x64 -Configuration Release -Framework net8.0
```

After publishing locally, run:

```powershell
intelligencex setup wizard
```

Notes:
- The web UI can be launched with `intelligencex setup web`.
- The website can detect a running local wizard on `http://127.0.0.1`.
- If the browser doesn’t open automatically, use the URL shown in the CLI output.
