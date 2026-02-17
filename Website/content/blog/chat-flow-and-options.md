---
title: Chat Flow and Options
description: A visual walkthrough of the IntelligenceX chat flow and the key option groups available in-session.
slug: chat-flow-and-options
collection: blog
layout: page
---

If you are new to IntelligenceX chat, this is the fastest way to build a mental model of how a full session runs.
You can think about it in four layers:

- the active chat flow for your current task
- profile-level defaults you can reuse
- system-level settings that shape global behavior
- tool visibility controls for safer, more focused runs

The screenshots below use an AD-focused session, but the same structure applies to other workflows.

## Chat Flow (AD-focused Session)

This sequence shows a realistic run from prompt to response.

### Step 1
![Chat flow step 1](/assets/screenshots/chat-flow/ChatAD_1_1.png)
Start with a concrete objective and enough context to avoid vague output.

### Step 2
![Chat flow step 2](/assets/screenshots/chat-flow/ChatAD_1_2.png)
Refine the ask when needed so the assistant can choose a better execution path.

### Step 3
![Chat flow step 3](/assets/screenshots/chat-flow/ChatAD_1_3.png)
Watch how the thread evolves. Small follow-ups usually work better than one giant prompt.

### Step 4
![Chat flow step 4](/assets/screenshots/chat-flow/ChatAD_1_4.png)
Use this stage to validate assumptions before you execute wider actions.

### Step 5
![Chat flow step 5](/assets/screenshots/chat-flow/ChatAD_1_5.png)
Close the loop with a summary, next actions, or a checkpoint for your team.

## Profile Options

Profiles are your repeatable presets. They reduce setup time and keep sessions consistent across team members.

### Profile View 1
![Profile option 1](/assets/screenshots/chat-flow/Profile_1_1.png)
Good for baseline defaults and standard team behavior.

### Profile View 2
![Profile option 2](/assets/screenshots/chat-flow/Profile_1_2.png)
Useful when you need different behavior for development, incident response, or review mode.

### Profile View 3
![Profile option 3](/assets/screenshots/chat-flow/Profile_1_3.png)
Treat profile changes like config changes: small, explicit, and easy to reason about.

## System Options

System settings define shared behavior beyond a single prompt or thread.

### System View 1
![System option 1](/assets/screenshots/chat-flow/System_1_1.png)
Set global expectations that should always be true.

### System View 2
![System option 2](/assets/screenshots/chat-flow/System_1_2.png)
Use system settings carefully. Small changes here can affect all sessions.

## Tools Available

Tool scope is where safety and productivity meet. Enable what you need, keep the rest off by default.

### Tools View 1
![Tools available 1](/assets/screenshots/chat-flow/ToolsAvailable_1_1.png)
Review available tools before starting a complex task.

### Tools View 2
![Tools available 2](/assets/screenshots/chat-flow/ToolsAvailable_1_2.png)
Narrow tool access when you want deterministic behavior.

### Tools View 3
![Tools available 3](/assets/screenshots/chat-flow/ToolsAvailable_1_3.png)
Expand tool access only when the task explicitly requires it.

### Tools View 4
![Tools available 4](/assets/screenshots/chat-flow/ToolsAvailable_1_4.png)
Double-check enabled tools before running high-impact operations.

## What Works Well In Practice

- Keep profiles task-specific. Smaller profile scope means fewer surprises.
- Prefer short iterative prompts over one very large instruction dump.
- Enable only the tools required for the task in front of you.
- Save successful flows as team playbooks so onboarding is faster.

If you want, the next post can break this down into a "first 10 minutes" quickstart with one concrete scenario end to end.
