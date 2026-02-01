---
title: IntelligenceX - AI-Powered Code Review for GitHub
description: Zero-trust GitHub Actions reviewer using your own ChatGPT or Copilot account. Your credentials, your GitHub App, your control.
slug: index
collection: pages
layout: home
meta.raw_html: true
---

<!-- Hero Section -->
<div class="hero">
    <div class="hero-badge">
        <span class="hero-badge-dot"></span>
        <span>Open Source &bull; MIT License</span>
    </div>

    <h1>AI Code Reviews Using Your Own<br/>ChatGPT or Copilot Account</h1>

    <p class="hero-tagline">
        Zero-trust GitHub Actions reviewer. No API keys, no backend, no middleman.
        Your credentials, your GitHub App, your control.
    </p>

    <div class="hero-buttons">
        <a href="/getting-started/" class="btn btn-primary">
            <svg class="btn-icon" viewBox="0 0 24 24" fill="currentColor"><path d="M8 5v14l11-7z"/></svg>
            Get Started
        </a>
        <a href="https://github.com/EvotecIT/IntelligenceX" target="_blank" class="btn btn-ghost">
            <svg class="btn-icon" viewBox="0 0 24 24" fill="currentColor"><path d="M12 0c-6.626 0-12 5.373-12 12 0 5.302 3.438 9.8 8.207 11.387.599.111.793-.261.793-.577v-2.234c-3.338.726-4.033-1.416-4.033-1.416-.546-1.387-1.333-1.756-1.333-1.756-1.089-.745.083-.729.083-.729 1.205.084 1.839 1.237 1.839 1.237 1.07 1.834 2.807 1.304 3.492.997.107-.775.418-1.305.762-1.604-2.665-.305-5.467-1.334-5.467-5.931 0-1.311.469-2.381 1.236-3.221-.124-.303-.535-1.524.117-3.176 0 0 1.008-.322 3.301 1.23.957-.266 1.983-.399 3.003-.404 1.02.005 2.047.138 3.006.404 2.291-1.552 3.297-1.23 3.297-1.23.653 1.653.242 2.874.118 3.176.77.84 1.235 1.911 1.235 3.221 0 4.609-2.807 5.624-5.479 5.921.43.372.823 1.102.823 2.222v3.293c0 .319.192.694.801.576 4.765-1.589 8.199-6.086 8.199-11.386 0-6.627-5.373-12-12-12z"/></svg>
            View on GitHub
        </a>
    </div>

    <div class="hero-command">
        <span class="prompt">$</span>
        <code>intelligencex setup wizard</code>
    </div>
</div>

<!-- How It Works -->
<section class="how-it-works">
    <div class="section-header">
        <span class="section-label">How It Works</span>
        <h2>Three Steps to AI Reviews</h2>
        <p>From zero to automated code reviews in under a minute.</p>
    </div>

    <div class="steps-grid">
        <div class="step-card">
            <div class="step-number">1</div>
            <h3>Authenticate with YOUR Account</h3>
            <p>Use your existing ChatGPT or Copilot login. No API keys needed, no middleman. Your credentials go straight to the provider.</p>
        </div>
        <div class="step-card">
            <div class="step-number">2</div>
            <h3>Set Up in Seconds</h3>
            <p>Run the CLI wizard or open the local web UI. Pick repos, choose a preset, and IntelligenceX creates a PR with the workflow.</p>
        </div>
        <div class="step-card">
            <div class="step-number">3</div>
            <h3>Get AI Reviews on Every PR</h3>
            <p>The reviewer runs automatically in GitHub Actions. Inline comments, summaries, or hybrid mode. Auto-resolves stale threads.</p>
        </div>
    </div>
</section>

<!-- Features -->
<section class="features">
    <div class="section-header">
        <span class="section-label">The Toolkit</span>
        <h2>Four Powerful Components</h2>
        <p>Everything you need for AI-powered development workflows.</p>
    </div>

    <div class="features-grid">
        <div class="feature-card featured">
            <div class="feature-icon">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5"><path d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z"/></svg>
            </div>
            <h3>GitHub Actions Reviewer</h3>
            <p>AI-powered PR reviews using ChatGPT or GitHub Copilot. Inline comments, summaries, or hybrid mode. Configurable presets from minimal to picky. Codex-style structured output with auto-resolve.</p>
            <span class="feature-tag">Star Feature</span>
            <br/><br/>
            <a href="/docs/reviewer/overview/" class="feature-link">Learn more &rarr;</a>
        </div>
        <div class="feature-card">
            <div class="feature-icon">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5"><path d="M8 9l3 3-3 3m5 0h3M5 20h14a2 2 0 002-2V6a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z"/></svg>
            </div>
            <h3>CLI Tools</h3>
            <p>One-command onboarding wizard. Local web UI. Multi-repo support, auth management, and usage tracking.</p>
            <a href="/docs/cli/overview/" class="feature-link">Learn more &rarr;</a>
        </div>
        <div class="feature-card">
            <div class="feature-icon">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5"><path d="M10 20l4-16m4 4l4 4-4 4M6 16l-4-4 4-4"/></svg>
            </div>
            <h3>.NET Library</h3>
            <p>Codex app-server client, Easy.ChatAsync one-liner, JSON-RPC support for building your own AI tools.</p>
            <a href="/docs/library/overview/" class="feature-link">Learn more &rarr;</a>
        </div>
        <div class="feature-card">
            <div class="feature-icon">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5"><path d="M9.663 17h4.673M12 3v1m6.364 1.636l-.707.707M21 12h-1M4 12H3m3.343-5.657l-.707-.707m2.828 9.9a5 5 0 117.072 0l-.548.547A3.374 3.374 0 0014 18.469V19a2 2 0 11-4 0v-.531c0-.895-.356-1.754-.988-2.386l-.548-.547z"/></svg>
            </div>
            <h3>PowerShell Module</h3>
            <p>Binary cmdlets for scripting and automation. Connect, send messages, run diagnostics.</p>
            <a href="/docs/powershell/overview/" class="feature-link">Learn more &rarr;</a>
        </div>
    </div>
</section>

<!-- Trust Banner -->
<section class="trust-banner">
    <div class="trust-card">
        <h2>You Don't Have to Trust Us</h2>
        <div class="trust-points">
            <div class="trust-point">
                <svg class="trust-check" viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="#10B981" stroke-width="2.5"><path d="M20 6L9 17l-5-5"/></svg>
                <span><strong>No backend service</strong> &mdash; everything runs locally or in your GitHub Actions</span>
            </div>
            <div class="trust-point">
                <svg class="trust-check" viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="#10B981" stroke-width="2.5"><path d="M20 6L9 17l-5-5"/></svg>
                <span><strong>Secrets never leave your machine</strong> &mdash; stored in GitHub Actions secrets you own</span>
            </div>
            <div class="trust-point">
                <svg class="trust-check" viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="#10B981" stroke-width="2.5"><path d="M20 6L9 17l-5-5"/></svg>
                <span><strong>Bring Your Own GitHub App</strong> &mdash; your org identity, your permissions, your audit trail</span>
            </div>
            <div class="trust-point">
                <svg class="trust-check" viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="#10B981" stroke-width="2.5"><path d="M20 6L9 17l-5-5"/></svg>
                <span><strong>All changes via PRs</strong> &mdash; review every workflow change before it's applied</span>
            </div>
        </div>
        <a href="/security/" class="btn btn-ghost" style="border-color: rgba(16, 185, 129, 0.4); color: #10B981;">Learn About Our Security Model &rarr;</a>
    </div>
</section>

<!-- Code Examples -->
<section class="code-examples">
    <div class="section-header">
        <span class="section-label">See It In Action</span>
        <h2>Multiple Ways to Use IntelligenceX</h2>
        <p>From GitHub Actions to C# and PowerShell.</p>
    </div>

    <div class="code-tabs">
        <button class="code-tab active" data-tab="workflow">GitHub Actions</button>
        <button class="code-tab" data-tab="config">Configuration</button>
        <button class="code-tab" data-tab="csharp">C# Library</button>
        <button class="code-tab" data-tab="powershell">PowerShell</button>
    </div>
    <div class="code-panel active" data-panel="workflow">
<pre><code><span style="color:#94A3B8"># .github/workflows/review-intelligencex.yml</span>
<span style="color:#00D9FF">jobs</span>:
  <span style="color:#00D9FF">review</span>:
    <span style="color:#8B5CF6">uses</span>: evotecit/github-actions/.github/workflows/review-intelligencex.yml@master
    <span style="color:#8B5CF6">with</span>:
      <span style="color:#8B5CF6">reviewer_source</span>: release
      <span style="color:#8B5CF6">openai_transport</span>: native
      <span style="color:#8B5CF6">output_style</span>: claude
      <span style="color:#8B5CF6">style</span>: colorful
    <span style="color:#8B5CF6">secrets</span>: inherit</code></pre>
    </div>
    <div class="code-panel" data-panel="config">
<pre><code>{
  <span style="color:#00D9FF">"review"</span>: {
    <span style="color:#8B5CF6">"provider"</span>: <span style="color:#10B981">"openai"</span>,
    <span style="color:#8B5CF6">"model"</span>: <span style="color:#10B981">"gpt-5.2-codex"</span>,
    <span style="color:#8B5CF6">"mode"</span>: <span style="color:#10B981">"hybrid"</span>,
    <span style="color:#8B5CF6">"length"</span>: <span style="color:#10B981">"long"</span>,
    <span style="color:#8B5CF6">"outputStyle"</span>: <span style="color:#10B981">"claude"</span>,
    <span style="color:#8B5CF6">"reviewUsageSummary"</span>: <span style="color:#F59E0B">false</span>
  }
}</code></pre>
    </div>
    <div class="code-panel" data-panel="csharp">
<pre><code><span style="color:#8B5CF6">using</span> IntelligenceX.OpenAI;

<span style="color:#94A3B8">// One-liner - send a message and get a response</span>
<span style="color:#8B5CF6">var</span> result = <span style="color:#8B5CF6">await</span> Easy.ChatAsync(<span style="color:#10B981">"Review this code for security issues"</span>);
Console.WriteLine(result.Text);

<span style="color:#94A3B8">// Full app-server client</span>
<span style="color:#8B5CF6">var</span> client = <span style="color:#8B5CF6">await</span> AppServerClient.StartAsync(<span style="color:#8B5CF6">new</span> AppServerOptions {
    ExecutablePath = <span style="color:#10B981">"codex"</span>,
    Arguments = <span style="color:#10B981">"app-server"</span>
});
<span style="color:#8B5CF6">var</span> thread = <span style="color:#8B5CF6">await</span> client.StartThreadAsync(<span style="color:#10B981">"gpt-5.2-codex"</span>);
<span style="color:#8B5CF6">await</span> client.StartTurnAsync(thread.Id, <span style="color:#10B981">"Hello from IntelligenceX"</span>);</code></pre>
    </div>
    <div class="code-panel" data-panel="powershell">
<pre><code><span style="color:#94A3B8"># Import and connect</span>
Import-Module IntelligenceX

<span style="color:#00D9FF">$session</span> = Connect-IntelligenceX -Diagnostics
Initialize-IntelligenceX
Start-IntelligenceXLogin
Wait-IntelligenceXLogin

<span style="color:#94A3B8"># Start a thread and send a message</span>
<span style="color:#00D9FF">$thread</span> = Start-IntelligenceXThread -Model <span style="color:#10B981">'gpt-5.2-codex'</span>
Send-IntelligenceXMessage -ThreadId <span style="color:#00D9FF">$thread</span>.Id -Content <span style="color:#10B981">'Analyze this code'</span>

Disconnect-IntelligenceX</code></pre>
    </div>
</section>

<!-- FAQ -->
<section class="faq">
    <div class="section-header">
        <span class="section-label">FAQ</span>
        <h2>Frequently Asked Questions</h2>
    </div>

    <div class="faq-list">
        {{ for item in data.faq }}
        <details class="faq-item">
            <summary>{{ item.question }}</summary>
            <div class="faq-answer">{{ item.answer }}</div>
        </details>
        {{ end }}
    </div>
</section>

<!-- CTA -->
<section class="how-it-works" style="text-align: center;">
    <h2>Ready to Get Started?</h2>
    <p style="max-width: 500px; margin: 0 auto 1.5rem;">Set up AI-powered code reviews in under a minute. No backend, no API keys, just your own credentials.</p>
    <div class="hero-buttons">
        <a href="/getting-started/" class="btn btn-primary">Get Started</a>
        <a href="/docs/" class="btn btn-secondary">Read the Docs</a>
    </div>
</section>
