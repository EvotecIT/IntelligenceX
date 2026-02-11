# Skill: intelligencex-analysis-gate

Use this skill when changing analysis behavior or static-analysis policy:
- `Analysis/**`
- `IntelligenceX.Cli/Analysis/**`
- reviewer analysis loading/reporting/gate logic

## Trigger Phrases
- "analysis gate"
- "analyze run"
- "analysis packs"
- "catalog validation"
- "PSScriptAnalyzer"

## Strict Execution Order
1. Scope and rule-impact identification
2. Preflight + catalog integrity checks
3. Implement change
4. Run deterministic analysis suite
5. Verify strict/non-strict behavior where relevant
6. Record warnings/failure classification in PR notes

## Commands
- Run suite:
  - `.agents/skills/intelligencex-analysis-gate/scripts/run-analysis-suite.sh fast`
  - `.agents/skills/intelligencex-analysis-gate/scripts/run-analysis-suite.sh full`

## Fail-Fast Rules
- Fail if catalog validation is not clean.
- Fail if analyzer commands regress exit-code semantics.
- Fail if required test harnesses fail.

## References
- `references/command-matrix.md`
