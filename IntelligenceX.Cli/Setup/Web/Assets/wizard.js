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
let authMethod = null;        // 'device' | 'app' | 'token'
let selectedOperation = 'setup';
let selectedProvider = 'openai';
let selectedPresetProfile = 'balanced';
let secretOption = 'skip';    // 'skip' | 'paste' | 'file'
let deviceState = null;
let lastRecommendation = null;
let lastSummaryBase = 'Ready to preview or apply.';
let lastUsageSummary = null;

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
}

// ── Operation selection ──
function selectOperation(op) {
  selectedOperation = op;
  document.querySelectorAll('[data-op]').forEach(c => {
    c.classList.toggle('selected', c.dataset.op === op);
  });
  $('setupOptions').classList.toggle('hidden', op !== 'setup');
  $('cleanupOptions').classList.toggle('hidden', op !== 'cleanup');
}

// ── Provider toggle ──
function selectProvider(p) {
  selectedProvider = p;
  document.querySelectorAll('[data-provider]').forEach(btn => {
    btn.classList.toggle('active', btn.dataset.provider === p);
  });
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
  $('secretPasteFlow').classList.toggle('visible', opt === 'paste');
  $('secretFileFlow').classList.toggle('visible', opt === 'file');
}

// ── Manual entry toggle ──
function toggleManualEntry() {
  $('manualEntry').classList.toggle('visible');
}

// ── Helpers ──
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
  output.textContent = text;
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

// ── Build review table ──
function buildReviewTable() {
  const tbody = document.querySelector('#reviewTable tbody');
  tbody.innerHTML = '';
  const rows = [
    ['Auth method', authMethod || 'none'],
    ['Token', getToken() ? 'provided' : 'missing'],
    ['Repositories', selectedRepos().join(', ') || 'none'],
    ['Operation', selectedOperation],
  ];
  if (selectedOperation === 'setup') {
    rows.push(['Provider', selectedProvider]);
    rows.push(['Review profile', selectedPresetProfile]);
    if (reviewMode.value) rows.push(['Review mode', reviewMode.value]);
    if (reviewCommentMode.value) rows.push(['Comment mode', reviewCommentMode.value]);
    if (branchName.value.trim()) rows.push(['Branch', branchName.value.trim()]);
    if (withConfig.checked) rows.push(['Create config', 'yes']);
    if (force.checked) rows.push(['Force overwrite', 'yes']);
    if (upgrade.checked) rows.push(['Upgrade managed', 'yes']);
    if (explicitSecrets.checked) rows.push(['Explicit secrets', 'yes']);
  }
  if (selectedOperation === 'cleanup' && keepSecret.checked) {
    rows.push(['Keep secret', 'yes']);
  }
  if (!shouldSkipSecrets()) {
    rows.push(['Secret', secretOption]);
  }
  rows.forEach(([label, value]) => {
    const tr = document.createElement('tr');
    tr.innerHTML = `<td>${label}</td><td>${value}</td>`;
    tbody.appendChild(tr);
  });
}

// ── Build request body ──
function buildRequestBody(dryRun) {
  const skipSecret = shouldSkipSecrets() || secretOption === 'skip';
  return {
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
}

// ── Format helpers ──
function formatResults(data) {
  if (data && Array.isArray(data.results)) {
    const lines = [];
    const total = data.results.length;
    const succeeded = data.results.filter(r => r.exitCode === 0).length;
    const failed = total - succeeded;
    setSummary(`Results: ${succeeded}/${total} succeeded${failed > 0 ? `, ${failed} failed` : ''}.`);
    lines.push(`Summary: ${succeeded}/${total} succeeded`);
    if (failed > 0) lines.push(`Failures: ${failed}`);
    lines.push('');
    data.results.forEach(result => {
      const name = result.repo || 'repo';
      const status = result.exitCode === 0 ? 'success' : 'failed';
      lines.push(`== ${name} ==`);
      lines.push(`status: ${status}`);
      if (typeof result.exitCode !== 'undefined') lines.push(`exit: ${result.exitCode}`);
      if (result.error && result.error.trim().length > 0) {
        lines.push('error:');
        lines.push(result.error.trim());
      }
      if (result.output && result.output.trim().length > 0) {
        lines.push('output:');
        lines.push(result.output.trim());
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

// ── API calls ──
async function loadRepos() {
  write('Loading repos...');
  try {
    const res = await fetch('/api/repos', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ token: getToken() })
    });
    const data = await res.json();
    if (data.error) { write('Repo error: ' + data.error); return; }
    repoList.innerHTML = '';
    (data.repos || []).forEach(r => {
      const opt = document.createElement('option');
      opt.value = r.name;
      opt.textContent = r.name;
      repoList.appendChild(opt);
    });
    write(`Repos loaded (${data.source || 'user'}).`);
    updateRepoCount();
  } catch (e) {
    write('Failed to load repos: ' + (e.message || e));
  }
}

// ── Device flow ──
$('deviceStart').addEventListener('click', async () => {
  write('Starting device flow...');
  try {
    const res = await fetch('/api/device-code', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ clientId: clientId.value })
    });
    const data = await res.json();
    if (data.error) { write('Error: ' + data.error); return; }
    deviceState = data;
    $('deviceInfo').textContent = `Open ${data.verificationUri} and enter code ${data.userCode}`;
    $('devicePoll').disabled = false;
    window.open(data.verificationUri, '_blank');
  } catch (e) {
    write('Device flow error: ' + (e.message || e));
  }
});

$('devicePoll').addEventListener('click', async () => {
  if (!deviceState) return;
  write('Polling for token...');
  try {
    const res = await fetch('/api/device-poll', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        clientId: clientId.value,
        deviceCode: deviceState.deviceCode,
        intervalSeconds: deviceState.intervalSeconds,
        expiresIn: deviceState.expiresIn
      })
    });
    const data = await res.json();
    if (data.error) { write('Poll error: ' + data.error); return; }
    token.value = data.token || '';
    write('Token acquired!');
    goToStep(1); // Auto-advance
  } catch (e) {
    write('Poll error: ' + (e.message || e));
  }
});

// ── App flow ──
$('createApp').addEventListener('click', async () => {
  write('Starting GitHub App manifest flow...');
  try {
    const res = await fetch('/api/app-manifest', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        appName: appName.value.trim() || 'IntelligenceX Reviewer',
        owner: appOwner.value.trim()
      })
    });
    const data = await res.json();
    if (data.error) { write('App error: ' + data.error); return; }
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
    const res = await fetch('/api/app-installations', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ appId: id, pem: appPem.value })
    });
    const data = await res.json();
    if (data.error) { write('Error: ' + data.error); return; }
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
    const res = await fetch('/api/app-token', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ appId: id, pem: appPem.value, installationId: installId })
    });
    const data = await res.json();
    if (data.error) { write('Error: ' + data.error); return; }
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
  const filter = repoFilter.value.toLowerCase();
  Array.from(repoList.options).forEach(opt => {
    opt.hidden = filter && !opt.value.toLowerCase().includes(filter);
  });
});

repoList.addEventListener('change', updateRepoCount);
if (repo) repo.addEventListener('input', updateRepoCount);

// ── Config auto-enable ──
configJson.addEventListener('input', () => { if (configJson.value.trim()) withConfig.checked = true; });
configPath.addEventListener('input', () => { if (configPath.value.trim()) withConfig.checked = true; });

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
    const res = await fetch('/api/usage', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        authB64: authB64 ? authB64.value.trim() : '',
        authB64Path: authB64Path ? authB64Path.value.trim() : '',
        includeEvents: usageEvents.checked
      })
    });
    if (!res.ok) {
      usageSummaryEl.textContent = 'Usage error: ' + (await res.text() || res.status);
      return;
    }
    const data = await res.json();
    if (data.error) { usageSummaryEl.textContent = 'Usage error: ' + data.error; return; }
    const updated = data.updatedAt ? `Updated: ${data.updatedAt}\n\n` : '';
    usageSummaryEl.textContent = updated + formatUsageResult(data);
    setUsageSummary(formatUsageSummaryShort(data));
  } catch (err) {
    usageSummaryEl.textContent = 'Usage error: ' + (err?.message || err);
  }
});

async function loadUsageCache() {
  try {
    const res = await fetch('/api/usage-cache');
    if (!res.ok) return;
    const data = await res.json();
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
    const res = await fetch('/api/repo-status', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ repos, token: getToken() })
    });
    const data = await res.json();
    write(formatStatus(data));
  } catch (e) {
    write('Inspect error: ' + (e.message || e));
  }
}

async function doPlan() {
  const repos = selectedRepos();
  if (repos.length === 0) { write('Select repos first.'); return; }
  if (!getToken()) { write('Token required.'); return; }
  write('Planning (dry run)...');
  try {
    const res = await fetch('/api/setup/plan', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(buildRequestBody(true))
    });
    const data = await res.json();
    write(formatResults(data));
  } catch (e) {
    write('Plan error: ' + (e.message || e));
  }
}

async function doApply() {
  const repos = selectedRepos();
  if (repos.length === 0) { write('Select repos first.'); return; }
  if (!getToken()) { write('Token required.'); return; }
  write('Applying...');
  try {
    const res = await fetch('/api/setup/apply', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(buildRequestBody(false))
    });
    const data = await res.json();
    write(formatResults(data));
  } catch (e) {
    write('Apply error: ' + (e.message || e));
  }
}

// ── Init ──
refreshPresets();
loadUsageCache();
updateProgressBar();
