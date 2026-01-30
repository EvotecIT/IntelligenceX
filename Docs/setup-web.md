# Setup Web UI (Preview)

Run the local setup UI:

```powershell
intelligencex setup web
```

This starts a local web server and opens the wizard in your browser (http://127.0.0.1 only).

Operations available:
- Setup / update workflow + config
- Update OpenAI secret only (requires auth bundle)
- Cleanup (remove workflow/config)
- Optional GitHub App manifest flow (create app + installation token)
- Load existing config from a repo (manage existing setup)
- Load workflow preview for the managed workflow
- Save/load config presets in the browser

Advanced options:
- Provider toggle (openai | copilot)
- Auth bundle input for secret updates (INTELLIGENCEX_AUTH_B64)

## GitHub App flow (optional)

If you want to avoid personal access tokens, you can use the GitHub App manifest flow:

1. Enter App name + App owner (org login).
2. Click “Create App (manifest)”. A browser window opens to create the app.
3. Install the app in the org/user and return to the wizard.
4. Click “List installations”, select the installation, then click “Use installation token”.
5. The GitHub token field is populated with the installation token; proceed to load repos.

## Current limitations

- OpenAI login is not wired into the web UI yet.
  Provide `authB64`/`authB64Path` (INTELLIGENCEX_AUTH_B64 export) or keep “Skip OpenAI secret” enabled.
- Update-secret in the web UI requires a pre-exported auth bundle.
- The UI supports multi-repo setup (plan/apply), repo inspection, and setup recommendations.
- GitHub App installation tokens can only list repos the app is installed on.
- Buttons are disabled until required inputs are provided (token, repo selection, auth bundle).

## Security notes

- The server listens on 127.0.0.1 only over HTTP (no HTTPS binding).
- GitHub tokens are sent to the local CLI server and never leave your machine.
- Use this on trusted machines only.
