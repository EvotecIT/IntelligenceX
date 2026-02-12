# Security Model (Draft)

This app runs tools on a user machine. Default stance: **zero trust**.

## Principles

- Least privilege: only enable tool packs the user explicitly selects.
- Safe-by-default: tools validate inputs, cap sizes, and return structured outputs.
- Human-in-the-loop: destructive/high-risk actions require approval.
- Local only: host API should bind to localhost or named pipes.
- Explicit boundaries: the model should not have direct access to secrets unless the user opts in.

## Tool execution

Tools should:
- validate required inputs and reject unknown fields (schema `additionalProperties=false`)
- enforce timeouts and size caps
- avoid returning secrets by default

## Data handling

Decide early (and document):
- where conversations are stored (if at all)
- how tool outputs are stored
- opt-in telemetry (default off)

