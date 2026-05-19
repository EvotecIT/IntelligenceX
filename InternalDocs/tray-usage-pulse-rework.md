# Tray Usage Pulse Rework

The tray app should behave like a compact system flyout, not a compressed report.
The first screen answers one question: what should the user know right now?

## Building Blocks

- `TrayShell`: title, freshness, provider switcher, one content surface, bottom actions.
- `ProviderPulseStrip`: compact provider pills with icon, health dot, and one tiny metric.
- `GlanceCard`: dominant provider-specific signal, secondary metric, status chip, and one insight line.
- `MetricChips`: short supporting facts such as 7d, 30d, health, freshness, or limits.
- `TokenMixStrip`: visual-first fresh input, cached input, output, and reasoning composition.
- `DetailsSheet`: slide-up inspection layer with Activity, Limits, Models, Events, and Scope modes.
- `ActionStrip`: report/export/detail actions. Deep exploration moves to the report, not the first tray screen.

## Provider Slots

Each provider fills a stable set of slots:

- `primaryMetric`: the largest thing to read.
- `secondaryMetric`: cost, API equivalent, requests, or repo movement.
- `health`: account, limit, snapshot, or credential state.
- `trend`: daily bars, sparkline, or movement summary.
- `mix`: token composition, language mix, or repo movement mix.
- `topItems`: top models, accounts, repos, or recent rollups.
- `actions`: fix credentials, sync, export, open report, or inspect details.

Codex/OpenAI lead with tokens, API equivalent, cached-input savings, and top models.
Claude leads with session and limit health when live usage is unavailable.
Copilot leads with quota/status and local IDE usage.
GitHub leads with repo movement, stars/forks/watchers, and local code correlation.
The combined view leads with the top changing provider and overall health.

## First Slice

The WPF branch adds the pulse vocabulary without removing the older report sections yet:

- provider tabs now show a compact metric and health dot
- selected usage providers render a glance card before report-style sections
- Details opens an inline inspection sheet instead of exposing filters first
- GitHub gets a repo-pulse hero instead of being treated like token telemetry

This keeps the change reviewable while giving us a concrete tray-friendly surface to iterate on.
