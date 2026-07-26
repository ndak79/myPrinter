# Machine License and Nonblocking Print Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist a once-bound license per Windows machine without runtime fingerprinting and let manual-duplex jobs become recoverable without blocking the next print.

**Architecture:** `MachineLicenseStore` owns machine-scoped DPAPI storage behind an injectable protector, while `LicenseGuard` uses live fingerprint data only to activate or migrate a legacy file. Recovery changes expose the existing persisted collection, and the frontend separates active printing from selected recovery state.

**Tech Stack:** .NET 10 Windows, Windows DPAPI, xUnit/FluentAssertions, ASP.NET Minimal API, vanilla JavaScript.

---

### Task 1: Machine-scoped license store

**Files:**
- Create: `desktop/Activation/MachineLicenseStore.cs`
- Modify: `desktop/Activation/LicenseStorage.cs`
- Modify: `desktop/Properties/AssemblyInfo.cs`
- Test: `desktop.Tests/MachineLicenseStoreTests.cs`

- [ ] **Step 1: Write failing storage tests**

```csharp
[Fact]
public void Load_RejectsEnvelopeProtectedForAnotherMachine()
{
    var source = new TestProtector("machine-a");
    var target = new TestProtector("machine-b");
    new MachineLicenseStore(_directory, source).TrySave(_token).Should().BeTrue();
    new MachineLicenseStore(_directory, target).Load().Should().BeNull();
}
```

- [ ] **Step 2: Run the focused test and confirm it fails because `MachineLicenseStore` does not exist.**

Run: `dotnet test desktop.Tests/desktop.Tests.csproj --filter FullyQualifiedName~MachineLicenseStoreTests`

- [ ] **Step 3: Implement a versioned, atomic DPAPI envelope.**

```csharp
internal sealed class MachineLicenseStore
{
    internal bool TrySave(LicenseToken token)
        => TryWrite(ProtectedData.Protect(
            JsonSerializer.SerializeToUtf8Bytes(token),
            Entropy,
            DataProtectionScope.LocalMachine));

    internal LicenseToken? Load()
        => TryRead(out var blob)
            ? JsonSerializer.Deserialize<LicenseToken>(ProtectedData.Unprotect(
                blob, Entropy, DataProtectionScope.LocalMachine))
            : null;
}
```

Use `%ProgramData%\\myPrinter`, `DataProtectionScope.LocalMachine`, fixed product entropy, a machine-wide mutex, and no plaintext fallback.

- [ ] **Step 4: Re-run focused tests and add write-failure/round-trip cases.**

Run: `dotnet test desktop.Tests/desktop.Tests.csproj --filter FullyQualifiedName~MachineLicenseStoreTests`

### Task 2: Activation migration and fingerprint-free startup

**Files:**
- Modify: `desktop/Activation/LicenseStorage.cs`
- Modify: `desktop/Activation/LicenseGuard.cs`
- Modify: `desktop.Tests/ActivationCompatibilityTests.cs`
- Test: `desktop.Tests/MachineLicenseStoreTests.cs`

- [ ] **Step 1: Write failing migration and startup regression tests.**

```csharp
[Fact]
public void IsActivated_LoadsMachineStoreBeforeAnyFingerprintLookup()
    => ExtractMethodSource(guard, "public static bool IsActivated")
       .Should().NotContain("FingerprintHelper");
```

- [ ] **Step 2: Run focused tests and confirm the current implementation still calls `GetFingerprintCandidates`.**

Run: `dotnet test desktop.Tests/desktop.Tests.csproj --filter FullyQualifiedName~ActivationCompatibilityTests`

- [ ] **Step 3: Route normal load/save/delete through the machine store.**

`IsActivated` loads the sealed token and validates it using its stored signed fingerprint. `ActivateOnlineAsync` and `ActivateOfflineAsync` calculate once, validate, then save. Legacy AES loading is an explicit one-time migration path only when no machine envelope exists.

- [ ] **Step 4: Run activation tests and full desktop tests.**

Run: `dotnet test desktop.Tests/desktop.Tests.csproj`

### Task 3: Recoverable job collection API

**Files:**
- Modify: `backend/Services/FileSessionService.cs`
- Modify: `backend/Models/PrintModels.cs`
- Modify: `backend/BackendStartup.cs`
- Modify: `backend.Tests/FileSessionServiceCleanupTests.cs`
- Create: `backend.Tests/PrintRecoveryContextTests.cs`

- [ ] **Step 1: Write failing tests for ordered, sibling-safe recovery records.**

```csharp
[Fact]
public void GetRecoverableJobs_ReturnsEveryLiveJobInCreationOrder()
{
    _sut.GetRecoverableJobs().Select(job => job.JobId)
        .Should().Equal(first.JobId, second.JobId);
}
```

- [ ] **Step 2: Run the service tests and confirm the collection method is absent.**

Run: `dotnet test backend.Tests/backend.Tests.csproj --filter FullyQualifiedName~FileSessionServiceCleanupTests`

- [ ] **Step 3: Implement the collection and backward-compatible response.**

`GetRecoverableJobs` filters with existing `IsRecoverable`, orders by `CreatedAt`, and returns an immutable snapshot. `PrintResponse.JobStates` carries the collection; `JobState` remains the latest item for existing clients. Completing one job only removes that job.

- [ ] **Step 4: Run backend tests.**

Run: `dotnet test backend.Tests/backend.Tests.csproj`

### Task 4: Nonblocking manual-duplex completion and recovery selection

**Files:**
- Modify: `frontend/index.html`
- Modify: `frontend/app.js`
- Modify: `frontend/i18n/vi.js`
- Modify: `frontend/i18n/en.js`
- Modify: `frontend/styles.css`
- Modify: `frontend/tests/print-actions-layout.test.mjs`
- Create: `frontend/tests/print-recovery-collection.test.mjs`

- [ ] **Step 1: Write failing frontend assertions for completion state.**

```js
assert.doesNotMatch(continuePrintBlock, /dataset\.mode = 'phase2-review'/);
assert.match(continuePrintBlock, /this\._setPrintButtonIdle\(btn\)/);
assert.match(appJs, /jobStates \|\| result\.JobStates/);
```

- [ ] **Step 2: Run both frontend tests and confirm they fail against the cancel-to-finish flow.**

Run: `node frontend/tests/print-actions-layout.test.mjs; node frontend/tests/print-recovery-collection.test.mjs`

- [ ] **Step 3: Implement separate active and recovery collections.**

After each successful back pass, add history exactly once, clear only active print state, refresh recovery records, return Print to idle, and resume queued files. Add a compact recovery chooser modal only when more than one job exists; selecting a job sets the recovery target without changing Print mode.

- [ ] **Step 4: Run frontend tests.**

Run: `node frontend/tests/print-actions-layout.test.mjs; node frontend/tests/print-copy-sequencing.test.mjs; node frontend/tests/print-recovery-collection.test.mjs`

### Task 5: Integration review and release checks

**Files:**
- Modify: `docs/superpowers/specs/2026-07-26-machine-license-and-nonblocking-print-recovery-design.md` only if implementation exposes a spec inconsistency.

- [ ] **Step 1: Run graph change detection.**

Run: `gitnexus detect-changes --repo "D:\\Projs\\myPrinter"`

- [ ] **Step 2: Run all impacted checks.**

Run: `dotnet test backend.Tests/backend.Tests.csproj; dotnet test desktop.Tests/desktop.Tests.csproj; node frontend/tests/print-actions-layout.test.mjs; node frontend/tests/print-copy-sequencing.test.mjs; node frontend/tests/print-recovery-collection.test.mjs`

- [ ] **Step 3: Perform counterfactual verification.**

Temporarily bypass the sealed-store load path and the idle transition in the working tree one at a time, confirm their focused regression tests fail, restore the implementation, and rerun the tests.

- [ ] **Step 4: Inspect the final diff, run `git diff --check`, and record any unverified runtime proof.**
