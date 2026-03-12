---
title: IntelligenceX FAQ
description: Get quick answers about IntelligenceX onboarding, zero-trust security, supported providers, platform support, review customization, and GitHub App ownership.
---

# FAQ

Quick answers for the questions teams usually ask first about trust, setup, supported providers, and how much control they keep.

## Auth

### `auth login` opens a URL but nothing happens
Ensure the browser can reach the callback URL and try `--print` to paste the code manually.

### I see `No OpenAI auth bundle found`
Run `intelligencex auth login` or `intelligencex auth export --format store-base64` and add it as `INTELLIGENCEX_AUTH_B64`.

## GitHub

### Repos are missing in the wizard
Verify the token scope or ensure the GitHub App is installed on those repos.

### Can onboarding auto-detect what I should do first?
Yes. Run `intelligencex setup autodetect` (or use the Web auto-detect panel in step 1). It runs doctor-style checks and suggests a path (`new-setup`, `refresh-auth`, or `maintenance`) before repo selection.

### Why does the Web UI show a contract version/fingerprint?
It comes from the shared onboarding contract used by CLI/Web/Bot tooling. If your Bot tools compare this metadata before apply (`reviewer_setup_contract_verify`), they can stop on contract drift instead of running outdated commands.

### PRs were not created
Check that the GitHub token includes write permissions for `contents` and `pull_requests`.

## Reviewer

### Inline comments are missing
Ensure `review.mode` is `inline` or `hybrid` and the provider supports inline.

## CLI

### `intelligencex` is not found
Use `dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj -c Release -- <command>`.

### How do I run cleanup from the guided flow?
Use `intelligencex setup wizard --operation cleanup` for CLI, or pick the Cleanup path in `intelligencex setup web`.

### Which paths require AI auth?
`new-setup` and `refresh-auth` require AI auth. `cleanup` and `maintenance` do not require AI auth by default.
