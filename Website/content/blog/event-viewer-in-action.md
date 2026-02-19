---
title: Event Viewer in Action
description: A real IntelligenceX triage session that turns noisy AD event logs into a fix-first incident plan.
slug: event-viewer-in-action
date: 2026-02-11
categories: ["Walkthrough"]
tags: ["event-viewer", "ad", "triage"]
image: /assets/screenshots/event-viewer-in-action/event_viewer1.png
collection: blog
layout: page
---

Event logs can be overwhelming fast, especially when three domain controllers are all shouting at once.
This walkthrough shows a real IntelligenceX session where we started broad, narrowed scope, pulled raw evidence, and ended with a practical fix-first order.

## The Situation

The ask was straightforward: check AD0, AD1, and AD2, pull relevant errors, and summarize what is wrong right now.

![Chat thread where the user requests AD0 AD1 AD2 event triage and the assistant begins with an executive summary](/assets/screenshots/event-viewer-in-action/event_viewer1.png)

## First Pass: Find the Cross-DC Pattern

The first response is a broad triage across System, Directory Service, and DNS Server channels.
It immediately highlights a pattern instead of isolated errors: AD1 and AD2 look noisier, AD0 has NETLOGON signal, and DNS/DS warnings appear across the estate.

![Detailed cross-domain-controller triage output highlighting DNS, Directory Service, and system instability signals by server](/assets/screenshots/event-viewer-in-action/event_viewer2.png)

The summary is not locked in a paragraph either. You can open it as a table view and export it for sharing or deeper analysis.

![Assistant Data View modal presenting tabular per-server signals with quick export buttons for CSV, Excel, and Word](/assets/screenshots/event-viewer-in-action/event_viewer3_dataview.png)

The first pass also calls out caveats (like truncated high-volume channels), then proposes what to verify next and where to focus first.

![Per-server highlights section followed by caveat about truncated event channels in the first-pass summary](/assets/screenshots/event-viewer-in-action/event_viewer4.png)
![Potential issues to verify section mapping DNS and Directory Service signals to explicit investigation steps](/assets/screenshots/event-viewer-in-action/event_viewer5.png)
![Recommended next fixes from the first pass prioritizing DNS dependency checks and NETLOGON validation](/assets/screenshots/event-viewer-in-action/event_viewer6.png)

## Second Pass: Narrow to What Is Active Now

This is where the flow becomes useful in practice. Instead of guessing, the assistant asks for scope: last 24h, last 2h, or since reboot.

![Assistant asks for scope selection between last 24 hours last 2 hours or since reboot for evidence triage](/assets/screenshots/event-viewer-in-action/event_viewer7.png)

After selecting 24h, the analysis immediately re-runs in a focused window.

![User confirms 24-hour scope and assistant starts focused triage with active instability framing](/assets/screenshots/event-viewer-in-action/event_viewer8.png)

## Third Pass: Separate Active Risk from Historic Noise

Now the correlation is time-aware. AD1 is flagged as highest active risk, AD0 as medium, AD2 as lower in this window.
That is a very different operational story than "everything is broken everywhere".

![Focused 24-hour breakdown showing AD1 highest risk AD0 medium risk and AD2 comparatively quiet](/assets/screenshots/event-viewer-in-action/event_viewer9.png)

The assistant then gives targeted next fixes, not a shotgun list.

![Targeted follow-up recommendations based on active signals rather than broad historical noise](/assets/screenshots/event-viewer-in-action/event_viewer10.png)

And before over-committing to root cause claims, it explicitly asks for one more evidence pull (raw event messages).

![Assistant requests raw event message extraction before naming exact crashing services and root causes](/assets/screenshots/event-viewer-in-action/event_viewer11.png)

## Fourth Pass: Raw Messages to Concrete Actions

With raw messages included, the output becomes specific: recurring Windows Admin Center crashes, Windows Update path issues, DCOM timeout CLSID evidence, DNS 4015 on AD1, and recurring NETLOGON 3210 on AD0.

![Raw-message triage confirming AD1 service failures DNS critical error and recurring AD0 NETLOGON authentication issue](/assets/screenshots/event-viewer-in-action/event_viewer12.png)

From there, the assistant builds a clear fix-first order.

![Fix-first priority order listing AD1 stabilization then AD0 NETLOGON remediation and cross-DC recheck](/assets/screenshots/event-viewer-in-action/event_viewer13.png)

It also keeps traceability by pairing each signal with why it matters and the exact validation action.

![Verification checklist linking each signal to why it matters and what validation step to run next](/assets/screenshots/event-viewer-in-action/event_viewer14.png)

Finally, it closes with practical remediation actions in sequence.

![Final recommended remediation actions including Windows Update service path fix, crash-loop containment, and NETLOGON hardening](/assets/screenshots/event-viewer-in-action/event_viewer15.png)

## Prompt Pack You Can Reuse

If you want the same signal progression in your own environment, this prompt sequence is a reliable starter:

```
Pass 1 (broad):
Check AD0, AD1, AD2 event logs for current errors and summarize cross-DC patterns.

Pass 2 (time scope):
Now scope that to last 24h and separate active risk from historic noise.

Pass 3 (evidence escalation):
Pull raw event messages for top recurring errors before naming root causes.

Pass 4 (fix order):
Build a fix-first sequence with validation steps after each fix.
```

## Export and Handoff Pattern

Use the Data View export buttons from the triage response at each pass:

1. Export broad-pass summary (CSV/Excel) for immediate team visibility.
2. Export scoped 24h summary (CSV/Excel) for on-call execution.
3. Export final fix-first checklist (Word/Excel) for change tracking.

This keeps the incident thread consistent from first triage to post-fix verification.

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

A remediation-focused follow-up can document the exact runbook: what to execute first on AD1, what to validate on AD0, and how to confirm stability in the next 2-hour recheck.
