import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend, Counter } from 'k6/metrics';

const baseUrl = __ENV.BASE_URL || 'http://localhost:5080';
const scenario = __ENV.SCENARIO || 'all-tenants';
const durationMs = Number(__ENV.JOB_DURATION_MS || '500');
const rate = Number(__ENV.RATE || '40');
const duration = __ENV.DURATION || '1m';

export const options = {
  scenarios: {
    submit_jobs: {
      executor: 'constant-arrival-rate',
      rate,
      timeUnit: '1s',
      duration,
      preAllocatedVUs: 20,
      maxVUs: 100,
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<750'],
  },
};

const submitted = new Counter('jobs_submitted');
const acceptedLatency = new Trend('job_submit_latency');

export function setup() {
  http.post(`${baseUrl}/demo/reset`);
  return { scenario, rate, duration, durationMs };
}

export default function () {
  const tenantId = pickTenant();

  const response = http.post(`${baseUrl}/demo/custom-jobs`, JSON.stringify({
    tenantId,
    count: 1,
    durationMs,
  }), {
    headers: { 'Content-Type': 'application/json' },
  });

  check(response, {
    'job accepted': (res) => res.status === 202,
  });

  submitted.add(1, { tenant: tenantId, scenario });
  acceptedLatency.add(response.timings.duration, { tenant: tenantId, scenario });
  sleep(0.1);
}

export function teardown() {
  const response = http.get(`${baseUrl}/demo/jobs`);
  check(response, {
    'job list readable after load': (res) => res.status === 200,
  });
}

function pickTenant() {
  if (scenario === 'two-tenants') {
    return `tenant-${(__VU % 2) + 1}`;
  }

  if (scenario === 'activation-burst') {
    const second = Math.floor(Date.now() / 1000) % 20;
    return `tenant-${second + 1}`;
  }

  return `tenant-${(__ITER % 20) + 1}`;
}
