# Full-run projection records

Version 0.5 records all available hour/minute/fine-hand projections every 15 seconds, even when a curve is hidden. State changes can add a boundary sample. The desktop tail remains a recent view; the run window can show the entire launch, zoom, pan, reopen or export a CSV.

## Local storage

Each launch creates a separate `history/run-<UTC timestamp>-<random suffix>.csv`. Recording is enabled by default and may be paused in Floating window settings. Pausing inserts an invalid boundary marker and retains existing files. Demo mode is not written into real records. Ordinary exit drains the pending writer queue; power loss can lose pending work. An incomplete final CSV line is ignored on reopen.

Previous files remain after restart; use **Open record**. Version 0.5 begins collecting at startup and cannot recreate samples discarded by earlier versions. There is no automatic deletion policy: disk use grows with the requested full history, roughly half a megabyte per day at the normal cadence. Choose a writable app folder and review stored records before sharing them.

The CSV starts with `# QuotaClock projection history v1` and a UTC session-start header, followed by:

| Column | Meaning |
|---|---|
| `elapsed_s` | Monotonic seconds since this launch began |
| `consumed_percent` | The quota position used to draw the needles |
| `hour_y`, `minute_y`, `fine_y` | Vertical tip positions, relative to the centre and normalized by fine-hand length; negative means above the centre |
| `coarse_valid` | Whether the hour/minute position was available |
| `fine_valid` | Whether the fine-hand position was available |
| `estimated` | Whether the fine position was estimated |
| `epoch` | Local sequence separating quota-window/reset changes; no account or window identifiers are stored |

The short/long/fine cycles are 100%, 10% and 1% consumption. Their projected amplitudes respect the actual 67:94:102 needle-length ratio. Dots are sampled positions. Missing values, pauses, suspend gaps, epoch changes, backward corrections and large jumps are not joined as continuous consumption.

## Bounded work

- The desktop tail retains at most 256 recent samples / one hour and displays the latest 15 minutes.
- The run recorder retains at most 4096 raw recent samples and 2048 overview buckets. When needed, overview buckets merge while retaining first/last observations, extrema and gap metadata; they are not used to invent missing cycles.
- A long overview shows sampled min/max envelopes. Zoom uses recent raw samples or an indexed read from the CSV; reopened files are read on demand. Disk reads run off the UI thread and are cancelable.
- A background writer appends only new records, with a bounded 256-row queue. Write failures and unwritten-sample counts are visible; they do not cause repeated high-frequency retries. File checkpoints are also bounded.
- No extra model calls or quota requests are made for history. The existing 15-second official refresh remains independent of rendering.

The window offers full / 15-minute / 1-hour / 6-hour / 24-hour ranges, a pan slider, live follow and individual hand visibility. Opening and exporting records do not change the source CSV.
