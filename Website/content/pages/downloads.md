---
title: Downloads
description: Download IntelligenceX desktop builds and release artifacts with platform and release grouping.
slug: downloads
collection: pages
layout: page
meta.raw_html: true
---

<p>Download IntelligenceX release assets directly from this page. Source of truth remains <a href="https://github.com/EvotecIT/IntelligenceX/releases">GitHub Releases</a>.</p>

<p>If the cards below show <code>No releases found</code>, no desktop artifacts have been published yet. In the meantime, start with <a href="/docs/getting-started/">Getting Started</a> and project docs.</p>

<div class="ix-release-links" aria-label="Release resources">
  <a href="/changelog/">Changelog</a>
  <a href="/docs/getting-started/">Install Guide</a>
  <a href="/docs/">Documentation</a>
</div>

<div class="ix-release-cta">
{{< release-button placement="downloads.chat_stable" >}}
{{< release-button placement="downloads.chat_preview" >}}
</div>

<section class="pf-release-downloads-panel">
  <h2>Stable Downloads By Platform</h2>
  <p>Choose the stable IX Chat build for your platform.</p>
{{< release-buttons placement="downloads.chat_platforms" >}}
</section>

<section class="pf-release-downloads-panel">
  <h2>Stable Downloads By Release</h2>
  <p>Browse stable packages grouped by release tag.</p>
{{< release-buttons placement="downloads.chat_releases" >}}
</section>

<section class="pf-release-downloads-panel">
  <h2>All Stable Product Assets</h2>
  <p>Cross-product view for releases that publish more than one package in the same tag.</p>
{{< release-buttons placement="downloads.all_products" >}}
</section>
