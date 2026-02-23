# IntelligenceX

Zero-trust AI platform with code reviews, desktop chat, tool packs, and developer libraries.  
Your credentials, your GitHub App, your control.

[![top language](https://img.shields.io/github/languages/top/EvotecIT/IntelligenceX.svg)](https://github.com/EvotecIT/IntelligenceX)
[![license](https://img.shields.io/github/license/EvotecIT/IntelligenceX.svg)](https://github.com/EvotecIT/IntelligenceX)
[![build](https://github.com/EvotecIT/IntelligenceX/actions/workflows/test-dotnet.yml/badge.svg)](https://github.com/EvotecIT/IntelligenceX/actions/workflows/test-dotnet.yml)

## Platform Areas 🧭

- GitHub Actions Reviewer
- IX Chat (Windows desktop tray app)
- IX Tools (tool packs for chat and integrations)
- CLI tools (setup/auth/ops workflows)
- .NET library
- PowerShell module
- Issue Ops + Project Control

## Product Walkthrough 🖼️

Screenshots below are sourced from current blog/gallery assets and mirrored under `Assets/README/` for stable GitHub rendering.

### GitHub Actions Reviewer 🤖

AI PR reviewer for actionable findings, merge-blocking triage, and cleaner review loops.

- Docs: [https://intelligencex.dev/docs/reviewer/overview/](https://intelligencex.dev/docs/reviewer/overview/)
- Blog: [https://intelligencex.dev/blog/ix-reviewer-in-action/](https://intelligencex.dev/blog/ix-reviewer-in-action/)

Picture 1:

<img src="Assets/README/reviewer.jpg" alt="IntelligenceX reviewer output on a runtime pull request" width="760" />

Picture 2:

<img src="Assets/README/reviewer-2.jpg" alt="Inline reviewer follow-up thread with implementation evidence checks" width="760" />

### IX Chat 💬

Windows tray chat app with provider/runtime selection and tool-calling support for diagnostics and investigation workflows.

> [!WARNING]
> **IX Chat is experimental.**
> - Use in dev/test environments only.
> - Do not use for unattended production operations.
> - Keep human review in the loop.

- Docs: [https://intelligencex.dev/docs/chat/overview/](https://intelligencex.dev/docs/chat/overview/)
- Blog: [https://intelligencex.dev/blog/chat-flow-and-options/](https://intelligencex.dev/blog/chat-flow-and-options/)
- Blog: [https://intelligencex.dev/blog/multilanguage-support-in-action/](https://intelligencex.dev/blog/multilanguage-support-in-action/)

Picture 1:

<img src="Assets/README/chat.png" alt="IX Chat AD triage conversation with summary findings" width="760" />

Picture 2:

<img src="Assets/README/chat-2.png" alt="IX Chat remediation-focused follow-up response" width="760" />

### IX Tools 🧰

Tool packs for event/AD/system workflows used by IX Chat and custom integrations.

- Docs: [https://intelligencex.dev/docs/tools/overview/](https://intelligencex.dev/docs/tools/overview/)
- Docs: [https://intelligencex.dev/docs/library/tool-packs/](https://intelligencex.dev/docs/library/tool-packs/)
- Blog: [https://intelligencex.dev/blog/event-viewer-in-action/](https://intelligencex.dev/blog/event-viewer-in-action/)

Current packs and representative tools:

| Pack | Representative tools | License | Usage |
|---|---|---|---|
| Event Log (EventViewerX) | `eventlog_pack_info`, `eventlog_channels_list`, `eventlog_live_query`, `eventlog_evtx_query` | MIT | Open-source pack; usable in IX Chat and custom integrations. |
| File System | `fs_pack_info`, `fs_list`, `fs_read`, `fs_search` | MIT | Open-source pack; usable in IX Chat and custom integrations. |
| Reviewer Setup | `reviewer_setup_pack_info`, `reviewer_setup_contract_verify` | MIT | Open-source pack; usable in IX Chat and custom integrations. |
| Email (Mailozaurr) | `email_pack_info`, `email_imap_search`, `email_imap_get`, `email_smtp_send` | MIT | Open-source pack; runtime dependency gated. |
| Office Documents (OfficeIMO) | `officeimo_pack_info`, `officeimo_read` | MIT | Open-source pack; runtime dependency gated. |
| PowerShell Runtime | `powershell_pack_info`, `powershell_environment_discover`, `powershell_hosts`, `powershell_run` | MIT | Open-source pack; opt-in by policy due execution risk. |
| ADPlayground | `ad_pack_info`, `ad_domain_info`, `ad_group_members`, `ad_search` | Private/commercial | Usable inside IntelligenceX (licensed/private builds) only; not for external/custom-host reuse. |
| TestimoX | `testimox_pack_info`, `testimox_rules_list`, `testimox_rules_run` | Private/commercial | Usable inside IntelligenceX (licensed/private builds) only; not for external/custom-host reuse. |
| ComputerX | `system_pack_info`, `system_info`, `system_process_list`, `system_service_list` | Private/commercial | Usable inside IntelligenceX (licensed/private builds) only; not for external/custom-host reuse. |

Picture 1:

<img src="Assets/README/tools.png" alt="Tools pack availability and toggle controls" width="760" />

Picture 2:

<img src="Assets/README/tools-2.png" alt="Expanded TestimoX tool pack view with specific tools" width="760" />

### Issue Ops + Project Control 📋

Project board and issue triage workflows for handling blockers, confidence signals, and follow-up actions.

- Docs: [https://intelligencex.dev/docs/project-ops/overview/](https://intelligencex.dev/docs/project-ops/overview/)
- Docs: [https://intelligencex.dev/docs/reviewer/projects-pr-monitoring/](https://intelligencex.dev/docs/reviewer/projects-pr-monitoring/)
- Blog: [https://intelligencex.dev/blog/ix-issue-ops-in-action/](https://intelligencex.dev/blog/ix-issue-ops-in-action/)

Picture 1:

_Placeholder (generated visual; to be replaced with real product capture)._

<img src="Assets/README/issue-ops.svg" alt="Issue Ops board overview" width="760" />

Picture 2:

_Placeholder (generated visual; to be replaced with real product capture)._

<img src="Assets/README/issue-ops-2.svg" alt="Issue review columns and confidence signals" width="760" />

### CLI + .NET + PowerShell 🛠️

Developer-facing interfaces for setup automation, embedding IntelligenceX in .NET apps, and scripting in PowerShell.

- Docs: [https://intelligencex.dev/docs/cli/overview/](https://intelligencex.dev/docs/cli/overview/)
- Docs: [https://intelligencex.dev/docs/library/overview/](https://intelligencex.dev/docs/library/overview/)
- Docs: [https://intelligencex.dev/docs/powershell/overview/](https://intelligencex.dev/docs/powershell/overview/)
- Blog: [https://intelligencex.dev/blog/setup-best-practices-for-teams/](https://intelligencex.dev/blog/setup-best-practices-for-teams/)

Picture 1:

_Placeholder (generated visual; to be replaced with real product capture)._

<img src="Assets/README/cli.svg" alt="CLI product visual" width="760" />

Picture 2:

_Placeholder (generated visual; to be replaced with real product capture)._

<img src="Assets/README/library.svg" alt=".NET library product visual" width="760" />

## Quick Start 🚀

Recommended onboarding:

```powershell
intelligencex setup wizard
```

Local web setup flow:

```powershell
intelligencex setup web
```

From source:

```powershell
dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj -c Release -- setup wizard
```

## Trust Model 🔐

- No backend service: IntelligenceX runs locally and/or in your GitHub Actions.
- Secrets stay under your control: stored in the environments you own.
- Bring your own GitHub App for identity, permissions, and auditability.
- Workflow changes happen via PRs so setup changes stay reviewable.

## Documentation 📚

- Start here: [https://intelligencex.dev/docs/getting-started/](https://intelligencex.dev/docs/getting-started/)
- Reviewer: [https://intelligencex.dev/docs/reviewer/overview/](https://intelligencex.dev/docs/reviewer/overview/)
- IX Chat: [https://intelligencex.dev/docs/chat/overview/](https://intelligencex.dev/docs/chat/overview/)
- Tools: [https://intelligencex.dev/docs/tools/overview/](https://intelligencex.dev/docs/tools/overview/)
- CLI: [https://intelligencex.dev/docs/cli/overview/](https://intelligencex.dev/docs/cli/overview/)
- .NET library: [https://intelligencex.dev/docs/library/overview/](https://intelligencex.dev/docs/library/overview/)
- PowerShell: [https://intelligencex.dev/docs/powershell/overview/](https://intelligencex.dev/docs/powershell/overview/)
- Project Ops: [https://intelligencex.dev/docs/project-ops/overview/](https://intelligencex.dev/docs/project-ops/overview/)
- Security: [https://intelligencex.dev/docs/security/](https://intelligencex.dev/docs/security/)

## Repository Layout 🗂️

- `IntelligenceX/`: core library
- `IntelligenceX.Reviewer/`: review pipeline executable
- `IntelligenceX.Cli/`: setup/auth/ops commands
- `IntelligenceX.Chat/`: desktop chat app, host, service, client
- `IntelligenceX.Tools/`: in-repo tool packs and contracts
- `Docs/`: source docs (published to the website)

## Build 🧪

Core CI-equivalent build check:

```powershell
dotnet build IntelligenceX.CI.slnf -c Release
dotnet test IntelligenceX.CI.slnf -c Release

# CI also runs the executable test harness:
dotnet ./IntelligenceX.Tests/bin/Release/net8.0/IntelligenceX.Tests.dll
dotnet ./IntelligenceX.Tests/bin/Release/net10.0/IntelligenceX.Tests.dll
```

`IntelligenceX.sln` includes Chat/Tools projects for local integration work.

Publish CLI (self-contained single-file):

```powershell
pwsh ./Build/Publish-Cli.ps1 -Runtime win-x64 -Configuration Release -Framework net8.0
```

## License

MIT
