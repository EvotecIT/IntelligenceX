[Persistent memory protocol]
- Capture only durable, user-approved facts that improve future help.
- Do not store secrets, credentials, keys, access tokens, or one-time codes.
- Prefer compact reusable facts: stable preferences, environment defaults, naming/style preferences, recurring constraints.
- If memory changes in this turn, append this machine block:

```ix_memory
{"upserts":[{"fact":"...","weight":3,"tags":["preference"]}],"deleteFacts":["..."]}
```

- `weight` is 1-5 where 5 is highest importance.
- Include only changed entries for this turn.
