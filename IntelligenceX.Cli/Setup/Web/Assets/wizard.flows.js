// ── Device flow ──
let pollingActive = false;

async function startDeviceFlow(clientIdValue, infoElement) {
  write('Starting device flow...');
  const startBtn = $('deviceStart');
  if (startBtn) {
    startBtn.disabled = true;
    startBtn.textContent = 'Opening browser...';
  }
  try {
    const data = await fetchJsonSafe('/api/device-code', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ clientId: clientIdValue })
    });
    deviceState = data;
    deviceState.clientId = clientIdValue;
    if (infoElement) {
      infoElement.innerHTML = `
        <div class="device-code-display">
          <div class="device-code-label">Your code:</div>
          <div class="device-code-value">${data.userCode}</div>
          <div class="device-code-hint">Enter this code at <a href="${data.verificationUri}" target="_blank">github.com/login/device</a></div>
        </div>
        <div class="polling-status">
          <span class="spinner"></span> Waiting for authorization...
        </div>
      `;
    }
    window.open(data.verificationUri, '_blank');
    // Auto-start polling
    startAutoPolling();
    return data;
  } catch (e) {
    write('Device flow error: ' + (e.message || e));
    if (startBtn) {
      startBtn.disabled = false;
      startBtn.textContent = 'Sign in with GitHub';
    }
    return null;
  }
}

function startAutoPolling() {
  if (pollingActive || !deviceState) return;
  pollingActive = true;
  pollLoop();
}

async function pollLoop() {
  if (!pollingActive || !deviceState) return;

  try {
    const data = await fetchJsonSafe('/api/device-poll', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        clientId: deviceState.clientId,
        deviceCode: deviceState.deviceCode,
        intervalSeconds: 2, // Poll faster for better UX
        expiresIn: 30 // Short timeout per poll, we'll retry
      })
    });

    if (data.token) {
      // Success!
      pollingActive = false;
      token.value = data.token;
      write('Signed in successfully!');
      $('deviceInfo').innerHTML = '<div class="auth-success"><span class="check-icon">&#x2713;</span> Authenticated with GitHub</div>';
      const startBtn = $('deviceStart');
      if (startBtn) {
        startBtn.textContent = 'Signed in';
        startBtn.classList.add('success');
      }
      // Auto-advance after short delay
      setTimeout(() => goToStep(1), 500);
      return;
    }

    if (data.error && !data.error.includes('authorization_pending') && !data.error.includes('expired')) {
      // Real error
      write('Auth error: ' + data.error);
      pollingActive = false;
      resetDeviceFlow();
      return;
    }

    // Still waiting, poll again after interval
    if (pollingActive) {
      setTimeout(pollLoop, (deviceState.intervalSeconds || 5) * 1000);
    }
  } catch (e) {
    // Network error, retry
    if (pollingActive) {
      setTimeout(pollLoop, 5000);
    }
  }
}

function resetDeviceFlow() {
  pollingActive = false;
  deviceState = null;
  const startBtn = $('deviceStart');
  if (startBtn) {
    startBtn.disabled = false;
    startBtn.textContent = 'Sign in with GitHub';
    startBtn.classList.remove('success');
  }
  $('deviceInfo').innerHTML = '';
}

$('deviceStart').addEventListener('click', async () => {
  if (pollingActive) return; // Already in progress
  const effectiveClientId = getEffectiveClientId();
  if (!effectiveClientId) {
    write('No Client ID available.');
    return;
  }
  await startDeviceFlow(effectiveClientId, $('deviceInfo'));
});

// Custom device flow handlers
const customDeviceStart = $('customDeviceStart');
if (customDeviceStart) {
  customDeviceStart.addEventListener('click', async () => {
    const customId = $('customClientId');
    if (!customId || !customId.value.trim()) {
      write('Please enter a Client ID.');
      return;
    }
    await startDeviceFlow(customId.value.trim(), $('customDeviceInfo'));
  });
}

// ── App flow ──
$('createApp').addEventListener('click', async () => {
  write('Starting GitHub App manifest flow...');
  try {
    const data = await fetchJsonSafe('/api/app-manifest', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        appName: appName.value.trim() || 'IntelligenceX Reviewer',
        owner: appOwner.value.trim()
      })
    });
    appId.value = data.appId || '';
    appPem.value = data.pem || '';
    updateAppControls();
    write('GitHub App created. Install it, then list installations.');
  } catch (e) {
    write('App error: ' + (e.message || e));
  }
});

$('listInstalls').addEventListener('click', async () => {
  write('Loading installations...');
  const id = parseInt(appId.value, 10);
  if (!id || !appPem.value.trim()) { write('Provide App ID and PEM first.'); return; }
  try {
    const data = await fetchJsonSafe('/api/app-installations', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ appId: id, pem: appPem.value })
    });
    installation.innerHTML = '';
    (data.installations || []).forEach(item => {
      const opt = document.createElement('option');
      opt.value = item.id;
      opt.textContent = `${item.login} (id ${item.id})`;
      installation.appendChild(opt);
    });
    updateAppControls();
    write(data.installations?.length ? 'Select an installation and generate a token.' : 'No installations found.');
  } catch (e) {
    write('Error: ' + (e.message || e));
  }
});

$('useInstallationToken').addEventListener('click', async () => {
  const id = parseInt(appId.value, 10);
  const installId = parseInt(installation.value, 10);
  if (!id || !installId || !appPem.value.trim()) { write('Provide App ID, PEM, and installation.'); return; }
  write('Generating installation token...');
  try {
    const data = await fetchJsonSafe('/api/app-token', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ appId: id, pem: appPem.value, installationId: installId })
    });
    token.value = data.token || '';
    write('Installation token acquired!');
    goToStep(1); // Auto-advance
  } catch (e) {
    write('Error: ' + (e.message || e));
  }
});

function updateAppControls() {
  $('listInstalls').disabled = !appId.value.trim() || !appPem.value.trim();
  $('useInstallationToken').disabled = !appId.value.trim() || !appPem.value.trim() || !installation.value;
}

// ── Token paste: auto-advance on paste ──
if (token) {
  token.addEventListener('input', () => {
    // No auto-advance, user clicks Next
  });
}

// ── Repo filter ──
repoFilter.addEventListener('input', () => {
  const filterText = repoFilter.value;
  const activeOrgBtn = document.querySelector('.org-btn.active');
  const filterOrg = activeOrgBtn ? activeOrgBtn.dataset.org : null;
  renderRepoList(filterOrg || null, filterText);
});

repoList.addEventListener('change', updateRepoCount);
if (repo) repo.addEventListener('input', updateRepoCount);

// ── Config auto-enable ──
configJson.addEventListener('input', () => {
  if (configJson.value.trim()) withConfig.checked = true;
  updateAnalysisControls();
  updateReviewConfigControls();
  updateOpenAiAccountControls();
});
configPath.addEventListener('input', () => {
  if (configPath.value.trim()) withConfig.checked = true;
  updateAnalysisControls();
  updateReviewConfigControls();
  updateOpenAiAccountControls();
});
withConfig.addEventListener('change', () => {
  updateAnalysisControls();
  updateReviewConfigControls();
  updateOpenAiAccountControls();
});

// ── Static analysis toggle ──
function updateAnalysisControls() {
  const hasConfigOverride = (configJson.value.trim().length > 0) || (configPath.value.trim().length > 0);
  const applicable = selectedOperation === 'setup' && withConfig.checked && !hasConfigOverride;
  const enabled = applicable && analysisEnabled && analysisEnabled.checked;

  if (analysisEnabled) analysisEnabled.disabled = !applicable;
  if (analysisGate) analysisGate.disabled = !enabled;
  if (analysisRunStrict) analysisRunStrict.disabled = !enabled;
  if (analysisPacks) analysisPacks.disabled = !enabled;
  if (analysisExportPath) analysisExportPath.disabled = !enabled;

  const hint = $('analysisHint');
  if (hint) {
    hint.textContent = applicable
      ? 'Leave empty to use defaults. Examples: all-50, all-multilang-50, all-100, all-500, all-security-50, all-security-default, powershell-50, javascript-50, python-50. Browse packs: intelligencex analyze list-rules --workspace <repo-root> --format markdown. Gate semantics/docs: Docs/reviewer/static-analysis.md'
      : 'Static analysis settings apply only when generating config from presets (no Config JSON/path override).';
  }
}
if (analysisEnabled) analysisEnabled.addEventListener('change', updateAnalysisControls);
updateAnalysisControls();

function updateReviewConfigControls() {
  const hasConfigOverride = (configJson.value.trim().length > 0) || (configPath.value.trim().length > 0);
  const applicable = selectedOperation === 'setup' && withConfig.checked && !hasConfigOverride;
  const visionPolicySelected = reviewLoopPolicy && reviewLoopPolicy.value === 'vision';

  if (reviewIntentInput) reviewIntentInput.disabled = !applicable;
  if (reviewStrictnessInput) reviewStrictnessInput.disabled = !applicable;
  if (reviewLoopPolicy) reviewLoopPolicy.disabled = !applicable;
  if (reviewVisionPathInput) reviewVisionPathInput.disabled = !applicable || !visionPolicySelected;
  if (mergeBlockerSectionsInput) mergeBlockerSectionsInput.disabled = !applicable;
  if (mergeBlockerRequireAllSectionsInput) mergeBlockerRequireAllSectionsInput.disabled = !applicable;
  if (mergeBlockerRequireSectionMatchInput) mergeBlockerRequireSectionMatchInput.disabled = !applicable;

  const hint = $('mergeBlockerHint');
  if (hint) {
    hint.textContent = applicable
      ? (visionPolicySelected
        ? 'Use this to match your repo review contract and no-blockers thread sweep behavior. Vision path is used for intent/strictness inference.'
        : 'Use this to match your repo review contract and no-blockers thread sweep behavior. Vision path is available when loop policy is vision.')
      : 'Review strictness, vision, and merge-blocker loop settings apply only when generating reviewer config (no Config JSON/path override).';
  }
}
updateReviewConfigControls();
if (reviewLoopPolicy) reviewLoopPolicy.addEventListener('change', updateReviewConfigControls);
if (mergeBlockerRequireAllSectionsInput) {
  mergeBlockerRequireAllSectionsInput.addEventListener('change', () => {
    mergeBlockerRequireAllSectionsTouched = true;
  });
}
if (mergeBlockerRequireSectionMatchInput) {
  mergeBlockerRequireSectionMatchInput.addEventListener('change', () => {
    mergeBlockerRequireSectionMatchTouched = true;
  });
}

function updateOpenAiAccountControls() {
  const hasConfigOverride = (configJson.value.trim().length > 0) || (configPath.value.trim().length > 0);
  const applicable = selectedOperation === 'setup' &&
    selectedProvider === 'openai' &&
    withConfig.checked &&
    !hasConfigOverride;

  if (openAiAccountIdInput) openAiAccountIdInput.disabled = !applicable;
  if (openAiAccountIdsInput) openAiAccountIdsInput.disabled = !applicable;
  if (openAiAccountRotation) openAiAccountRotation.disabled = !applicable;
  if (openAiAccountFailover) openAiAccountFailover.disabled = !applicable;
  const loadBtn = $('loadOpenAiAccounts');
  if (loadBtn) loadBtn.disabled = !applicable;

  const hint = $('openAiAccountsHint');
  if (hint) {
    hint.textContent = applicable
      ? 'Use this when you want reviewer account rotation/failover in GitHub Actions.'
      : 'OpenAI account routing applies only when generating reviewer config (no Config JSON/path override).';
  }
}
updateOpenAiAccountControls();
const loadOpenAiAccountsBtn = $('loadOpenAiAccounts');
if (loadOpenAiAccountsBtn) {
  loadOpenAiAccountsBtn.addEventListener('click', async () => {
    await loadOpenAiAccountsFromAuth(true);
  });
}

// ── App field watchers ──
appId.addEventListener('input', updateAppControls);
appPem.addEventListener('input', updateAppControls);
installation.addEventListener('change', updateAppControls);

// ── Auth bundle watchers ──
function updateUsageBtn() {
  const hasAuth = (authB64 && authB64.value.trim().length > 0) || (authB64Path && authB64Path.value.trim().length > 0);
  $('checkUsage').disabled = !hasAuth;
}
if (authB64) authB64.addEventListener('input', updateUsageBtn);
if (authB64Path) authB64Path.addEventListener('input', updateUsageBtn);
if (authB64) authB64.addEventListener('input', () => { openAiAccountId = ''; });
if (authB64Path) authB64Path.addEventListener('input', () => { openAiAccountId = ''; });

// ── Presets (localStorage) ──
function readPresets() {
  try {
    const raw = localStorage.getItem('ix.setup.presets');
    if (!raw) return [];
    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed : [];
  } catch { return []; }
}

function writePresets(presets) {
  localStorage.setItem('ix.setup.presets', JSON.stringify(presets));
}

function refreshPresets() {
  const presets = readPresets().slice().sort((a, b) => a.name.localeCompare(b.name));
  presetList.innerHTML = '';
  presets.forEach(item => {
    const opt = document.createElement('option');
    opt.value = item.name;
    opt.textContent = item.name;
    presetList.appendChild(opt);
  });
  updatePresetControls();
}

function updatePresetControls() {
  const hasName = presetName.value.trim().length > 0;
  const hasJson = configJson.value.trim().length > 0;
  const hasSel = !!presetList.value;
  $('savePreset').disabled = !hasName || !hasJson;
  $('loadPreset').disabled = !hasSel;
  $('deletePreset').disabled = !hasSel;
}

presetName.addEventListener('input', updatePresetControls);
presetList.addEventListener('change', updatePresetControls);

$('savePreset').addEventListener('click', () => {
  const name = presetName.value.trim();
  const content = configJson.value.trim();
  if (!name || !content) return;
  const presets = readPresets();
  const existing = presets.find(p => p.name === name);
  if (existing) existing.content = content;
  else presets.push({ name, content });
  writePresets(presets);
  refreshPresets();
  write(`Saved preset '${name}'.`);
});

$('loadPreset').addEventListener('click', () => {
  const name = presetList.value;
  if (!name) return;
  const preset = readPresets().find(p => p.name === name);
  if (!preset) { refreshPresets(); return; }
  configJson.value = preset.content || '';
  withConfig.checked = true;
  write(`Loaded preset '${name}'.`);
});

$('deletePreset').addEventListener('click', () => {
  const name = presetList.value;
  if (!name) return;
  writePresets(readPresets().filter(p => p.name !== name));
  refreshPresets();
  write(`Deleted preset '${name}'.`);
});

function downloadFile(name, content, contentType) {
  const blob = new Blob([content], { type: contentType });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = name;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}

$('exportPresets').addEventListener('click', () => {
  const presets = readPresets();
  if (presets.length === 0) { write('No presets to export.'); return; }
  downloadFile('intelligencex-presets.json', JSON.stringify(presets, null, 2), 'application/json');
});

$('importPresets').addEventListener('click', () => { importFile.value = ''; importFile.click(); });

importFile.addEventListener('change', async () => {
  const file = importFile.files && importFile.files[0];
  if (!file) return;
  try {
    const text = await file.text();
    const parsed = JSON.parse(text);
    if (!Array.isArray(parsed)) { write('Invalid preset file.'); return; }
    const normalized = parsed
      .filter(item => item && typeof item.name === 'string' && typeof item.content === 'string')
      .map(item => ({ name: item.name.trim(), content: item.content }))
      .filter(item => item.name.length > 0);
    if (normalized.length === 0) { write('No valid presets found.'); return; }
    const existing = readPresets();
    const conflicts = normalized.filter(item => existing.some(p => p.name === item.name));
    if (conflicts.length > 0) {
      const list = conflicts.map(i => i.name).slice(0, 5).join(', ');
      if (!confirm(`Overwrite ${conflicts.length} preset(s)? (${list})`)) return;
    }
    normalized.forEach(item => {
      const match = existing.find(p => p.name === item.name);
      if (match) match.content = item.content;
      else existing.push(item);
    });
    writePresets(existing);
    refreshPresets();
    write(`Imported ${normalized.length} preset(s).`);
  } catch (err) {
    write('Import failed: ' + (err && err.message ? err.message : err));
  }
});

// ── Usage ──
$('checkUsage').addEventListener('click', async () => {
  const usageSummaryEl = $('usageSummary');
  usageSummaryEl.textContent = 'Checking usage...';
  try {
    const data = await fetchJsonSafe('/api/usage', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        authB64: authB64 ? authB64.value.trim() : '',
        authB64Path: authB64Path ? authB64Path.value.trim() : '',
        accountId: (openAiAccountIdInput && openAiAccountIdInput.value.trim())
          ? openAiAccountIdInput.value.trim()
          : openAiAccountId,
        includeEvents: usageEvents.checked,
        includeDailyBreakdown: usageEvents.checked
      })
    });
    const updated = data.updatedAt ? `Updated: ${data.updatedAt}\n\n` : '';
    usageSummaryEl.textContent = updated + formatUsageResult(data);
    setUsageSummary(formatUsageSummaryShort(data));
  } catch (err) {
    usageSummaryEl.textContent = 'Usage error: ' + (err?.message || err);
  }
});

async function loadUsageCache() {
  try {
    const data = await fetchJsonSafe('/api/usage-cache');
    if (data && data.usage) {
      const updated = data.updatedAt ? `Updated: ${data.updatedAt}\n\n` : '';
      $('usageSummary').textContent = updated + formatUsageResult(data);
      setUsageSummary(formatUsageSummaryShort(data));
    }
  } catch { }
}

// ── Inspect / Plan / Apply ──
async function doInspect() {
  const repos = selectedRepos();
  if (repos.length === 0) { write('Select repos first.'); return; }
  if (!getToken()) { write('Token required.'); return; }
  write('Checking existing setup...');
  try {
    const data = await fetchJsonSafe('/api/repo-status', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ repos, token: getToken() })
    });
    write(formatStatus(data));
  } catch (e) {
    write('Inspect error: ' + (e.message || e));
  }
}

async function doPlan() {
  const repos = selectedRepos();
  if (repos.length === 0) { write('Select repos first.'); return; }
  if (!getToken()) { write('Token required.'); return; }
  if (!await ensureOpenAiAuthIfNeeded()) { return; }
  write('Planning (dry run)...');
  try {
    const data = await fetchJsonSafe('/api/setup/plan', {
      method: 'POST',
      headers: getSetupRequestHeaders(),
      body: JSON.stringify(buildRequestBody(true))
    });
    write(formatResults(data));
  } catch (e) {
    write('Plan error: ' + (e.message || e));
  }
}

function confirmApply() {
  const repos = selectedRepos();
  if (repos.length === 0) { write('Select repos first.'); return; }
  if (!getToken()) { write('Token required.'); return; }

  const dependabotNote = '\n\nDependabot note: PR workflows usually cannot access repo secrets, so comments may appear as github-actions instead of your app bot.';
  const msg = repos.length === 1
    ? `Apply changes to ${repos[0]}?\n\nThis will create a PR with the workflow and upload secrets.${dependabotNote}`
    : `Apply changes to ${repos.length} repositories?\n\nThis will create PRs with the workflow and upload secrets.${dependabotNote}`;

  if (!confirm(msg)) return;
  doApply();
}

async function doApply() {
  const repos = selectedRepos();
  if (repos.length === 0) { write('Select repos first.'); return; }
  if (!getToken()) { write('Token required.'); return; }
  if (!await ensureOpenAiAuthIfNeeded()) { return; }
  write('Applying...');
  try {
    const data = await fetchJsonSafe('/api/setup/apply', {
      method: 'POST',
      headers: getSetupRequestHeaders(),
      body: JSON.stringify(buildRequestBody(false))
    });
    write(formatResults(data));
  } catch (e) {
    write('Apply error: ' + (e.message || e));
  }
}

// ── Init ──
refreshPresets();
loadUsageCache();
loadOpenAiAccountsFromAuth(false);
updateProgressBar();
selectSecretOption(secretOption);
setOnboardingPathContracts(FALLBACK_ONBOARDING_PATHS, null, null);
syncOnboardingPathVisualState();
const runAutodetectBtn = $('runAutodetect');
if (runAutodetectBtn) {
  runAutodetectBtn.addEventListener('click', async () => {
    await runAutoDetect();
  });
}
const applyAutodetectPathBtn = $('applyAutodetectPath');
if (applyAutodetectPathBtn) {
  applyAutodetectPathBtn.addEventListener('click', () => {
    const suggested = normalizeRecommendedPath(applyAutodetectPathBtn.dataset.path);
    applyOnboardingPath(suggested);
  });
}
runAutoDetect();
