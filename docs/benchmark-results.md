# Benchmark Results

Այս արդյունքները ստացվել են TenantScheduler ալգորիթմի deterministic simulation-ով։ Ժամանակը ներկայացված է simulation tick-երով, ոչ իրական վայրկյաններով։

| Սցենար | Jobs | Active tenants | Slots | Total ticks | Throughput (jobs/tick) | Avg latency | P95 latency | Slot utilization | Fairness spread |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Սցենար 1. 20 ակտիվ tenant | 200 | 20 | 20 | 51 | 3.00 | 27.50 | 50.00 | 98.0% | 0 |
| Սցենար 2. 2 ակտիվ tenant | 200 | 2 | 20 | 51 | 3.00 | 27.50 | 50.00 | 98.0% | 0 |
| Սցենար 3. Tenant activation burst | 100 | 20 | 20 | 26 | 3.00 | 15.00 | 25.00 | 96.2% | 0 |

Մեկնաբանություն.

- 20 ակտիվ tenant-ների դեպքում fairness spread-ը պետք է մոտ լինի 0-ին, քանի որ բոլոր tenant-ները ստանում են համաչափ հնարավորություն։
- 2 ակտիվ tenant-ների դեպքում slot utilization-ը բարձր է, քանի որ work-conserving մոտեցումը ազատ slot-երը տալիս է առկա ակտիվ tenant-ներին։
- Activation burst սցենարում tenant-ները աստիճանաբար ակտիվանում են, իսկ scheduler-ը հաջորդ ցիկլերում ներառում է նոր tenant-ներին բաշխման մեջ։