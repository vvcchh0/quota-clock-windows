# Short local performance measurements

Windows 11, Intel i7-13700H, 20 logical processors; 15-second quota refresh, local estimation and taskbar widget enabled, detail/settings windows closed. The new version also draws the history.

| Version | Average process-tree CPU | Average working set | Sampled working-set peak | Average private commit |
|---|---:|---:|---:|---:|
| 0.3, repeated CLI startup | 0.119% | 115.8 MiB | 310.4 MiB | Not measured for the full tree |
| 0.4, reuse-only experiment | 0.011% | 121.8 MiB | 241.5 MiB | 128.0 MiB |
| 0.4, bounded cache | 0.015% | 113.1 MiB | 207.8 MiB | 126.2 MiB |

Each observation followed a 30-second warmup and lasted about 130 seconds with nine reads. CPU is cumulative process-tree time divided by elapsed time and 20 logical processors. The previous run sampled inside the app at about 100 ms; the newer run sampled externally at about 250 ms. Peaks and small differences depend on sampling, networking and background activity; shared working-set pages can be counted more than once. Cold startup adds a separate peak.

Most savings come from avoiding repeated native CLI startup. Private commit remains a real cost: working-set reclamation makes resident pages reclaimable, not all private allocations disappear. The app applies that only to its own idle child, at completed reads; it retires a connection above 96 MiB private cache or after 120 reads. The observation window did not reach the 120-read cap; synthetic transport tests cover rotation, oversized cache, timeout and recovery.

These short measurements are useful local observations, not a long-running soak test or a guarantee for other machines.

## Version 0.5 recording and three curves

The full-run journal appends in the background and keeps a bounded 4096-sample recent cache, 2048 summary buckets and bounded file checkpoints. A synthetic 100000-sample test verifies complete coverage and preserved extrema within those limits. Closed history windows do not render or scan old records. Visible history redraws on new data or view changes; desktop scrolling remains one update per second.

An initial 0.5 live measurement included opening details/settings, so it is retained as an interaction sample and is not used as a controlled background comparison. Long-running behaviour and disk-full/fault recovery still need broader real-device testing.

A final 60-second check after a 30-second warmup kept details/settings/history closed throughout the measured window: average process-tree CPU 0.030%, main working set 127.7 MiB, combined working set 162.3 MiB, combined private commit 159.4 MiB. Four quota reads completed with no journal error or dropped samples. This run is not a claim of lower memory than 0.4: UI/resource loading, the local calibration workload and native CLI state affect the result.
