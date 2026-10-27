# Related-project review

Reviewed on 2026-10-27 using GitHub repository/code search and primary README/source files. Search indexing and wording limit coverage; this is not proof that no other implementation exists.

| Project | What was verified | Relationship to this project |
|---|---|---|
| [JinpengLiu-6/QuotaClock](https://github.com/JinpengLiu-6/QuotaClock) | [QuotaClockDial.vue](https://github.com/JinpengLiu-6/QuotaClock/blob/main/src/components/QuotaClockDial.vue) renders two percentage progress rings and a numeric value. | Same broad quota-clock idea and name; a browser extension with a different mapping. |
| [carey-bk/QuotaClock](https://github.com/carey-bk/QuotaClock) | README describes a native macOS quota/balance monitor with menu bar, widgets and screen saver. | Similar glanceable quota-monitoring surfaces. |
| [zongjiexiang/codex-token-clock](https://github.com/zongjiexiang/codex-token-clock) | README describes a transparent Windows/macOS quota widget using app-server, short/weekly limits and reset timestamps. | Close desktop/use-case and data-source overlap. |
| [except-pass/tinstar](https://github.com/except-pass/tinstar) | [CcQuotaClock.tsx](https://github.com/except-pass/tinstar/blob/main/src/components/CanvasHud/CcQuotaClock.tsx) combines a quota arc with an hour hand derived from the local wall clock. | A genuine quota-clock visualization, with a different hand meaning. |
| [kevin-zhengzzh/dsh_plugin_quota-clock](https://github.com/kevin-zhengzzh/dsh_plugin_quota-clock) | README describes API balance beside a local-time three-hand clock highlighting peak-pricing windows. | Clock plus quota/pricing, rather than quota-driven hand positions. |
| [akechi1980/QuotaClock](https://github.com/akechi1980/QuotaClock) | README describes ESP32-S3 seven-segment time and three quota progress rows. | Hardware desktop display with a different presentation. |

Quota Clock for Windows focuses on quota-driven short/long/fine hands and a scrolling projection of the fine hand. No matching combination was identified in the inspected results, but related quota-clock concepts already exist. No code from these comparison projects was incorporated.

The explicitly attributed visual reference is [zoefix/agent-speed](https://github.com/zoefix/agent-speed); palette stop values adapted from it retain its MIT notice.
