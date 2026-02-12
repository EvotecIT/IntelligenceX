# UI / UX Requirements (Draft)

Target: Windows tray app with a chat UI that looks intentional and can handle rich content.

App name (planned): **IntelligenceX Chat**.

## Must-haves

- Tray icon with quick actions:
  - Open/Hide window
  - New chat
  - Pause tool execution (panic button)
- Chat timeline:
  - user messages
  - assistant messages (markdown)
  - tool call blocks (collapsible)
  - error blocks (distinct styling)
- Rich rendering:
  - markdown
  - code fences
  - tables (GitHub-flavored)
  - Mermaid diagrams (fenced `mermaid` blocks)
- Copy/export:
  - copy message
  - copy table as TSV/CSV
  - export conversation as Markdown

## Tool trace UX

Each tool call should show:
- tool name + parameters (formatted)
- execution status + duration
- output preview + full output on expand

Tool output contract (for `summary_markdown`, `meta`, `render`): `Docs/tool-output-contract.md`

## Staging for high-risk actions

For actions like “send email” or “apply AD changes”:
- tools should produce a “draft/plan” object
- UI shows an approval step
- only after approval the tool executes the final action
