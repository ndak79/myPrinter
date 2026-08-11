# Community Release Without Product Activation

## Goal

Prepare Smart Printer for public community sharing by removing the product activation system end to end, while preserving the existing printing workflow and keeping an open-source MIT license for contributors and users.

## Scope

The change removes only product activation and licensing enforcement:

- Delete the desktop activation client, token/crypto/storage helpers, fingerprint and NTP code, activation form, runtime license monitor, production keyset, and their focused tests.
- Remove the startup gate and runtime reactivation path from the desktop host.
- Preserve the unrelated single-instance signal that brings an already-running window to the foreground.
- Remove activation-only NuGet dependencies and desktop packaging/configuration assets. Keep `System.Management` in the backend because printer discovery and spooler inspection still use WMI.
- Simplify the installer build script and Inno Setup definition so publishing requires only the executable, backend `appsettings.json`, and frontend assets.
- Remove activation-specific internal design documents and stale activation wording from remaining public-facing documentation.
- Rewrite `README.md` from the current implementation rather than the old .NET 8/activation-era description.
- Add a standard MIT `LICENSE` file.

## Non-goals

- Do not change printer discovery, PDF/Word/image conversion, page ordering, booklet layout, manual duplex, recovery, preview, history, tray, or localization behavior.
- Do not remove `SingleInstanceCoordinator.StartActivationListener`; its name describes inter-process window activation, not product licensing.
- Do not clean, reset, or commit pre-existing build artefacts in the working tree.

## Design

### Desktop startup

`Program.Main` will continue to handle worker mode, single-instance coordination, Windows auto-start, backend startup, backend readiness, the main form, and graceful backend shutdown. The activation/configuration gate and its helper methods will be removed, so a fresh community build can reach the main application without a key, fingerprint, activation server, or keyset.

### Main window

`MainForm` will no longer create a periodic license monitor or show a reactivation dialog. `ShowFromExternalActivation` will keep its marshaling and foreground behavior, but will always show the main window because there is no product dialog to foreground.

### Packaging

The desktop project will retain only dependencies used by desktop/runtime code. The installer script will retain version selection, test execution, publish, frontend-artifact validation, and Inno Setup compilation, but will not accept or generate activation parameters or validate key material.

### Documentation

The README will document the real .NET 10/Windows architecture, supported file types, print modes, preview/page controls, manual duplex/recovery flow, API surface, setup, tests, installer prerequisites, and the no-product-activation community model. It will not claim unsupported Excel/PowerPoint conversion or .NET 8.

## Acceptance criteria

1. `manual-duplex` is based on the current `master` HEAD and contains only the requested implementation changes plus the design/plan/license documentation.
2. The desktop app has no startup or runtime product activation gate.
3. No activation server URL, product ID, transport key, keyset, fingerprint storage, license token, or activation dialog remains in source/config/installer files.
4. Activation-only packages are removed; backend WMI dependencies remain because printer features still require them.
5. Existing backend, desktop, and frontend tests pass, and the desktop project builds/publishes through the community installer path.
6. The README is accurate against the current endpoints and UI behavior, and the repository has a standard MIT license.
7. Review finds no unresolved Critical/High/Medium correctness, security, maintainability, packaging, or documentation gaps.
