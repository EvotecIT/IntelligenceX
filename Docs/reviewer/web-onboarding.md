# Web Onboarding Flow

> This flow runs locally and never uploads tokens to a backend.

## Overview

The web onboarding flow runs locally and guides you through GitHub auth, repo selection, and reviewer setup.
It never uploads tokens to a backend.

## Steps (current UI)

1) Start the local wizard:

```powershell
intelligencex setup web
```

2) Authenticate with GitHub (device flow, token, or GitHub App).
3) Select repositories.
4) Plan changes and review the summary.
5) Apply changes (PRs are created by default).

## Screenshots (placeholders)

- `Docs/screenshots/setup-web-start.png`
- `Docs/screenshots/setup-web-plan.png`
- `Docs/screenshots/setup-web-apply.png`
