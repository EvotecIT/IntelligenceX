# ADR-0001: Chat/Tools Contract Boundary

Status: Accepted  
Date: March 1, 2026  
Owners: IntelligenceX maintainers

## Context

The current architecture is mixed:

- Chat contains generic orchestration and policy logic.
- At proposal time, Chat also contained hardcoded per-pack fallback behavior (`PackCapabilityFallback` cross-pack builders).
- Tools already expose meaningful metadata (`ToolRoutingContract`, tags, `ToolPackGuidance`), but not enough to fully remove Chat hardcoding.

This makes new tool/pack onboarding expensive and increases routing drift risk.

## Decision

Adopt a strict contract-first boundary:

- Chat is orchestration-only (selection, execution loop, status, safety policy, transparency).
- Tools/packs declare routing, role, setup, handoff, and recovery contracts.
- Chat does not implement per-pack fallback execution behavior.
- Resilience fallback (for example provider/engine fallback like CIM -> WMI) lives inside tool/engine implementation, not Chat routing.
- Legacy compatibility for Chat fallback engine is not required; the hardcoded fallback surface was removed during this migration.

## Consequences

Positive:

- New tools/packs can be registered without editing Chat core logic.
- Domain boundaries (for example ADPlayground vs DomainDetective) become explicit and testable.
- Reduced hardcoded routing drift in Chat.

Tradeoffs:

- Initial migration touches both Tools and Chat.
- Existing fallback tests must be rewritten or removed as behavior is deleted.

## Migration Notes

- Execution and sequencing are tracked in root [PLAN.md](../../../PLAN.md) and [PLAN-EXECUTION-ORDER.md](../../../PLAN-EXECUTION-ORDER.md).
- Guardrail tests now prevent hardcoded Chat fallback behavior from being reintroduced.
