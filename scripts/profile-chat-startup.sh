#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ps_script="$script_dir/profile-chat-startup.ps1"

if command -v pwsh >/dev/null 2>&1; then
  exec pwsh -NoLogo -NoProfile -File "$ps_script" "$@"
fi

if command -v powershell >/dev/null 2>&1; then
  exec powershell -NoLogo -NoProfile -File "$ps_script" "$@"
fi

echo "PowerShell is required. Install pwsh to run startup profiling." >&2
exit 1
