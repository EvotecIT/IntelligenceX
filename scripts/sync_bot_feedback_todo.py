#!/usr/bin/env python3
import argparse
import json
import re
import subprocess
import sys
from dataclasses import dataclass
from typing import Dict, List, Optional, Sequence, Tuple


TASK_RE = re.compile(r"^\s*[-*]\s+\[(?P<state>[ xX])\]\s+(?P<text>.+?)\s*$")


@dataclass(frozen=True)
class TaskItem:
    checked: bool
    text: str
    url: str


@dataclass(frozen=True)
class PullRequestTasks:
    number: int
    title: str
    url: str
    tasks: List[TaskItem]


def run_gh(args: Sequence[str]) -> str:
    proc = subprocess.run(
        ["gh", *args],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
    )
    if proc.returncode != 0:
        raise RuntimeError(proc.stderr.strip() or f"gh failed: {' '.join(args)}")
    return proc.stdout


def html_escape(text: str) -> str:
    return (
        text.replace("&", "&amp;")
        .replace("<", "&lt;")
        .replace(">", "&gt;")
    )


def parse_tasks(body: str, url: str) -> List[TaskItem]:
    tasks: List[TaskItem] = []
    for line in (body or "").splitlines():
        m = TASK_RE.match(line)
        if not m:
            continue
        state = m.group("state")
        checked = state.lower() == "x"
        text = m.group("text").strip()
        if not text:
            continue
        tasks.append(TaskItem(checked=checked, text=text, url=url))
    return tasks


def fetch_open_pr_tasks(repo: str, bots: Sequence[str], limit: int) -> List[PullRequestTasks]:
    owner, name = repo.split("/", 1)
    bots_norm = {b.strip().lower() for b in bots if b.strip()}
    if not bots_norm:
        raise ValueError("No bots provided.")

    query = """
query($owner: String!, $name: String!, $n: Int!, $cursor: String) {
  repository(owner: $owner, name: $name) {
    pullRequests(states: OPEN, first: $n, after: $cursor, orderBy: { field: UPDATED_AT, direction: DESC }) {
      pageInfo { hasNextPage endCursor }
      nodes {
        number
        title
        url
        comments(last: 50) { nodes { author { login } body url } }
        reviews(last: 50)  { nodes { author { login } body url } }
      }
    }
  }
}
""".strip()

    out: List[PullRequestTasks] = []
    cursor: Optional[str] = None
    remaining = max(0, limit)
    page_size = min(50, remaining) if remaining else 50
    while remaining > 0:
        variables = {
            "owner": owner,
            "name": name,
            "n": min(page_size, remaining),
            "cursor": cursor,
        }
        raw = run_gh(["api", "graphql", "-f", f"query={query}", "-f", f"owner={owner}", "-f", f"name={name}", "-f", f"n={variables['n']}", *(
            ["-f", f"cursor={cursor}"] if cursor else []
        )])
        payload = json.loads(raw)
        pr_conn = payload["data"]["repository"]["pullRequests"]
        nodes = pr_conn.get("nodes") or []
        for pr in nodes:
            tasks: List[TaskItem] = []
            for c in (pr.get("comments") or {}).get("nodes") or []:
                login = ((c.get("author") or {}).get("login") or "").lower()
                if login in bots_norm:
                    tasks.extend(parse_tasks(c.get("body") or "", c.get("url") or pr.get("url") or ""))
            for r in (pr.get("reviews") or {}).get("nodes") or []:
                login = ((r.get("author") or {}).get("login") or "").lower()
                if login in bots_norm:
                    tasks.extend(parse_tasks(r.get("body") or "", r.get("url") or pr.get("url") or ""))
            if tasks:
                out.append(
                    PullRequestTasks(
                        number=int(pr["number"]),
                        title=str(pr.get("title") or ""),
                        url=str(pr.get("url") or ""),
                        tasks=tasks,
                    )
                )

        remaining -= len(nodes)
        if not pr_conn.get("pageInfo", {}).get("hasNextPage"):
            break
        cursor = pr_conn.get("pageInfo", {}).get("endCursor")
        if not cursor:
            break
    return out


def render_pr_block(pr: PullRequestTasks) -> str:
    title = pr.title.strip()
    summary = f"PR #{pr.number} {title}".strip()
    lines = []
    lines.append("<details>")
    lines.append(f"<summary>{html_escape(summary)}</summary>")
    lines.append("")
    for t in pr.tasks:
        checkbox = "x" if t.checked else " "
        # Keep a single link per task; avoid nested bullet structure.
        lines.append(f"- [{checkbox}] {t.text}. Links: {t.url}")
    lines.append("</details>")
    lines.append("")
    return "\n".join(lines)


def update_todo(todo_path: str, prs: List[PullRequestTasks]) -> Tuple[bool, str]:
    with open(todo_path, "r", encoding="utf-8") as f:
        original = f.read()

    header = "## Review Feedback Backlog (Bots)"
    idx = original.find(header)
    if idx < 0:
        raise RuntimeError(f"Missing section header in {todo_path}: {header}")

    # Insert/update right after the section intro paragraph.
    after_header = original.find("\n", idx)
    if after_header < 0:
        after_header = idx + len(header)
    insert_at = after_header + 1
    # Skip following blank lines and the section description line(s) if present.
    # We keep it simple: insert after the first blank line following the header block.
    m = re.search(rf"{re.escape(header)}\n(?:.*\n)*?\n", original[idx:])
    if m:
        insert_at = idx + m.end()

    text = original
    changed = False

    # Update existing blocks if present, else insert at top in the fetched order.
    inserts: List[str] = []
    for pr in prs:
        block = render_pr_block(pr)
        # Replace existing <details> block for this PR if present.
        pattern = re.compile(
            rf"(?s)<details>\n<summary>PR\s*#\s*{pr.number}\b.*?\n</details>\n\n"
        )
        if pattern.search(text):
            new_text = pattern.sub(block, text, count=1)
            if new_text != text:
                text = new_text
                changed = True
        else:
            inserts.append(block)

    if inserts:
        text = text[:insert_at] + "".join(inserts) + text[insert_at:]
        changed = True

    return changed, text


def main() -> int:
    ap = argparse.ArgumentParser(description="Sync bot checklist items from PR reviews/comments into TODO.md.")
    ap.add_argument("--repo", default="EvotecIT/IntelligenceX", help="GitHub repo (owner/name)")
    ap.add_argument("--todo", default="TODO.md", help="Path to TODO.md")
    ap.add_argument("--max-prs", type=int, default=30, help="Max open PRs to scan")
    ap.add_argument(
        "--bot",
        action="append",
        default=["intelligencex-review"],
        help="Bot login to include (repeatable). Default: intelligencex-review",
    )
    args = ap.parse_args()

    prs = fetch_open_pr_tasks(args.repo, args.bot, args.max_prs)
    if not prs:
        print("No bot checklist items found in open PRs.")
        return 0

    changed, updated = update_todo(args.todo, prs)
    if not changed:
        print("TODO.md already up to date.")
        return 0

    with open(args.todo, "w", encoding="utf-8") as f:
        f.write(updated)
    print(f"Updated {args.todo} with {len(prs)} PR blocks.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

