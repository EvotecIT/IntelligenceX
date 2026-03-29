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
      const pullRequestUrl = typeof result.pullRequestUrl === 'string' ? result.pullRequestUrl.trim() : '';
      if (pullRequestUrl.length > 0) {
        lines.push(`pr: ${pullRequestUrl}`);
      }
      const errorText = typeof result.error === 'string' ? result.error.trim() : '';
      if (errorText.length > 0) {
        lines.push('error:');
        lines.push(errorText);
      }
      const outputText = typeof result.output === 'string' ? result.output.trim() : '';
      if (outputText.length > 0) {
        lines.push('output:');
        lines.push(outputText);
      }
      if (result.verify) {
        const verify = result.verify;
        const verifySkipped = coerceBoolean(verify.skipped);
        const verifyPassed = coerceBoolean(verify.passed);
        const verifyStatus = verifySkipped === true
          ? 'skipped'
          : verifyPassed === true
            ? 'ok'
            : verifyPassed === false
              ? 'failed'
              : 'unknown';
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
            const checkNoteText = safeCheck && safeCheck.note != null
              ? String(safeCheck.note).trim()
              : '';
            const note = checkNoteText.length > 0 ? ` (${checkNoteText})` : '';
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
