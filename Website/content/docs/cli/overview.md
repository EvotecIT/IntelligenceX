---
title: CLI Overview
description: IntelligenceX CLI for auth, onboarding, and reviewer management
collection: docs
layout: docs
nav.weight: 10
---

# CLI Overview

The `intelligencex` CLI handles auth, onboarding, reviewer runs, and utilities like usage/credits.

## Common Commands

```bash
# Authentication
intelligencex auth login
intelligencex auth export --format store-base64

# Setup
intelligencex setup wizard
intelligencex setup web

# Reviewer operations
intelligencex reviewer run
intelligencex reviewer resolve-threads

# Usage tracking
intelligencex usage
intelligencex usage --events
```

## Auth Export for GitHub Secrets

Export the auth bundle for storing in GitHub:

```bash
intelligencex auth export --format store-base64
```

## One-Step GitHub Secret Sync

Login and upload the secret in one command:

```bash
intelligencex auth login --set-github-secret --repo owner/name --github-token $TOKEN
```

## Usage and Credits

Track your AI provider usage:

```bash
intelligencex usage --events
```

See [CLI Quick Start](/docs/cli/quickstart/) for complete workflow examples.
