# Hangfire Comparison Matrix

Run the same `load-tests/k6-tenant-fairness.js` scenarios against the tenant-aware system and the Hangfire baseline.

| Criterion | Tenant-aware scheduler | Hangfire baseline |
| --- | --- | --- |
| Tenant fairness | Dynamic active-tenant slot allocation plus Round-Robin dispatch | FIFO/background queue behavior; tenant fairness requires custom filters or separate queues |
| Resource utilization | Work-conserving: idle slots are reused by active tenants | High utilization, but without built-in per-tenant fairness |
| Configuration complexity | Requires scheduler parameters: slots, lock timeout, retry policy | Simple startup, but tenant-aware fairness needs additional custom configuration |
| 20 active tenants | Expected near-even distribution: about one active slot per tenant with 20 slots | Expected distribution depends on enqueue order |
| 2 active tenants | Expected work-conserving split: about ten slots per tenant with 20 slots | May be dominated by the tenant that enqueued first or fastest |
| Activation burst | Newly active tenants enter the scheduler set on the next dispatch cycle | Burst behavior depends on queue order and worker availability |

Recommended metrics:

- `throughput`: accepted jobs per second and completed jobs per second.
- `latency per tenant`: p50, p95, and p99 from submission to completion.
- `slot utilization`: running jobs divided by configured `TOTAL_SLOTS`.
- `fairness`: per-tenant completed job count variance under identical load.
