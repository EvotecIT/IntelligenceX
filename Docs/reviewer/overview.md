# Reviewer Overview

The reviewer runs in GitHub Actions (and Azure DevOps summary-only) and posts a structured review comment on PRs. Azure DevOps summary-only uses the PR-level changes endpoint (cumulative diff).
It can use:
- ChatGPT (native transport) with a ChatGPT login bundle.
- Copilot (via Copilot CLI) for teams already using GitHub Copilot.
- Copilot direct HTTP transport (experimental) for custom gateways.

## Recommended onboarding
- CLI wizard: `intelligencex setup wizard`
- Local web UI (preview): `intelligencex setup web`

Related docs:
- [Onboarding Wizard](/docs/reviewer/onboarding-wizard/)
- [Web Setup UI](/docs/reviewer/setup-web/)
- [Understanding Reviewer Output](/docs/reviewer/review-output/)
- [Configuration](/docs/reviewer/configuration/)
- [Workflow vs JSON](/docs/reviewer/workflow-vs-json/)
- [Security and Trust](/docs/reviewer/security-trust/)
- [Projects + PR Monitoring](/docs/reviewer/projects-pr-monitoring/)
- [Project Bootstrap and Sync](/docs/reviewer/project-bootstrap-sync/)
- [Project Views and Operations](/docs/reviewer/project-views-and-ops/)

## Runtime Flow

```mermaid
flowchart LR
  classDef trigger fill:#BAE6FD,stroke:#0369A1,color:#082F49,stroke-width:2px;
  classDef engine fill:#DDD6FE,stroke:#5B21B6,color:#2E1065,stroke-width:2px;
  classDef provider fill:#FDE68A,stroke:#B45309,color:#451A03,stroke-width:2px;
  classDef output fill:#A7F3D0,stroke:#047857,color:#052E2B,stroke-width:2px;

  A["Pull request event"] --> B["GitHub Actions job"]
  B --> C["IntelligenceX review pipeline"]
  C --> D["Context builder<br/>diff selection, chunking, redaction"]
  D --> E["Provider call<br/>OpenAI or Copilot"]
  E --> F["Findings parser and formatter"]
  F --> G["PR summary comment"]
  F --> H["Inline comments (if enabled)"]
  C --> I["Thread triage and optional auto-resolve"]

  class A,B trigger;
  class C,D,F,I engine;
  class E provider;
  class G,H output;
```

## Trust model (short version)
- BYO GitHub App is supported for branded bot identity.
- Secrets are stored in GitHub Actions (you control access).
- Web UI binds to localhost only; tokens never leave your machine.

**Engine Scope**
- Review pipeline: resolve inputs, build context, assemble prompt, call provider, parse inline comments, post summary/inline output.
- Providers and transports: OpenAI (native/appserver), OpenAI-compatible HTTP endpoints (Ollama/OpenRouter/etc.), and Copilot (CLI/direct).
- Context builder: diff-range selection, file filtering, chunking, redaction, language hints, related PRs.
- Formatter/output: summary templates, inline comment formatting, structured findings block.
- Thread triage/auto-resolve: load threads, require evidence, summarize/append optional replies.

**Success Metrics**
- Review latency (p50/p95) from job start to posted comment.
- Failure rate for review runs (auth, preflight, provider errors).
- Reviewer usefulness score (maintainer feedback or “kept” findings rate).
- Inline quality (false-positive rate based on fixes/confirmations).

**Default Mode + Model Policy**
- Default review mode: `hybrid` (summary + inline when supported; falls back to summary-only).
- Default provider/model: OpenAI with `gpt-5.3-codex` unless configured otherwise; Copilot is opt-in.
- Safe defaults: skip drafts; skip workflow changes unless allowed; no secrets/writes on untrusted PRs; fail-open only for transient errors; budget summary enabled; auto-resolve limited to bot threads with evidence; secrets audit on.

## Reusable workflow (quick start)

```yaml
jobs:
  review:
    uses: evotecit/github-actions/.github/workflows/review-intelligencex.yml@master
    with:
      reviewer_source: source
      openai_transport: native
      review_config_path: .intelligencex/reviewer.json
      mode: hybrid
      length: medium
      style: direct
    secrets: inherit
```

## Inputs → environment mapping (short)

The reusable workflow maps `with:` inputs to environment variables the reviewer reads.

| Workflow input | Environment variable |
| --- | --- |
| `repo` | `INPUT_REPO` |
| `pr_number` | `INPUT_PR_NUMBER` |
| `reviewer_token` | `INTELLIGENCEX_GITHUB_TOKEN` |
| `reviewer_source` | `REVIEWER_SOURCE` |
| `reviewer_release_repo` | `REVIEWER_RELEASE_REPO` |
| `reviewer_release_tag` | `REVIEWER_RELEASE_TAG` |
| `reviewer_release_asset` | `REVIEWER_RELEASE_ASSET` |
| `reviewer_release_url` | `REVIEWER_RELEASE_URL` |

## Minimal config (native ChatGPT)

```json
{
  "review": {
    "provider": "openai",
    "openaiTransport": "native",
    "model": "gpt-5.3-codex",
    "mode": "inline",
    "length": "long",
    "reviewUsageSummary": true
  }
}
```

## Quick flow (end-to-end)

```powershell
# 1) Auth login (stores tokens locally)
intelligencex auth login

# 2) Setup reviewer (creates PR)
intelligencex setup wizard
```

## What to configure next
- Model/provider + output style
- Review length and strictness
- Auto-resolve/triage behavior for bot threads
- Triage-only mode (skip full review, only triage threads)
- Usage summary line (optional)

## How to interpret the review comment

By default:
- `Todo List ✅` and `Critical Issues ⚠️` are treated as merge blockers.
- `Other Issues 🧯` are suggestions and should not block merges by default.

## Usage and credits line

Enable `reviewUsageSummary` to append limits/credits (ChatGPT native only). See [Configuration](/docs/reviewer/configuration/).
When a code-review rate-limit window is present, its label is explicitly prefixed with `code review` (for example, `code review weekly limit`) so it is distinct from general limits.
