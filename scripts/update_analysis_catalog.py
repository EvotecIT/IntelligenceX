#!/usr/bin/env python3
import argparse
import json
import os
import re
from pathlib import Path
from typing import Dict, List, Tuple

RULE_HEADER_RE = re.compile(r"^## \[(CA\d{4})\]\(([^)]+)\):\s*(.+?)\s*$")
RECOMMENDED_RE = re.compile(r"dotnet_diagnostic\.(CA\d{4})\.severity\s*=\s*(\w+)", re.IGNORECASE)

SEVERITY_MAP = {
    "hidden": "none",
    "info": "info",
    "warning": "warning",
    "error": "error",
}


def normalize_text(value: str) -> str:
    return re.sub(r"\s+", " ", value or "").strip()


def find_latest_nuget_dir(base: Path, package_id: str) -> Path:
    package_root = base / package_id.lower()
    if not package_root.exists():
        raise FileNotFoundError(f"NuGet package folder not found: {package_root}")
    versions = sorted([p for p in package_root.iterdir() if p.is_dir()], key=lambda p: p.name)
    if not versions:
        raise FileNotFoundError(f"No version folders under: {package_root}")
    return versions[-1]


def parse_net_analyzers_markdown(path: Path) -> Dict[str, Dict[str, str]]:
    lines = path.read_text(encoding="utf-8").splitlines()
    index = 0
    rules: Dict[str, Dict[str, str]] = {}

    while index < len(lines):
        match = RULE_HEADER_RE.match(lines[index])
        if not match:
            index += 1
            continue

        rule_id, docs_url, title = match.groups()
        index += 1

        description_lines: List[str] = []
        while index < len(lines):
            line = lines[index]
            if line.startswith("|Item|Value|"):
                break
            if line.strip() == "---":
                break
            if line.strip():
                description_lines.append(line.strip())
            index += 1

        category = "General"
        severity = "warning"

        if index < len(lines) and lines[index].startswith("|Item|Value|"):
            index += 1
            if index < len(lines) and lines[index].startswith("|-|-|"):
                index += 1
            while index < len(lines):
                line = lines[index].strip()
                if not line.startswith("|"):
                    break
                parts = [part.strip() for part in line.strip("|").split("|", 1)]
                if len(parts) == 2:
                    key = parts[0].lower()
                    value = normalize_text(parts[1])
                    if key == "category" and value:
                        category = value
                    if key == "severity" and value:
                        severity = SEVERITY_MAP.get(value.lower(), "warning")
                index += 1

        while index < len(lines) and lines[index].strip() != "---":
            index += 1
        if index < len(lines) and lines[index].strip() == "---":
            index += 1

        description = normalize_text(" ".join(description_lines))
        if not description:
            description = title

        rules[rule_id] = {
            "id": rule_id,
            "language": "csharp",
            "tool": "Microsoft.CodeAnalysis.NetAnalyzers",
            "toolRuleId": rule_id,
            "title": normalize_text(title),
            "description": description,
            "category": category,
            "defaultSeverity": severity,
            "docs": docs_url,
        }

    return rules


def write_rule_files(repo_root: Path, rules: Dict[str, Dict[str, str]]) -> int:
    csharp_root = repo_root / "Analysis" / "Catalog" / "rules" / "csharp"
    csharp_root.mkdir(parents=True, exist_ok=True)

    existing_ca_files = [file for file in csharp_root.glob("CA*.json") if file.is_file()]
    for file in existing_ca_files:
        file.unlink()

    for rule_id in sorted(rules.keys()):
        out_path = csharp_root / f"{rule_id}.json"
        out_path.write_text(json.dumps(rules[rule_id], indent=2) + "\n", encoding="utf-8")

    return len(rules)


def read_recommended_ids(path: Path) -> List[str]:
    ids: List[str] = []
    seen = set()
    for line in path.read_text(encoding="utf-8").splitlines():
        match = RECOMMENDED_RE.search(line)
        if not match:
            continue
        rule_id = match.group(1).upper()
        if rule_id not in seen:
            seen.add(rule_id)
            ids.append(rule_id)
    return ids


def read_catalog_csharp_ids(repo_root: Path) -> List[str]:
    csharp_root = repo_root / "Analysis" / "Catalog" / "rules" / "csharp"
    ids: List[str] = []
    for file in sorted(csharp_root.glob("*.json"), key=lambda p: p.name.lower()):
        try:
            payload = json.loads(file.read_text(encoding="utf-8"))
        except Exception:
            continue
        rule_id = (payload.get("id") or "").strip()
        if rule_id:
            ids.append(rule_id)
    return ids


def build_tier(seed: List[str], prioritized: List[str], allowed: set, limit: int) -> List[str]:
    ordered: List[str] = []
    seen = set()

    for rule_id in seed + prioritized:
        if rule_id not in allowed:
            continue
        if rule_id in seen:
            continue
        ordered.append(rule_id)
        seen.add(rule_id)
        if len(ordered) >= limit:
            break

    return ordered


def write_pack_rules(pack_path: Path, rules: List[str]) -> None:
    payload = json.loads(pack_path.read_text(encoding="utf-8"))
    payload["rules"] = rules
    pack_path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description="Update IntelligenceX analysis catalog + tiered C# packs.")
    parser.add_argument("--repo-root", default=".", help="Repository root path")
    parser.add_argument("--nuget-root", default=str(Path.home() / ".nuget" / "packages"), help="NuGet packages root")
    parser.add_argument("--analysis-level", default="9", help="NetAnalyzers analysis level for recommended tier source")
    args = parser.parse_args()

    repo_root = Path(args.repo_root).resolve()
    nuget_root = Path(args.nuget_root).resolve()

    net_pkg = find_latest_nuget_dir(nuget_root, "microsoft.codeanalysis.netanalyzers")
    markdown_path = net_pkg / "documentation" / "Microsoft.CodeAnalysis.NetAnalyzers.md"
    if not markdown_path.exists():
        raise FileNotFoundError(f"NetAnalyzers documentation file not found: {markdown_path}")

    recommended_path = net_pkg / "buildTransitive" / "config" / f"analysislevel_{args.analysis_level}_recommended.globalconfig"
    if not recommended_path.exists():
        raise FileNotFoundError(f"Recommended config file not found: {recommended_path}")

    rules = parse_net_analyzers_markdown(markdown_path)
    generated_count = write_rule_files(repo_root, rules)

    recommended_ids = read_recommended_ids(recommended_path)
    catalog_ids = read_catalog_csharp_ids(repo_root)
    allowed_ids = set(catalog_ids)

    pinned = ["CA2000", "CA1062", "SA1600"]

    csharp_50 = build_tier(pinned, recommended_ids, allowed_ids, 50)
    csharp_100 = build_tier(csharp_50, recommended_ids, allowed_ids, 100)
    csharp_500 = build_tier(csharp_100, recommended_ids + sorted(catalog_ids, key=str.upper), allowed_ids, 500)

    packs_root = repo_root / "Analysis" / "Packs"
    write_pack_rules(packs_root / "csharp-50.json", csharp_50)
    write_pack_rules(packs_root / "csharp-100.json", csharp_100)
    write_pack_rules(packs_root / "csharp-500.json", csharp_500)

    print(f"Generated CA rule files: {generated_count}")
    print(f"csharp-50 size: {len(csharp_50)}")
    print(f"csharp-100 size: {len(csharp_100)}")
    print(f"csharp-500 size: {len(csharp_500)}")
    print(f"Recommended source: {recommended_path}")
    print(f"Rule metadata source: {markdown_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
