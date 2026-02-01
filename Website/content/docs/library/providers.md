---
title: Providers
description: ChatGPT and Copilot provider support in IntelligenceX
collection: docs
layout: docs
nav.weight: 20
---

# Providers

IntelligenceX supports two AI providers for code review and chat.

## OpenAI (ChatGPT Native Transport)

- Uses ChatGPT OAuth login (your own account)
- Best for users with an existing ChatGPT subscription
- Requires `INTELLIGENCEX_AUTH_B64` in GitHub Secrets
- Supports models like `gpt-5.2-codex`

## Copilot

- Optional provider for users with GitHub Copilot
- Uses Copilot CLI (experimental path)
- Native Copilot client is planned
- No additional secrets required beyond GitHub token
