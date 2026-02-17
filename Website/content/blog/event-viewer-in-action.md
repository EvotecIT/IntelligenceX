---
title: Event Viewer in Action
description: A real IntelligenceX triage session that turns noisy AD event logs into a fix-first incident plan.
slug: event-viewer-in-action
collection: blog
layout: page
---

Event logs can be overwhelming fast, especially when three domain controllers are all shouting at once.
This walkthrough shows a real IntelligenceX session where we started broad, narrowed scope, pulled raw evidence, and ended with a practical fix-first order.

## The Situation

The ask was straightforward: check AD0, AD1, and AD2, pull relevant errors, and summarize what is wrong right now.

![Initial request and first executive summary](/assets/screenshots/event-viewer-in-action/event_viewer1.png)

## First Pass: Find the Cross-DC Pattern

The first response is a broad triage across System, Directory Service, and DNS Server channels.
It immediately highlights a pattern instead of isolated errors: AD1 and AD2 look noisier, AD0 has NETLOGON signal, and DNS/DS warnings appear across the estate.

![Cross-DC pattern with per-server highlights](/assets/screenshots/event-viewer-in-action/event_viewer2.png)

The summary is not locked in a paragraph either. You can open it as a table view and export it for sharing or deeper analysis.

![Assistant data view with export options](/assets/screenshots/event-viewer-in-action/event_viewer3_dataview.png)

The first pass also calls out caveats (like truncated high-volume channels), then proposes what to verify next and where to focus first.

![Per-server highlights with caveat and verification items](/assets/screenshots/event-viewer-in-action/event_viewer4.png)
![Verification section with DNS and DS follow-up logic](/assets/screenshots/event-viewer-in-action/event_viewer5.png)
![Recommended next fixes from the first pass](/assets/screenshots/event-viewer-in-action/event_viewer6.png)

## Second Pass: Narrow to What Is Active Now

This is where the flow becomes useful in practice. Instead of guessing, the assistant asks for scope: last 24h, last 2h, or since reboot.

![Scope-choice prompt for evidence-based triage](/assets/screenshots/event-viewer-in-action/event_viewer7.png)

After selecting 24h, the analysis immediately re-runs in a focused window.

![Focused 24h triage starts after scope confirmation](/assets/screenshots/event-viewer-in-action/event_viewer8.png)

## Third Pass: Separate Active Risk from Historic Noise

Now the correlation is time-aware. AD1 is flagged as highest active risk, AD0 as medium, AD2 as lower in this window.
That is a very different operational story than "everything is broken everywhere".

![24h active-risk breakdown by AD1 AD0 and AD2](/assets/screenshots/event-viewer-in-action/event_viewer9.png)

The assistant then gives targeted next fixes, not a shotgun list.

![Targeted next fixes after focused triage](/assets/screenshots/event-viewer-in-action/event_viewer10.png)

And before over-committing to root cause claims, it explicitly asks for one more evidence pull (raw event messages).

![Prompt to pull raw evidence before naming exact failing services](/assets/screenshots/event-viewer-in-action/event_viewer11.png)

## Fourth Pass: Raw Messages to Concrete Actions

With raw messages included, the output becomes specific: recurring Windows Admin Center crashes, Windows Update path issues, DCOM timeout CLSID evidence, DNS 4015 on AD1, and recurring NETLOGON 3210 on AD0.

![Raw-message evidence with concrete AD1 and AD0 findings](/assets/screenshots/event-viewer-in-action/event_viewer12.png)

From there, the assistant builds a clear fix-first order.

![Fix-first priority order after raw evidence correlation](/assets/screenshots/event-viewer-in-action/event_viewer13.png)

It also keeps traceability by pairing each signal with why it matters and the exact validation action.

![Potential issues to verify with mapped validation actions](/assets/screenshots/event-viewer-in-action/event_viewer14.png)

Finally, it closes with practical remediation actions in sequence.

![Recommended next fixes with service and auth-path actions](/assets/screenshots/event-viewer-in-action/event_viewer15.png)

## How Correlation Happens During the Run

What makes this useful is that correlation is not a one-shot result. It evolves as evidence improves:

- broad sweep first: normalize signals across DCs and channels
- scoped time windows next: separate active incidents from background noise
- evidence escalation: move from counts to raw event messages before naming causes
- cross-signal linking: connect service crashes, DCOM timeouts, DNS failures, and NETLOGON symptoms
- priority ordering: produce a fix-first path based on active blast radius

## Why This Helps in Real Operations

- You get a decision path, not just a pile of event IDs.
- You avoid panic by proving what is active now versus what is historical noise.
- You can hand off quickly using table/extract exports and concrete next validation steps.
- You get practical sequencing, so teams can fix in the right order and then re-check the same window for regression.

If you want, the next post can cover the remediation side only: what to run first on AD1, what to validate on AD0, and what "stable again" should look like in the next 2-hour recheck.
