# Web Onboarding Flow

This flow runs locally and never uploads tokens to a backend.

## Canonical Path Contract

CLI, Web, and Bot all use the same path contract in `SetupOnboardingContract`.

| Path ID | Default Operation | GitHub Auth | Repo Selection | AI Auth | Typical Apply Command |
|---|---|---|---|---|---|
| `new-setup` | `setup` | Required | Required | Required | `intelligencex setup --repo owner/name --with-config` |
| `refresh-auth` | `update-secret` | Required | Required | Required | `intelligencex setup --repo owner/name --update-secret --auth-b64 <base64>` |
| `cleanup` | `cleanup` | Required | Required | Optional | `intelligencex setup --repo owner/name --cleanup` |
| `maintenance` | `setup` | Required | Required | Optional | `intelligencex setup web` |

## Path Flow Diagram

```mermaid
flowchart TD
  A["Start intelligencex setup web"] --> B["Run doctor auto-detect"]
  B --> C{"Choose path"}

  C --> D["new-setup"]
  D --> D1["GitHub auth required"]
  D1 --> D2["Repo selection required"]
  D2 --> D3["Configure reviewer and workflow"]
  D3 --> D4["AI auth required"]
  D4 --> D5["Plan apply verify setup"]

  C --> E["refresh-auth"]
  E --> E1["GitHub auth required"]
  E1 --> E2["Repo selection required"]
  E2 --> E3["AI auth required"]
  E3 --> E4["Plan apply verify update-secret"]

  C --> F["cleanup"]
  F --> F1["GitHub auth required"]
  F1 --> F2["Repo selection required"]
  F2 --> F3["Cleanup options"]
  F3 --> F4["Plan apply verify cleanup"]

  C --> G["maintenance"]
  G --> G1["GitHub auth required"]
  G1 --> G2["Repo selection required"]
  G2 --> G3["Inspect workflow config and secrets"]
  G3 --> G4{"Choose operation"}
  G4 --> D5
  G4 --> E4
  G4 --> F4
```

## Bot Contract-Check Flow

`.Chat` + `.Tools` should validate contract metadata before mutating setup:

```mermaid
flowchart LR
  A["reviewer_setup_pack_info"] --> B["setup autodetect --json"]
  B --> C["reviewer_setup_contract_verify"]
  C --> D{"Metadata matches"}
  D -->|yes| E["Run setup update-secret or cleanup command"]
  D -->|no| F["Stop and request contract/tool update"]
```

## Steps

1. Start web UI: `intelligencex setup web`
2. Run auto-detect first and review suggested path.
3. Confirm path requirements shown in step 1.
4. Authenticate with GitHub and select repositories.
5. Plan, then apply.

## Notes

- Auto-detect response includes `contractVersion`, `contractFingerprint`, paths, and command templates.
- Web step-1 path hints are derived from contract metadata to reduce CLI/Web drift.
