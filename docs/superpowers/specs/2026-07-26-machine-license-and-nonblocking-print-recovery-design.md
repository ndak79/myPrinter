# Machine License and Nonblocking Print Recovery

## Goal

Stop intermittent reactivation caused by runtime hardware discovery, while keeping a license unusable when copied to another machine. Let any Windows user on the licensed machine run the app. Return the primary Print action to its ready state immediately after a manual-duplex back pass is sent, without discarding recovery.

## License Design

### Stored License

- New activations calculate the hardware fingerprint exactly once, before the server or offline license token is validated.
- The validated `LicenseToken` is stored in `%ProgramData%\\myPrinter\\license.dat` as a versioned envelope protected with Windows DPAPI `LocalMachine`.
- The envelope contains the signed token and runtime trust metadata. It is bound to the application and product through fixed additional entropy.
- Startup and runtime monitoring open only this machine-protected envelope. They never call `FingerprintHelper` and never compare against live hardware.
- Each startup still verifies the token signature, issuer, product, signed expiry, signed fingerprint claim, heartbeat policy, and trusted-time policy before accepting the license.
- Read-modify-write operations use a machine-wide license mutex and randomized same-directory temporary file followed by an atomic replacement. Two elevated users starting the desktop app at once cannot overwrite each other's trusted-time update or leave a partial envelope.
- The desktop application already declares `requireAdministrator`; therefore standard Windows users must have the existing UAC permission to run it. This change does not expand that privilege boundary.
- `MachineLicenseStore` owns the path, envelope format, atomic I/O, and DPAPI calls behind a small protector interface. Production uses Windows DPAPI; focused tests use an isolated directory and deterministic protector to exercise unreadable copied data and write failures without touching the real machine license.

### Security Boundary

- A copied envelope cannot be unprotected on a different Windows machine, so a copied license file cannot activate another machine.
- `LocalMachine` deliberately permits all local Windows users to use the same installation. It is not a defense against an untrusted local account or administrator that can execute arbitrary code on the licensed machine.
- The signed token remains authoritative for entitlement fields. Persisted fields are revalidated against the signed compact token before use.
- The shared license supports each Windows user running the existing elevated desktop app. Simultaneous interactive use in separate Windows sessions remains unsupported by the pre-existing fixed localhost backend port and per-session single-instance model; this work does not claim to solve that unrelated runtime limitation.

### Upgrade Migration

- If the new machine envelope does not exist, the app attempts one compatibility migration from an existing per-user or executable-adjacent legacy license file.
- Migration may calculate the legacy fingerprint candidates once because the old AES file cannot be opened otherwise.
- It verifies the legacy token, writes the new DPAPI envelope, and removes legacy copies only after the new write succeeds.
- A migration failure leaves the legacy file untouched and shows activation instead of silently deleting a usable license.
- Once a machine envelope exists, including a malformed one, startup never falls back to a legacy copy. This prevents a damaged or copied new file from reviving a stale legacy license.

## Print Recovery Design

### Normal Completion

- When `/api/print/continue` reports that a manual-duplex back pass has been sent, the current print command is complete from the main workflow perspective.
- The UI records the history entry, clears `currentJob`, returns the Print button to idle, resumes any pending print queue, and allows a new print immediately.
- The primary Print button is never repurposed as a success-path Cancel action.

### Nonblocking Recovery

- Backend jobs that have sent their back pass remain recoverable until explicit dismissal, cancellation, or the existing two-hour retention cleanup.
- Recovery context becomes a collection rather than a single latest job. `GET /api/print/recovery-context` returns the full ordered collection while retaining the legacy latest-job field for compatibility.
- The recovery control opens a chooser when more than one job is recoverable, then operates on the selected job only. Starting a new print cannot erase existing recovery records.
- The current `POST /api/print/complete` endpoint remains the explicit final dismissal path for a selected recovery record.

## Error Handling

- DPAPI corruption, cross-machine copied files, malformed envelopes, or signature failures are treated as unactivated. They do not trigger live fingerprint fallback after a machine envelope has existed.
- A failed DPAPI write rejects activation and preserves the prior valid envelope.
- A failed back pass keeps only the affected job in recovery and does not reset unrelated queued jobs.
- A recovery job whose source PDF disappears is omitted from the collection and cleaned up by the backend; the UI refreshes its list instead of treating the missing record as an active print.

## Verification

1. Focused activation tests prove new startup paths have no runtime fingerprint call, migration is one-way, a mismatched DPAPI protector cannot read a copied envelope, and signature/expiry checks still fail closed.
2. Backend endpoint tests prove recovery records are listed in order and individually completed without removing siblings.
3. Frontend tests prove a successful manual back pass restores Print, retains recovery items, adds history once, resumes queued files, and opens the intended recovery job from a multi-job list.
4. Regression tests include multi-file manual duplex, failed continuation, concurrent license writes, legacy migration, malformed/cross-machine license data, and stale recovery records.
