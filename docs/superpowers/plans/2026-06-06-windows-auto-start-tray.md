# Windows Auto Start Tray Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add default-on Windows auto-start that launches Smart Printer hidden in the tray and can be toggled from the tray context menu.

**Architecture:** Add a focused `WindowsStartupService` in the desktop project to manage a Task Scheduler logon task with `schtasks.exe` and a per-user opt-out preference. Thread the `--start-hidden` switch through `Program.Main` into `MainForm`, and have `MainForm` suppress only its first automatic show and tray balloon for that launch mode.

**Tech Stack:** .NET 10 Windows Forms, xUnit, FluentAssertions, Windows `schtasks.exe`.

---

### Task 1: Startup Service

**Files:**
- Create: `desktop/WindowsStartupService.cs`
- Test: `desktop.Tests/WindowsStartupServiceTests.cs`

- [ ] **Step 1: Write failing tests**

Add tests that verify `Enable` invokes `schtasks.exe /Create /SC ONLOGON /RL HIGHEST /F` with the current executable plus `--start-hidden`, `Disable` invokes `/Delete /F`, and `IsEnabled` treats exit code 0 as enabled.

- [ ] **Step 2: Run red tests**

Run: `dotnet test desktop.Tests/desktop.Tests.csproj --filter WindowsStartupServiceTests`
Expected: fail because `WindowsStartupService` does not exist.

- [ ] **Step 3: Implement the service**

Create `WindowsStartupService` with an injectable runner delegate, a stable task name, and public `IsEnabled`, `Enable`, `Disable`, and `EnsureEnabledByDefault` methods.

- [ ] **Step 4: Run green tests**

Run: `dotnet test desktop.Tests/desktop.Tests.csproj --filter WindowsStartupServiceTests`
Expected: pass.

### Task 2: Hidden Startup Flow

**Files:**
- Modify: `desktop/Program.cs`
- Modify: `desktop/MainForm.cs`
- Test: `desktop.Tests/SingleInstanceCoordinatorTests.cs`

- [ ] **Step 1: Write failing tests**

Add source-level tests that require `Program.Main` to detect `--start-hidden`, ensure Windows startup by default, and construct `new MainForm(startHidden)`.

- [ ] **Step 2: Run red tests**

Run: `dotnet test desktop.Tests/desktop.Tests.csproj --filter SingleInstanceCoordinatorTests`
Expected: fail until the startup switch and default enablement are added.

- [ ] **Step 3: Implement hidden startup**

Parse `Environment.GetCommandLineArgs()` in `Program.Main`, call `WindowsStartupService.Default.EnsureEnabledByDefault()`, pass `startHidden` into `MainForm`, and override `SetVisibleCore` in `MainForm` to suppress only the first automatic show.

- [ ] **Step 4: Run green tests**

Run: `dotnet test desktop.Tests/desktop.Tests.csproj --filter SingleInstanceCoordinatorTests`
Expected: pass.

### Task 3: Tray Toggle

**Files:**
- Modify: `desktop/MainForm.cs`
- Test: `desktop.Tests/ActivationCompatibilityTests.cs`

- [ ] **Step 1: Write failing tests**

Add compatibility checks that the tray menu contains `Start with Windows`, uses `CheckOnClick`, and calls startup enable/disable methods.

- [ ] **Step 2: Run red tests**

Run: `dotnet test desktop.Tests/desktop.Tests.csproj --filter ActivationCompatibilityTests`
Expected: fail until the tray item is added.

- [ ] **Step 3: Implement tray menu item**

Add a checked tray menu item between Hide and Exit. Initialize it from `IsEnabled`, update Task Scheduler on click, and restore the check state if the command fails.

- [ ] **Step 4: Run green tests**

Run: `dotnet test desktop.Tests/desktop.Tests.csproj --filter ActivationCompatibilityTests`
Expected: pass.

### Task 4: Verification and Git

**Files:**
- All changed implementation, tests, spec, and plan files.

- [ ] **Step 1: Full verification**

Run: `dotnet test MyPrinter.slnx`
Expected: pass.

- [ ] **Step 2: GitNexus change detection**

Run: `npx gitnexus detect-changes`
Expected: changed symbols limited to desktop startup/tray code and associated tests/docs.

- [ ] **Step 3: Review**

Review diffs for medium+ bugs: quoting, UAC/task behavior, hidden-window flash, tray toggle state consistency, unrelated staged files.

- [ ] **Step 4: Commit and push**

Stage only feature files, commit with `feat: add windows auto-start tray toggle`, and push `master` to `origin`.
