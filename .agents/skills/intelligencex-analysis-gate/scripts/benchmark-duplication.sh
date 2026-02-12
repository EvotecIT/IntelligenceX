#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "$REPO_ROOT"

FILES="${FILES:-200}"
LINES="${LINES:-120}"
LANGUAGE="${LANGUAGE:-csharp}"
FRAMEWORK="${FRAMEWORK:-net8.0}"
KEEP_WORKDIR="${KEEP_WORKDIR:-0}"
WORKDIR="${WORKDIR:-}"

if [[ -z "$WORKDIR" ]]; then
  WORKDIR="$(mktemp -d "${TMPDIR:-/tmp}/ix-dup-bench-XXXXXX")"
fi

cleanup() {
  if [[ "$KEEP_WORKDIR" == "1" ]]; then
    echo "Workspace preserved at: $WORKDIR"
    return
  fi
  rm -rf "$WORKDIR"
}
trap cleanup EXIT

language_normalized="$(printf '%s' "$LANGUAGE" | tr '[:upper:]' '[:lower:]')"
case "$language_normalized" in
  csharp|cs)
    language_name="csharp"
    ext=".cs"
    ;;
  powershell|ps)
    language_name="powershell"
    ext=".ps1"
    ;;
  javascript|js)
    language_name="javascript"
    ext=".js"
    ;;
  typescript|ts)
    language_name="typescript"
    ext=".ts"
    ;;
  python|py)
    language_name="python"
    ext=".py"
    ;;
  *)
    echo "ERROR: unsupported LANGUAGE '$LANGUAGE'. Use csharp|powershell|javascript|typescript|python." >&2
    exit 1
    ;;
esac

mkdir -p "$WORKDIR/.intelligencex" "$WORKDIR/Analysis/Catalog/rules/internal" "$WORKDIR/Analysis/Packs" "$WORKDIR/src"

cat > "$WORKDIR/.intelligencex/reviewer.json" <<JSON
{
  "analysis": {
    "enabled": true,
    "packs": ["intelligencex-maintainability-default"]
  }
}
JSON

cat > "$WORKDIR/Analysis/Catalog/rules/internal/IXDUP001.json" <<JSON
{
  "id": "IXDUP001",
  "language": "internal",
  "tool": "IntelligenceX.Maintainability",
  "toolRuleId": "IXDUP001",
  "title": "Duplication benchmark rule",
  "description": "Synthetic duplication benchmark rule.",
  "category": "Maintainability",
  "defaultSeverity": "warning",
  "tags": [
    "max-duplication-percent:25",
    "dup-window-lines:8",
    "include-ext:${ext#.}"
  ]
}
JSON

cat > "$WORKDIR/Analysis/Packs/intelligencex-maintainability-default.json" <<JSON
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXDUP001"]
}
JSON

generate_csharp_file() {
  local file="$1"
  local i="$2"
  {
    echo "namespace Bench;"
    echo "public class C$i {"
    echo "    public int Sum$i(int input) {"
    echo "        var total = 0;"
    echo "        total += input;"
    local j
    for ((j=1; j<=LINES; j++)); do
      echo "        total += $j;"
    done
    echo "        return total;"
    echo "    }"
    echo "}"
  } > "$file"
}

generate_powershell_file() {
  local file="$1"
  local i="$2"
  {
    echo "function Get-Sum$i {"
    echo "    param([int]\$InputValue)"
    echo "    \$total = 0"
    echo "    \$total += \$InputValue"
    local j
    for ((j=1; j<=LINES; j++)); do
      echo "    \$total += $j"
    done
    echo "    return \$total"
    echo "}"
  } > "$file"
}

generate_javascript_file() {
  local file="$1"
  local i="$2"
  {
    echo "export function sum$i(inputValue) {"
    echo "  let total = 0;"
    echo "  total += inputValue;"
    local j
    for ((j=1; j<=LINES; j++)); do
      echo "  total += $j;"
    done
    echo "  return total;"
    echo "}"
  } > "$file"
}

generate_typescript_file() {
  local file="$1"
  local i="$2"
  {
    echo "export function sum$i(inputValue: number): number {"
    echo "  let total = 0;"
    echo "  total += inputValue;"
    local j
    for ((j=1; j<=LINES; j++)); do
      echo "  total += $j;"
    done
    echo "  return total;"
    echo "}"
  } > "$file"
}

generate_python_file() {
  local file="$1"
  local i="$2"
  {
    echo "def sum_$i(input_value):"
    echo "    total = 0"
    echo "    total += input_value"
    local j
    for ((j=1; j<=LINES; j++)); do
      echo "    total += $j"
    done
    echo "    return total"
  } > "$file"
}

echo "Generating synthetic sources: files=$FILES lines=$LINES language=$language_name workspace=$WORKDIR"
for ((i=1; i<=FILES; i++)); do
  file="$WORKDIR/src/sample_$i$ext"
  case "$language_name" in
    csharp) generate_csharp_file "$file" "$i" ;;
    powershell) generate_powershell_file "$file" "$i" ;;
    javascript) generate_javascript_file "$file" "$i" ;;
    typescript) generate_typescript_file "$file" "$i" ;;
    python) generate_python_file "$file" "$i" ;;
  esac
done

start="$(perl -MTime::HiRes=time -e 'printf("%.6f", time)')"
dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj --framework "$FRAMEWORK" -- \
  analyze run \
  --workspace "$WORKDIR" \
  --config "$WORKDIR/.intelligencex/reviewer.json" \
  --out "$WORKDIR/artifacts" >/dev/null
end="$(perl -MTime::HiRes=time -e 'printf("%.6f", time)')"
elapsed="$(awk -v s="$start" -v e="$end" 'BEGIN { printf "%.3f", (e - s) }')"

metrics_file="$WORKDIR/artifacts/intelligencex.duplication.json"
if [[ ! -f "$metrics_file" ]]; then
  echo "ERROR: expected duplication metrics file was not produced: $metrics_file" >&2
  exit 1
fi
total_significant="$(rg -o '"totalSignificantLines"\s*:\s*[0-9]+' "$metrics_file" | head -n1 | rg -o '[0-9]+' || echo 0)"
duplicated_significant="$(rg -o '"duplicatedSignificantLines"\s*:\s*[0-9]+' "$metrics_file" | head -n1 | rg -o '[0-9]+' || echo 0)"
overall_percent="$(rg -o '"overallDuplicatedPercent"\s*:\s*[0-9.]+' "$metrics_file" | head -n1 | rg -o '[0-9.]+' || echo 0)"

files_per_second="$(awk -v f="$FILES" -v t="$elapsed" 'BEGIN { if (t <= 0) { print "0.00" } else { printf "%.2f", f / t } }')"
lines_per_second="$(awk -v l="$total_significant" -v t="$elapsed" 'BEGIN { if (t <= 0) { print "0.00" } else { printf "%.2f", l / t } }')"

echo "Duplication benchmark complete"
echo "- Elapsed seconds: $elapsed"
echo "- Files generated: $FILES"
echo "- Significant lines: $total_significant"
echo "- Duplicated significant lines: $duplicated_significant"
echo "- Overall duplicated percent: $overall_percent"
echo "- Throughput (files/sec): $files_per_second"
echo "- Throughput (significant-lines/sec): $lines_per_second"
