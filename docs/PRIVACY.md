# Privacy and local state

- Official quota reads use the installed Codex app-server over an owned stdio connection. This app sends initialize, initialized and account/rateLimits/read; it does not submit model work or redeem quota resets.
- Codex itself handles its existing authentication. Quota Clock does not directly read or save credential files.
- The optional estimator reads local Codex session/archived-session logs to extract token/model and quota observations. Those files can contain conversations; the app does not upload them, display conversations, or write changes back.
- Calibration remains in memory. Full-run projection recording is enabled by default and writes one local CSV per launch under `history/`: elapsed time, consumed percentage, three normalized needle-tip coordinates, validity/estimate flags and a local epoch counter. It does not include account identifiers, conversations or authentication. Recording can be paused in settings; existing files are retained.
- Preferences and small diagnostic status files are also written beside the executable. CSV history describes usage and should be treated as private. Do not publish local records or diagnostics with bug reports without reviewing them.
- `history/`, `test-output/`, preferences and runtime snapshots are excluded by `.gitignore`. No recorded quota data is uploaded automatically.
- There is no app analytics endpoint or app-owned network listener. The Codex child contacts its account service as part of quota queries.
- Demo screenshots in this repository use illustrative values. No personal configuration, login state or live account snapshots are included.
