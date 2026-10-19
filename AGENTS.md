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
