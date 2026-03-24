---
title: Setup Web UI
description: Run the local IntelligenceX setup web UI, review the flow, and understand the localhost-only onboarding experience.
---

# Setup Web UI (Preview)

Run the local setup UI:

```powershell
intelligencex setup web
```

This starts a local web server and opens the wizard in your browser (http://127.0.0.1 only).

## Screenshots

- Configure step: [Screenshot](/docs/screenshots/#web-ui---configure)
- Verify step: [Screenshot](/docs/screenshots/#web-ui---verify)

## Quick flow

```text
1) Start the web UI
2) Choose onboarding path (new setup / fix auth / cleanup / maintenance)
3) Run auto-detect preflight
4) Authenticate with GitHub (device flow or app install)
5) Select repos
6) Plan + Apply
```

```mermaid
flowchart LR
  classDef step fill:#BAE6FD,stroke:#0369A1,color:#082F49,stroke-width:2px;
  classDef auth fill:#FDE68A,stroke:#B45309,color:#451A03,stroke-width:2px;
  classDef apply fill:#A7F3D0,stroke:#047857,color:#052E2B,stroke-width:2px;

  A["Path"] --> B["Auto-Detect"]
  B --> C["GitHub Auth"]
  C --> D["Repos"]
  D --> E["Configure/Auth"]
  E --> F["Plan"]
  F --> G["Apply + Verify"]

  class A,B,D,E,F step;
  class C auth;
  class G apply;
```

Path requirements (GitHub/repo/AI auth) and Bot contract checks are defined in [Web Onboarding Flow](/docs/reviewer/web-onboarding/).

Operations available:
- Setup / update workflow + config
- Update provider secret only
- Cleanup (remove workflow/config)
- Maintenance (inspect and choose operation)
- Optional GitHub App manifest flow (create app + installation token)
- Load existing config from a repo (manage existing setup)
- Load workflow preview for the managed workflow
- Save/load browser presets for provider, model, review knobs, and config overrides
- Export/import presets as JSON files
- Import prompts before overwriting existing presets

Advanced options:
- Provider toggle (`openai` | `claude` | `copilot`)
- Provider-aware model field in the Configure step, with quick picks for common OpenAI and Claude models
- Review step summarizes why the selected provider/model pair is a good fit before apply
- Browser presets now retain the named provider/model profile alongside the raw provider/model values
- Static analysis controls when generating preset config (`analysisEnabled`, `analysisGateEnabled`, `analysisRunStrict`, packs, export path)
- OpenAI account routing supports primary-only setup (rotation/failover can be configured without `account ids`)
- OpenAI auth bundle input for `INTELLIGENCEX_AUTH_B64`
- Claude API key / key-path input for `ANTHROPIC_API_KEY`

For YAML vs JSON ownership and precedence, see [Workflow vs JSON](/docs/reviewer/workflow-vs-json/).

## GitHub App flow (optional)

If you want to avoid personal access tokens, you can use the GitHub App manifest flow:

1. Enter App name + App owner (org login).
2. Click “Create App (manifest)”. A browser window opens to create the app.
3. Install the app in the org/user and return to the wizard.
4. Click “List installations”, select the installation, then click “Use installation token”.
5. The GitHub token field is populated with the installation token; proceed to load repos.

## Current limitations

- OpenAI login runs locally in the web UI ("Sign in with ChatGPT") and returns an auth bundle (`authB64`) you can upload as `INTELLIGENCEX_AUTH_B64`.
- Claude setup in the web UI uses an API key or key file path; there is no browser-login equivalent.
- Update-secret in the web UI requires provider-specific credentials:
  - OpenAI: `authB64` or `authB64Path`
  - Claude: `anthropicApiKey` or `anthropicApiKeyPath`
- The UI supports multi-repo setup (plan/apply), repo inspection, and setup recommendations.
- GitHub App installation tokens can only list repos the app is installed on.
- Buttons are disabled until required inputs are provided (token, repo selection, and any required provider secret input).
- Inline hints describe what is missing before plan/apply can run.
- Browser presets intentionally do not store GitHub tokens, OpenAI auth bundles, or Claude API keys.
- Switching providers resets incompatible default models so stale GPT/Claude model values are not carried forward accidentally, while still letting you type a custom model id.

## Tips

- Use the "Load workflow preview" button before applying changes.
- If you want zero secret handling in the UI, enable "Skip provider secret" and paste secrets manually in GitHub.
- Save a browser preset after choosing your provider/model pair if you frequently onboard repos with the same setup shape.
- Start with auto-detect to get a recommended path before selecting repositories.
- If you automate setup with Bot tools, verify `contractVersion` + `contractFingerprint` match before apply.

## Security notes

- The server listens on 127.0.0.1 only over HTTP (no HTTPS binding).
- GitHub tokens are sent to the local CLI server and never leave your machine.
- Use this on trusted machines only.
