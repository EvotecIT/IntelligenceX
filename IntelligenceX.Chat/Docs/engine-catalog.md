# Engine Catalog (Canonical Sources)

Goal: stop re-implementing AD/EventLog/System logic inside tool packs or the chat host by making the engine layer explicit, discoverable, and the single source of truth.

## Layering Rules

- `IntelligenceX.Chat` is orchestration and UX only. It should not contain AD parsing, LDAP helpers, DN helpers, or event log parsing.
- `IntelligenceX.Tools.*` projects expose stable tool schemas and shape outputs. They should be thin wrappers around engines.
- Engines (`ADPlayground`, `ComputerX`, `EventViewerX`) own domain logic, parsing, and low-level access.
- Use the PowerShell modules (`*.PowerShell`) as a discoverable index of engine capabilities and examples of intended usage.

## Canonical Locations (Avoid The “Wrong Repo” Problem)

- Active Directory engine: `C:\Support\GitHub\TestimoX\ADPlayground`
- AD PowerShell cmdlets (capability index): `C:\Support\GitHub\TestimoX\ADPlayground.PowerShell`
- Computer/system engine: `C:\Support\GitHub\TestimoX\ComputerX`
- ComputerX PowerShell cmdlets (capability index): `C:\Support\GitHub\TestimoX\ComputerX.PowerShell`
- Event log engine: `C:\Support\GitHub\PSEventViewer\Sources\EventViewerX`
- Persistence engine (SQLite client): `C:\Support\GitHub\DbaClientX\DbaClientX.SQLite`

If you are about to add a helper (DN parsing, typed SearchResult reads, SPN parsing, LDAP diagnostics, FILETIME conversions), search the engine first. Only add code in Tools when it is output-shaping or tool-argument policy.

## ADPlayground (Active Directory)

Prefer these structured components over raw LDAP reads:

- Users: `ADPlayground.UserGatherer.GetUsersAsync(UserQueryOptions, ...)` returns `UserRecord`
  - Used by PowerShell: `Get-ADXUsers` in `ADPlayground.PowerShell\CmdletGetADXUsers.cs`
- Groups: `ADPlayground.GroupGatherer.GetGroupsAdvancedAsync(GroupQueryOptions, ...)` returns `GroupInfo`
  - Used by PowerShell: `Get-ADXGroups` in `ADPlayground.PowerShell\CmdletGetADXGroups.cs`
- Computers: `ADPlayground.ComputerGatherer.GetComputersAsync(ComputerQueryOptions, ...)` returns `ComputerRecord`
  - Used by PowerShell: `Get-ADXComputers` in `ADPlayground.PowerShell\CmdletGetADXComputers.cs`
- Privileged groups and effective nested membership resolution: `ADPlayground.Groups.AdminGroupService`
  - Used by Tools: `ad_privileged_groups_summary`, `ad_domain_admins_summary`
- Expired + stale account queries (small, server-side filtered): `ADPlayground.DirectoryOps.ExpiredUsersService`, `ADPlayground.DirectoryOps.StaleAccountsService`
  - Used by Tools: `ad_users_expired`, `ad_stale_accounts`
- LDAP/LDAPS diagnostics: `ADPlayground.Ldap.LdapScanner` / `ADPlayground.Ldap.LdapTester`
  - Used by PowerShell: `Test-ADXLdap` in `ADPlayground.PowerShell\CmdletTestADXLdap.cs`
- SPN posture and duplicates:
  - `ADPlayground.Kerberos.DuplicateSpnDetector`
  - `ADPlayground.Kerberos.SpnHygieneService`
  - Used by PowerShell: `Get-ADXDuplicateSpn` in `ADPlayground.PowerShell\CmdletGetADXDuplicateSpn.cs`

Use typed reads only when a tool is intentionally “exploratory LDAP”:

- Typed reads for LDAP results: `ADPlayground.Helpers.SearchResultExtensions` (GetString/GetInt/GetFileTime/GetMultiString/...)
  - Source: `ADPlayground\Helpers\LdapQueryHelper.cs`

## ComputerX (System Inventory / Config)

Prefer structured queries over ad-hoc WMI/registry logic in Tools:

- OS info: `ComputerX.OperatingSystem.OsInfoQuery`
  - Used by PowerShell: `Get-CxOsInfo` in `ComputerX.PowerShell\CmdletGetCxOsInfo.cs`
- Hardware: `ComputerX.Bios.BiosInfoQuery`, `ComputerX.Bios.BaseBoardInfoQuery`, CPU/video queries
- Updates: `ComputerX.Updates.*`, `ComputerX.PatchDetails.*`
- Networking and diagnostics: `ComputerX.Network.*`, `ComputerX.Diagnostics.*`

## EventViewerX (Event Logs)

Prefer typed report logic and stable extractors over custom event parsing in Tools:

- Core engine: `C:\Support\GitHub\PSEventViewer\Sources\EventViewerX`
- Tools should wrap existing EventViewerX reports where possible (Security 4624/4625/4740 etc.).

## “Thin Tool” Checklist (Before Adding A Tool Or Helper)

- Does ADPlayground/ComputerX/EventViewerX already have this capability?
- Does the PowerShell module already expose it as a cmdlet?
- Can Tools call a structured service (gatherer/scanner) rather than reading attributes?
- If you must touch LDAP results, are you using `SearchResultExtensions` instead of local `TryGetString` helpers?
- Is new code output-shaping only (JSON/table envelopes, truncation, allowlists), not domain parsing?
