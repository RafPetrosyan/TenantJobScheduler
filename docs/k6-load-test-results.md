# k6 Load Test Results

This report was produced by sending real HTTP requests to the Tenant API.

## Test Environment

| Field | Value |
| --- | --- |
| Timestamp | 2026-04-26 13:17:49 |
| Machine | PETROSYAN |
| User | Admin |
| OS | Майкрософт Windows 11 Pro (10.0.26200) |
| CPU | 12th Gen Intel(R) Core(TM) i5-12400 |
| Logical processors | 12 |
| Memory GB | 15.77 |
| .NET SDK | 9.0.305 |
| k6 | k6.exe v1.7.1 (commit/9f82e6f1fc, go1.26.1, windows/amd64) |

## Load Conditions

| Field | Value |
| --- | --- |
| Base URL | http://localhost:5080 |
| Arrival rate | 40 req/s |
| Duration per scenario | 1m |
| Job duration | 500 ms |
| Queue worker slots | 20 |
| Scenarios | all-tenants, two-tenants, activation-burst |

## Results

| Scenario | Submitted jobs | HTTP failed rate | HTTP avg ms | HTTP p95 ms | Submit avg ms | Submit p95 ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| all-tenants | 2400 | 0,00% | 19,84 | 79,71 | 19,83 | 79,76 |
| two-tenants | 2401 | 0,00% | 25,13 | 124,86 | 25,10 | 123,32 |
| activation-burst | 2401 | 0,00% | 24,95 | 118,93 | 24,96 | 118,94 |

Notes:

- all-tenants checks concurrent activity from 20 tenants.
- two-tenants checks work-conserving behavior when only 2 tenants are active.
- activation-burst checks API and queue response when tenants become active in bursts.

Raw JSON summaries are stored in the docs\k6-results folder.
