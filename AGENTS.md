# Publishing Policy

- Do not automatically upload, publish, or update this mod on Steam Workshop.
- The user handles Steam publishing. A request to develop, update, build,
  test, package, commit, or push code is not permission to publish on Steam.
- Steam publishing requires a new, explicit user request for that release.
- Local builds, versioned packages, and backed-up local installation updates
  are separate from publishing and do not authorize any remote upload.

# Compatibility Diagnostics

- Keep diagnostics developer-only and explicitly invoked, not a tick or
  render callback. Do not add diagnostic work to normal gameplay loops.
- Preserve native invisibility, per-observer targeting, discovery events,
  and the existing detection cache when collecting a report.
- A negative game invisibility predicate does not prove custom stealth.
  Report this as inconclusive rather than guessing from DefNames or types.
- Build both 1.6 and 1.7. Tests using the 1.7 runtime are not a real 1.6
  playtest, and headless checks do not verify the in-game dialog visually.

# Optional Stealth Adapters

- Keep the core raw invisibility predicate native-only. Non-native stealth
  must be handled in a separate, explicitly supported adapter.
- Alpha Animals support uses its actual stalking-state hediff type, never
  a race check or a guessed DefName. Preserve the hediff and native AI rules.
- The optional adapter is enabled by default when its supported API is
  loaded and can be disabled in mod settings. It is not a required mod.
- Rendering and the optional graphics branch must only read published
  caches. Do not scan hediffs, resolve types, or check LoS during rendering.
- Invalidate graphics when either player detection or custom visual-state
  membership changes, including when native invisibility remains active.
