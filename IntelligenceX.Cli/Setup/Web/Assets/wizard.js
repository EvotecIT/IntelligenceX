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
let lastAutodetect = null;
let deviceState = null;
let lastRecommendation = null;
let lastSummaryBase = 'Ready to preview or apply.';
let lastUsageSummary = null;
let openAiAccountId = '';
let onboardingContractVersion = null;
let onboardingContractFingerprint = null;
let mergeBlockerRequireAllSectionsTouched = false;
let mergeBlockerRequireSectionMatchTouched = false;

const FALLBACK_ONBOARDING_PATHS = [
  {
    id: 'new-setup',
    displayName: 'New Setup',
    description: 'Configure workflow and reviewer config for first-time onboarding.',
    defaultOperation: 'setup',
    requiresGitHubAuth: true,
    requiresRepoSelection: true,
    requiresAiAuth: true,
    flow: [
      'Authenticate with GitHub',
      'Select repositories',
      'Configure workflow and reviewer profile',
      'Authenticate with AI provider',
      'Plan, apply, verify'
    ]
  },
  {
    id: 'refresh-auth',
    displayName: 'Fix Expired Auth',
    description: 'Refresh OpenAI/ChatGPT auth and update INTELLIGENCEX_AUTH_B64 secret.',
    defaultOperation: 'update-secret',
    requiresGitHubAuth: true,
    requiresRepoSelection: true,
    requiresAiAuth: true,
    flow: [
      'Authenticate with GitHub',
      'Select repositories',
      'Refresh AI auth bundle',
      'Apply update-secret',
      'Verify secret presence'
    ]
  },
  {
    id: 'cleanup',
    displayName: 'Cleanup',
    description: 'Remove workflow/config and optionally remove secrets from repositories.',
    defaultOperation: 'cleanup',
    requiresGitHubAuth: true,
    requiresRepoSelection: true,
    requiresAiAuth: false,
    flow: [
      'Authenticate with GitHub',
      'Select repositories',
      'Choose cleanup options',
      'Plan, apply cleanup',
      'Verify removal'
    ]
  },
  {
    id: 'maintenance',
    displayName: 'Maintenance',
    description: 'Run preflight checks, inspect existing setup, then choose setup/update-secret/cleanup.',
    defaultOperation: 'setup',
    requiresGitHubAuth: true,
    requiresRepoSelection: true,
    requiresAiAuth: false,
    flow: [
      'Run auto-detect preflight',
      'Inspect current workflow/config status',
      'Select operation based on findings',
      'Plan, apply, verify'
    ]
  }
];
let onboardingPaths = FALLBACK_ONBOARDING_PATHS.slice();
let onboardingPathMap = {};

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
const reviewIntentInput = $('reviewIntent');
const reviewStrictnessInput = $('reviewStrictness');
const reviewLoopPolicy = $('reviewLoopPolicy');
const reviewVisionPathInput = $('reviewVisionPath');
const mergeBlockerSectionsInput = $('mergeBlockerSections');
const mergeBlockerRequireAllSectionsInput = $('mergeBlockerRequireAllSections');
const mergeBlockerRequireSectionMatchInput = $('mergeBlockerRequireSectionMatch');
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
const analysisRunStrict = $('analysisRunStrict');
const analysisPacks = $('analysisPacks');
const analysisExportPath = $('analysisExportPath');
const openAiAccountIdInput = $('openAiAccountId');
const openAiAccountIdsInput = $('openAiAccountIds');
const openAiAccountRotation = $('openAiAccountRotation');
const openAiAccountFailover = $('openAiAccountFailover');

function normalizeOperationId(operation) {
  const normalized = String(operation || '').trim().toLowerCase();
  if (normalized === 'update-secret' || normalized === 'refresh-auth') return 'update-secret';
  if (normalized === 'cleanup') return 'cleanup';
  return 'setup';
}

function normalizePathContract(path) {
  if (!path || typeof path !== 'object') return null;
  const id = String(path.id || '').trim().toLowerCase();
  if (!id) return null;
  const flow = Array.isArray(path.flow)
    ? path.flow.map(step => String(step || '').trim()).filter(step => step.length > 0)
    : [];
  return {
    id,
    displayName: String(path.displayName || id),
    description: String(path.description || ''),
    defaultOperation: normalizeOperationId(path.defaultOperation || path.operation),
    requiresGitHubAuth: !!path.requiresGitHubAuth,
    requiresRepoSelection: !!path.requiresRepoSelection,
    requiresAiAuth: !!path.requiresAiAuth,
    flow
  };
}

function findPathByOperation(operation, preferredId) {
  const normalizedOperation = normalizeOperationId(operation);
  if (preferredId && onboardingPathMap[preferredId]) {
    const preferred = onboardingPathMap[preferredId];
    if (preferred.defaultOperation === normalizedOperation) {
      return preferred.id;
    }
  }
  const match = onboardingPaths.find(path => path.defaultOperation === normalizedOperation);
  return match ? match.id : 'new-setup';
}

function getOnboardingPathContract(pathId) {
  const normalized = normalizeRecommendedPath(pathId);
  if (onboardingPathMap[normalized]) {
    return onboardingPathMap[normalized];
  }
  return onboardingPaths[0] || FALLBACK_ONBOARDING_PATHS[0];
}

function renderPathCardsFromContract() {
  document.querySelectorAll('[data-path]').forEach(card => {
    const pathId = String(card.dataset.path || '').trim().toLowerCase();
    const path = onboardingPathMap[pathId];
    if (!path) return;

    const title = card.querySelector('[data-path-title]');
    if (title) title.textContent = path.displayName;

    const desc = card.querySelector('[data-path-desc]');
    if (desc) desc.textContent = path.description;
  });
}

function renderPathContractStatus() {
  const contractEl = $('pathContractVersion');
  if (!contractEl) return;

  if (!onboardingContractVersion) {
    contractEl.textContent = 'Contract: local defaults (auto-detect metadata unavailable).';
    return;
  }

  const fingerprintPrefix = onboardingContractFingerprint
    ? ` | fingerprint ${String(onboardingContractFingerprint).slice(0, 8)}`
    : '';
  contractEl.textContent = `Contract: ${onboardingContractVersion}${fingerprintPrefix}`;
}

function renderPathRequirements(path) {
  const requirementsEl = $('pathRequirements');
  if (!requirementsEl) return;

  requirementsEl.innerHTML = '';
  const requirements = [
    { label: 'GitHub auth', required: !!path.requiresGitHubAuth },
    { label: 'Repo selection', required: !!path.requiresRepoSelection },
    { label: 'AI auth', required: !!path.requiresAiAuth }
  ];

  requirements.forEach(item => {
    const badge = document.createElement('span');
    badge.className = `path-requirement ${item.required ? 'required' : 'optional'}`;
    badge.textContent = `${item.label}: ${item.required ? 'required' : 'optional'}`;
    requirementsEl.appendChild(badge);
  });
}

function setOnboardingPathContracts(paths, contractVersion, contractFingerprint) {
  const normalizedPaths = Array.isArray(paths)
    ? paths.map(normalizePathContract).filter(path => !!path)
    : [];

  onboardingPaths = normalizedPaths.length > 0
    ? normalizedPaths
    : FALLBACK_ONBOARDING_PATHS.slice();
  onboardingPathMap = {};
  onboardingPaths.forEach(path => {
    onboardingPathMap[path.id] = path;
  });

  onboardingContractVersion = contractVersion || null;
  onboardingContractFingerprint = contractFingerprint || null;

  renderPathCardsFromContract();
  renderPathContractStatus();

  selectedOnboardingPath = normalizeRecommendedPath(selectedOnboardingPath);
  if (!onboardingPathMap[selectedOnboardingPath]) {
    selectedOnboardingPath = onboardingPaths[0] ? onboardingPaths[0].id : 'new-setup';
  }
}

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
  const normalized = normalizeOperationId(op);
  switch (normalized) {
    case 'update-secret':
      return findPathByOperation('update-secret', 'refresh-auth');
    case 'cleanup':
      return findPathByOperation('cleanup', 'cleanup');
    case 'setup':
    default:
      if (onboardingPathMap['new-setup']) return 'new-setup';
      return findPathByOperation('setup', null);
  }
}

function getOnboardingPathHint(path) {
  const selectedPath = getOnboardingPathContract(path);
  const flowPreview = selectedPath.flow.length > 0
    ? selectedPath.flow.slice(0, 3).join(' -> ')
    : 'Authenticate with GitHub -> Select repositories -> Plan and apply';
  return `Path selected: ${selectedPath.displayName}. Next: ${flowPreview}.`;
}

function selectOperation(op) {
  selectedOperation = op;
  document.querySelectorAll('[data-op]').forEach(c => {
    c.classList.toggle('selected', c.dataset.op === op);
  });
  $('setupOptions').classList.toggle('hidden', op !== 'setup');
  $('cleanupOptions').classList.toggle('hidden', op !== 'cleanup');
  updateAnalysisControls();
  updateReviewConfigControls();
  updateOpenAiAccountControls();

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
  const selectedPath = getOnboardingPathContract(selectedOnboardingPath);
  setOnboardingPathHint(getOnboardingPathHint(selectedOnboardingPath));
  renderPathRequirements(selectedPath);
}

function applyOnboardingPath(path) {
  const normalizedPath = normalizeRecommendedPath(path);
  const effectivePath = getOnboardingPathContract(normalizedPath);
  const effectivePathId = effectivePath.id;
  switch (effectivePathId) {
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
    case 'maintenance':
      selectOperation('setup');
      selectProvider('openai');
      selectSecretOption('skip');
      if (withConfig) withConfig.checked = true;
      break;
    case 'new-setup':
    default:
      selectOperation('setup');
      selectProvider('openai');
      selectSecretOption('login');
      if (withConfig) withConfig.checked = true;
      break;
  }

  selectedOnboardingPath = effectivePathId;

  syncOnboardingPathVisualState();
  refreshPathStateAfterOnboardingSelection();
}

function refreshPathStateAfterOnboardingSelection() {
  updateAnalysisControls();
  updateReviewConfigControls();
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
  updateOpenAiAccountControls();
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
  if (statusEl) {
    statusEl.style.color = '';
    statusEl.innerHTML = '<span class="spinner"></span> Opening ChatGPT login...';
  }
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
      openAiAccountId = data.accountId ? String(data.accountId).trim() : '';
      if (statusEl) {
        const details = [];
        if (openAiAccountId) details.push(`account ${openAiAccountId}`);
        if (data.expiresAt) details.push(`expires ${data.expiresAt}`);
        const suffix = details.length > 0 ? ` (${details.join(', ')})` : '';
        statusEl.textContent = `\u2713 Authenticated with ChatGPT${suffix}`;
        statusEl.style.color = 'var(--pf-success)';
      }
      if (btn) {
        btn.textContent = 'Switch ChatGPT account';
        btn.classList.add('success');
      }
      // Refresh usage button enablement.
      try { updateUsageBtn(); } catch { }
      // Load known accounts from auth store for optional multi-account routing.
      try { await loadOpenAiAccountsFromAuth(false); } catch { }
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

async function loadOpenAiAccountsFromAuth(showStatus = true) {
  const btn = $('loadOpenAiAccounts');
  const hint = $('openAiAccountsHint');
  if (btn) btn.disabled = true;
  if (hint && showStatus) hint.textContent = 'Loading OpenAI accounts from auth bundle...';

  try {
    const data = await fetchJsonSafe('/api/openai-accounts', {
      method: 'POST',
      headers: getSetupRequestHeaders(),
      body: JSON.stringify({
        authB64: authB64 ? authB64.value.trim() : '',
        authB64Path: authB64Path ? authB64Path.value.trim() : ''
      })
    });

    const accounts = Array.isArray(data.accounts) ? data.accounts : [];
    const accountIds = normalizeOpenAiAccountIdsCsv(accounts.map(a => a && a.accountId ? a.accountId : '').join(','));
    if (accountIds.length > 0) {
      if (openAiAccountIdsInput && !openAiAccountIdsInput.value.trim()) {
        openAiAccountIdsInput.value = accountIds.join(',');
      }
      const selected = data.selectedAccountId ? String(data.selectedAccountId).trim() : '';
      if (openAiAccountIdInput && !openAiAccountIdInput.value.trim()) {
        openAiAccountIdInput.value = selected || accountIds[0];
      }
      if (hint) {
        hint.textContent = `Detected ${accountIds.length} account(s): ${accountIds.join(', ')}.`;
      }
    } else if (hint) {
      hint.textContent = data.error
        ? `No OpenAI accounts detected (${data.error}).`
        : 'No OpenAI accounts detected in current auth bundle.';
    }
  } catch (e) {
    if (hint) hint.textContent = `Account detection failed: ${e.message || e}`;
  } finally {
    if (btn) btn.disabled = false;
  }
}

// ── Manual entry toggle ──
function toggleManualEntry() {
  $('manualEntry').classList.toggle('visible');
}
