# Web Onboarding Flow

This flow runs locally and never uploads tokens to a backend.

## Overview

The web onboarding flow is path-first:

1. Choose path (`new-setup`, `refresh-auth`, `cleanup`, `maintenance`)
2. Run auto-detect preflight (doctor-backed)
3. Authenticate with GitHub
4. Select repositories
5. Configure/apply based on selected path

## Flow Diagram

```mermaid
flowchart TD
  A["Start: intelligencex setup web"] --> B["Choose Path"]
  B --> C["New Setup"]
  B --> D["Fix Expired Auth"]
  B --> E["Cleanup"]
  B --> F["Maintenance"]

  C --> C1["Auto-Detect (Doctor)"]
  C1 --> C2["GitHub Auth"]
  C2 --> C3["Select Repos"]
  C3 --> C4["Configure + AI Auth"]
  C4 --> C5["Plan/Apply setup"]

  D --> D1["Auto-Detect (Doctor)"]
  D1 --> D2["GitHub Auth"]
  D2 --> D3["Select Repos"]
  D3 --> D4["AI Auth"]
  D4 --> D5["Plan/Apply update-secret"]

  E --> E1["Auto-Detect (Doctor)"]
  E1 --> E2["GitHub Auth"]
  E2 --> E3["Select Repos"]
  E3 --> E4["Cleanup options"]
  E4 --> E5["Plan/Apply cleanup"]

  F --> F1["Auto-Detect (Doctor)"]
  F1 --> F2["GitHub Auth"]
  F2 --> F3["Select Repos"]
  F3 --> F4["Inspect repo status"]
  F4 --> F5["Choose setup/update-secret/cleanup"]
```

## Steps

1. Start the local wizard:

```powershell
intelligencex setup web
```

2. Choose onboarding path and run auto-detect.
3. Authenticate with GitHub (device flow, token, or GitHub App).
4. Select repositories.
5. Configure operation and provider-specific auth.
6. Plan changes and review summary.
7. Apply changes (PRs are created by default).

## Status

This page tracks onboarding UX direction and expected flow. It is safe to publish as documentation-in-progress.
