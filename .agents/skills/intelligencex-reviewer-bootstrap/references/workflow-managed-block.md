# Managed Workflow Block Rules

`review-intelligencex.yml` must keep a managed block:
- start marker: `# INTELLIGENCEX:BEGIN`
- end marker: `# INTELLIGENCEX:END`

Minimum expected shape inside managed block:
- `review:` job exists
- `uses: <actions-repo>/.github/workflows/review-intelligencex.yml@<ref>`
- `with.provider` and `with.model` present
- `with.openai_transport` present
- `with.repo` and `with.pr_number` present
- `with.review_config_path` should point to `.intelligencex/reviewer.json` when repo policy is used
- `with.mode` / `with.length` / `with.style` should be explicit when visual output needs to be stable
- either `secrets: inherit` OR explicit secrets mapping (when `--explicit-secrets`)

When analysis is enabled in reviewer config, expect:
- `.intelligencex/reviewer.json` contains `analysis.enabled=true`
- `analysis.packs` non-empty (default `all-50`)
