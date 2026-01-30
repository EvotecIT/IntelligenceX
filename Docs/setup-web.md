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

Advanced options:
- Provider toggle (openai | copilot)

## Current limitations

- OpenAI login is not wired into the web UI yet.
  Provide `authB64`/`authB64Path` (INTELLIGENCEX_AUTH_B64 export) or keep “Skip OpenAI secret” enabled.
- Update-secret in the web UI requires a pre-exported auth bundle.
- The UI supports multi-repo setup (plan/apply), repo inspection, and setup recommendations.

## Security notes

- The server listens on 127.0.0.1 only over HTTP (no HTTPS binding).
- GitHub tokens are sent to the local CLI server and never leave your machine.
- Use this on trusted machines only.
