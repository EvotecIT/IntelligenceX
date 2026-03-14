// ── Helpers ──
async function fetchJsonSafe(url, options) {
  const res = await fetch(url, options);
  const contentType = res.headers.get('content-type') || '';
  let payload = null;
  if (contentType.includes('application/json')) {
    try {
      payload = await res.json();
    } catch {
      payload = null;
    }
  } else {
    const text = await res.text();
    if (text && text.trim().length > 0) {
      payload = { error: text };
    }
  }
  if (!res.ok) {
    const message = payload && payload.error
      ? payload.error
      : `Request failed (${res.status} ${res.statusText || 'Error'})`;
    throw new Error(message);
  }
  return payload || {};
}

function getSetupRequestHeaders() {
  return {
    'Content-Type': 'application/json',
    'X-IntelligenceX-Setup-Request': '1'
  };
}

function normalizeRecommendedPath(path) {
  const normalized = String(path || '').trim().toLowerCase();
  if (onboardingPathMap[normalized]) {
    return normalized;
  }
  switch (normalized) {
    case 'refresh-auth':
    case 'fix-expired-auth':
    case 'update-secret':
      return 'refresh-auth';
    case 'cleanup':
      return 'cleanup';
    case 'maintenance':
      return 'maintenance';
    case 'new-setup':
    case 'setup':
    default:
      return 'new-setup';
  }
}

function summarizeAutoDetectChecks(checks) {
  let ok = 0;
  let warn = 0;
  let fail = 0;
  (checks || []).forEach(check => {
    const status = String(check.status || '').toLowerCase();
    if (status === 'fail') fail++;
    else if (status === 'warn') warn++;
    else ok++;
  });
  return { ok, warn, fail };
}

function formatAutoDetectOutput(data) {
  const lines = [];
  lines.push(`status: ${data.status || 'unknown'}`);
  lines.push(`workspace: ${data.workspace || '-'}`);
  lines.push(`repo: ${data.repo || '(not detected)'}`);
  if (data.contractVersion) lines.push(`contract version: ${data.contractVersion}`);
  if (data.contractFingerprint) lines.push(`contract fingerprint: ${data.contractFingerprint}`);
  lines.push(`local workflow: ${data.localWorkflowExists ? 'yes' : 'no'}`);
  lines.push(`local config: ${data.localConfigExists ? 'yes' : 'no'}`);
  lines.push(`recommended path: ${data.recommendedPath || '-'}`);
  if (data.recommendedReason) lines.push(`reason: ${data.recommendedReason}`);
  if (data.commandTemplates) {
    lines.push(`command auto-detect: ${data.commandTemplates.autoDetect || '-'}`);
    lines.push(`command setup apply: ${data.commandTemplates.newSetupApply || '-'}`);
    lines.push(`command update-secret apply: ${data.commandTemplates.refreshAuthApply || '-'}`);
    lines.push(`command cleanup apply: ${data.commandTemplates.cleanupApply || '-'}`);
  }
  lines.push('');
  lines.push('checks:');
  (data.checks || []).forEach(check => {
    lines.push(`- [${check.status || 'ok'}] ${check.message || ''}`);
  });
  if (data.dailyBreakdown && data.dailyBreakdown.data && data.dailyBreakdown.data.length > 0) {
    lines.push('');
    lines.push(`Daily token breakdown${data.dailyBreakdown.units ? ` (${data.dailyBreakdown.units})` : ''}:`);
    data.dailyBreakdown.data.forEach(day => {
      const values = day.productSurfaceUsageValues || {};
      const active = Object.entries(values)
        .filter(([, value]) => typeof value === 'number' && Math.abs(value) > 0)
        .sort((a, b) => b[1] - a[1]);
      if (active.length === 0) {
        lines.push(`- ${day.date || '-'} | total 0`);
        return;
      }
      const total = active.reduce((sum, [, value]) => sum + value, 0);
      const parts = active.map(([surface, value]) => `${surface} ${value}`);
      lines.push(`- ${day.date || '-'} | total ${total} | ${parts.join(' | ')}`);
    });
  }
  return lines.join('\n');
}

function renderAutoDetect(data) {
  const summaryEl = $('autodetectSummary');
  const outputEl = $('autodetectOutput');
  const applyBtn = $('applyAutodetectPath');
  if (!summaryEl || !outputEl) return;

  const recommendedPath = normalizeRecommendedPath(data.recommendedPath);
  const counts = summarizeAutoDetectChecks(data.checks);
  const status = String(data.status || '').toLowerCase();
  const statusText = status === 'fail'
    ? 'Fail'
    : status === 'warn'
      ? 'Warning'
      : 'OK';
  const contractText = data.contractVersion ? ` | contract: ${data.contractVersion}` : '';
  summaryEl.textContent = `${statusText} | checks: ${counts.ok} ok, ${counts.warn} warn, ${counts.fail} fail | Suggested path: ${recommendedPath}${contractText}. ${data.recommendedReason || ''}`;
  outputEl.textContent = formatAutoDetectOutput(data);
  setOnboardingPathContracts(data.paths, data.contractVersion, data.contractFingerprint);
  syncOnboardingPathVisualState();

  if (applyBtn) {
    applyBtn.dataset.path = recommendedPath;
    applyBtn.disabled = !recommendedPath;
  }
}

async function runAutoDetect() {
  const summaryEl = $('autodetectSummary');
  const outputEl = $('autodetectOutput');
  const runBtn = $('runAutodetect');
  const repoInput = $('repo');
  if (summaryEl) summaryEl.textContent = 'Running doctor preflight...';
  if (outputEl) outputEl.textContent = '';
  if (runBtn) runBtn.disabled = true;
  try {
    const data = await fetchJsonSafe('/api/setup/autodetect', {
      method: 'POST',
      headers: getSetupRequestHeaders(),
      body: JSON.stringify({
        repoHint: repoInput ? repoInput.value.trim() : ''
      })
    });
    lastAutodetect = data;
    renderAutoDetect(data);
  } catch (e) {
    setOnboardingPathContracts(null, null, null);
    syncOnboardingPathVisualState();
    if (summaryEl) summaryEl.textContent = `Auto-detect failed: ${e.message || e}`;
    if (outputEl) outputEl.textContent = '';
  } finally {
    if (runBtn) runBtn.disabled = false;
  }
}

function getToken() {
  return (token ? token.value.trim() : '') || '';
}

function selectedRepos() {
  const selected = Array.from(repoList.selectedOptions).map(opt => opt.value);
  if (selected.length > 0) return selected;
  if (repo && repo.value.trim()) return [repo.value.trim()];
  return [];
}

function write(text) {
  const outputEl = $('output');
  const outputCard = $('outputCard');
  if (outputEl) outputEl.textContent = text;
  if (outputCard && text) outputCard.style.display = 'block';
}

function setSummary(text) {
  lastSummaryBase = text;
  renderSummary();
}

function setUsageSummary(text) {
  lastUsageSummary = text;
  renderSummary();
}

function renderSummary() {
  if (lastUsageSummary && lastUsageSummary.trim().length > 0) {
    summary.textContent = `${lastSummaryBase}\n\nUsage:\n${lastUsageSummary}`;
    return;
  }
  summary.textContent = lastSummaryBase;
}

function updateRepoCount() {
  const count = selectedRepos().length;
  const el = $('repoCount');
  if (el) el.textContent = `${count} repo${count !== 1 ? 's' : ''} selected`;
}

function escapeHtml(value) {
  return String(value ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

function coerceBoolean(value) {
  if (value === true || value === false) return value;
  if (typeof value === 'number') return value === 0 ? false : (value === 1 ? true : null);
  if (typeof value === 'string') {
    const normalized = value.trim().toLowerCase();
    if (normalized === 'true' || normalized === '1' || normalized === 'yes') return true;
    if (normalized === 'false' || normalized === '0' || normalized === 'no') return false;
  }
  return null;
}

function normalizeOpenAiAccountIdsCsv(raw) {
  if (!raw || typeof raw !== 'string') return [];
  const seen = new Set();
  const values = [];
  raw.split(',').forEach(part => {
    const value = String(part || '').trim();
    if (!value) return;
    const key = value.toLowerCase();
    if (seen.has(key)) return;
    seen.add(key);
    values.push(value);
  });
  return values;
}

function normalizeLoopPolicy(rawValue) {
  const value = String(rawValue || '').trim().toLowerCase();
  if (!value) return '';
  return value;
}

function isVisionLoopPolicy(rawValue) {
  return normalizeLoopPolicy(rawValue) === 'vision';
}

function shouldIncludeMergeBlockerBooleansInPayload() {
  // Preserve loop-policy defaults unless the user explicitly touched these controls.
  return mergeBlockerRequireAllSectionsTouched || mergeBlockerRequireSectionMatchTouched;
}

// ── Build review grid ──
function buildReviewTable() {
  const grid = $('reviewGrid');
  if (!grid) return;

  const repos = selectedRepos();
  const withConfigEffective = (withConfig && withConfig.checked) || (configJson && configJson.value.trim().length > 0) || (configPath && configPath.value.trim().length > 0);
  const hasConfigOverride = (configJson && configJson.value.trim().length > 0) || (configPath && configPath.value.trim().length > 0);
  const analysisState = selectedOperation !== 'setup' || !withConfigEffective || hasConfigOverride
    ? 'not applicable'
    : (analysisEnabled && analysisEnabled.checked ? 'enabled' : 'disabled');
  const analysisExportPathValue = analysisState === 'enabled' && analysisExportPath && analysisExportPath.value.trim().length > 0
    ? analysisExportPath.value.trim()
    : '';
  const analysisRunStrictValue = analysisState === 'enabled' && analysisRunStrict && analysisRunStrict.checked;
  const reviewTweaksApply = selectedOperation === 'setup' && withConfigEffective && !hasConfigOverride;
  const reviewIntentValue = reviewIntentInput ? reviewIntentInput.value.trim() : '';
  const reviewStrictnessValue = reviewStrictnessInput ? reviewStrictnessInput.value.trim() : '';
  const reviewLoopPolicyValue = reviewLoopPolicy ? reviewLoopPolicy.value : '';
  const reviewLoopPolicyKey = normalizeLoopPolicy(reviewLoopPolicyValue);
  const reviewVisionPathValue = reviewVisionPathInput ? reviewVisionPathInput.value.trim() : '';
  const mergeBlockerSectionsValue = mergeBlockerSectionsInput ? mergeBlockerSectionsInput.value.trim() : '';
  const mergeBlockerRequireAllSectionsValue = mergeBlockerRequireAllSectionsInput
    ? !!mergeBlockerRequireAllSectionsInput.checked
    : true;
  const mergeBlockerRequireSectionMatchValue = mergeBlockerRequireSectionMatchInput
    ? !!mergeBlockerRequireSectionMatchInput.checked
    : true;
  const includeMergeBlockerBooleans = shouldIncludeMergeBlockerBooleansInPayload();
  const visionPolicySelected = isVisionLoopPolicy(reviewLoopPolicyKey);
  const includeReviewVisionPath = visionPolicySelected && reviewVisionPathValue.length > 0;
  const providerLabel = selectedProvider === 'openai' ? 'ChatGPT / OpenAI' : 'GitHub Copilot';
  const profileLabels = {
    balanced: 'Balanced',
    picky: 'Strict',
    security: 'Security',
    highlevel: 'Minimal',
    performance: 'Performance',
    tests: 'Tests'
  };
  const safeOperation = escapeHtml(selectedOperation);
  const safeProviderLabel = escapeHtml(providerLabel);
  const safeProfile = escapeHtml(profileLabels[selectedPresetProfile] || selectedPresetProfile);
  const safeReviewMode = escapeHtml(reviewMode && reviewMode.value ? reviewMode.value : 'default');
  const safeReviewCommentMode = escapeHtml(reviewCommentMode && reviewCommentMode.value ? reviewCommentMode.value : 'default');
  const safeAnalysisState = escapeHtml(analysisState);
  const safeAnalysisExportPath = escapeHtml(analysisExportPathValue);
  const safeAnalysisRunStrict = analysisRunStrictValue ? 'enabled' : 'disabled';
  const safeReviewIntent = escapeHtml(reviewIntentValue || '(default)');
  const safeReviewStrictness = escapeHtml(reviewStrictnessValue || '(default)');
  const safeReviewLoopPolicy = escapeHtml(reviewLoopPolicyValue || '(default)');
  const safeReviewVisionPath = escapeHtml(
    includeReviewVisionPath
      ? reviewVisionPathValue
      : (visionPolicySelected ? '(auto: VISION.md|vision.md)' : '(not sent)')
  );
  const safeMergeBlockerSections = escapeHtml(mergeBlockerSectionsValue || '(default)');
  const safeMergeBlockerRequireAllSections = includeMergeBlockerBooleans
    ? (mergeBlockerRequireAllSectionsValue ? 'true' : 'false')
    : '(preset/default)';
  const safeMergeBlockerRequireSectionMatch = includeMergeBlockerBooleans
    ? (mergeBlockerRequireSectionMatchValue ? 'true' : 'false')
    : '(preset/default)';
  const openAiAccountIdValue = openAiAccountIdInput ? openAiAccountIdInput.value.trim() : '';
  const openAiAccountIdsValue = openAiAccountIdsInput
    ? normalizeOpenAiAccountIdsCsv(openAiAccountIdsInput.value).join(',')
    : '';
  const openAiAccountRotationValue = openAiAccountRotation ? openAiAccountRotation.value : 'first-available';
  const openAiAccountFailoverValue = openAiAccountFailover ? openAiAccountFailover.checked : true;
  const accountRoutingApplies = selectedOperation === 'setup' &&
    selectedProvider === 'openai' &&
    withConfigEffective &&
    !hasConfigOverride;
  const safeOpenAiAccountId = escapeHtml(openAiAccountIdValue || '(auto)');
  const safeOpenAiAccountIds = escapeHtml(openAiAccountIdsValue || '(not set)');
  const safeOpenAiAccountRotation = escapeHtml(openAiAccountRotationValue);
  const safeOpenAiAccountFailover = openAiAccountFailoverValue ? 'enabled' : 'disabled';
  const safeRepoHtml = repos.map(r => `<code>${escapeHtml(r)}</code>`).join(' ');

  let html = `
    <div class="review-section">
      <div class="review-section-title">GitHub</div>
      <div class="review-item">
        <span class="review-label">Authentication</span>
        <span class="review-value">${getToken() ? '<span class="badge badge-ok">&#x2713; Connected</span>' : '<span class="badge badge-warn">Not connected</span>'}</span>
      </div>
      <div class="review-item">
        <span class="review-label">Repositories</span>
        <span class="review-value">${repos.length > 0 ? `<strong>${repos.length}</strong> selected` : '<span class="badge badge-warn">None</span>'}</span>
      </div>
      ${repos.length > 0 && repos.length <= 5 ? `<div class="review-repos">${safeRepoHtml}</div>` : ''}
    </div>

    <div class="review-section">
      <div class="review-section-title">Configuration</div>
      <div class="review-item">
        <span class="review-label">Operation</span>
        <span class="review-value"><strong>${safeOperation}</strong></span>
      </div>
      <div class="review-item">
        <span class="review-label">AI Provider</span>
        <span class="review-value">${safeProviderLabel}</span>
      </div>
      <div class="review-item">
        <span class="review-label">Review Profile</span>
        <span class="review-value">${safeProfile}</span>
      </div>
      <div class="review-item">
        <span class="review-label">With config</span>
        <span class="review-value">${withConfigEffective ? 'yes' : 'no'}</span>
      </div>
      <div class="review-item">
        <span class="review-label">Review mode</span>
        <span class="review-value">${safeReviewMode}</span>
      </div>
      <div class="review-item">
        <span class="review-label">Comment mode</span>
        <span class="review-value">${safeReviewCommentMode}</span>
      </div>
      <div class="review-item">
        <span class="review-label">Static analysis</span>
        <span class="review-value">${safeAnalysisState}</span>
      </div>
      ${analysisState === 'enabled' ? `
      <div class="review-item">
        <span class="review-label">Analysis runner strict</span>
        <span class="review-value">${safeAnalysisRunStrict}</span>
      </div>` : ''}
      ${analysisExportPathValue ? `
      <div class="review-item">
        <span class="review-label">Analysis export path</span>
        <span class="review-value"><code>${safeAnalysisExportPath}</code></span>
      </div>` : ''}
      ${reviewTweaksApply ? `
      <div class="review-item">
        <span class="review-label">Review intent</span>
        <span class="review-value">${safeReviewIntent}</span>
      </div>
      <div class="review-item">
        <span class="review-label">Review strictness</span>
        <span class="review-value">${safeReviewStrictness}</span>
      </div>
      <div class="review-item">
        <span class="review-label">Loop policy</span>
        <span class="review-value">${safeReviewLoopPolicy}</span>
      </div>
      <div class="review-item">
        <span class="review-label">Vision file path</span>
        <span class="review-value">${safeReviewVisionPath}</span>
      </div>
      <div class="review-item">
        <span class="review-label">Merge blocker sections</span>
        <span class="review-value">${safeMergeBlockerSections}</span>
      </div>
      <div class="review-item">
        <span class="review-label">Require all sections</span>
        <span class="review-value">${safeMergeBlockerRequireAllSections}</span>
      </div>
      <div class="review-item">
        <span class="review-label">Require section match</span>
        <span class="review-value">${safeMergeBlockerRequireSectionMatch}</span>
      </div>` : ''}
      ${accountRoutingApplies ? `
      <div class="review-item">
        <span class="review-label">OpenAI primary account</span>
        <span class="review-value">${safeOpenAiAccountId}</span>
      </div>
      <div class="review-item">
        <span class="review-label">OpenAI account ids</span>
        <span class="review-value">${safeOpenAiAccountIds}</span>
      </div>
      <div class="review-item">
        <span class="review-label">OpenAI account rotation</span>
        <span class="review-value">${safeOpenAiAccountRotation}</span>
      </div>
      <div class="review-item">
        <span class="review-label">OpenAI account failover</span>
        <span class="review-value">${safeOpenAiAccountFailover}</span>
      </div>` : ''}
    </div>
  `;

  if (!shouldSkipSecrets()) {
    const secretLabels = {
      login: 'ChatGPT browser login',
      paste: 'Auth bundle (pasted)',
      file: 'Auth bundle (file path)',
      skip: 'Skipped (set up later)'
    };
    const safeSecretMethod = escapeHtml(secretLabels[secretOption] || secretOption);
    html += `
      <div class="review-section">
        <div class="review-section-title">AI Authentication</div>
        <div class="review-item">
          <span class="review-label">Method</span>
          <span class="review-value">${safeSecretMethod}</span>
        </div>
      </div>
    `;
  }

  grid.innerHTML = html;
  refreshEffectiveConfigPreview();
}

async function refreshEffectiveConfigPreview() {
  const previewEl = $('effectiveConfigPreview');
  const noteEl = $('effectiveConfigNote');
  if (!previewEl || !noteEl) return;

  previewEl.textContent = '(loading...)';
  noteEl.textContent = 'Generated from your current setup choices.';

  try {
    const data = await fetchJsonSafe('/api/setup/effective-config', {
      method: 'POST',
      headers: getSetupRequestHeaders(),
      body: JSON.stringify(buildRequestBody(true))
    });

    if (data && data.config && String(data.config).trim().length > 0) {
      previewEl.textContent = data.config;
    } else {
      previewEl.textContent = '(no config preview available)';
    }

    if (data && data.note) {
      noteEl.textContent = data.note;
    }
  } catch (e) {
    console.warn('Effective config preview refresh failed.', e);
    previewEl.textContent = '(preview unavailable)';
    noteEl.textContent = 'Effective config preview is unavailable.';
  }
}

function clearOutput() {
  $('output').textContent = '';
  $('summary').textContent = '';
  $('outputCard').style.display = 'none';
}

function showOutput(text) {
  $('output').textContent = text;
  $('outputCard').style.display = 'block';
}

// ── Build request body ──
function buildRequestBody(dryRun) {
  const skipSecret = shouldSkipSecrets() || secretOption === 'skip';
  const hasConfigOverride = (configJson.value.trim().length > 0) || (configPath.value.trim().length > 0);
  const wantAnalysis = selectedOperation === 'setup' && withConfig.checked && !hasConfigOverride;
  const wantReviewTweaks = selectedOperation === 'setup' && withConfig.checked && !hasConfigOverride;
  const wantOpenAiAccountRouting = selectedOperation === 'setup' &&
    selectedProvider === 'openai' &&
    withConfig.checked &&
    !hasConfigOverride;
  const analysisEnabledValue = wantAnalysis && analysisEnabled && analysisEnabled.checked ? true : null;
  const analysisOn = analysisEnabledValue === true;
  const analysisRunStrictValue = analysisOn && analysisRunStrict && analysisRunStrict.checked;
  const packsRaw = analysisPacks ? analysisPacks.value.trim() : '';
  const exportPathRaw = analysisExportPath ? analysisExportPath.value.trim() : '';
  const reviewIntentValue = reviewIntentInput ? reviewIntentInput.value.trim() : '';
  const reviewStrictnessValue = reviewStrictnessInput ? reviewStrictnessInput.value.trim() : '';
  const reviewLoopPolicyValue = reviewLoopPolicy ? reviewLoopPolicy.value.trim() : '';
  const reviewLoopPolicyKey = normalizeLoopPolicy(reviewLoopPolicyValue);
  const reviewVisionPathValue = reviewVisionPathInput ? reviewVisionPathInput.value.trim() : '';
  const mergeBlockerSectionsValue = mergeBlockerSectionsInput ? mergeBlockerSectionsInput.value.trim() : '';
  const mergeBlockerRequireAllSectionsValue = mergeBlockerRequireAllSectionsInput
    ? !!mergeBlockerRequireAllSectionsInput.checked
    : true;
  const mergeBlockerRequireSectionMatchValue = mergeBlockerRequireSectionMatchInput
    ? !!mergeBlockerRequireSectionMatchInput.checked
    : true;
  const openAiAccountIdValue = openAiAccountIdInput ? openAiAccountIdInput.value.trim() : '';
  const openAiAccountIdsValue = openAiAccountIdsInput
    ? normalizeOpenAiAccountIdsCsv(openAiAccountIdsInput.value).join(',')
    : '';
  const openAiAccountRotationValue = openAiAccountRotation ? openAiAccountRotation.value : 'first-available';
  const openAiAccountFailoverValue = openAiAccountFailover ? !!openAiAccountFailover.checked : true;
  const body = {
    repos: selectedRepos(),
    gitHubToken: getToken(),
    gitHubClientId: clientId ? clientId.value.trim() : '',
    withConfig: withConfig.checked || (configJson.value.trim().length > 0) || (configPath.value.trim().length > 0),
    configJson: configJson.value.trim(),
    configPath: configPath.value.trim(),
    authB64: authB64 ? authB64.value.trim() : '',
    authB64Path: authB64Path ? authB64Path.value.trim() : '',
    provider: selectedProvider,
    reviewProfile: selectedPresetProfile,
    reviewMode: reviewMode.value,
    reviewCommentMode: reviewCommentMode.value,
    skipSecret: skipSecret,
    explicitSecrets: explicitSecrets.checked,
    dryRun: dryRun,
    upgrade: upgrade.checked,
    force: force.checked,
    branchName: branchName.value.trim(),
    cleanup: selectedOperation === 'cleanup',
    updateSecret: selectedOperation === 'update-secret',
    keepSecret: keepSecret.checked
  };
  if (wantOpenAiAccountRouting) {
    if (openAiAccountIdValue.length > 0) body.openAIAccountId = openAiAccountIdValue;
    if (openAiAccountIdsValue.length > 0) {
      body.openAIAccountIds = openAiAccountIdsValue;
    }
    if (openAiAccountIdValue.length > 0 || openAiAccountIdsValue.length > 0) {
      body.openAIAccountRotation = openAiAccountRotationValue;
      body.openAIAccountFailover = openAiAccountFailoverValue;
    }
  }
  if (wantReviewTweaks) {
    if (reviewIntentValue.length > 0) body.reviewIntent = reviewIntentValue;
    if (reviewStrictnessValue.length > 0) body.reviewStrictness = reviewStrictnessValue;
    if (reviewLoopPolicyValue.length > 0) body.reviewLoopPolicy = reviewLoopPolicyValue;
    if (isVisionLoopPolicy(reviewLoopPolicyKey) && reviewVisionPathValue.length > 0) {
      body.reviewVisionPath = reviewVisionPathValue;
    }
    if (mergeBlockerSectionsValue.length > 0) body.mergeBlockerSections = mergeBlockerSectionsValue;
    const includeMergeBlockerBooleans = shouldIncludeMergeBlockerBooleansInPayload();
    if (includeMergeBlockerBooleans) {
      body.mergeBlockerRequireAllSections = mergeBlockerRequireAllSectionsValue;
      body.mergeBlockerRequireSectionMatch = mergeBlockerRequireSectionMatchValue;
    }
  }
  if (wantAnalysis) {
    body.analysisEnabled = analysisEnabledValue;
    if (analysisOn) {
      body.analysisGateEnabled = !!(analysisGate && analysisGate.checked);
      body.analysisRunStrict = !!analysisRunStrictValue;
      if (packsRaw.length > 0) body.analysisPacks = packsRaw;
      if (exportPathRaw.length > 0) body.analysisExportPath = exportPathRaw;
    }
  }
  return body;
}
