using System;
using System.Collections.Generic;
using System.Text;

namespace IntelligenceX.Cli.Setup.Web;

internal static class WebStaticAssets {
    private static readonly Dictionary<string, (byte[] content, string contentType)> Assets = new(StringComparer.OrdinalIgnoreCase) {
        ["/index.html"] = (Encoding.UTF8.GetBytes(IndexHtml), "text/html; charset=utf-8"),
        ["/app.js"] = (Encoding.UTF8.GetBytes(AppJs), "text/javascript; charset=utf-8"),
        ["/styles.css"] = (Encoding.UTF8.GetBytes(StylesCss), "text/css; charset=utf-8")
    };

    public static byte[]? TryGet(string path, out string contentType) {
        if (Assets.TryGetValue(path, out var entry)) {
            contentType = entry.contentType;
            return entry.content;
        }
        contentType = "application/octet-stream";
        return null;
    }

    private const string IndexHtml = @"<!doctype html>
<html lang=""en"">
  <head>
    <meta charset=""utf-8"" />
    <meta name=""viewport"" content=""width=device-width,initial-scale=1"" />
    <title>IntelligenceX Setup</title>
    <link rel=""stylesheet"" href=""/styles.css"" />
  </head>
  <body>
    <main class=""container"">
      <header>
        <h1>IntelligenceX Setup</h1>
        <p>Local onboarding wizard (preview).</p>
      </header>
      <section class=""card"">
        <h2>Step 1: GitHub authentication</h2>
        <label>GitHub Client ID (device flow)</label>
        <input id=""clientId"" placeholder=""client id"" />
        <button id=""deviceStart"">Start Device Flow</button>
        <button id=""devicePoll"" disabled>Poll Token</button>
        <div id=""deviceInfo"" class=""hint""></div>

        <label>GitHub Token (optional)</label>
        <input id=""token"" placeholder=""token"" />
      </section>

      <section class=""card"">
        <h2>Step 2: Repo + options</h2>
        <label>Repository selection</label>
        <div class=""row"">
          <button id=""loadRepos"">Load Repos</button>
          <input id=""repoFilter"" placeholder=""filter repos"" />
        </div>
        <select id=""repoList"" multiple size=""6""></select>
        <label>Or enter repo manually</label>
        <input id=""repo"" placeholder=""owner/name"" />

        <label>Operation</label>
        <select id=""operation"">
          <option value=""setup"">Setup (create/update workflow)</option>
          <option value=""cleanup"">Cleanup (remove workflow/config)</option>
        </select>

        <div class=""row"">
          <label><input type=""checkbox"" id=""withConfig"" /> Create config</label>
          <label><input type=""checkbox"" id=""skipSecret"" checked /> Skip OpenAI secret</label>
        </div>
        <p class=""hint"">OpenAI login is not wired in the web UI yet. Keep “Skip OpenAI secret” enabled.</p>
        <div class=""row"">
          <label><input type=""checkbox"" id=""explicitSecrets"" /> Explicit secrets block</label>
          <label><input type=""checkbox"" id=""dryRun"" checked /> Dry run</label>
        </div>

        <details>
          <summary>Advanced options</summary>
          <label>Review profile</label>
          <select id=""reviewProfile"">
            <option value="""">default</option>
            <option value=""balanced"">balanced</option>
            <option value=""picky"">picky</option>
            <option value=""highlevel"">highlevel</option>
            <option value=""security"">security</option>
            <option value=""performance"">performance</option>
            <option value=""tests"">tests</option>
          </select>

          <label>Review mode</label>
          <select id=""reviewMode"">
            <option value="""">default</option>
            <option value=""hybrid"">hybrid</option>
            <option value=""summary"">summary</option>
            <option value=""inline"">inline</option>
          </select>

          <label>Comment mode</label>
          <select id=""reviewCommentMode"">
            <option value="""">default</option>
            <option value=""sticky"">sticky</option>
            <option value=""fresh"">fresh</option>
          </select>

          <label>Branch name (optional)</label>
          <input id=""branchName"" placeholder=""setup/intelligencex"" />

          <div class=""row"">
            <label><input type=""checkbox"" id=""upgrade"" /> Upgrade managed sections</label>
            <label><input type=""checkbox"" id=""force"" /> Force overwrite</label>
          </div>
          <label><input type=""checkbox"" id=""keepSecret"" /> Keep existing secret on cleanup</label>

          <label>Config JSON</label>
          <textarea id=""configJson"" rows=""6"" placeholder=""{ ... }""></textarea>
          <label>Config path</label>
          <input id=""configPath"" placeholder=""path to config.json"" />
          <p class=""hint"">Config JSON/path will auto-enable “Create config”.</p>
        </details>

        <div class=""row"">
          <button id=""inspect"">Check existing setup</button>
          <button id=""plan"">Plan</button>
          <button id=""apply"">Apply</button>
        </div>
      </section>

      <section class=""card"">
        <h2>Output</h2>
        <pre id=""output"" class=""output""></pre>
      </section>
    </main>
    <script src=""/app.js""></script>
  </body>
</html>";

    private const string AppJs = @"const clientId = document.getElementById('clientId');
const token = document.getElementById('token');
const repo = document.getElementById('repo');
const repoList = document.getElementById('repoList');
const repoFilter = document.getElementById('repoFilter');
const loadRepos = document.getElementById('loadRepos');
const operation = document.getElementById('operation');
const withConfig = document.getElementById('withConfig');
const skipSecret = document.getElementById('skipSecret');
const explicitSecrets = document.getElementById('explicitSecrets');
const dryRun = document.getElementById('dryRun');
const configJson = document.getElementById('configJson');
const configPath = document.getElementById('configPath');
const reviewProfile = document.getElementById('reviewProfile');
const reviewMode = document.getElementById('reviewMode');
const reviewCommentMode = document.getElementById('reviewCommentMode');
const branchName = document.getElementById('branchName');
const upgrade = document.getElementById('upgrade');
const force = document.getElementById('force');
const keepSecret = document.getElementById('keepSecret');
const output = document.getElementById('output');
const inspect = document.getElementById('inspect');
const deviceStart = document.getElementById('deviceStart');
const devicePoll = document.getElementById('devicePoll');
const deviceInfo = document.getElementById('deviceInfo');
let deviceState = null;

function write(text) {
  output.textContent = text;
}

function formatResults(data) {
  if (data && Array.isArray(data.results)) {
    const lines = [];
    const total = data.results.length;
    const succeeded = data.results.filter(r => r.exitCode === 0).length;
    const failed = total - succeeded;
    lines.push(`Summary: ${succeeded}/${total} succeeded`);
    if (failed > 0) {
      lines.push(`Failures: ${failed}`);
    }
    lines.push('');
    data.results.forEach(result => {
      const name = result.repo || 'repo';
      const status = result.exitCode === 0 ? 'success' : 'failed';
      lines.push(`== ${name} ==`);
      lines.push(`status: ${status}`);
      if (typeof result.exitCode !== 'undefined') {
        lines.push(`exit: ${result.exitCode}`);
      }
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
  if (data && data.error) {
    return `Error: ${data.error}`;
  }
  return JSON.stringify(data, null, 2);
}

function formatStatus(data) {
  if (!data || !Array.isArray(data.status)) {
    return formatResults(data);
  }
  const lines = [];
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

deviceStart.addEventListener('click', async () => {
  write('Starting device flow...');
  const res = await fetch('/api/device-code', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ clientId: clientId.value })
  });
  const data = await res.json();
  if (data.error) {
    write('Error: ' + data.error);
    return;
  }
  deviceState = data;
  deviceInfo.textContent = `Open ${data.verificationUri} and enter code ${data.userCode}`;
  devicePoll.disabled = false;
  window.open(data.verificationUri, '_blank');
});

devicePoll.addEventListener('click', async () => {
  if (!deviceState) {
    return;
  }
  write('Polling for token...');
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
  if (data.error) {
    write('Poll error: ' + data.error);
    return;
  }
  token.value = data.token || '';
  write('Token acquired.');
});

loadRepos.addEventListener('click', async () => {
  write('Loading repos...');
  const res = await fetch('/api/repos', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ token: token.value })
  });
  const data = await res.json();
  if (data.error) {
    write('Repo error: ' + data.error);
    return;
  }
  repoList.innerHTML = '';
  const filter = repoFilter.value.toLowerCase();
  data.repos
    .filter(r => r.name.toLowerCase().includes(filter))
    .forEach(r => {
      const option = document.createElement('option');
      option.value = r.name;
      option.textContent = r.name;
      repoList.appendChild(option);
    });
  write('Repos loaded.');
});

repoFilter.addEventListener('input', () => {
  const filter = repoFilter.value.toLowerCase();
  Array.from(repoList.options).forEach(opt => {
    opt.hidden = filter && !opt.value.toLowerCase().includes(filter);
  });
});

function selectedRepos() {
  const selected = Array.from(repoList.selectedOptions).map(opt => opt.value);
  if (selected.length > 0) {
    return selected;
  }
  if (repo.value.trim()) {
    return [repo.value.trim()];
  }
  return [];
}

inspect.addEventListener('click', async () => {
  write('Checking existing setup...');
  const repos = selectedRepos();
  if (repos.length === 0) {
    write('Select or enter a repository.');
    return;
  }
  if (!token.value) {
    write('GitHub token required to inspect repositories.');
    return;
  }
  const res = await fetch('/api/repo-status', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      repos,
      token: token.value
    })
  });
  const data = await res.json();
  write(formatStatus(data));
});

function resolveWithConfig() {
  return withConfig.checked
    || configJson.value.trim().length > 0
    || configPath.value.trim().length > 0;
}

operation.addEventListener('change', () => {
  if (operation.value === 'cleanup') {
    skipSecret.checked = true;
  }
});

configJson.addEventListener('input', () => {
  if (configJson.value.trim()) {
    withConfig.checked = true;
  }
});

configPath.addEventListener('input', () => {
  if (configPath.value.trim()) {
    withConfig.checked = true;
  }
});

document.getElementById('plan').addEventListener('click', async () => {
  write('Planning...');
  const repos = selectedRepos();
  if (repos.length === 0) {
    write('Select or enter a repository.');
    return;
  }
  const res = await fetch('/api/setup/plan', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      repos,
      gitHubToken: token.value,
      gitHubClientId: clientId.value,
      withConfig: resolveWithConfig(),
      configJson: configJson.value.trim(),
      configPath: configPath.value.trim(),
      reviewProfile: reviewProfile.value,
      reviewMode: reviewMode.value,
      reviewCommentMode: reviewCommentMode.value,
      skipSecret: skipSecret.checked,
      explicitSecrets: explicitSecrets.checked,
      dryRun: dryRun.checked,
      upgrade: upgrade.checked,
      force: force.checked,
      branchName: branchName.value.trim(),
      cleanup: operation.value === 'cleanup',
      keepSecret: keepSecret.checked
    })
  });
  const data = await res.json();
  write(formatResults(data));
});

document.getElementById('apply').addEventListener('click', async () => {
  write('Applying...');
  const repos = selectedRepos();
  if (repos.length === 0) {
    write('Select or enter a repository.');
    return;
  }
  const res = await fetch('/api/setup/apply', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      repos,
      gitHubToken: token.value,
      gitHubClientId: clientId.value,
      withConfig: resolveWithConfig(),
      configJson: configJson.value.trim(),
      configPath: configPath.value.trim(),
      reviewProfile: reviewProfile.value,
      reviewMode: reviewMode.value,
      reviewCommentMode: reviewCommentMode.value,
      skipSecret: skipSecret.checked,
      explicitSecrets: explicitSecrets.checked,
      dryRun: dryRun.checked,
      upgrade: upgrade.checked,
      force: force.checked,
      branchName: branchName.value.trim(),
      cleanup: operation.value === 'cleanup',
      keepSecret: keepSecret.checked
    })
  });
  const data = await res.json();
  write(formatResults(data));
});";

    private const string StylesCss = @"body {
  margin: 0;
  font-family: 'Segoe UI', system-ui, sans-serif;
  background: #f5f7fb;
  color: #1d2330;
}

.container {
  max-width: 720px;
  margin: 48px auto;
  padding: 0 24px;
}

header h1 {
  margin: 0 0 8px;
}

.card {
  background: white;
  border-radius: 12px;
  padding: 20px;
  box-shadow: 0 8px 24px rgba(15, 25, 40, 0.08);
  margin-bottom: 16px;
}

button {
  background: #1b6ef3;
  color: white;
  border: none;
  padding: 10px 16px;
  border-radius: 8px;
  cursor: pointer;
}

.output {
  background: #f0f2f6;
  padding: 12px;
  border-radius: 8px;
  min-height: 120px;
  font-family: Consolas, monospace;
  font-size: 13px;
  white-space: pre-wrap;
}

label {
  display: block;
  font-weight: 600;
  margin: 12px 0 6px;
}

input {
  width: 100%;
  padding: 8px 10px;
  border: 1px solid #d6dbe6;
  border-radius: 8px;
}

textarea {
  width: 100%;
  padding: 8px 10px;
  border: 1px solid #d6dbe6;
  border-radius: 8px;
  font-family: Consolas, monospace;
  font-size: 13px;
}

select {
  width: 100%;
  padding: 8px 10px;
  border: 1px solid #d6dbe6;
  border-radius: 8px;
  background: white;
}

details {
  margin-top: 12px;
}

summary {
  font-weight: 600;
  cursor: pointer;
}

.row {
  display: flex;
  gap: 16px;
  margin-top: 12px;
  flex-wrap: wrap;
}

.hint {
  font-size: 12px;
  color: #4b5563;
  margin-top: 8px;
}";
}
