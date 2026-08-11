# Community Release Without Product Activation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove product activation/licensing from Smart Printer and publish an accurate, community-ready README and MIT license without changing printing behavior.

**Architecture:** Keep the existing WinForms host, embedded ASP.NET backend, WebView2 frontend, printer/printing services, and mutex-only single-instance protection. Delete product activation and the cross-process window-activation channel, then simplify consumers and packaging so desktop startup depends only on local application code and backend readiness.

**Tech Stack:** C# / .NET 10 Windows Forms, ASP.NET Core, WebView2, xUnit, PowerShell, Inno Setup, vanilla JavaScript frontend.

## Global Constraints

- Work only on `manual-duplex`, created from the current `master` HEAD.
- Preserve pre-existing dirty build/test artefacts; stage only files belonging to this change.
- Remove product activation/licensing, but keep the open-source MIT license.
- Preserve all printing, preview, conversion, recovery, tray, startup, mutex-based single-instance, and localization behavior; remove cross-process window signaling.
- Run GitNexus impact analysis before editing existing symbols and `gitnexus detect-changes` before committing.

### Task 1: Add failing community-boundary tests

**Files:**
- Create: `desktop.Tests/CommunityBuildTests.cs`

**Interfaces:**
- Consumes: repository files under `desktop`, `build-installer.ps1`, and `installer/myPrinter.iss`.
- Produces: executable regression checks proving startup, project, and packaging no longer depend on product activation.

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;

namespace desktop.Tests;

public class CommunityBuildTests
{
    [Fact]
    public void Desktop_startup_and_main_window_have_no_product_gate()
    {
        ReadRepoFile("desktop", "Program.cs")
            .Should().NotContain("LicenseGuard")
            .And.NotContain("LoadActivationConfig")
            .And.NotContain("LoadPublicKeysetJson")
            .And.NotContain("ActivationForm");

        ReadRepoFile("desktop", "MainForm.cs")
            .Should().NotContain("RuntimeLicense")
            .And.NotContain("LicenseGuard")
            .And.NotContain("ActivationForm");
    }

    [Fact]
    public void Desktop_project_has_no_product_activation_assets_or_packages()
    {
        if (Directory.Exists(RepoPath("desktop", "Activation")))
            Directory.GetFileSystemEntries(RepoPath("desktop", "Activation")).Should().BeEmpty();
        File.Exists(RepoPath("desktop", "ActivationForm.cs")).Should().BeFalse();
        File.Exists(RepoPath("desktop", "ActivationForm.Designer.cs")).Should().BeFalse();
        File.Exists(RepoPath("desktop", "RuntimeLicenseMonitor.cs")).Should().BeFalse();
        File.Exists(RepoPath("desktop", "smartprinter.appsettings.json")).Should().BeFalse();

        ReadRepoFile("desktop", "MyPrinter.Desktop.csproj")
            .Should().NotContain("BouncyCastle")
            .And.NotContain("NSec.Cryptography")
            .And.NotContain("license_keyset")
            .And.NotContain("smartprinter.appsettings");
    }

    [Fact]
    public void Installer_does_not_require_product_activation_inputs()
    {
        ReadRepoFile("build-installer.ps1")
            .Should().NotContain("Activation")
            .And.NotContain("ServerUrl")
            .And.NotContain("ProductId")
            .And.NotContain("TransportKey")
            .And.NotContain("Keyset");

        ReadRepoFile("installer", "myPrinter.iss")
            .Should().NotContain("KeysetFileName")
            .And.NotContain("smartprinter.appsettings")
            .And.NotContain("Activation");
    }

    private static string RepoPath(params string[] parts)
        => Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", .. parts]);

    private static string ReadRepoFile(params string[] parts)
        => File.ReadAllText(RepoPath(parts));
}
```

- [ ] **Step 2: Run the focused tests and verify they fail for the existing activation surface**

Run: `dotnet test desktop.Tests/desktop.Tests.csproj -c Debug --filter FullyQualifiedName~CommunityBuildTests --logger "console;verbosity=minimal"`

Expected: FAIL because the current startup, project, and installer still contain activation code/assets.

### Task 2: Remove activation consumers from desktop startup

**Files:**
- Modify: `desktop/Program.cs`
- Modify: `desktop/MainForm.cs`
- Modify: `desktop.Tests/SingleInstanceCoordinatorTests.cs`

**Interfaces:**
- Consumes: `WindowsStartupService`, `SingleInstanceCoordinator`, `BackendStartup`, `MainForm`.
- Produces: startup that reaches the main application without product activation or cross-process window signaling.

- [ ] **Step 1: Run GitNexus impact for `Program.Main`, `Program.LoadActivationConfig`, `MainForm.SetupRuntimeLicenseMonitor`, `MainForm.ShowFromExternalActivation`, `SingleInstanceCoordinator.StartActivationListener`, and `SingleInstanceCoordinator.SignalExistingInstance`**

Run: `gitnexus impact -r myPrinter -f desktop/Program.cs --depth 3 --include-tests Program.Main`, `gitnexus impact -r myPrinter -f desktop/Program.cs --depth 3 --include-tests LoadActivationConfig`, `gitnexus impact -r myPrinter -f desktop/MainForm.cs --depth 3 --include-tests SetupRuntimeLicenseMonitor`, `gitnexus impact -r myPrinter -f desktop/MainForm.cs --depth 3 --include-tests ShowFromExternalActivation`, `gitnexus impact -r myPrinter -f desktop/SingleInstanceCoordinator.cs --depth 3 --include-tests StartActivationListener`, and `gitnexus impact -r myPrinter -f desktop/SingleInstanceCoordinator.cs --depth 3 --include-tests SignalExistingInstance`.

Expected: direct callers are limited to desktop startup/tests; product-gate helpers and the external window-activation channel are removed.

- [ ] **Step 2: Remove the startup gate and dead configuration helpers**

Delete the `MyPrinter.Desktop.Activation` import, pinned transport constants, activation `try/catch`, `LoadPublicKeysetJson`, `LoadActivationConfig`, `ValidateTransportConfig`, and activation-specific imports from `Program.cs`. Keep the existing worker-mode, mutex-only single-instance, auto-start, backend, readiness, main-form, and shutdown code; remove the listener and secondary-launch signal.

- [ ] **Step 3: Remove runtime license monitoring and window signaling**

Delete the runtime monitor fields, setup call, stop call, reactivation handler, product-dialog branch, and `ShowFromExternalActivation` callback from `MainForm.cs`. Remove the now-unused foreground-window P/Invoke.

- [ ] **Step 4: Update the single-instance source test**

Replace assertions for secondary launch routing, listener registration, and exception handling with assertions that a secondary launch exits after the mutex check and that no window-activation channel is exposed. Keep auto-start ordering and handle creation checks.

- [ ] **Step 5: Run the focused startup tests**

Run: `dotnet test desktop.Tests/desktop.Tests.csproj -c Debug --filter "FullyQualifiedName~SingleInstanceCoordinatorTests|FullyQualifiedName~CommunityBuildTests" --logger "console;verbosity=minimal"`

Expected: PASS after the startup consumers are simplified, with no product or window-activation channel required.

### Task 3: Delete activation implementation, assets, tests, and packages

**Files:**
- Delete: `desktop/Activation/ActivationClient.cs`
- Delete: `desktop/Activation/ActivationTransportEnvelope.cs`
- Delete: `desktop/Activation/FingerprintHelper.cs`
- Delete: `desktop/Activation/LicenseGuard.cs`
- Delete: `desktop/Activation/LicenseStorage.cs`
- Delete: `desktop/Activation/LicenseToken.cs`
- Delete: `desktop/Activation/MachineLicenseStore.cs`
- Delete: `desktop/Activation/NtpClient.cs`
- Delete: `desktop/Activation/license_keyset_prod_smartprinter.json`
- Delete: `desktop/ActivationForm.cs`
- Delete: `desktop/ActivationForm.Designer.cs`
- Delete: `desktop/RuntimeLicenseMonitor.cs`
- Delete: `desktop.Tests/ActivationCompatibilityTests.cs`
- Delete: `desktop.Tests/MachineLicenseStoreTests.cs`
- Delete: `desktop.Tests/RuntimeLicenseMonitorTests.cs`
- Modify: `desktop/MyPrinter.Desktop.csproj`
- Modify: `desktop.Tests/desktop.Tests.csproj`

**Interfaces:**
- Consumes: no activation code; backend retains its own `System.Management` dependency for printer features.
- Produces: a smaller desktop dependency graph with no activation-only files or packages.

- [ ] **Step 1: Remove activation-only package references and content-copy rules**

Remove `BouncyCastle.Cryptography`, `NSec.Cryptography`, desktop-only `System.Management`, the keyset copy item, and the `smartprinter.appsettings.json` copy item from the desktop project. Remove BouncyCastle and NSec from the desktop test project. Do not remove the backend `System.Management` reference.

- [ ] **Step 2: Delete the activation implementation and focused tests**

Delete the files listed above. Keep `WindowsStartupService`; retain only the mutex portion of `SingleInstanceCoordinator` and remove its window-activation channel.

- [ ] **Step 3: Run the full .NET test suites**

Run: `dotnet test backend.Tests/backend.Tests.csproj -c Debug --logger "console;verbosity=minimal"` and `dotnet test desktop.Tests/desktop.Tests.csproj -c Debug --logger "console;verbosity=minimal"`.

Expected: PASS with no activation projects, types, or package restores required.

### Task 4: Make publishing and installer community-safe

**Files:**
- Modify: `build-installer.ps1`
- Modify: `installer/myPrinter.iss`
- Delete: `desktop/smartprinter.appsettings.json`
- Delete: `docs/superpowers/plans/2026-04-11-activation-system.md`
- Delete: `docs/superpowers/specs/2026-07-26-machine-license-and-nonblocking-print-recovery-design.md`
- Create: `docs/superpowers/specs/2026-07-26-nonblocking-print-recovery-design.md`
- Modify: `docs/superpowers/specs/2026-06-06-windows-auto-start-tray-design.md`

**Interfaces:**
- Consumes: desktop publish output and backend `appsettings.json`.
- Produces: installer scripts that have no activation server, product, keyset, or transport inputs.

- [ ] **Step 1: Remove activation parameters and keyset/config validation from the PowerShell build**

Keep version selection, safe repository path resolution, .NET SDK selection, optional backend/desktop tests, desktop publish, frontend artifact validation, Inno Setup discovery, and compiler invocation. Remove activation parameters, crypto/keyset helpers, runtime activation config generation/validation, activation required files, and the `KeysetFileName` compiler define.

- [ ] **Step 2: Remove activation assets from Inno Setup**

Delete the `KeysetFileName` macro and remove `smartprinter.appsettings.json` and `Activation\...` from `[Files]`. Keep the executable, backend `appsettings.json`, frontend, icons, shortcuts, and launch behavior.

- [ ] **Step 3: Remove stale private activation documentation and preserve recovery guidance**

Delete the activation implementation plan and the mixed machine-license spec because they document removed internals and server details. Preserve its independent manual-duplex recovery guidance in `docs/superpowers/specs/2026-07-26-nonblocking-print-recovery-design.md`. Remove the remaining sentence that says auto-start occurs after activation/config checks; describe it as occurring after normal startup checks.

- [ ] **Step 4: Validate packaging source statically**

Run: `rg -n -i "activation|license|fingerprint|heartbeat|keyset|ServerUrl|ProductId|TransportKey" build-installer.ps1 installer desktop/MyPrinter.Desktop.csproj desktop.Tests/desktop.Tests.csproj`

Expected: no matches in those packaging/project files; backend `System.Management` remains present elsewhere for printer functionality.

### Task 5: Rewrite public documentation and add MIT license

**Files:**
- Modify: `README.md`
- Create: `LICENSE`

**Interfaces:**
- Consumes: current frontend labels/help, backend endpoints, project target frameworks, and installer prerequisites.
- Produces: a UTF-8, accurate community onboarding document and standard MIT license.

- [ ] **Step 1: Write README sections**

Include: product summary, feature list, supported formats, prerequisites (Windows, .NET 10 SDK, WebView2, Microsoft Word for `.doc`/`.docx`), build/run/test commands, installer command, user workflow, manual duplex and booklet explanation, page/preview controls, recovery/history/tray/localization, architecture, API table, troubleshooting, contribution guidance, and explicit statement that product activation/server/key is not required.

- [ ] **Step 2: Add the standard MIT license**

Use the standard MIT text with `Copyright (c) 2026 Smart Printer contributors` and keep the README license section aligned with the file.

- [ ] **Step 3: Run documentation consistency checks**

Run: `rg -n -i "\.NET 8|Excel|PowerPoint|license_keyset|smartprinter\.appsettings|activation server|Activation\.ServerUrl" README.md build-installer.ps1 installer desktop backend frontend --glob '!**/bin/**' --glob '!**/obj/**'`

Expected: no stale .NET 8 or unsupported conversion claims; only the README's no-product-activation statement and MIT license wording may mention licensing.

### Task 6: Review, verify, commit, and push

**Files:**
- Review all staged files and only those files.

**Interfaces:**
- Consumes: completed implementation, tests, packaging scripts, README, and GitNexus graph.
- Produces: a verified commit pushed to `origin/manual-duplex`.

- [ ] **Step 1: Run the five-pass acceptance review**

Check the oracle (no product gate/assets/secrets; printing unchanged), portfolio (unit/source checks plus .NET suites), adversarial cases (fresh launch, publish without activation files, backend printer WMI dependency), falsification (the new boundary tests failed before removal and pass after), and runtime reality (build/publish output contains no activation asset).

- [ ] **Step 2: Review the diff across correctness, readability, architecture, security, and performance**

Run `git diff --check`, inspect all intended file diffs, run the repository-wide source scan excluding generated `bin/obj`, and resolve every Critical/High/Medium finding before proceeding.

- [ ] **Step 3: Run full verification**

Run: `dotnet build MyPrinter.slnx -c Release`, `dotnet test backend.Tests/backend.Tests.csproj -c Release --logger "console;verbosity=minimal"`, `dotnet test desktop.Tests/desktop.Tests.csproj -c Release --logger "console;verbosity=minimal"`, `dotnet publish desktop/MyPrinter.Desktop.csproj -c Release -r win-x64 /p:PublishSingleFile=true -o publish`, and PowerShell parse checks for `build-installer.ps1` and `installer/myPrinter.iss` where tooling is available.

- [ ] **Step 4: Run GitNexus change detection before commit**

Stage only the intended source, test, packaging, documentation, and license paths. Run `gitnexus detect-changes --scope staged -r myPrinter` and confirm the affected flows are desktop startup/packaging only; do not stage pre-existing build artefacts.

- [ ] **Step 5: Commit and push**

Use conventional message `feat: prepare community release without product activation`, verify `git status` shows only the intended commit plus preserved pre-existing worktree changes, then push with upstream `origin/manual-duplex`.
