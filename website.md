# IntelligenceX Website - Improvement Ideas

## Current State Assessment

The website has a landing page with feature sections, 15 docs pages covering configuration and usage, an API reference generated from XML docs, and dark/light theme support. The design uses a cyan-to-purple gradient accent. Inter font is self-hosted.

### What's Working Well
- Clean, professional landing page with feature grid
- Comprehensive docs covering setup, configuration, reviewers, output formats
- API reference with sidebar navigation and light/dark theme support
- Custom 404 page
- Consistent design system with CSS variables

### Content Gaps
- No screenshots anywhere (docs are entirely text-based)
- No visual workflow guide showing what a review looks like
- No before/after examples of code reviews
- No integration guides for CI/CD platforms
- No architecture or data flow diagrams
- No troubleshooting/FAQ content
- No changelog or release history
- Docs pages have no visual aids (all prose + code snippets)

---

## Ideas

### 1. Visual Workflow Guide / Screenshots

**Priority**: High

The biggest gap. Users can't see what IntelligenceX actually does until they install and run it. Add screenshots showing:

- Running a review from PowerShell (terminal output)
- The generated HTML report (full page screenshot)
- Review findings with severity indicators
- Diff view / change annotations
- Summary dashboard with metrics
- Configuration wizard web UI (if applicable)

Each docs page that describes a feature should include a screenshot of that feature in action.

### 2. Before/After Code Review Examples

**Priority**: High

Show concrete examples of what IntelligenceX catches:

- Security issues (hardcoded secrets, SQL injection patterns)
- Code quality (complexity, naming, dead code)
- Style violations (formatting, conventions)
- Documentation gaps (missing XML docs, README updates)
- Performance concerns (N+1 queries, unnecessary allocations)

Format as cards: "Before" code snippet, finding description, "After" code snippet. Makes the value proposition immediately tangible.

### 3. Getting Started Walkthrough

**Priority**: High

A step-by-step page with visuals:

1. Install the module (terminal screenshot)
2. Configure your first review target (config file snippet + explanation)
3. Run a review (terminal output screenshot)
4. Read the results (HTML report screenshot)
5. Customize rules (config snippet)
6. Integrate into CI pipeline (YAML snippet)

Each step: numbered card with code/config on one side, screenshot/output on the other.

### 4. CI/CD Integration Guides

**Priority**: Medium-High

Dedicated pages for each platform:

- **Azure DevOps Pipelines** - YAML task definition, artifact publishing, PR comments
- **GitHub Actions** - Workflow file, status checks, PR annotations
- **GitLab CI** - .gitlab-ci.yml configuration, merge request integration
- **Jenkins** - Pipeline script, report archiving
- **Local / Pre-commit** - Git hooks, developer workflow

Each guide should be complete and copy-paste ready with a working pipeline definition.

### 5. Configuration Reference Enhancement

**Priority**: Medium

Current docs cover configuration but could be improved with:

- Visual config builder / interactive form
- Annotated config file with comments explaining each option
- Config examples for common scenarios (small project, enterprise monorepo, OSS library)
- Validation - what happens with invalid config (error messages shown)

### 6. Reviewer Deep Dives

**Priority**: Medium

Expand on each built-in reviewer with:

- What it checks (comprehensive rule list)
- Example findings with code snippets
- Configuration options specific to that reviewer
- When to enable/disable it
- Custom rule creation guide

### 7. Output Format Gallery

**Priority**: Medium

Show what each output format looks like:

- HTML report (full screenshot + interactive demo if possible)
- JSON output (formatted sample)
- CSV output (spreadsheet screenshot)
- Markdown output (rendered preview)
- Console output (terminal screenshot)

Help users pick the right format for their workflow.

### 8. Architecture & How It Works

**Priority**: Medium-Low

For users who want to understand the system:

- High-level architecture diagram (input -> analysis -> output pipeline)
- How reviewers are loaded and configured
- Extension points for custom reviewers
- Data flow: what gets sent where (important for security-conscious users)
- Performance characteristics (file count, repo size scaling)

### 9. Comparison Page

**Priority**: Medium-Low

Position IntelligenceX relative to alternatives:

- vs SonarQube - lighter weight, PowerShell native, no server needed
- vs Roslyn analyzers - broader scope, report generation, CI integration
- vs ReSharper CLI - different focus areas, complementary usage
- Feature comparison table

### 10. Changelog / Release Notes

**Priority**: Medium

A `/changelog/` page:

- Version history with dates
- New reviewers and rules added
- Breaking changes highlighted
- Links to GitHub releases

### 11. Troubleshooting / FAQ

**Priority**: Medium-Low

Common issues:

- "Review takes too long" - File exclusion patterns, reviewer selection
- "Too many false positives" - Severity thresholds, rule suppression
- "Module won't install" - PowerShell version requirements, Gallery issues
- "Report doesn't open" - Browser/path issues, output format selection
- "Can't find config file" - Config search paths, precedence rules

### 12. Community & Contributing

**Priority**: Low

- How to write custom reviewers
- How to contribute rules
- How to report false positives
- Links to GitHub discussions/issues
- Roadmap or planned features

### 13. Enterprise Features Page

**Priority**: Low

If applicable:

- Team configuration sharing
- Baseline management (suppress existing issues)
- Trend tracking over time
- Policy enforcement
- Custom report branding

---

## Content Assets Needed

| Asset | Purpose | Format |
|-------|---------|--------|
| HTML report screenshots | Workflow guide, output gallery | PNG (light + dark) |
| Terminal output screenshots | Getting started, CI guides | PNG |
| Config editor screenshots | Configuration docs | PNG |
| Architecture diagram | How it works page | SVG |
| CI pipeline screenshots | Integration guides | PNG |
| Before/after code examples | Review examples page | Code snippets |
| Reviewer output samples | Reviewer deep dives | PNG + code |

## Priority Order

1. Screenshots for existing docs (biggest bang for effort)
2. Visual getting started walkthrough
3. Before/after code review examples
4. CI/CD integration guides (Azure DevOps first)
5. Output format gallery
6. Reviewer deep dives
7. Comparison page
8. Changelog
9. Troubleshooting/FAQ
10. Everything else
