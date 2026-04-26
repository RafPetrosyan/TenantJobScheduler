const scenarioEl = document.querySelector('#scenario');
const durationEl = document.querySelector('#duration');
const seedBtn = document.querySelector('#seedBtn');
const resetBtn = document.querySelector('#resetBtn');
const refreshBtn = document.querySelector('#refreshBtn');
const tenantSelector = document.querySelector('#tenantSelector');
const tenantLoadBtn = document.querySelector('#tenantLoadBtn');
const customTenantIdEl = document.querySelector('#customTenantId');
const customJobCountEl = document.querySelector('#customJobCount');
const customDurationEl = document.querySelector('#customDuration');
const addJobsBtn = document.querySelector('#addJobsBtn');
const faultTenantIdEl = document.querySelector('#faultTenantId');
const failJobBtn = document.querySelector('#failJobBtn');
const stuckJobBtn = document.querySelector('#stuckJobBtn');
const faultResultEl = document.querySelector('#faultResult');

seedBtn.addEventListener('click', seedScenario);
resetBtn.addEventListener('click', resetDemo);
refreshBtn.addEventListener('click', refreshAll);
tenantLoadBtn.addEventListener('click', loadTenantView);
addJobsBtn.addEventListener('click', addCustomJobs);
failJobBtn.addEventListener('click', () => createFaultDemo('fail'));
stuckJobBtn.addEventListener('click', () => createFaultDemo('stuck'));

refreshAll();
setInterval(refreshAll, 2500);

async function seedScenario() {
  await fetch('/demo/scenarios', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      scenario: scenarioEl.value,
      durationMs: Number(durationEl.value),
    }),
  });
  await refreshAll();
}

async function resetDemo() {
  await fetch('/demo/reset', { method: 'POST' });
  await refreshAll();
}

async function addCustomJobs() {
  const tenantId = customTenantIdEl.value.trim();
  const count = Number(customJobCountEl.value);
  const durationMs = Number(customDurationEl.value);

  if (!tenantId || count < 1) {
    return;
  }

  await fetch('/demo/custom-jobs', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ tenantId, count, durationMs }),
  });
  await refreshAll();
}

async function createFaultDemo(mode) {
  const tenantId = faultTenantIdEl.value.trim() || 'tenant-fault-demo';
  const response = await fetch('/demo/faults', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      tenantId,
      mode,
      durationMs: 500,
      createAsRunning: mode === 'stuck',
    }),
  });
  const responseBody = await response.json().catch(() => ({}));
  faultResultEl.textContent = JSON.stringify({
    status: response.status,
    response: responseBody,
    expected: mode === 'fail'
      ? 'Worker fails it repeatedly; Queue Service retries and then moves it to Dead-letter.'
      : 'Queue Service sees expired Running lock and recovers it back to Queued.',
  }, null, 2);
  await refreshAll();
}

async function refreshAll() {
  await Promise.all([
    loadHealth(),
    loadSecurity(),
    loadJobs(),
    loadPreview(),
  ]);
}

async function loadHealth() {
  const label = document.querySelector('#apiStatus');
  try {
    const response = await fetch('/health');
    label.textContent = response.ok ? 'API online' : 'API problem';
    label.className = response.ok ? 'pill ok' : 'pill warn';
  } catch {
    label.textContent = 'API offline';
    label.className = 'pill warn';
  }
}

async function loadSecurity() {
  const response = await fetch('/demo/security');
  const security = await response.json();
  const httpsStatus = document.querySelector('#httpsStatus');
  httpsStatus.textContent = security.https.enabledForThisRequest ? 'HTTPS active' : 'HTTP demo mode';
  httpsStatus.className = security.https.enabledForThisRequest ? 'pill ok' : 'pill warn';

  document.querySelector('#securityPanel').innerHTML = [
    securityItem('HTTPS communication', security.https.note, security.https.enabledForThisRequest ? '' : 'warn'),
    securityItem('Job payload encryption', `Storage contains encrypted payload only: ${security.payloadEncryption.encryptedPayloadPreview}`),
    securityItem('Tenant isolation', `${security.tenantIsolation.allowedExample}; ${security.tenantIsolation.deniedExample}`),
  ].join('');
}

async function loadJobs() {
  const response = await fetch('/demo/jobs');
  const jobs = await response.json();
  const counts = countByStatus(jobs);
  document.querySelector('#queuedMetric').textContent = counts.Queued || 0;
  document.querySelector('#runningMetric').textContent = counts.Running || 0;
  document.querySelector('#completedMetric').textContent = counts.Completed || 0;
  document.querySelector('#deadMetric').textContent = counts.DeadLetter || 0;

  const tenantIds = [...new Set(jobs.map((job) => job.tenantId))].sort();
  tenantSelector.innerHTML = tenantIds.map((tenantId) => `<option value="${tenantId}">${tenantId}</option>`).join('');

  document.querySelector('#jobsTable').innerHTML = jobs.slice(-80).reverse().map((job) => `
    <tr>
      <td>${escapeHtml(job.tenantId)}</td>
      <td>${escapeHtml(job.status)}</td>
      <td>${job.attemptCount}</td>
      <td><code>${escapeHtml(job.encryptedPayloadPreview)}</code></td>
    </tr>
  `).join('');
}

async function loadPreview() {
  const response = await fetch('/demo/scheduler-preview');
  const preview = await response.json();
  const tenants = [...preview.perTenant];
  const maxSelected = Math.max(1, ...tenants.map((tenant) => tenant.selected));

  document.querySelector('#tenantBars').innerHTML = tenants.map((tenant) => `
    <div class="tenant-row">
      <strong>${escapeHtml(tenant.tenantId)}</strong>
      <div class="bar-track">
        <div class="bar-fill" style="width: ${Math.max(4, tenant.selected / maxSelected * 100)}%"></div>
      </div>
      <span>${tenant.selected} slot</span>
    </div>
  `).join('') || '<p>No queued jobs. Apply a scenario to preview scheduling.</p>';
}

async function loadTenantView() {
  const tenantId = tenantSelector.value;
  if (!tenantId) {
    document.querySelector('#tenantResult').textContent = 'No tenant is available yet.';
    return;
  }

  const allowed = await fetch(`/tenants/${tenantId}/jobs`, {
    headers: { 'X-Tenant-Id': tenantId },
  });
  const denied = await fetch(`/tenants/${tenantId}/jobs`, {
    headers: { 'X-Tenant-Id': 'different-tenant' },
  });
  const jobs = await allowed.json();

  document.querySelector('#tenantResult').textContent = JSON.stringify({
    allowedRequestStatus: allowed.status,
    returnedTenantIds: [...new Set(jobs.map((job) => job.tenantId))],
    deniedCrossTenantStatus: denied.status,
  }, null, 2);
}

function countByStatus(jobs) {
  return jobs.reduce((counts, job) => {
    counts[job.status] = (counts[job.status] || 0) + 1;
    return counts;
  }, {});
}

function securityItem(title, body, variant = '') {
  return `<div class="security-item ${variant}"><strong>${escapeHtml(title)}</strong><span>${escapeHtml(body)}</span></div>`;
}

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');
}
