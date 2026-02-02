# CLI Install

> Placeholder commands below will be updated once packages are published.

## Windows (winget)

```powershell
winget install IntelligenceX
```

## macOS (Homebrew)

```bash
brew install evotecit/tap/intelligencex
```

## Linux (curl installer)

```bash
curl -fsSL https://evotec.it/intelligencex/install.sh | bash
```

## After install

```powershell
intelligencex setup wizard
```

Notes:
- The web UI can be launched with `intelligencex setup web`.
- The website can detect a running local wizard on `http://127.0.0.1`.
- If the browser doesn’t open automatically, use the URL shown in the CLI output.
