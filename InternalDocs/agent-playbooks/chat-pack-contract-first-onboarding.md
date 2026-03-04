# Chat Pack Contract-First Onboarding

Internal guide for adding a new tool pack so Chat picks it up with zero Chat source changes.

## Outcome
- New pack/tool registers through contracts only.
- Chat routing/preflight/setup reads the orchestration catalog and works without hardcoded branches.
- Startup/status/debug surfaces pack load progress automatically.

## Zero-Chat-Edits Rule
When adding a pack or tool, do not modify:
- `IntelligenceX.Chat.App/*` for tool-name formatting/routing special-cases.
- `IntelligenceX.Chat.Service/*` for pack-id/tool-name switches.
- Any fallback path that rewrites from one pack/tool to another.

If behavior needs to change, publish the contract in tool definitions instead.

## Required Contracts Per Tool
Every tool must publish explicit routing metadata:

```csharp
new ToolDefinition(
    "contoso_example_query",
    "Describe what the tool does",
    schema: ToolSchema.Object(
            ("target", ToolSchema.String("Target host or domain")))
        .Required("target")
        .NoAdditionalProperties(),
    category: "contoso",
    routing: new ToolRoutingContract {
        IsRoutingAware = true,
        PackId = "contoso",
        Role = ToolRoutingRole.Operational,
        Scope = "remote",
        Operation = "query",
        Entity = "host",
        Risk = ToolRoutingRisk.ReadOnly,
        DomainIntentFamily = "public_domain",
        DomainIntentActionId = "act_domain_scope_public"
    },
    setup: new ToolSetupContract {
        Requirements = new[] { "network_access", "credentials" },
        UserHints = new[] { "Provide FQDN when possible." }
    },
    recovery: new ToolRecoveryContract {
        RetryableErrorCodes = new[] { "timeout", "transport_disconnect" }
    });
```

## Optional Contracts
- `ToolHandoffContract`: declare source-to-target argument mapping between tools.
- `ToolPackGuidance`: richer operator guidance, but routing-critical fields must remain in `ToolRoutingContract`.

## Pack Registration Checklist
1. Add tool(s) to pack bootstrap/registration path.
2. Ensure each tool has explicit `PackId` + `Role`.
3. Add setup/handoff/recovery contracts where applicable.
4. Run catalog validation:
   - `dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj --framework net8.0 -- analyze validate-catalog --workspace .`
5. Run targeted tests:
   - pack contract tests
   - routing/domain-intent tests
   - startup/bootstrap status tests if startup behavior changed

## Plugin Author Contract Schema (Minimal)
Use this baseline for plugin-authored tools:

```json
{
  "name": "contoso_example_query",
  "routing": {
    "is_routing_aware": true,
    "pack_id": "contoso",
    "role": "operational",
    "scope": "remote",
    "operation": "query",
    "entity": "host",
    "risk": "read_only",
    "domain_intent_family": "public_domain",
    "domain_intent_action_id": "act_domain_scope_public"
  },
  "setup": {
    "requirements": ["network_access"],
    "user_hints": ["Provide FQDN when possible."]
  },
  "recovery": {
    "retryable_error_codes": ["timeout"]
  }
}
```

## Anti-Patterns (Do Not Reintroduce)
- Tool-name suffix parsing as routing truth (`*_pack_info`, `*_environment_discover`).
- Chat-side pack inference maps or string alias tables for pack ids.
- Keyword-gated execution logic tied to one language.
- Chat fallback execution that auto-switches packs/tools after failure.

## End-to-End Smoke
Use a strict scenario to prove behavior:
1. Add/adjust scenario in `IntelligenceX.Chat/scenarios/`.
2. Run strictness tests:
   - `dotnet test IntelligenceX.Chat/IntelligenceX.Chat.Tests/IntelligenceX.Chat.Tests.csproj -c Release --filter "FullyQualifiedName~HostScenarioCatalogStrictnessTests"`
3. Run live harness:
   - `pwsh ./Build/Run-ChatLiveConversation.ps1 -ScenarioFile ./IntelligenceX.Chat/scenarios/<scenario>.json -ExpectedTurns <n> -OutDir ./artifacts/chat-live`
