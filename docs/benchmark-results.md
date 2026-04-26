# Benchmark Results

Այս արդյունքները ստացվել են TenantScheduler ալգորիթմի deterministic simulation-ով։ Ժամանակը ներկայացված է simulation tick-երով, ոչ իրական վայրկյաններով։

| Սցենար | Jobs | Active tenants | Slots | Reserved headroom | Total ticks | Throughput (jobs/tick) | Avg latency | P95 latency | Slot utilization | Fairness spread |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Սցենար 1. 20 ակտիվ tenant | 200 | 20 | 20 | 1 | 56 | 3.57 | 28.43 | 50.00 | 89.3% | 0 |
| Սցենար 2. 2 ակտիվ tenant | 200 | 2 | 20 | 1 | 56 | 3.57 | 28.88 | 50.00 | 89.3% | 0 |
| Սցենար 3. Tenant activation burst | 100 | 20 | 20 | 1 | 31 | 3.23 | 15.35 | 25.00 | 80.6% | 0 |

Մեկնաբանություն.

- 20 ակտիվ tenant-ների դեպքում fairness spread-ը պետք է մոտ լինի 0-ին, քանի որ բոլոր tenant-ները ստանում են համաչափ հնարավորություն։
- 2 ակտիվ tenant-ների դեպքում slot utilization-ը բարձր է, քանի որ work-conserving մոտեցումը ազատ slot-երը տալիս է առկա ակտիվ tenant-ներին։
- Activation burst սցենարում tenant-ները աստիճանաբար ակտիվանում են, իսկ scheduler-ը հաջորդ ցիկլերում ներառում է նոր tenant-ներին բաշխման մեջ։