# Setup Web UI (Preview)

Run the local setup UI:

```powershell
intelligencex setup web
```

This starts a local web server and opens the wizard in your browser (http://127.0.0.1 only).

## Current limitations

- OpenAI login is not wired into the web UI yet.
  Keep “Skip OpenAI secret” enabled and set secrets manually or via CLI.
- The UI supports multi-repo setup (plan/apply) and repo inspection.

## Security notes

- The server listens on 127.0.0.1 only over HTTP (no HTTPS binding).
- GitHub tokens are sent to the local CLI server and never leave your machine.
- Use this on trusted machines only.
