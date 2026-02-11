// ── Theme ──
function getPreferredTheme() {
  const stored = localStorage.getItem('theme');
  if (stored === 'light' || stored === 'dark') return stored;
  return window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
}

function applyTheme(theme) {
  document.documentElement.setAttribute('data-theme', theme);
  localStorage.setItem('theme', theme);
}

function toggleTheme() {
  const current = document.documentElement.getAttribute('data-theme') || 'dark';
  applyTheme(current === 'dark' ? 'light' : 'dark');
}

applyTheme(getPreferredTheme());

// ── State ──
let currentStep = 0;
const totalSteps = 5;
let authMethod = 'device';    // 'device' | 'app' | 'token' | 'custom'
let selectedOperation = 'setup';
let selectedProvider = 'openai';
let selectedPresetProfile = 'balanced';
let secretOption = 'login';   // 'login' | 'paste' | 'file' | 'skip'
let selectedOnboardingPath = 'new-setup';
let deviceState = null;
let lastRecommendation = null;
let lastSummaryBase = 'Ready to preview or apply.';
let lastUsageSummary = null;

// Default IntelligenceX GitHub App Client ID
const DEFAULT_GITHUB_CLIENT_ID = 'Iv23li0wcHDzWa25HKz3';

// ── DOM refs ──
const $ = id => document.getElementById(id);
const token = $('token');
const clientId = $('clientId');
const repo = $('repo');
const repoList = $('repoList');
const repoFilter = $('repoFilter');
const reviewMode = $('reviewMode');
const reviewCommentMode = $('reviewCommentMode');
const branchName = $('branchName');
const withConfig = $('withConfig');
const force = $('force');
const upgrade = $('upgrade');
const explicitSecrets = $('explicitSecrets');
const configJson = $('configJson');
const configPath = $('configPath');
const presetName = $('presetName');
const presetList = $('presetList');
const importFile = $('importFile');
const keepSecret = $('keepSecret');
const authB64 = $('authB64');
const authB64Path = $('authB64Path');
const usageEvents = $('usageEvents');
const output = $('output');
const summary = $('summary');
const appName = $('appName');
const appOwner = $('appOwner');
const appId = $('appId');
const appPem = $('appPem');
const installation = $('installation');
const analysisEnabled = $('analysisEnabled');
const analysisGate = $('analysisGate');
const analysisPacks = $('analysisPacks');
const analysisExportPath = $('analysisExportPath');

// ── Step navigation ──
function goToStep(step) {
  if (step < 0 || step >= totalSteps) return;

  // Skip secrets step if not needed
  if (step === 3 && shouldSkipSecrets()) {
    step = currentStep < 3 ? 4 : 2;
  }

  currentStep = step;
  document.querySelectorAll('.wizard-step').forEach(el => el.classList.remove('active'));
  const target = document.querySelector(`[data-wizard-step="${currentStep}"]`);
  if (target) target.classList.add('active');
  updateProgressBar();

  // Auto-load repos when entering step 2
  if (currentStep === 1 && getToken() && repoList.options.length === 0) {
    loadRepos();
  }

  // Build review table when entering step 5
  if (currentStep === 4) {
    buildReviewTable();
  }
}

function goNext() {
  if (!validateStep(currentStep)) return;
  goToStep(currentStep + 1);
}

function goPrev() {
  let target = currentStep - 1;
  if (target === 3 && shouldSkipSecrets()) {
    target = 2;
  }
  goToStep(target);
}

function shouldSkipSecrets() {
  if (selectedOperation === 'cleanup') return true;
  if (selectedOperation === 'setup' && selectedProvider === 'copilot') return true;
  return false;
}

function validateStep(step) {
  if (step === 0) {
    if (!getToken()) {
      showSubFlowHint('Authenticate first to continue.');
      return false;
    }
    return true;
  }
  if (step === 1) {
    if (selectedRepos().length === 0) {
      write('Select at least one repository.');
      return false;
    }
    return true;
  }
  return true;
}

function showSubFlowHint(msg) {
  // Show hint in the visible sub-flow or device info
  const info = $('deviceInfo');
  if (info) info.textContent = msg;
}

// ── Progress bar ──
function updateProgressBar() {
  document.querySelectorAll('.progress-step').forEach(el => {
    const s = parseInt(el.dataset.step);
    el.classList.remove('active', 'completed');
    if (s < currentStep) el.classList.add('completed');
    else if (s === currentStep) el.classList.add('active');
  });
  document.querySelectorAll('.progress-line').forEach(el => {
    const l = parseInt(el.dataset.line);
    el.classList.toggle('filled', l < currentStep);
  });
}

// ── Auth selection ──
function selectAuth(method) {
  authMethod = method;
  document.querySelectorAll('[data-auth]').forEach(c => {
    c.classList.toggle('selected', c.dataset.auth === method);
  });
  $('authDeviceFlow').classList.toggle('visible', method === 'device');
  $('authAppFlow').classList.toggle('visible', method === 'app');
  $('authTokenFlow').classList.toggle('visible', method === 'token');
  const customFlow = $('authCustomFlow');
  if (customFlow) customFlow.classList.toggle('visible', method === 'custom');
}

// ── Get effective client ID ──
function getEffectiveClientId() {
  if (authMethod === 'custom') {
    const customId = $('customClientId');
    return customId ? customId.value.trim() : '';
  }
  // Default: use IntelligenceX app
  return DEFAULT_GITHUB_CLIENT_ID;
}

// ── Operation selection ──
function getOnboardingPathForOperation(op) {
  switch (op) {
    case 'update-secret':
      return 'refresh-auth';
    case 'cleanup':
      return 'cleanup';
    case 'setup':
    default:
      return 'new-setup';
  }
}

function getOnboardingPathHint(path) {
  switch (path) {
    case 'refresh-auth':
      return 'Path selected: Fix Expired Auth. Next: authenticate, choose repos, then run update-secret.';
    case 'cleanup':
      return 'Path selected: Cleanup. Next: authenticate, select repos, then preview and remove setup files.';
    case 'new-setup':
    default:
      return 'Path selected: New Setup. Next: authenticate with GitHub, then select repositories.';
  }
}

function selectOperation(op) {
  selectedOperation = op;
  document.querySelectorAll('[data-op]').forEach(c => {
    c.classList.toggle('selected', c.dataset.op === op);
  });
  $('setupOptions').classList.toggle('hidden', op !== 'setup');
  $('cleanupOptions').classList.toggle('hidden', op !== 'cleanup');
  updateAnalysisControls();

  selectedOnboardingPath = getOnboardingPathForOperation(op);
  syncOnboardingPathVisualState();
}

function setOnboardingPathHint(message) {
  const hint = $('pathHint');
  if (hint) {
    hint.textContent = message;
  }
}

function syncOnboardingPathVisualState() {
  document.querySelectorAll('[data-path]').forEach(c => {
    c.classList.toggle('selected', c.dataset.path === selectedOnboardingPath);
  });
  setOnboardingPathHint(getOnboardingPathHint(selectedOnboardingPath));
}

function applyOnboardingPath(path) {
  switch (path) {
    case 'refresh-auth':
      selectOperation('update-secret');
      selectProvider('openai');
      selectSecretOption('login');
      if (withConfig) withConfig.checked = false;
      break;
    case 'cleanup':
      selectOperation('cleanup');
      selectProvider('openai');
      selectSecretOption('skip');
      if (withConfig) withConfig.checked = false;
      break;
    case 'new-setup':
    default:
      selectOperation('setup');
      selectProvider('openai');
      selectSecretOption('login');
      if (withConfig) withConfig.checked = true;
      break;
  }

  syncOnboardingPathVisualState();
  refreshPathStateAfterOnboardingSelection();
}

function refreshPathStateAfterOnboardingSelection() {
  updateAnalysisControls();
  if (currentStep === 4) {
    buildReviewTable();
  }
}

// ── Provider toggle ──
function selectProvider(p) {
  selectedProvider = p;
  document.querySelectorAll('[data-provider]').forEach(btn => {
    btn.classList.toggle('active', btn.dataset.provider === p);
  });
  // Update hint text
  const hint = $('providerHint');
  if (hint) {
    hint.textContent = p === 'openai'
      ? 'Recommended. Uses your ChatGPT account for reviews.'
      : 'Uses GitHub Copilot CLI. Requires Copilot subscription and CLI installed.';
  }
}

// ── Preset selection ──
function selectPreset(name) {
  selectedPresetProfile = name;
  document.querySelectorAll('.preset-card').forEach(c => {
    c.classList.toggle('selected', c.dataset.preset === name);
  });
}

// ── Secret option ──
function selectSecretOption(opt) {
  secretOption = opt;
  document.querySelectorAll('[data-secret]').forEach(c => {
    c.classList.toggle('selected', c.dataset.secret === opt);
  });
  const loginFlow = $('secretLoginFlow');
  const pasteFlow = $('secretPasteFlow');
  const fileFlow = $('secretFileFlow');
  if (loginFlow) loginFlow.classList.toggle('visible', opt === 'login');
  if (pasteFlow) pasteFlow.classList.toggle('visible', opt === 'paste');
  if (fileFlow) fileFlow.classList.toggle('visible', opt === 'file');
}

// ── ChatGPT Login ──
async function runChatGptLogin() {
  const statusEl = $('chatgptLoginStatus');
  const btn = $('chatgptLogin');
  if (statusEl) statusEl.innerHTML = '<span class="spinner"></span> Opening ChatGPT login...';
  if (btn) {
    btn.disabled = true;
  }

  try {
    const data = await fetchJsonSafe('/api/openai-login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({})
    });
    if (data.authB64) {
      if (authB64) authB64.value = data.authB64;
      if (statusEl) statusEl.innerHTML = '<span style="color: var(--pf-success);">&#x2713; Authenticated with ChatGPT</span>';
      if (btn) {
        btn.textContent = 'Switch ChatGPT account';
        btn.classList.add('success');
      }
      // Refresh usage button enablement.
      try { updateUsageBtn(); } catch { }
      return true;
    }

    if (statusEl) statusEl.innerHTML = '<span style="color: var(--pf-danger);">Login did not return an auth bundle.</span>';
    return false;
  } catch (e) {
    if (statusEl) statusEl.innerHTML = `<span style="color: var(--pf-danger);">Error: ${e.message || e}</span>`;
    return false;
  } finally {
    if (btn) {
      btn.disabled = false;
    }
  }
}

const chatgptLoginBtn = $('chatgptLogin');
if (chatgptLoginBtn) {
  chatgptLoginBtn.addEventListener('click', async () => {
    await runChatGptLogin();
  });
}

async function ensureOpenAiAuthIfNeeded() {
  if (shouldSkipSecrets()) return true;
  if (selectedProvider !== 'openai') return true;
  if (secretOption === 'skip') return true;

  const hasAuth = (authB64 && authB64.value.trim().length > 0) || (authB64Path && authB64Path.value.trim().length > 0);
  if (hasAuth) return true;

  if (secretOption === 'login') {
    write('OpenAI auth is required. Starting ChatGPT login...');
    const ok = await runChatGptLogin();
    if (!ok) {
      write('OpenAI auth login failed. You can paste authB64/authB64Path or enable "Skip OpenAI secret".');
      return false;
    }
    return true;
  }

  write('OpenAI auth bundle is missing. Provide authB64/authB64Path or select "ChatGPT browser login".');
  return false;
}

// ── Manual entry toggle ──
function toggleManualEntry() {
  $('manualEntry').classList.toggle('visible');
}

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
      ${analysisExportPathValue ? `
      <div class="review-item">
        <span class="review-label">Analysis export path</span>
        <span class="review-value"><code>${safeAnalysisExportPath}</code></span>
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
  const analysisEnabledValue = wantAnalysis && analysisEnabled && analysisEnabled.checked ? true : null;
  const analysisOn = analysisEnabledValue === true;
  const packsRaw = analysisPacks ? analysisPacks.value.trim() : '';
  const exportPathRaw = analysisExportPath ? analysisExportPath.value.trim() : '';
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
  if (wantAnalysis) {
    body.analysisEnabled = analysisEnabledValue;
    if (analysisOn) {
      body.analysisGateEnabled = !!(analysisGate && analysisGate.checked);
      if (packsRaw.length > 0) body.analysisPacks = packsRaw;
      if (exportPathRaw.length > 0) body.analysisExportPath = exportPathRaw;
    }
  }
  return body;
}

// ── Format helpers ──
function formatResults(data) {
  if (data && Array.isArray(data.results)) {
    const lines = [];
    const total = data.results.length;
    const succeeded = data.results.filter(r => r.exitCode === 0).length;
    const failed = total - succeeded;
    const verifyFailed = data.results.filter(r => {
      if (!r || !r.verify) return false;
      const skipped = coerceBoolean(r.verify.skipped);
      const passed = coerceBoolean(r.verify.passed);
      return skipped !== true && passed !== true;
    }).length;
    const verifyText = verifyFailed > 0 ? `, verify issues in ${verifyFailed}` : '';
    setSummary(`Results: ${succeeded}/${total} succeeded${failed > 0 ? `, ${failed} failed` : ''}${verifyText}.`);
    lines.push(`Summary: ${succeeded}/${total} succeeded`);
    if (failed > 0) lines.push(`Failures: ${failed}`);
    if (verifyFailed > 0) lines.push(`Verification issues: ${verifyFailed}`);
    lines.push('');
    data.results.forEach(result => {
      const name = result.repo || 'repo';
      const status = result.exitCode === 0 ? 'success' : 'failed';
      lines.push(`== ${name} ==`);
      lines.push(`status: ${status}`);
      if (typeof result.exitCode !== 'undefined') lines.push(`exit: ${result.exitCode}`);
      if (result.pullRequestUrl && result.pullRequestUrl.trim().length > 0) {
        lines.push(`pr: ${result.pullRequestUrl.trim()}`);
      }
      if (result.error && result.error.trim().length > 0) {
        lines.push('error:');
        lines.push(result.error.trim());
      }
      if (result.output && result.output.trim().length > 0) {
        lines.push('output:');
        lines.push(result.output.trim());
      }
      if (result.verify) {
        const verify = result.verify;
        const verifySkipped = coerceBoolean(verify.skipped);
        const verifyPassed = coerceBoolean(verify.passed);
        const verifyStatus = verifySkipped === true ? 'skipped' : (verifyPassed === true ? 'ok' : 'failed');
        lines.push(`verify: ${verifyStatus}`);
        if (verify.checkedRef && String(verify.checkedRef).trim().length > 0) {
          const source = verify.checkedRefSource ? String(verify.checkedRefSource) : 'ref';
          lines.push(`verify-ref: ${source}=${verify.checkedRef}`);
        }
        if (verify.note && String(verify.note).trim().length > 0) {
          lines.push(`verify-note: ${String(verify.note).trim()}`);
        }
        if (Array.isArray(verify.checks) && verify.checks.length > 0) {
          verify.checks.forEach(check => {
            const safeCheck = check && typeof check === 'object' ? check : null;
            let checkStatus = 'fail';
            const checkSkipped = safeCheck ? coerceBoolean(safeCheck.skipped) : null;
            const checkPassed = safeCheck ? coerceBoolean(safeCheck.passed) : null;
            if (checkSkipped === true) {
              checkStatus = 'skip';
            } else if (checkPassed === true) {
              checkStatus = 'ok';
            }
            const expected = safeCheck && safeCheck.expected != null
              ? String(safeCheck.expected)
              : 'n/a';
            const actual = safeCheck && safeCheck.actual != null
              ? String(safeCheck.actual)
              : 'n/a';
            const note = safeCheck && safeCheck.note ? ` (${String(safeCheck.note)})` : '';
            const checkName = safeCheck && safeCheck.name ? String(safeCheck.name) : 'check';
            lines.push(`- ${checkName}: ${checkStatus} (expected ${expected}, actual ${actual})${note}`);
          });
        }
      }
      lines.push('');
    });
    return lines.join('\n');
  }
  if (data && data.error) return `Error: ${data.error}`;
  return JSON.stringify(data, null, 2);
}

function formatStatus(data) {
  if (!data || !Array.isArray(data.status)) return formatResults(data);
  const lines = [];
  const workflowCount = data.status.filter(item => item.workflowExists).length;
  const configCount = data.status.filter(item => item.configExists).length;
  lastRecommendation = buildRecommendation(data.status);
  setSummary(`Inspection: workflow in ${workflowCount}/${data.status.length}, config in ${configCount}/${data.status.length}.\n${lastRecommendation.summary}`);
  data.status.forEach(item => {
    lines.push(`== ${item.repo} ==`);
    if (item.error) {
      lines.push(`error: ${item.error}`);
      lines.push('');
      return;
    }
    lines.push(`default branch: ${item.defaultBranch || 'unknown'}`);
    if (item.workflowExists) {
      lines.push(`workflow: present${item.workflowManaged ? ' (managed)' : ''}`);
    } else {
      lines.push('workflow: missing');
    }
    lines.push(`config: ${item.configExists ? 'present' : 'missing'}`);
    lines.push('');
  });
  return lines.join('\n');
}

function buildRecommendation(items) {
  let missingWorkflow = 0;
  let unmanaged = 0;
  let missingConfig = 0;
  items.forEach(item => {
    if (item.error) return;
    if (!item.workflowExists) missingWorkflow++;
    else if (!item.workflowManaged) unmanaged++;
    if (!item.configExists) missingConfig++;
  });

  if (missingWorkflow === 0 && unmanaged === 0 && missingConfig === 0) {
    return { action: 'none', force: false, withConfig: false, summary: 'No changes needed. Consider update-secret if you rotate auth.' };
  }

  const parts = [];
  if (missingWorkflow > 0) parts.push(`add workflow to ${missingWorkflow}`);
  if (unmanaged > 0) parts.push(`overwrite unmanaged in ${unmanaged}`);
  if (missingConfig > 0) parts.push(`add config to ${missingConfig}`);

  return {
    action: 'setup',
    force: unmanaged > 0,
    withConfig: missingConfig > 0,
    summary: `Recommendation: setup (${parts.join(', ')}).`
  };
}

function formatUsageResult(data) {
  if (!data || !data.usage) return 'No usage data.';
  const usage = data.usage;
  const lines = [];
  if (usage.planType) lines.push(`Plan: ${usage.planType}`);
  if (usage.email) lines.push(`Email: ${usage.email}`);
  if (usage.accountId) lines.push(`Account: ${usage.accountId}`);
  if (usage.rateLimit) lines.push(formatRateLimit('Rate limit', usage.rateLimit));
  if (usage.codeReviewRateLimit) lines.push(formatRateLimit('Code review limit', usage.codeReviewRateLimit));
  if (usage.credits) {
    const c = usage.credits;
    lines.push(`Credits: ${c.hasCredits ? 'yes' : 'no'}${c.unlimited ? ' (unlimited)' : ''}`);
    if (c.balance !== null && c.balance !== undefined) lines.push(`Credits balance: ${c.balance}`);
  }
  if (data.events && data.events.length > 0) {
    lines.push('');
    lines.push('Credit usage events:');
    data.events.forEach(evt => {
      lines.push(`- ${evt.date || '-'} | ${evt.productSurface || '-'} | ${evt.creditAmount ?? '-'} | ${evt.usageId || '-'}`);
    });
  }
  return lines.join('\n');
}

function formatUsageSummaryShort(data) {
  if (!data || !data.usage) return 'No usage data.';
  const usage = data.usage;
  const parts = [];
  if (usage.planType) parts.push(`Plan ${usage.planType}`);
  if (usage.credits && usage.credits.balance !== null && usage.credits.balance !== undefined) parts.push(`Credits ${usage.credits.balance}`);
  if (usage.rateLimit && usage.rateLimit.limitReached) parts.push('Limit reached');
  return parts.length === 0 ? 'Usage available.' : parts.join(' | ');
}

function formatRateLimit(label, limit) {
  const parts = [`${label}: ${limit.allowed ? 'allowed' : 'blocked'}`];
  if (limit.limitReached) parts.push('limit reached');
  if (limit.primary) parts.push(`primary ${formatWindow(limit.primary)}`);
  if (limit.secondary) parts.push(`secondary ${formatWindow(limit.secondary)}`);
  return parts.join(', ');
}

function formatWindow(w) {
  const s = [];
  if (w.usedPercent !== null && w.usedPercent !== undefined) s.push(`${w.usedPercent}% used`);
  if (w.limitWindowSeconds) s.push(`${Math.round(w.limitWindowSeconds / 60)}m window`);
  if (w.resetAfterSeconds) s.push(`resets in ${Math.round(w.resetAfterSeconds / 60)}m`);
  else if (w.resetAt) s.push(`reset at ${w.resetAt}`);
  return s.join(', ');
}

// ── Repo data ──
let allRepos = [];
let reposByOrg = {};

// ── API calls ──
async function loadRepos() {
  write('Loading repos...');
  try {
    const data = await fetchJsonSafe('/api/repos', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ token: getToken() })
    });

    // Filter to only repos where user has admin access (required for setting secrets)
    const accessibleRepos = (data.repos || []).filter(r => r.canAdmin);
    const pushOnlyCount = (data.repos || []).filter(r => r.canPush && !r.canAdmin).length;
    const readOnlyCount = (data.repos || []).filter(r => !r.canPush && !r.canAdmin).length;

    allRepos = accessibleRepos.map(r => r.name);

    // Group repos by organization/owner
    reposByOrg = {};
    allRepos.forEach(repoName => {
      const [org] = repoName.split('/');
      if (!reposByOrg[org]) reposByOrg[org] = [];
      reposByOrg[org].push(repoName);
    });

    renderRepoList();
    renderOrgFilter();
    let msg = `Loaded ${allRepos.length} repos with admin access.`;
    const hiddenParts = [];
    if (pushOnlyCount > 0) hiddenParts.push(`${pushOnlyCount} write-only`);
    if (readOnlyCount > 0) hiddenParts.push(`${readOnlyCount} read-only`);
    if (hiddenParts.length > 0) {
      msg += ` Hidden: ${hiddenParts.join(', ')} (admin access required to set secrets).`;
    }
    write(msg);
    updateRepoCount();
  } catch (e) {
    write('Failed to load repos: ' + (e.message || e));
  }
}

function renderRepoList(filterOrg = null, filterText = '') {
  repoList.innerHTML = '';
  const orgs = Object.keys(reposByOrg).sort();

  orgs.forEach(org => {
    if (filterOrg && filterOrg !== org) return;

    const repos = reposByOrg[org].filter(r =>
      !filterText || r.toLowerCase().includes(filterText.toLowerCase())
    );
    if (repos.length === 0) return;

    // Add org header as optgroup
    const group = document.createElement('optgroup');
    group.label = `${org} (${repos.length})`;

    repos.forEach(repoName => {
      const opt = document.createElement('option');
      opt.value = repoName;
      opt.textContent = repoName.split('/')[1]; // Just show repo name, org is in group
      group.appendChild(opt);
    });

    repoList.appendChild(group);
  });
}

function renderOrgFilter() {
  const container = $('orgFilter');
  if (!container) return;

  const orgs = Object.keys(reposByOrg).sort();
  if (orgs.length <= 1) {
    container.style.display = 'none';
    return;
  }

  container.style.display = 'flex';
  container.innerHTML = '<button class="org-btn active" data-org="">All</button>';
  orgs.forEach(org => {
    const count = reposByOrg[org].length;
    const btn = document.createElement('button');
    btn.className = 'org-btn';
    btn.dataset.org = org;
    btn.textContent = `${org} (${count})`;
    btn.onclick = () => selectOrgFilter(org);
    container.appendChild(btn);
  });
}

function selectOrgFilter(org) {
  document.querySelectorAll('.org-btn').forEach(btn => {
    btn.classList.toggle('active', btn.dataset.org === org);
  });
  const filterText = repoFilter ? repoFilter.value : '';
  renderRepoList(org || null, filterText);
}

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
});
configPath.addEventListener('input', () => {
  if (configPath.value.trim()) withConfig.checked = true;
  updateAnalysisControls();
});
withConfig.addEventListener('change', updateAnalysisControls);

// ── Static analysis toggle ──
function updateAnalysisControls() {
  const hasConfigOverride = (configJson.value.trim().length > 0) || (configPath.value.trim().length > 0);
  const applicable = selectedOperation === 'setup' && withConfig.checked && !hasConfigOverride;
  const enabled = applicable && analysisEnabled && analysisEnabled.checked;

  if (analysisEnabled) analysisEnabled.disabled = !applicable;
  if (analysisGate) analysisGate.disabled = !enabled;
  if (analysisPacks) analysisPacks.disabled = !enabled;
  if (analysisExportPath) analysisExportPath.disabled = !enabled;

  const hint = $('analysisHint');
  if (hint) {
    hint.textContent = applicable
      ? 'Leave empty to use defaults. Examples: all-50, all-100, all-500, all-security-default, powershell-50. Browse packs: intelligencex analyze list-rules --workspace <repo-root> --format markdown. Gate semantics/docs: Docs/reviewer/static-analysis.md'
      : 'Static analysis settings apply only when generating config from presets (no Config JSON/path override).';
  }
}
if (analysisEnabled) analysisEnabled.addEventListener('change', updateAnalysisControls);
updateAnalysisControls();

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
        includeEvents: usageEvents.checked
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
updateProgressBar();
selectSecretOption(secretOption);
syncOnboardingPathVisualState();
