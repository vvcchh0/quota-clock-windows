# Privacy and local state

- Official quota reads use the installed Codex app-server over an owned stdio connection. This app sends initialize, initialized and account/rateLimits/read; it does not submit model work or redeem quota resets.
- Codex itself handles its existing authentication. Quota Clock does not directly read or save credential files.
- The optional estimator reads local Codex session/archived-session logs to extract token/model and quota observations. Those files can contain conversations; the app does not upload them, display conversations, or write changes back.
- Calibration and trail history are held in memory. Preferences and small diagnostic status files are written beside the executable. Do not publish these local files with bug reports without reviewing them.
- There is no app analytics endpoint or app-owned network listener. The Codex child contacts its account service as part of quota queries.
- Demo screenshots in this repository use illustrative values. No personal configuration, login state or live account snapshots are included.
