# Nonblocking Manual Duplex Print Recovery

## Goal

Return the primary Print action to its ready state immediately after a manual-duplex back pass is sent, without discarding recovery information for sheets that may still need replacement.

## Normal completion

- When `/api/print/continue` reports that a manual-duplex back pass has been sent, the current print command is complete from the main workflow perspective.
- The UI records the history entry, clears `currentJob`, returns the Print button to idle, resumes any pending print queue, and allows a new print immediately.
- The primary Print button is never repurposed as a success-path Cancel action.

## Nonblocking recovery

- Backend jobs that have sent their back pass remain recoverable until explicit dismissal, cancellation, or the existing retention cleanup.
- Recovery context is a collection rather than a single latest job. `GET /api/print/recovery-context` returns the full ordered collection while retaining the legacy latest-job field for compatibility.
- The recovery control opens a chooser when more than one job is recoverable, then operates on the selected job only. Starting a new print cannot erase existing recovery records.
- `POST /api/print/complete` remains the explicit final dismissal path for a selected recovery record.

## Error handling

- A failed back pass keeps only the affected job in recovery and does not reset unrelated queued jobs.
- A recovery job whose source PDF disappears is omitted from the collection and cleaned up by the backend; the UI refreshes its list instead of treating the missing record as an active print.

## Verification

1. Backend endpoint tests prove recovery records are listed in order and individually completed without removing siblings.
2. Frontend tests prove a successful manual back pass restores Print, retains recovery items, adds history once, resumes queued files, and opens the intended recovery job from a multi-job list.
3. Regression tests cover multi-file manual duplex, failed continuation, and stale recovery records.
