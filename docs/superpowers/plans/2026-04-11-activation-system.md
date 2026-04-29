# Activation System Integration — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Integrate `D:\Pro\ACTIVATION_SYSTEM` into myPrinter so the app is blocked at startup unless the device is activated via a valid activation key (online) or a signed `.lic` file (offline).

**Architecture:** Re-implement the Python `license_guard.py` client logic in C# (no Python dependency). The activation server (`ACTIVATION_SYSTEM/activation_server/`) is deployed on a VPS and is maintained separately (see Known Server Bugs for remaining issues; the server has received 18 rounds of security hardening independent of this spec). The C# client calls the server's HTTP API (`/activate`, `/heartbeat`) and verifies the response's Ed25519 signature locally. A WinForms activation dialog is shown on startup if the device is not yet activated; the main window only opens on success. **Startup timing:** NTP + network verification happens inside `VerifyToken()` — only when a license token already exists. On **first (unactivated) launch**: only fingerprint collection blocks (up to ~45s if all 3 WMI calls time out; typically < 1s). On **subsequent activated launches**: fingerprint (up to ~45s WMI worst case, typically < 1s) + NTP (3s) + heartbeat (10s) + grace-retry heartbeat (10s) = up to ~68s worst case; typically ~3–5s. **Fingerprint cache is process-lifetime only** — each app launch recomputes WMI.

**Tech Stack:** C# 12 / **net10.0-windows**, WinForms, ASP.NET Core (backend already present), `NSec.Cryptography` (Ed25519 verification), `System.Security.Cryptography` (AES-GCM), NTP socket (raw UDP), JSON (System.Text.Json), `LOCALAPPDATA` for license.dat persistence.

---

## Context Summary

### ACTIVATION_SYSTEM

| Component | Location | Role |
|---|---|---|
| Activation server | `ACTIVATION_SYSTEM/activation_server/server.py` | Flask HTTP API + Admin dashboard, deployed on VPS |
| DB layer | `ACTIVATION_SYSTEM/activation_server/db.py` | SQLite, manages keys / devices / audit |
| Python client | `ACTIVATION_SYSTEM/clients/python/license_guard.py` | Reference implementation — **not reused, rewritten in C#** |
| Bootstrap tool | `ACTIVATION_SYSTEM/setup_licensing.py` | One-shot key generation — run once, produces `.env` + `.pem` files |

### Server HTTP API (language-agnostic)

> **Note:** Response columns below show only fields consumed by the C# client. Actual server responses may include additional fields (`success`, `message`, `expired`, `expiry`, `activated_at`, etc.).

| Endpoint | Method | Request body | Success response (client-relevant fields) |
|---|---|---|---|
| `/activate` | POST | `{ "activation_key": "ABCDE-FGHIJ-KLMNO-PQRST", "fingerprint": "<sha256>" }` | `{ "license": { "fingerprint", "expiry", "signature", "version": 2 } }` |
| `/heartbeat` | POST | `{ "fingerprint": "<sha256>" }` | `{ "valid": true/false, "revoked": true/false, "heartbeat_grace_days": N }` — ⚠️ `heartbeat_grace_days` is **always present** in every `/heartbeat` response. For unknown fingerprints, the server returns exactly `30` (hardcoded). For known devices, the server returns the stored `heartbeat_grace_days` from the `activated_devices` row (set during key generation or offline generation, default `30`, range `0–365`). |
| `/check` | POST | `{ "fingerprint": "<sha256>" }` | `{ "valid": true/false }` — ⚠️ `@csrf.exempt` (verified against server.py line 589). The C# client does not call `/check`. |

License token format (signed by Ed25519 private key on server):
```json
{ "fingerprint": "<sha256-hex>", "expiry": <unix-ts>, "signature": "<base64url>", "version": 2 }
```

Canonical payload for Ed25519 verification:
```json
{"expiry":<int>,"fingerprint":"<hex>","version":2}
```
(JSON with keys sorted alphabetically, no spaces, UTF-8 bytes — must match Python `_canonical_license_payload()` exactly)

### myPrinter integration points

| Layer | Touchpoint | What changes |
|---|---|---|
| `desktop/Program.cs` | App entry point — before backend thread starts | Add activation gate: show `ActivationForm` before `MainForm` |
| `desktop/MainForm.cs` | WebView2 host — unchanged | Only reached if activation passes |
| `backend/BackendStartup.cs` | ASP.NET Core startup — unchanged | Backend stays unmodified |
| NEW `desktop/Activation/` | New folder | All activation C# code lives here |

---

## Architecture Decision: Python → C# rewrite

`license_guard.py` is Python. myPrinter has zero Python runtime. Options:

| Option | Verdict |
|---|---|
| Bundle Python + .py as sidecar | ❌ ~50 MB overhead, fragile, subprocess activation dialog is ugly |
| Call server API from C# only (no local signature verify) | ❌ Requires internet at every startup; offline grace period impossible |
| **Rewrite client logic in C# (chosen)** | ✅ Native, no dependencies except one NuGet package, near-parity with Python (see documented intentional divergences in Task 3 parity notes and Task 8 design notes) |

Rewrite scope:
- Hardware fingerprint (4-layer WMI via PowerShell — same queries as Python)
- Ed25519 signature verification (`NSec.Cryptography` — proper SPKI PEM parse)
- AES-256-GCM license.dat encryption (`AesGcm` in `System.Security.Cryptography`)
- NTP time check (raw UDP socket)
- Heartbeat with offline grace period
- Online activation via `/activate` HTTP POST
- Offline activation via `.lic` file import
- WinForms activation dialog (mirrors Python `customtkinter` dialog)
- HTTPS enforcement for non-localhost server URLs
- Legacy `license.dat` path migration (best-effort)

---

## File Map

### New files (create)

| File | Responsibility |
|---|---|
| `desktop/Activation/LicenseGuard.cs` | Main public API: `IsActivated()`, `ActivateOnlineAsync()`, `ActivateOfflineAsync()`, `Configure()` |
| `desktop/Activation/LicenseToken.cs` | Data model for license JSON + Ed25519 verify logic |
| `desktop/Activation/FingerprintHelper.cs` | 4-layer hardware fingerprint (WMI via PowerShell + MAC) |
| `desktop/Activation/NtpClient.cs` | Raw UDP NTP query |
| `desktop/Activation/LicenseStorage.cs` | AES-256-GCM encrypt/decrypt for `license.dat` + legacy migration |
| `desktop/Activation/ActivationClient.cs` | HTTP calls to activation server (`/activate`, `/heartbeat`) |
| `desktop/ActivationForm.cs` | WinForms dialog: shows fingerprint, key entry, offline .lic import |
| `desktop/ActivationForm.Designer.cs` | WinForms designer file |
| `desktop/appsettings.json` | `Activation.ServerUrl` and `Activation.AllowInsecureHttp` config (no secrets — safe to commit) |

### Modified files

| File | Change |
|---|---|
| `desktop/Program.cs` | Add activation gate before backend thread + `Application.Run(new MainForm())` |
| `desktop/myPrinter.Desktop.csproj` | Add `NSec.Cryptography` NuGet reference + embedded resource |

> **Note:** Public key (`license_public.pem`) is embedded in the assembly as a resource — not in `appsettings.json`. The public key is not secret; it is safe to commit.

### NOT changed

- `backend/BackendStartup.cs` — backend API unchanged
- `frontend/app.js` — frontend unchanged
- `ACTIVATION_SYSTEM/` — server maintained separately; deploy as-is from its own repo

---

## Task 1: Bootstrap — run `setup_licensing.py` once

This task is manual (admin/dev, not in-app code). Document it here so implementers know the prerequisite.

**Files:** `D:\Pro\ACTIVATION_SYSTEM\setup_licensing.py`

- [ ] **Step 1: Install Python deps and run bootstrap**

```bash
cd D:\Pro\ACTIVATION_SYSTEM
pip install cryptography
python setup_licensing.py --server-url https://your-activation-server.com
```

This generates:
- `activation_server/.env` — server secrets (`FLASK_SECRET_KEY`, `ADMIN_PASSWORD_HASH`, `KEY_GENERATION_SECRET`, `TOTP_ENCRYPTION_KEY`, `LICENSE_PRIVATE_KEY_FILE=secrets/license_private.pem`)
  > `LICENSE_PRIVATE_KEY_FILE` is the bootstrap script's default output and the recommended production path. `server.py` accepts **either** `LICENSE_PRIVATE_KEY_FILE` **or** `LICENSE_PRIVATE_KEY_PEM` — startup fails only if **both** are absent. In development mode, if both are absent, the server emits a `RuntimeWarning` (not silent) and generates an in-memory ephemeral key that changes on every restart, invalidating all previously issued licenses.
- `activation_server/secrets/license_private.pem` — Ed25519 private key (NEVER commit)
- `backend/license_public.pem` — Ed25519 public key (embed in myPrinter app — safe to commit)
- `backend/license_runtime.env` — server URL + flags

- [ ] **Step 2: Copy `license_public.pem` into myPrinter**

Copy `ACTIVATION_SYSTEM/backend/license_public.pem` → `desktop/Activation/license_public.pem`

Set it as embedded resource in `myPrinter.Desktop.csproj`:
```xml
<ItemGroup>
  <EmbeddedResource Include="Activation\license_public.pem" />
</ItemGroup>
```

- [ ] **Step 3: Deploy activation server to VPS**

```powershell
# From Windows:
.\scripts\deploy_activation_vps.ps1 -Host user@your-vps-ip
```

Then verify server is running:
```bash
curl https://your-activation-server.com/
# Expected: { "service": "SmartOCR Activation Server", "status": "running" }
```

> **Production env requirements:** Add these to `activation_server/.env` before deploying:
> ```env
> FLASK_ENV=production
> CORS_ORIGINS=https://your-activation-server.com
> ```
> `server.py` requires `FLASK_ENV=production` for hardened mode. Without it, the server runs in development mode (weaker defaults). In production mode, `CORS_ORIGINS` is mandatory — the server raises `RuntimeError("CORS_ORIGINS is required when FLASK_ENV=production")` on startup if it is missing or empty.

---

## Task 2: Add NuGet dependency and config

**Files:**
- Modify: `desktop/myPrinter.Desktop.csproj`

- [ ] **Step 1: Verify target framework**

Open `desktop/myPrinter.Desktop.csproj` and confirm:
```xml
<TargetFramework>net10.0-windows</TargetFramework>
```

The plan targets `net10.0-windows` (not .NET 8). All package versions chosen below are compatible with net10.

- [ ] **Step 2: Add NSec.Cryptography for Ed25519**

```bash
cd D:\Pro\myPrinter\desktop
dotnet add package NSec.Cryptography --version 23.9.0
```

Verify `myPrinter.Desktop.csproj` now contains:
```xml
<PackageReference Include="NSec.Cryptography" Version="23.9.0" />
```

- [ ] **Step 3: Add appsettings.json for activation config**

Create `desktop/appsettings.json`:
```json
{
  "Activation": {
    "ServerUrl": "https://your-activation-server.com",
    "AllowInsecureHttp": false
  }
}
```

> `appsettings.json` contains only the server URL and an insecure-HTTP override — no secrets. It is safe to commit.

> ⚠️ Do NOT add a `<Content Include="appsettings.json">` csproj entry here. SDK-style projects auto-include `appsettings.json` as `None` by default; adding a `Content Include` creates a duplicate item (NETSDK1022 error in some configurations). The correct csproj registration using `<None Update="appsettings.json">` is done in **Task 10 Step 3.5** — proceed there for the csproj change.

---

## Task 3: Hardware Fingerprint

**Files:**
- Create: `desktop/Activation/FingerprintHelper.cs`

The fingerprint is `SHA256(uuid|cpuId|gpuId|mac)` — same algorithm as Python's `get_fingerprint()`.

**Parity notes vs Python:**
- WMI queries use identical class/field names via identical PowerShell command (primary path).
    - WMIC fallback is not implemented — `_wmi_query()` in Python falls back from PowerShell to the absolute WMIC path (`System32\wbem\wmic.exe`) whenever PowerShell fails (not only on older Windows), and `get_real_mac()` also falls back to WMIC when `psutil` is unavailable. C# intentionally drops both WMIC fallback paths; machines with broken/disabled PowerShell or without psutil-equivalent behavior may fail fingerprint collection.
- MAC: Python uses `psutil` and returns first non-virtual adapter regardless of `OperationalStatus`. C# matches this: filter by name keywords only, no `OperationalStatus.Up` check, return first valid MAC.
- Output format: both produce `AA:BB:CC:DD:EE:FF` uppercase colon-separated.
    - **⚠️ Intentional divergence:** C# `ComputeFingerprint()` throws `InvalidOperationException` if fewer than 2 components are readable (WMI all-fail produces `|||UNKNOWN` collision risk). Python `get_fingerprint()` has a different fallback: when all 4 hardware layers are empty/UNKNOWN, Python hashes a persistent random UUID stored in `device_uuid.dat` (located in `_RUNTIME_DIR`) — it never throws. This means C# fails harder on WMI-restricted machines, while Python falls back to a stable device identity. This divergence is intentional for security (C# refuses to produce a weak fingerprint). Parity verification scripts should be run on a normal machine where at least one hardware component resolves.
- **⚠️ MAC adapter ordering caveat:** C# iterates `NetworkInterface.GetAllNetworkInterfaces()` and Python iterates `psutil.net_if_addrs()`. These APIs do not guarantee the same ordering when multiple physical adapters are present (e.g. both Wi-Fi and Ethernet). On machines with a single physical adapter (typical for printers/kiosk deployments), parity is reliable. If multi-adapter machines are a concern, both implementations should be updated to sort eligible MACs before selecting, ensuring deterministic selection. The standalone `fingerprint_only.py` parity script in Task 11 Step 1 will reveal mismatches on a given machine.

- [ ] **Step 1: Create `FingerprintHelper.cs`**

```csharp
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
// Note: SHA256 and Encoding are NOT imported here — ComputeFingerprint() uses fully-qualified
// System.Security.Cryptography.SHA256 and System.Text.Encoding.UTF8 to avoid ambiguity.

namespace MyPrinter.Desktop.Activation;

/// <summary>
/// 4-layer hardware fingerprint: System UUID | CPU ID | GPU PNP ID | MAC address.
/// Matches Python license_guard.py get_fingerprint() on normal supported machines,
/// except for documented intentional divergences (see spec parity notes in Task 3).
/// </summary>
internal static class FingerprintHelper
{
    // Virtual adapter keywords to filter — matches Python _VIRTUAL_KEYWORDS exactly
    private static readonly string[] VirtualKeywords =
        ["virtual", "vmware", "hyper-v", "vpn", "loopback", "docker", "wsl",
         "bluetooth", "teredo", "isatap", "pseudo", "tunnel", "tap-windows"];

    private static string? _cachedFingerprint; // WMI can take up to 15s/call; cache for process lifetime

    public static string GetFingerprint()
    {
        return _cachedFingerprint ??= ComputeFingerprint();
    }

    private static string ComputeFingerprint()
    {
        var uuid = GetSystemUuid();
        var cpu  = GetCpuId();
        var gpu  = GetGpuId();
        var mac  = GetRealMac();

        // Reject activation if too many components are missing — prevents |||UNKNOWN collisions
        // that would produce identical fingerprints on any machine where WMI fails entirely.
        int nonEmpty = (string.IsNullOrEmpty(uuid) ? 0 : 1)
                     + (string.IsNullOrEmpty(cpu)  ? 0 : 1)
                     + (string.IsNullOrEmpty(gpu)  ? 0 : 1)
                     + (mac == "UNKNOWN"            ? 0 : 1);
        if (nonEmpty < 2)
            throw new InvalidOperationException(
                "Hardware fingerprint is too weak — fewer than 2 components could be read. " +
                "WMI may be unavailable or restricted on this system.");

        var raw  = $"{uuid}|{cpu}|{gpu}|{mac}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Run a PowerShell Get-CimInstance query. Mirrors the Python primary path only;
    /// unlike Python _wmi_query(), this C# version does not fall back to wmic.exe.
    /// Returns empty string on failure or placeholder values.
    /// </summary>
    private static string WmiQuery(string className, string field)
    {
        try
        {
            // Identical command to Python: Get-CimInstance -ClassName <class> | Select -First 1
            var ps = $"(Get-CimInstance -ClassName {className} | Select-Object -First 1).{field}";
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    // ⚠️ Use absolute System32 path — matches Python LG-04 hardening against
                    // search-path hijacking. Bare "powershell" is vulnerable if PATH is tampered.
                    FileName               = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.System),
                        @"WindowsPowerShell\v1.0\powershell.exe"),
                    Arguments              = $"-NoProfile -Command \"{ps}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                }
            };
            proc.Start();
            // ⚠️ WaitForExit BEFORE ReadToEnd can deadlock if stdout pipe buffer fills (typically
            // 4KB on Windows). Safe here because WMI single-field responses are always a few bytes.
            // If WMI output ever grows (e.g., verbose warnings), switch to async reads.
            if (!proc.WaitForExit(15_000))
            {
                try { proc.Kill(); } catch { }
                return string.Empty;
            }
            var value = proc.StandardOutput.ReadToEnd().Trim();

            // Reject placeholder / empty values — same list as Python
            var bad = new[] { "", "default string", "to be filled by o.e.m.", "none" };
            if (!string.IsNullOrEmpty(value) && !bad.Contains(value.ToLowerInvariant()))
                return value;
        }
        catch { }
        return string.Empty;
    }

    internal static string GetSystemUuid() => WmiQuery("Win32_ComputerSystemProduct", "UUID");
    internal static string GetCpuId()      => WmiQuery("Win32_Processor",             "ProcessorId");
    internal static string GetGpuId()      => WmiQuery("Win32_VideoController",        "PNPDeviceID");

    /// <summary>
    /// Layer 4: Real physical MAC address.
    /// Matches Python get_real_mac(): filter virtual adapters by name keyword, return first valid MAC.
    /// No OperationalStatus.Up check — Python psutil does not check this either.
    /// </summary>
    internal static string GetRealMac()
    {
        try
        {
            foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
            {
                var name = iface.Name.ToLowerInvariant();

                // Skip loopback
                if (iface.NetworkInterfaceType is NetworkInterfaceType.Loopback) continue;

                // Skip virtual adapters by name — same keyword list as Python
                if (VirtualKeywords.Any(k => name.Contains(k))) continue;

                var mac = iface.GetPhysicalAddress().ToString(); // "AABBCCDDEEFF"
                if (!string.IsNullOrEmpty(mac) && mac != "000000000000" && mac.Length == 12)
                {
                    // Format as AA:BB:CC:DD:EE:FF — matches Python output exactly
                    return string.Join(":", Enumerable.Range(0, 6)
                        .Select(i => mac.Substring(i * 2, 2)))
                        .ToUpperInvariant();
                }
            }
        }
        catch { }
        return "UNKNOWN";
    }
}
```

- [ ] **Step 2: Verify fingerprint is stable**

Add a temporary `MessageBox.Show(FingerprintHelper.GetFingerprint())` call early in `Program.Main()` and run twice. The 64-char hex must be identical across runs.

For cross-language parity verification, use the standalone script from **Task 11 Step 1** (`fingerprint_only.py`) — do NOT use `python -c "import license_guard; ..."` as that has import-time dependencies on activation server config and cryptography packages.

Remove the `MessageBox` after verifying.

---

## Task 4: NTP Client

**Files:**
- Create: `desktop/Activation/NtpClient.cs`

- [ ] **Step 1: Create `NtpClient.cs`**

```csharp
using System;
using System.Net;
using System.Net.Sockets;

namespace MyPrinter.Desktop.Activation;

/// <summary>
/// Raw UDP NTP query.
/// ⚠️ Intentional divergence from Python: Python's _get_ntp_time() cross-checks UDP NTP
/// against HTTPS time (worldtimeapi.org) and only trusts HTTPS-confirmed time. This C#
/// implementation uses raw UDP NTP only, which is weaker against time spoofing via
/// network interception. A network attacker who can block HTTPS and forge UDP NTP can
/// control effective_time. This is a known, accepted security tradeoff — document it here
/// so future maintainers understand the gap.
/// Returns Unix epoch as double, or 0 on failure (caller treats 0 as "NTP unavailable").
/// </summary>
internal static class NtpClient
{
    private const string NtpServer      = "pool.ntp.org";
    private const int    NtpPort        = 123;
    private const long   NtpEpochOffset = 2208988800L; // seconds between 1900 and 1970

    public static double GetNtpTime()
    {
        try
        {
            var packet = new byte[48];
            packet[0] = 0x1b; // LI=0, VN=3, Mode=3 (client) — matches Python exactly

            using var udp = new UdpClient();
            udp.Client.ReceiveTimeout = 3_000; // 3s — matches Python timeout=3
            udp.Connect(NtpServer, NtpPort);
            udp.Send(packet, packet.Length);

            var remote   = new IPEndPoint(IPAddress.Any, 0);
            var response = udp.Receive(ref remote);

            if (response.Length >= 48)
            {
                // Transmit timestamp at bytes 40-43 (seconds since 1900)
                uint seconds = (uint)response[40] << 24
                             | (uint)response[41] << 16
                             | (uint)response[42] << 8
                             | response[43];
                return (double)(seconds - NtpEpochOffset);
            }
        }
        catch { }
        return 0;
    }
}
```

---

## Task 5: License Token Model + Ed25519 Verification

**Files:**
- Create: `desktop/Activation/LicenseToken.cs`

**PEM parsing:** Ed25519 SubjectPublicKeyInfo (SPKI) DER has a fixed 12-byte header followed by 32 bytes of raw key. Rather than blindly slicing `derBytes[^32..]`, we validate the SPKI OID prefix so a non-Ed25519 key is rejected.

The Ed25519 SPKI prefix is `30 2A 30 05 06 03 2B 65 70 03 21 00` (12 bytes). We check for this before extracting the key.

- [ ] **Step 1: Create `LicenseToken.cs`**

```csharp
using System;
using System.Text;
using System.Text.Json.Serialization;
using NSec.Cryptography;

namespace MyPrinter.Desktop.Activation;

/// <summary>
/// Matches Python license_guard token format (version 2).
/// Canonical payload for Ed25519 verify: sorted-key JSON, no whitespace.
/// </summary>
internal sealed class LicenseToken
{
    [JsonPropertyName("fingerprint")] public string Fingerprint { get; set; } = "";
    [JsonPropertyName("expiry")]      public long   Expiry      { get; set; }
    [JsonPropertyName("signature")]   public string Signature   { get; set; } = "";
    [JsonPropertyName("version")]     public int    Version     { get; set; }

    // Runtime-only metadata (stored in license.dat — not part of signature)
    [JsonPropertyName("_last_seen")]              public double LastSeen           { get; set; }
    [JsonPropertyName("_last_online_check")]      public double LastOnlineCheck    { get; set; }
    [JsonPropertyName("_heartbeat_grace_days")]   public int    HeartbeatGraceDays { get; set; } = 30;
    /// <summary>
    /// Last time we obtained a trusted time value (NTP or heartbeat).
    /// ⚠️ Python persists _last_trusted_time and enforces _MAX_OFFLINE_WINDOW_DAYS = 7:
    /// if no trusted time in 7 days, fail closed regardless of local clock.
    /// C# mirrors this with LastTrustedTime + the 7-day check in VerifyToken.
    /// </summary>
    [JsonPropertyName("_last_trusted_time")]      public double LastTrustedTime    { get; set; }

    /// <summary>
    /// Verify Ed25519 signature against the embedded public key PEM.
    /// Canonical payload MUST match Python _canonical_license_payload() exactly:
    ///   {"expiry":<int>,"fingerprint":"<hex>","version":2}  (keys sorted, no spaces)
    /// </summary>
    public bool VerifySignature(string publicKeyPem)
    {
        try
        {
            if (Version != 2) return false;

            // Build canonical payload — MUST match Python json.dumps(sort_keys=True, separators=(',',':'))
            var canonical = $"{{\"expiry\":{Expiry},\"fingerprint\":\"{Fingerprint}\",\"version\":2}}";
            var message   = Encoding.UTF8.GetBytes(canonical);

            // Base64url → base64 → bytes (pad to multiple of 4)
            var padded = Signature.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            var sigBytes = Convert.FromBase64String(padded);

            // Parse PEM: strip headers, base64-decode to DER (SubjectPublicKeyInfo)
            var pem = publicKeyPem.Trim();
            var b64 = pem
                .Replace("-----BEGIN PUBLIC KEY-----", "")
                .Replace("-----END PUBLIC KEY-----",   "")
                .Replace("\r", "").Replace("\n", "").Trim();
            var der = Convert.FromBase64String(b64);

            // Validate Ed25519 SPKI OID prefix: 30 2A 30 05 06 03 2B 65 70 03 21 00 (12 bytes)
            // This is the fixed DER encoding for "SubjectPublicKeyInfo { Ed25519 }".
            ReadOnlySpan<byte> ed25519SpkiPrefix = [
                0x30, 0x2A, 0x30, 0x05, 0x06, 0x03, 0x2B, 0x65, 0x70, 0x03, 0x21, 0x00
            ];
            if (der.Length != 44) return false; // Ed25519 SPKI is always exactly 44 bytes
            if (!((ReadOnlySpan<byte>)der[..12]).SequenceEqual(ed25519SpkiPrefix)) return false;

            // Extract raw 32-byte public key (bytes 12..43)
            var rawKey = der[12..];

            var algo   = SignatureAlgorithm.Ed25519;
            var pubKey = PublicKey.Import(algo, rawKey, KeyBlobFormat.RawPublicKey);
            return algo.Verify(pubKey, message, sigBytes);
        }
        catch
        {
            return false;
        }
    }

    public bool IsExpired(double effectiveTime) => effectiveTime > Expiry;
    public bool FingerprintMatches(string fp)   => Fingerprint == fp;
}
```

---

## Task 6: License Storage (AES-256-GCM)

**Files:**
- Create: `desktop/Activation/LicenseStorage.cs`

Storage format matches Python encrypted format exactly:
- File: `%LOCALAPPDATA%\myPrinter\license.dat` (path adapted for myPrinter; Python stores at `_RUNTIME_DIR\license.dat` — for frozen builds this is `LOCALAPPDATA\SmartOCR\license.dat`; for source runs it is the module directory)
- Format: `[0x02][12-byte nonce][AES-256-GCM ciphertext][16-byte GCM tag]`
- Key derivation: `SHA256("smartocr-license-v2:" + fingerprint)` — identical to Python

Legacy migration: Python migrates from an EXE-adjacent `license.dat` to `_RUNTIME_DIR` (LOCALAPPDATA for frozen builds, module dir for source runs). The C# implementation does the same best-effort **move** on load (LG-02: move + delete source, not copy-only — see `MigrateLegacyFile()`).

- [ ] **Step 1: Create `LicenseStorage.cs`**

```csharp
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MyPrinter.Desktop.Activation;

internal static class LicenseStorage
{
    private static string PersistentPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        // ⚠️ Intentional divergence from Python: C# fails hard if LOCALAPPDATA is empty or
        // directory creation fails (throws). Python _persistent_data_dir() falls back to
        // _exe_dir() when LOCALAPPDATA is unavailable or Directory.CreateDirectory fails.
        // If this matters for your deployment (locked-down profiles, containers, SYSTEM service),
        // add a fallback: if (string.IsNullOrEmpty(appData)) appData = AppContext.BaseDirectory;
        var dir     = Path.Combine(appData, "myPrinter");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "license.dat");
    }

    /// <summary>
    /// Best-effort migration of legacy EXE-adjacent license.dat to persistent LOCALAPPDATA dir.
    /// Matches Python _migrate_legacy_license_file() — LG-02 hardening: MOVE not copy.
    /// Copy-without-delete allowed revoked-license rollback: revocation deletes persistent file,
    /// but surviving legacy copy gets re-migrated on next launch, restoring the revoked license.
    /// </summary>
    private static void MigrateLegacyFile()
    {
        try
        {
            var persistent = PersistentPath();
            var legacyPath = Path.Combine(AppContext.BaseDirectory, "license.dat");

            if (File.Exists(persistent))
            {
                // Active file exists — delete stale legacy copy so it can never roll back.
                // Matches Python: os.remove(_LEGACY_LICENSE_FILE) when _LICENSE_FILE exists.
                try { File.Delete(legacyPath); } catch { }
                return;
            }

            if (File.Exists(legacyPath))
            {
                // Move (not copy) — atomic on same drive, deletes source. Matches Python shutil.move().
                File.Move(legacyPath, persistent, overwrite: false);
            }
        }
        catch { } // best-effort only
    }

    private static byte[] DeriveKey(string fingerprint)
    {
        // MUST match Python: hashlib.sha256(("smartocr-license-v2:" + fingerprint).encode()).digest()
        return SHA256.HashData(Encoding.UTF8.GetBytes("smartocr-license-v2:" + fingerprint));
    }

    public static bool TrySave(LicenseToken token)
    {
        try
        {
            var key   = DeriveKey(token.Fingerprint);
            var nonce = RandomNumberGenerator.GetBytes(12);
            var json  = JsonSerializer.Serialize(token);
            var plain = Encoding.UTF8.GetBytes(json);

            // AES-256-GCM with 16-byte tag (AesGcm.TagByteSizes.MaxSize = 16)
            using var aes    = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);
            var cipher       = new byte[plain.Length];
            var tag          = new byte[AesGcm.TagByteSizes.MaxSize];
            aes.Encrypt(nonce, plain, cipher, tag);

            // Format: [0x02][nonce 12][cipher][tag 16]
            var blob = new byte[1 + 12 + cipher.Length + tag.Length];
            blob[0] = 0x02;
            nonce.CopyTo(blob, 1);
            cipher.CopyTo(blob, 13);
            tag.CopyTo(blob, 13 + cipher.Length);

            var path = PersistentPath();
            // Use randomized same-dir temp file — matches Python tempfile.mkstemp() hardening.
            // Predictable ".tmp" suffix is vulnerable to local temp-path races/TOCTOU.
            var tmp  = path + "." + Path.GetRandomFileName() + ".tmp";
            File.WriteAllBytes(tmp, blob);
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Convenience wrapper — callers that don't care about persistence failure.</summary>
    public static void Save(LicenseToken token) => TrySave(token);

    public static LicenseToken? Load(string fingerprint)
    {
        MigrateLegacyFile();
        try
        {
            var path = PersistentPath();
            if (!File.Exists(path)) return null;

            var blob = File.ReadAllBytes(path);
            if (blob.Length == 0) return null;

            if (blob[0] == 0x02)
            {
                // Encrypted format (current) — minimum: 1 + 12 (nonce) + 1 (cipher) + 16 (tag) = 30
                if (blob.Length < 30) return null;
                var key    = DeriveKey(fingerprint);
                var nonce  = blob[1..13];
                var tagLen = AesGcm.TagByteSizes.MaxSize; // 16
                var cipher = blob[13..(blob.Length - tagLen)];
                var tag    = blob[(blob.Length - tagLen)..];

                using var aes = new AesGcm(key, tagLen);
                var plain = new byte[cipher.Length];
                aes.Decrypt(nonce, cipher, tag, plain);

                return JsonSerializer.Deserialize<LicenseToken>(plain);
            }
            else
            {
                // Legacy: plaintext JSON (Python < v2 format) — try to parse and re-encrypt
                var token = JsonSerializer.Deserialize<LicenseToken>(blob);
                if (token != null)
                    Save(token); // upgrade to encrypted format using token.Fingerprint as key
                    // ⚠️ Save() derives encryption key from token.Fingerprint, not from the
                    // fingerprint parameter passed to Load(). If they differ (fingerprint mismatch),
                    // the re-encrypted file uses the wrong key and will fail on next load.
                    // Benign: VerifyToken() will reject it on fingerprint mismatch regardless.
                return token;
            }
        }
        catch { return null; }
    }

    public static void Delete()
    {
        // Delete both persistent and legacy paths — matches Python which deletes both
        // _LICENSE_FILE and _LEGACY_LICENSE_FILE on revoke to prevent rollback resurrection.
        try { File.Delete(PersistentPath()); } catch { }
        try { File.Delete(Path.Combine(AppContext.BaseDirectory, "license.dat")); } catch { }
    }
}
```

---

## Task 7: Activation HTTP Client

**Files:**
- Create: `desktop/Activation/ActivationClient.cs`

> **Critical:** `/activate` request body uses `activation_key` (not `key`). Verified against `server.py` route handler: `data.get('activation_key', '')`.

- [ ] **Step 1: Create `ActivationClient.cs`**

```csharp
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MyPrinter.Desktop.Activation;

internal sealed record ActivateResponse(LicenseToken? License, string? Error);
internal sealed record HeartbeatResponse(bool Valid, bool Revoked, int? HeartbeatGraceDays);

internal static class ActivationClient
{
    // Separate timeouts to match Python: 30s for activate, 10s for heartbeat
    // ⚠️ AllowAutoRedirect = false — matches Python follow_redirects=False.
    // Default HttpClient follows redirects, which can leak activation traffic to unintended hosts.
    // ⚠️ UseProxy = false — matches Python trust_env=False (R9-M1).
    // Without this, HttpClient inherits ambient proxy/CA environment variables (HTTPS_PROXY, etc.).
    // Activation keys and fingerprints MUST NOT be routed through unintended proxy infrastructure.
    // If a proxy is required, configure it explicitly; do NOT rely on machine/environment defaults.
    private static readonly HttpClient _httpActivate  = new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
        { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly HttpClient _httpHeartbeat = new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
        { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// POST /activate — request body: { "activation_key": "...", "fingerprint": "..." }
    /// Server reads data.get('activation_key', '') — field name is NOT "key".
    /// </summary>
    public static async Task<ActivateResponse> ActivateAsync(
        string serverUrl, string activationKey, string fingerprint)
    {
        try
        {
            // Field name MUST be activation_key — verified against server.py
            var body = JsonSerializer.Serialize(new { activation_key = activationKey, fingerprint });
            var res  = await _httpActivate.PostAsync(
                $"{serverUrl.TrimEnd('/')}/activate",
                new StringContent(body, Encoding.UTF8, "application/json"));

            var json = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
            {
                var err = TryGetString(json, "error") ?? $"HTTP {(int)res.StatusCode}";
                return new ActivateResponse(null, err);
            }

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("license", out var lic))
                return new ActivateResponse(null, "Server response missing 'license' field");

            var token = JsonSerializer.Deserialize<LicenseToken>(lic.GetRawText());
            return new ActivateResponse(token, null);
        }
        catch (Exception ex)
        {
            return new ActivateResponse(null, ex.Message);
        }
    }

    /// <summary>
    /// POST /heartbeat — request body: { "fingerprint": "..." }
    /// Response: { "valid": bool, "revoked": bool, "heartbeat_grace_days": int }
    /// ⚠️ "heartbeat_grace_days" is ALWAYS present in every response.
    ///    - Unknown fingerprint: server returns exactly 30 (hardcoded).
    ///    - Known device: server returns stored heartbeat_grace_days from DB (0–365, default 30).
    /// Returns null if server unreachable (caller treats null as offline).
    /// </summary>
    public static async Task<HeartbeatResponse?> HeartbeatAsync(string serverUrl, string fingerprint)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { fingerprint });
            var res  = await _httpHeartbeat.PostAsync(
                $"{serverUrl.TrimEnd('/')}/heartbeat",
                new StringContent(body, Encoding.UTF8, "application/json"));

            if (!res.IsSuccessStatusCode) return null;
            // ⚠️ Intentional divergence from Python: Python always tries to parse the JSON body
            // regardless of HTTP status. C# returns null (treats as unreachable/offline) on any
            // non-2xx status. In practice /heartbeat always returns 200 for valid/revoked/unknown
            // fingerprints, so non-2xx only occurs on server errors (5xx) or rate limiting (429)
            // — both of which falling through to the offline grace period is the correct behavior.
            var json = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new HeartbeatResponse(
                Valid:              root.TryGetProperty("valid",               out var v) && v.GetBoolean(),
                Revoked:            root.TryGetProperty("revoked",             out var r) && r.GetBoolean(),
                HeartbeatGraceDays: root.TryGetProperty("heartbeat_grace_days", out var g) ? g.GetInt32() : null);
        }
        catch { return null; }
    }

    private static string? TryGetString(string json, string field)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(field, out var val) ? val.GetString() : null;
        }
        catch { return null; }
    }
}
```

---

## Task 8: License Guard (Main Public API)

**Files:**
- Create: `desktop/Activation/LicenseGuard.cs`

This is the orchestrator — mirrors Python `check_activation()` and `_verify_license_data()`.

**Key design decisions:**
- `Configure()` validates server URL (absolute, http/https scheme required, HTTPS for non-localhost unless `AllowInsecureHttp=true`), validates PEM format, and throws on invalid config — fails fast.
- `IsActivated()` runs full verification including NTP + heartbeat — only when a license token exists. No-token path returns false immediately after fingerprint collection. Worst-case with existing license: ~45s (WMI) + 3s (NTP) + 10s (heartbeat) + 10s (grace-retry) = ~68s; typically ~3–5s. No `Application.Run` sync-context exists yet, so `Task.Run().GetAwaiter().GetResult()` is safe.
- Online activation (`ActivateOnlineAsync`) runs a post-activation heartbeat to sync `HeartbeatGraceDays` from server.
- Offline activation heartbeats the server when reachable, rejecting revoked devices.
- `graceDays == 0` intentional behavior: **matches Python** — Python does NOT skip enforcement for 0; `grace_days = 0` means the device must reconnect immediately after any positive elapsed time since `last_online_check`. There is no "no heartbeat required" mode in the Python client. Server `heartbeat_days = 0` is stored as-is; the client enforces it as "heartbeat required at every launch once time has elapsed".
- `TrySave` failure in `VerifyToken` blocks user (returns false) — **intentional divergence from Python**: Python's `_save_license()` silently swallows all exceptions and `_verify_license_data()` returns `True` regardless. C# fails closed to prevent silent loss of runtime metadata (`_last_seen`, `_last_online_check`, `_last_trusted_time`). Consequence: machines with a read-only `%LOCALAPPDATA%\myPrinter\` directory will be blocked on every launch. If this is unacceptable for deployment, switch to Python-style lenient save (log + continue).

```csharp
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MyPrinter.Desktop.Activation;

/// <summary>
/// Main activation gate. Call Configure() then IsActivated() at startup.
/// Mirrors Python license_guard.check_activation() and _verify_license_data() logic.
/// </summary>
public static class LicenseGuard
{
    private static string? _serverUrl;
    private static string? _publicKeyPem;
    private static bool    _allowInsecureHttp;

    /// <summary>
    /// Must be called before IsActivated() or ActivateOnlineAsync().
    /// Validates serverUrl: must be absolute URL; must use HTTPS for non-localhost
    /// unless allowInsecureHttp is true. Throws ArgumentException on invalid config.
    /// Slightly stricter than Python _get_activation_server(), which does not separately
    /// restrict localhost URLs to http/https schemes.
    /// </summary>
    public static void Configure(string serverUrl, string publicKeyPem, bool allowInsecureHttp = false)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
            throw new ArgumentException("ServerUrl must not be empty.", nameof(serverUrl));
        if (string.IsNullOrWhiteSpace(publicKeyPem))
            throw new ArgumentException("PublicKeyPem must not be empty.", nameof(publicKeyPem));

        // Validate absolute URL with http or https scheme only
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri))
            throw new ArgumentException("ServerUrl must be an absolute URL.", nameof(serverUrl));
        if (uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("ServerUrl must use http or https scheme.", nameof(serverUrl));

        // Enforce HTTPS for non-localhost (matches Python _get_activation_server)
        var host    = uri.Host.ToLowerInvariant();
        var isLocal = host is "localhost" or "127.0.0.1" or "::1";
        var isHttps = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        if (!isLocal && !isHttps && !allowInsecureHttp)
            throw new ArgumentException(
                "ServerUrl must use HTTPS for non-local endpoints. " +
                "Set AllowInsecureHttp=true in appsettings.json to override (not recommended).",
                nameof(serverUrl));

        // Validate PEM at startup — dedicated parser that throws on invalid PEM/wrong key type.
        // Cannot use VerifySignature() here — it swallows all exceptions and returns false.
        // Mirrors Python _load_public_key() which validates type and raises RuntimeError.
        ValidatePublicKeyPemOrThrow(publicKeyPem);

        _serverUrl         = serverUrl.TrimEnd('/');
        _publicKeyPem      = publicKeyPem;
        _allowInsecureHttp = allowInsecureHttp;
    }

    /// <summary>
    /// Returns the 64-char hex hardware fingerprint for this device.
    /// Delegates to FingerprintHelper which caches after first call.
    /// Throws InvalidOperationException if too few hardware components can be read.
    /// </summary>
    public static string GetFingerprint()
        => FingerprintHelper.GetFingerprint();

    /// <summary>
    /// Main gate: verify existing license or return false (caller shows activation dialog).
    /// Throws InvalidOperationException if Configure() has not been called.
    /// ⚠️ MUST only be called before Application.Run() — this method blocks for up to ~68s
    /// (WMI + NTP + heartbeat + grace-retry). Safe at startup because no SynchronizationContext
    /// exists yet. Calling from UI thread after Application.Run() will freeze the UI.
    /// </summary>
    public static bool IsActivated()
    {
        EnsureConfigured();
        var fp    = GetFingerprint();
        var token = LicenseStorage.Load(fp);
        if (token == null) return false;
        return VerifyToken(token, fp);
    }

    /// <summary>
    /// Activate online: POST /activate, verify signature, sync heartbeat metadata, save license.dat.
    /// Returns (success, errorMessage).
    /// Throws InvalidOperationException if Configure() has not been called.
    /// </summary>
    public static async Task<(bool Ok, string? Error)> ActivateOnlineAsync(string activationKey)
    {
        EnsureConfigured();
        var fp = GetFingerprint();

        var result = await ActivationClient.ActivateAsync(_serverUrl!, activationKey.Trim(), fp);
        if (result.Error != null)
            return (false, result.Error);
        if (result.License == null)
            return (false, "Server returned no license token.");

        var token = result.License;
        if (token.Version != 2)
            return (false, $"Unsupported license version {token.Version} — expected 2.");
        if (!token.FingerprintMatches(fp))
            return (false, "License fingerprint mismatch — this key may be bound to another device.");
        if (!token.VerifySignature(_publicKeyPem!))
            return (false, "License signature invalid.");

        // Use NTP for expiry check (same as startup path)
        double ntpTime      = NtpClient.GetNtpTime();
        double now          = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        double effectiveTime = ntpTime > 0 ? ntpTime : now;
        if (token.IsExpired(effectiveTime))
            return (false, "License is already expired.");

        // Sync HeartbeatGraceDays from server — matches Python's post-activation heartbeat.
        // ⚠️ Grace policy drift risk: if this heartbeat fails (returns null), token.HeartbeatGraceDays
        // remains at its default (30). That default is FABRICATED — it does not represent the server's
        // configured value, and is wrong for any server-configured custom value, especially 0 ("no
        // heartbeat required"). Consequence: a custom heartbeat_days=0 server config will not be
        // applied until the NEXT successful heartbeat at startup. This is a known limitation —
        // the server does not embed heartbeat policy in the /activate response.
        var hb = await ActivationClient.HeartbeatAsync(_serverUrl!, fp);
        if (hb != null)
        {
            // Treat revoked=true and valid=false as separate states — do not collapse them.
            // revoked=true → key explicitly revoked on server.
            // valid=false && revoked=false → device unrecognized / generic rejection (not the same as revoked).
            if (hb.Revoked)
                return (false, "Key was revoked on the server.");
            if (!hb.Valid)
                return (false, "Activation rejected by server (device not recognized).");
            if (hb.HeartbeatGraceDays.HasValue)
                token.HeartbeatGraceDays = hb.HeartbeatGraceDays.Value;
        }
        // If hb == null (server unreachable): activation still succeeds but grace policy may be stale.
        // The next successful heartbeat at startup will correct token.HeartbeatGraceDays.

        token.LastSeen        = effectiveTime;
        token.LastOnlineCheck = effectiveTime;
        token.LastTrustedTime = effectiveTime; // Seed trusted-time anchor on activation — matches Python
        if (!LicenseStorage.TrySave(token))
            return (false, "Failed to save license file. Check disk permissions.");
        return (true, null);
    }

    /// <summary>
    /// Activate offline: import a .lic file generated by admin via dashboard.
    /// Uses NTP for expiry check. Heartbeats server when reachable to reject revoked devices.
    /// Async so callers can await without blocking WinForms; NTP and heartbeat are the long-running
    /// awaited calls. File I/O, JSON parse, fingerprinting, and signature verification run
    /// synchronously before the first await — callers should invoke on a background thread if
    /// these synchronous steps must not touch the UI thread (e.g. wrap in Task.Run at call site).
    /// Throws InvalidOperationException if Configure() not called.
    /// </summary>
    public static async Task<bool> ActivateOfflineAsync(string licFilePath)
    {
        EnsureConfigured();
        try
        {
            var json  = File.ReadAllText(licFilePath);
            var token = JsonSerializer.Deserialize<LicenseToken>(json);
            if (token == null) return false;
            if (token.Version != 2) return false; // Matches Python step 2: version == 2

            var fp = GetFingerprint();
            if (!token.FingerprintMatches(fp))         return false;
            if (!token.VerifySignature(_publicKeyPem!)) return false;

            // Use NTP for time — same as startup path
            double ntpTime       = NtpClient.GetNtpTime();
            double now           = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            double effectiveTime = ntpTime > 0 ? ntpTime : now;
            if (token.IsExpired(effectiveTime))         return false;

            token.LastSeen        = effectiveTime;
            // Set LastOnlineCheck at activation time so the grace period starts from activation.
            // Matches Python _verify_license_data(): sets _last_online_check on successful heartbeat
            // AND seeds it on first verification when missing (if last_online == 0). This is not
            // an intentional divergence — both implementations seed the anchor on first verification.
            token.LastOnlineCheck = effectiveTime;
            token.LastTrustedTime = effectiveTime; // Seed trusted-time anchor — matches Python

            // Heartbeat when server reachable — reject revoked/deleted devices even offline
            // Mirrors Python activate_offline → _verify_license_data → _heartbeat_check
            var hb = await ActivationClient.HeartbeatAsync(_serverUrl!, fp);
            if (hb != null && (hb.Revoked || !hb.Valid))
                return false;
            if (hb != null)
            {
                if (hb.HeartbeatGraceDays.HasValue)
                    token.HeartbeatGraceDays = hb.HeartbeatGraceDays.Value;
                token.LastOnlineCheck    = effectiveTime;
            }

            if (!LicenseStorage.TrySave(token))
                return false; // Fail if persistence fails
            return true;
        }
        catch { return false; }
    }

    // -----------------------------------------------------------------------
    // Internal: full verification — mirrors Python _verify_license_data()
    // -----------------------------------------------------------------------
    private static bool VerifyToken(LicenseToken token, string fp)
    {
        if (token.Version != 2)             return false; // Matches Python step 2: version == 2 check
        if (!token.FingerprintMatches(fp))  return false;
        if (!token.VerifySignature(_publicKeyPem!)) return false;

        // Time: NTP first, local fallback.
        // ⚠️ Intentional divergence: Python cross-checks UDP NTP against HTTPS (worldtimeapi.org)
        // and only trusts HTTPS-confirmed time. C# uses raw UDP NTP, which is weaker against
        // time-spoofing attacks. See NtpClient XML doc for the security tradeoff.
        double ntpTime       = NtpClient.GetNtpTime();
        double now           = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        double effectiveTime = ntpTime > 0 ? ntpTime : now;

        // Offline trusted-time window — matches Python _MAX_OFFLINE_WINDOW_DAYS = 7
        // If no trusted time (NTP or heartbeat) for > 7 days, fail closed to prevent
        // frozen-clock + blocked-network expiry bypass.
        const int MaxOfflineWindowDays = 7;
        double lastTrusted = token.LastTrustedTime;
        if (ntpTime > 0)
        {
            // Got NTP time — update trusted-time anchor
            token.LastTrustedTime = ntpTime;
        }
        else if (lastTrusted <= 0)
        {
            // First offline run — seed anchor from local clock (matches Python lines 473-475:
            // data["_last_trusted_time"] = now on first no-NTP verification).
            // Without this, LastTrustedTime stays 0 indefinitely and the 7-day check never fires.
            token.LastTrustedTime = now;
        }
        else if ((now - lastTrusted) > MaxOfflineWindowDays * 86400.0)
        {
            // No trusted time for > 7 days — fail closed (matches Python)
            return false;
        }

        // Clock rollback detection — matches Python logic
        double lastSeen       = token.LastSeen;
        bool   clockRolledBack = lastSeen > 0 && now < lastSeen - 86400;
        if (clockRolledBack && ntpTime <= 0)
        {
            ShowClockWarning();
            return false;
        }

        // Expiry check
        if (effectiveTime > token.Expiry) return false;

        // Update last_seen
        token.LastSeen = Math.Max(effectiveTime, lastSeen);

        // Heartbeat (startup check) — matches Python step 4
        var hb = HeartbeatSync(fp);
        if (hb != null)
        {
            if (hb.Revoked || !hb.Valid)
            {
                LicenseStorage.Delete();
                return false;
            }
            token.HeartbeatGraceDays  = hb.HeartbeatGraceDays ?? token.HeartbeatGraceDays;
            token.LastOnlineCheck     = effectiveTime;
            token.LastTrustedTime     = effectiveTime; // heartbeat success = trusted time anchor
        }

        // Grace period enforcement — matches Python (graceDays == 0 is NOT skipped;
        // 0 means "reconnect immediately once any positive time has elapsed")
        double lastOnline = token.LastOnlineCheck;
        int    graceDays  = token.HeartbeatGraceDays;
        if (hb == null && lastOnline > 0 && (effectiveTime - lastOnline) > graceDays * 86400.0)
        {
            // Grace expired and server unreachable — block
            var hb2 = HeartbeatSync(fp);
            if (hb2 == null) return false;
            if (hb2.Revoked || !hb2.Valid) { LicenseStorage.Delete(); return false; }
            if (hb2.HeartbeatGraceDays.HasValue)
                token.HeartbeatGraceDays = hb2.HeartbeatGraceDays.Value;
            token.LastOnlineCheck    = effectiveTime;
            token.LastTrustedTime    = effectiveTime; // grace-retry success = trusted time anchor
        }

        // Seed LastOnlineCheck on first verification even without a successful heartbeat —
        // matches Python _verify_license_data() which does: if last_online == 0: data["_last_online_check"] = effective_time.
        if (lastOnline <= 0) token.LastOnlineCheck = effectiveTime;

        // Save updated metadata — fail if persistence fails to prevent silent state loss
        if (!LicenseStorage.TrySave(token))
            return false;
        return true;
    }

    private static void EnsureConfigured()
    {
        if (_serverUrl == null || _publicKeyPem == null)
            throw new InvalidOperationException(
                "LicenseGuard.Configure() must be called before using LicenseGuard.");
    }

    /// <summary>
    /// Validates that publicKeyPem is a well-formed Ed25519 SubjectPublicKeyInfo PEM.
    /// Throws ArgumentException on invalid PEM, wrong encoding, or wrong key type.
    /// Mirrors Python _load_public_key() which raises RuntimeError on invalid/wrong type.
    /// </summary>
    private static void ValidatePublicKeyPemOrThrow(string publicKeyPem)
    {
        try
        {
            var pem = publicKeyPem.Trim();

            // Require PEM armor — matches Python load_pem_public_key() which rejects non-PEM input
            if (!pem.Contains("-----BEGIN PUBLIC KEY-----") || !pem.Contains("-----END PUBLIC KEY-----"))
                throw new ArgumentException("PublicKeyPem must be a PEM-encoded PUBLIC KEY (with -----BEGIN/END PUBLIC KEY----- markers).", nameof(publicKeyPem));
            var b64 = pem
                .Replace("-----BEGIN PUBLIC KEY-----", "")
                .Replace("-----END PUBLIC KEY-----",   "")
                .Replace("\r", "").Replace("\n", "").Trim();
            var der = Convert.FromBase64String(b64);  // throws FormatException on bad base64

            // Ed25519 SPKI DER must be exactly 44 bytes with the fixed 12-byte OID prefix
            ReadOnlySpan<byte> ed25519Prefix = [
                0x30, 0x2A, 0x30, 0x05, 0x06, 0x03, 0x2B, 0x65, 0x70, 0x03, 0x21, 0x00
            ];
            if (der.Length != 44)
                throw new ArgumentException($"Ed25519 public key SPKI DER must be 44 bytes; got {der.Length}.");
            if (!((ReadOnlySpan<byte>)der[..12]).SequenceEqual(ed25519Prefix))
                throw new ArgumentException("DER prefix does not match Ed25519 SubjectPublicKeyInfo OID.");

            // Import the raw key to confirm NSec accepts it
            var rawKey = der[12..];
            var algo   = NSec.Cryptography.SignatureAlgorithm.Ed25519;
            var _      = NSec.Cryptography.PublicKey.Import(algo, rawKey, NSec.Cryptography.KeyBlobFormat.RawPublicKey);
        }
        catch (ArgumentException) { throw; }
        catch (Exception ex)
        {
            throw new ArgumentException(
                $"PublicKeyPem is not a valid Ed25519 PEM: {ex.Message}",
                nameof(publicKeyPem),
                ex);
        }
    }

    /// <summary>
    /// Blocking heartbeat call — runs on thread-pool to avoid sync-context issues.
    /// Returns null if server unreachable.
    /// </summary>
    private static HeartbeatResponse? HeartbeatSync(string fp)
    {
        try
        {
            // Task.Run ensures no captured SynchronizationContext — safe in all callers
            return Task.Run(() => ActivationClient.HeartbeatAsync(_serverUrl!, fp))
                       .GetAwaiter().GetResult();
        }
        catch { return null; }
    }

    private static void ShowClockWarning()
    {
        MessageBox.Show(
            "Phát hiện đồng hồ hệ thống bị sai lệch nghiêm trọng.\n\n" +
            "Nguyên nhân có thể:\n" +
            "• Pin CMOS trên mainboard đã hết\n" +
            "• Đồng hồ hệ thống bị chỉnh sai\n\n" +
            "Cách khắc phục:\n" +
            "1. Kết nối Internet để đồng bộ thời gian tự động\n" +
            "2. Settings → Date & Time → Set time automatically: ON\n\n" +
            "Sau khi sửa, khởi động lại phần mềm.",
            "myPrinter — Lỗi thời gian hệ thống",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }
}
```

- [ ] **Step 1: Create `LicenseGuard.cs`** — write the file above to `desktop/Activation/LicenseGuard.cs`.

---

## Task 9: Activation Dialog (WinForms)

**Files:**
- Create: `desktop/ActivationForm.cs`
- Create: `desktop/ActivationForm.Designer.cs`

Mirror Python's `customtkinter` dialog in native WinForms: show fingerprint (click-to-copy), key entry, Activate button, Import .lic button.

**Async safety:** The click handler uses `async Task OnActivateClickedAsync()` with the activation/import call inside a try/catch block. The WinForms event wire-up (`async (_, _) => await handler()`) is an `async void` lambda — exceptions do NOT propagate out of WinForms events. The try/catch inside each handler catches exceptions from the awaited activation/import call and shows them as error messages. On success, buttons remain disabled and the form closes after 800ms — buttons are only re-enabled on failure so the user can retry. ⚠️ Code paths that execute BEFORE entering the try block (e.g. input validation) or in exception-path UI code must remain exception-safe on their own.

- [ ] **Step 1: Create `ActivationForm.Designer.cs`**

```csharp
namespace MyPrinter.Desktop;

partial class ActivationForm
{
    private System.ComponentModel.IContainer? components = null;
    private Label   lblTitle, lblFingerprint, lblKey, lblStatus;
    private Button  btnActivate, btnImportLic;
    private TextBox txtKey;
    private Panel   pnlFingerprint;
    private Label   lblFpValue;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        this.Text            = "myPrinter — Kích hoạt phần mềm";
        this.Size            = new Size(620, 440);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox     = false;
        this.MinimizeBox     = false;
        this.StartPosition   = FormStartPosition.CenterScreen;
        this.BackColor       = Color.White;
        this.Font            = new Font("Segoe UI", 10F);

        lblTitle = new Label
        {
            Text      = "🔐 Kích hoạt myPrinter",
            Font      = new Font("Segoe UI", 18F, FontStyle.Bold),
            ForeColor = Color.FromArgb(31, 83, 141),
            Location  = new Point(30, 25),
            Size      = new Size(560, 40),
            AutoSize  = false,
        };

        pnlFingerprint = new Panel
        {
            BackColor   = Color.FromArgb(240, 240, 240),
            Location    = new Point(30, 75),
            Size        = new Size(555, 85),
            BorderStyle = BorderStyle.None,
        };

        lblFingerprint = new Label
        {
            Text      = "Mã thiết bị / Device Fingerprint (Click để copy):",
            ForeColor = Color.FromArgb(85, 85, 85),
            Location  = new Point(10, 10),
            Size      = new Size(530, 20),
            AutoSize  = false,
        };

        lblFpValue = new Label
        {
            Text      = "",
            Font      = new Font("Consolas", 10F),
            ForeColor = Color.FromArgb(50, 50, 50),
            Location  = new Point(10, 35),
            Size      = new Size(535, 38),
            Cursor    = Cursors.Hand,
            AutoSize  = false,
        };
        lblFpValue.Click += (_, _) => CopyFingerprint();

        pnlFingerprint.Controls.AddRange([lblFingerprint, lblFpValue]);

        lblKey = new Label
        {
            Text     = "Nhập mã kích hoạt (Activation Key):",
            Location = new Point(30, 175),
            Size     = new Size(560, 22),
            AutoSize = false,
        };

        txtKey = new TextBox
        {
            Font            = new Font("Consolas", 11F),
            Location        = new Point(30, 200),
            Size            = new Size(555, 30),
            PlaceholderText = "Nhập mã kích hoạt tại đây...",
        };

        btnActivate = new Button
        {
            Text      = "Kích hoạt Online",
            Font      = new Font("Segoe UI", 11F, FontStyle.Bold),
            Location  = new Point(30, 255),
            Size      = new Size(200, 40),
            BackColor = Color.FromArgb(31, 83, 141),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor    = Cursors.Hand,
        };
        btnActivate.FlatAppearance.BorderSize = 0;
        btnActivate.Click += async (_, _) => await OnActivateClickedAsync();

        btnImportLic = new Button
        {
            Text      = "Import file .lic (Offline)",
            Font      = new Font("Segoe UI", 10F),
            Location  = new Point(245, 255),
            Size      = new Size(200, 40),
            BackColor = Color.FromArgb(108, 117, 125),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor    = Cursors.Hand,
        };
        btnImportLic.FlatAppearance.BorderSize = 0;
        btnImportLic.Click += async (_, _) => await OnImportLicClickedAsync();

        lblStatus = new Label
        {
            Text      = "",
            ForeColor = Color.FromArgb(220, 53, 69),
            Location  = new Point(30, 310),
            Size      = new Size(555, 80),
            AutoSize  = false,
        };

        this.Controls.AddRange([
            lblTitle, pnlFingerprint, lblKey, txtKey,
            btnActivate, btnImportLic, lblStatus
        ]);
    }
}
```

- [ ] **Step 2: Create `ActivationForm.cs`**

```csharp
using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyPrinter.Desktop.Activation;

namespace MyPrinter.Desktop;

public partial class ActivationForm : Form
{
    public bool Activated { get; private set; }
    private readonly string _fingerprint;

    public ActivationForm()
    {
        InitializeComponent();
        _fingerprint    = LicenseGuard.GetFingerprint();
        lblFpValue.Text = _fingerprint;
    }

    private const string FingerprintLabelDefault =
        "Mã thiết bị / Device Fingerprint (Click để copy):";

    private void CopyFingerprint()
    {
        // Guard Clipboard.SetText — clipboard contention (locked by another process) throws on Windows.
        // Mirrors Python copy_fp() which calls clipboard APIs unguarded; C# adds safety here
        // since an unhandled exception in a click handler can fault the entire activation dialog.
        try
        {
            Clipboard.SetText(_fingerprint);
        }
        catch (Exception)
        {
            SetStatus("❌ Không thể copy fingerprint vào Clipboard. Vui lòng thử lại.", error: true);
            return;
        }

        // Use constant instead of capturing current label text — fixes rapid-click race:
        // two fast clicks create two timers; if the second captures the already-mutated
        // success text, the last reset permanently leaves the label in "copied" state.
        // Mirrors Python copy_fp() which always resets to the fixed default string.
        lblFingerprint.Text      = "Mã thiết bị (✅ Đã copy vào Clipboard!):";
        lblFingerprint.ForeColor = Color.FromArgb(40, 167, 69);

        _ = Task.Delay(2000).ContinueWith(_ =>
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed) return;
                    lblFingerprint.Text      = FingerprintLabelDefault;
                    lblFingerprint.ForeColor = Color.FromArgb(85, 85, 85);
                }));
            }
            catch { }
        }, TaskScheduler.Default);
    }

    private async Task OnActivateClickedAsync()
    {
        var key = txtKey.Text.Trim();
        if (string.IsNullOrEmpty(key))
        {
            SetStatus("⚠️ Vui lòng nhập mã kích hoạt.", error: true);
            return;
        }

        btnActivate.Enabled  = false;
        btnImportLic.Enabled = false;
        btnActivate.Text     = "Đang kích hoạt...";
        SetStatus("Đang kết nối máy chủ...", error: false);

        bool ok;
        string? err;
        try
        {
            (ok, err) = await LicenseGuard.ActivateOnlineAsync(key);
        }
        catch (Exception ex)
        {
            ok  = false;
            err = ex.Message;
        }

        if (ok)
        {
            // Success: set Activated + DialogResult BEFORE delay so X-button close during
            // the 800ms cosmetic pause still returns DialogResult.OK to ShowDialog().
            Activated    = true;
            DialogResult = DialogResult.OK;
            SetStatus("✅ Kích hoạt thành công!", error: false);
            await Task.Delay(800);
            Close();
        }
        else
        {
            // Failure: re-enable so user can retry
            btnActivate.Enabled  = true;
            btnImportLic.Enabled = true;
            btnActivate.Text     = "Kích hoạt Online";
            SetStatus($"❌ Kích hoạt thất bại: {err}", error: true);
        }
    }

    // Async to match ActivateOfflineAsync (NTP + heartbeat should not block UI thread)
    private async Task OnImportLicClickedAsync()
    {
        using var ofd = new OpenFileDialog
        {
            Title  = "Chọn file .lic",
            Filter = "License files (*.lic)|*.lic|All files (*.*)|*.*",
        };
        if (ofd.ShowDialog() != DialogResult.OK) return;

        btnActivate.Enabled  = false;
        btnImportLic.Enabled = false;
        SetStatus("Đang kiểm tra file .lic...", error: false);

        bool ok;
        try   { ok = await LicenseGuard.ActivateOfflineAsync(ofd.FileName); }
        catch { ok = false; }

        if (ok)
        {
            // Success: set Activated + DialogResult BEFORE delay so X-button close during
            // the 800ms cosmetic pause still returns DialogResult.OK to ShowDialog().
            Activated    = true;
            DialogResult = DialogResult.OK;
            SetStatus("✅ Import file .lic thành công!", error: false);
            await Task.Delay(800);
            Close();
        }
        else
        {
            // Failure: re-enable so user can retry
            btnActivate.Enabled  = true;
            btnImportLic.Enabled = true;
            SetStatus("❌ File .lic không hợp lệ hoặc không khớp thiết bị này.", error: true);
        }
    }

    private void SetStatus(string msg, bool error)
    {
        lblStatus.Text      = msg;
        lblStatus.ForeColor = error
            ? Color.FromArgb(220, 53, 69)
            : Color.FromArgb(40, 167, 69);
    }
}
```

---

## Task 10: Wire into `Program.cs`

**Files:**
- Modify: `desktop/Program.cs`

> **Activation gate placement:** runs before the backend thread starts. If the user cancels activation, the app exits cleanly without ever starting the backend.

- [ ] **Step 1: Load public key from embedded resource**

Add this helper to `Program.cs`:

```csharp
private static string LoadPublicKeyPem()
{
    var asm  = System.Reflection.Assembly.GetExecutingAssembly();
    // Resource name = RootNamespace + "." + folder + "." + filename
    // RootNamespace = "MyPrinter.Desktop" (from myPrinter.Desktop.csproj)
    var name = "MyPrinter.Desktop.Activation.license_public.pem";
    using var stream = asm.GetManifestResourceStream(name)
        ?? throw new InvalidOperationException(
            $"Embedded resource '{name}' not found. " +
            "Ensure Activation\\license_public.pem is marked as EmbeddedResource in the csproj.");
    return new System.IO.StreamReader(stream).ReadToEnd();
}
```

- [ ] **Step 2: (See Step 3)** The full `Program.cs` in Step 3 below already includes `LoadPublicKeyPem()`, `LoadActivationConfig()`, and all helpers. No separate step needed — proceed directly to Step 3.

- [ ] **Step 3: Add activation gate in `Main()`**

Replace `Program.cs` with the following (full file — gate placed before backend thread):

```csharp
using Microsoft.AspNetCore.Builder;
using MyPrinter.Desktop.Activation;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;

namespace MyPrinter.Desktop;

static class Program
{
    public static int BackendPort { get; private set; } = 8787;
    public static WebApplication? BackendApp { get; private set; }

    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        // ── ACTIVATION GATE (before backend starts) ───────────────────────
        // Entire gate is wrapped in try/catch — catches config errors, bad PEM,
        // AND fingerprint/WMI failures (InvalidOperationException from ComputeFingerprint).
        try
        {
            var publicKeyPem = LoadPublicKeyPem();
            var (serverUrl, allowInsecure) = LoadActivationConfig();
            LicenseGuard.Configure(serverUrl, publicKeyPem, allowInsecure);

            if (!LicenseGuard.IsActivated())
            {
                using var activationForm = new ActivationForm();
                if (activationForm.ShowDialog() != DialogResult.OK || !activationForm.Activated)
                    return; // User cancelled — exit cleanly
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Lỗi khởi động:\n{ex.Message}\n\n" +
                "Nguyên nhân có thể:\n" +
                "• appsettings.json thiếu hoặc sai cấu hình\n" +
                "• Embedded license_public.pem không tìm thấy\n" +
                "• Phần cứng WMI không khả dụng (không đọc được fingerprint)\n\n" +
                "Liên hệ nhà cung cấp để được hỗ trợ.",
                "Lỗi khởi động",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }
        // ─────────────────────────────────────────────────────────────────

        // Find a free port (fallback if 8787 is taken)
        BackendPort = FindFreePort(8787);

        // Start ASP.NET Core backend in a background thread
        var backendThread = new Thread(() =>
        {
            try
            {
                BackendApp = PrinterApp.BackendStartup.Build(
                    [$"--urls=http://localhost:{BackendPort}"]);
                BackendApp.Run();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Không thể khởi động backend:\n{ex.Message}",
                    "Lỗi khởi động",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                Application.Exit();
            }
        })
        { IsBackground = true, Name = "BackendThread" };
        backendThread.Start();

        // Wait for Kestrel to be ready (max 8s); abort if it never starts
        if (!WaitForBackend(BackendPort, timeoutMs: 8000))
        {
            MessageBox.Show(
                "Backend không khởi động được trong 8 giây.\nKiểm tra logs và thử lại.",
                "Lỗi khởi động",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        // Launch WinForms UI
        Application.Run(new MainForm());

        // Graceful shutdown when window closes
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        BackendApp?.StopAsync(cts.Token).GetAwaiter().GetResult();
    }

    static int FindFreePort(int preferred)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, preferred);
            listener.Start();
            listener.Stop();
            return preferred;
        }
        catch
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }

    /// <summary>
    /// Returns true when Kestrel is listening (TCP port accepts connections); false if timed out.
    /// TCP probe avoids triggering expensive WMI printer discovery (/api/printers) before UI opens.
    /// Note: TCP success only proves the listener is up, not that routes/services are warm.
    /// In this app, routes are mapped in Build() before Run() starts Kestrel, so no race exists.
    /// </summary>
    static bool WaitForBackend(int port, int timeoutMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                using var tcp = new System.Net.Sockets.TcpClient();
                tcp.Connect("127.0.0.1", port);
                return true; // Kestrel is listening
            }
            catch { }
            Thread.Sleep(200);
        }
        return false;
    }

    private static string LoadPublicKeyPem()
    {
        var asm  = Assembly.GetExecutingAssembly();
        var name = "MyPrinter.Desktop.Activation.license_public.pem";
        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{name}' not found. " +
                "Ensure Activation\\license_public.pem is marked as EmbeddedResource in the csproj.");
        return new StreamReader(stream).ReadToEnd();
    }

    private static (string ServerUrl, bool AllowInsecureHttp) LoadActivationConfig()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                "appsettings.json not found. Create it with Activation.ServerUrl set to your activation server URL.",
                path);

        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("Activation", out var act))
            throw new InvalidOperationException("appsettings.json is missing the 'Activation' section.");

        var url = act.TryGetProperty("ServerUrl", out var u) ? u.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(url) || url.Contains("your-activation-server"))
            throw new InvalidOperationException(
                "Activation.ServerUrl in appsettings.json is not configured. " +
                "Replace 'https://your-activation-server.com' with the real server URL.");

        var allowInsecure = act.TryGetProperty("AllowInsecureHttp", out var a) && a.GetBoolean();
        return (url, allowInsecure);
    }
}
```

- [ ] **Step 3.5: Create and provision `appsettings.json`**

`LoadActivationConfig()` reads `Path.Combine(AppContext.BaseDirectory, "appsettings.json")` at startup — if the file is missing, the app throws and exits. It must be present in the output directory for both `dotnet run` and published builds.

Create `desktop/appsettings.json`:

```json
{
  "Activation": {
    "ServerUrl": "https://your-activation-server.com",
    "AllowInsecureHttp": false
  }
}
```

> ⚠️ Replace `https://your-activation-server.com` with the real deployed activation server URL before building. `LoadActivationConfig()` explicitly rejects this placeholder at runtime and will throw an `InvalidOperationException`.

Add to `desktop/MyPrinter.Desktop.csproj` so the file is copied to the output directory automatically:

```xml
<ItemGroup>
  <!-- Use Update (not Include) — SDK-style projects auto-include appsettings.json as None;
       Include would create a duplicate item and potentially cause NETSDK1022 errors. -->
  <None Update="appsettings.json">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
  </None>
</ItemGroup>
```

- [ ] **Step 4: Build and run**

```bash
cd D:\Pro\myPrinter\desktop
dotnet build
dotnet run
```

Expected: Activation dialog appears on first launch. After entering a valid key and clicking Activate, the app should start normally.

---

## Task 11: Verification

- [ ] **Step 1: Verify fingerprint parity with Python**

Before testing activation, confirm C# fingerprint matches Python reference on the same machine.
`import license_guard` has import-time deps (`cryptography`, `httpx`, env/public key config), so use a standalone script instead:

```python
# Save as D:\Pro\ACTIVATION_SYSTEM\clients\python\fingerprint_only.py
import hashlib, subprocess, platform, re, uuid, psutil

def wmi_query(cls, field):
    try:
        r = subprocess.run(
            ["powershell", "-NoProfile", "-Command",
             f"(Get-CimInstance -ClassName {cls} | Select-Object -First 1).{field}"],
            capture_output=True, text=True, timeout=15)
        v = r.stdout.strip()
        bad = {"", "default string", "to be filled by o.e.m.", "none"}
        return v if v.lower() not in bad else ""
    except: return ""

VIRTUAL = ["virtual","vmware","hyper-v","vpn","loopback","docker","wsl",
           "bluetooth","teredo","isatap","pseudo","tunnel","tap-windows"]

def get_mac():
    for iface, addrs in psutil.net_if_addrs().items():
        if any(k in iface.lower() for k in VIRTUAL): continue
        for a in addrs:
            if a.family == psutil.AF_LINK:
                mac = a.address.replace("-",":").upper()
                if mac and mac != "00:00:00:00:00:00": return mac
    return "UNKNOWN"

raw = "|".join([wmi_query("Win32_ComputerSystemProduct","UUID"),
                wmi_query("Win32_Processor","ProcessorId"),
                wmi_query("Win32_VideoController","PNPDeviceID"),
                get_mac()])
print(hashlib.sha256(raw.encode()).hexdigest())
```

```bash
cd D:\Pro\ACTIVATION_SYSTEM\clients\python
pip install psutil
python fingerprint_only.py
```

Run the C# app (with temporary MessageBox in Step 2 of Task 3) and compare. On single-adapter machines (typical kiosk/printer deployments), outputs must be identical **when at least one hardware component resolves** (UUID, CPU, GPU, or MAC). If all four layers return empty/UNKNOWN, Python falls back to hashing a persistent `device_uuid.dat` — the C# implementation throws instead (intentional divergence). On multi-adapter machines, see the MAC ordering caveat in Task 3 parity notes — a mismatch there is expected and requires making MAC selection deterministic before deployment.

⚠️ **Parity script limitations:** This script is a simplified reference — it omits fallback behavior that the real `license_guard.py` client uses: (1) `_wmi_query()` falls back from PowerShell to `System32\wbem\wmic.exe` on PowerShell failure; (2) `get_real_mac()` falls back from psutil to `wmic` if psutil is unavailable; (3) `get_fingerprint()` uses `device_uuid.dat` when all components are unknown. The script can therefore report a false mismatch even on a single-adapter machine if PowerShell fails or psutil is not installed correctly. If outputs differ despite psutil working and PowerShell running, check whether the real `license_guard.py` is taking a fallback path.

- [ ] **Step 2: Test first-launch flow**
  - Delete `%LOCALAPPDATA%\myPrinter\license.dat` if it exists
  - Also delete any EXE-adjacent `license.dat` in the app output directory (e.g. `desktop\bin\Debug\net10.0-windows\license.dat`) — `MigrateLegacyFile()` will re-migrate it on next launch otherwise, masking the "not activated" state
  - Launch app → activation dialog must appear
  - Enter an invalid key → must show error message
  - Enter valid key from admin dashboard → must succeed, app opens

- [ ] **Step 3: Test already-activated flow**
  - ⚠️ **Full process exit required** — closing the main window hides the app to the system tray; it does NOT terminate the process. Use tray menu **Thoát** or Task Manager to fully exit before relaunching.
  - Fully exit and relaunch app → must skip dialog entirely

- [ ] **Step 4: Test offline .lic import**
  - ⚠️ Server `/admin/generate-offline` blocks when ANY device row exists for this fingerprint
  - In admin dashboard, first **\"Nhả Key\"** (`/admin/reset-key`) for this device to clear the device row. If this device was online-activated, the original key is also freed for reuse. If it is an offline-activated device (`activation_key == "OFFLINE_LIC"`), only the device row is deleted — no key is freed.
  - Then generate offline `.lic` for the device fingerprint
  - Delete `%LOCALAPPDATA%\myPrinter\license.dat`
  - Also delete any EXE-adjacent `license.dat` in the app output directory (e.g. `desktop\bin\Debug\net10.0-windows\license.dat`) — `MigrateLegacyFile()` will re-migrate it on next launch otherwise, masking the "not activated" state
  - Disconnect from internet
  - Use Import .lic button → must succeed and app must open
  - To continue with Steps 5-6: delete `license.dat`, reconnect internet, and activate online with a **fresh/freed key** (offline license.dat must be removed first — app will skip dialog while it's valid)

- [ ] **Step 5: Test heartbeat/revoke**
  - ⚠️ **Machine must be online and activation server reachable** — revoke enforcement requires a successful heartbeat response from the server. If offline, `VerifyToken` falls back to grace-period logic (does NOT delete `license.dat`). This test is invalid if the machine or server is unreachable.
  - In admin dashboard, soft-revoke the activated device
  - ⚠️ **Full process exit required before relaunching** (see Step 3 note — window X hides to tray; use tray **Thoát** or Task Manager)
  - Fully exit and relaunch app → dialog must reappear
  - ⚠️ **Verify BOTH license files are gone** after revoke detection: `%LOCALAPPDATA%\myPrinter\license.dat` AND any legacy EXE-adjacent `license.dat` in the app output dir. `LicenseStorage.Delete()` deletes both. If a legacy copy survives, it will be re-migrated on next launch, defeating the revoke.

- [ ] **Step 6: Test grace period**
  - After soft-revoke (Step 5), per-row "Nhả Key" is hidden in UI but device can still be reset via **bulk action** or **direct POST `/admin/reset-key`**
  - ⚠️ **A fresh activation key alone is NOT enough to reactivate a soft-revoked device.** The server's `/activate` route rejects any fingerprint whose existing `activated_devices` row has `revoked=true` — regardless of which key is presented. You MUST clear the revoked device row first (bulk "Nhả Key" or direct `POST /admin/reset-key`), then activate with a fresh or freed key.
  - Reset/unbind the revoked device, then delete `%LOCALAPPDATA%\myPrinter\license.dat`
  - Also delete any EXE-adjacent `license.dat` in the app output directory — `MigrateLegacyFile()` will re-migrate it on next launch otherwise, defeating the grace-period test
  - activate online with a fresh/freed key
  - Set heartbeat grace to 1 day in admin dashboard
  - Add a temporary override in `LicenseStorage.Load()` that forces `token.LastOnlineCheck = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 2 * 86400` and `token.HeartbeatGraceDays = 1` after loading
  - Disconnect from internet
  - ⚠️ **Full process exit required before relaunching** (see Step 3 note)
  - Fully exit and relaunch app → must block (grace expired, offline)
  - Remove the temporary override after testing

- [ ] **Step 7: Test HTTPS enforcement**
  - Set `ServerUrl` to `http://your-vps-ip` (not localhost) with `AllowInsecureHttp: false`
  - Launch app → must show ArgumentException / startup error, NOT silently degrade

---

## Admin: Offline License Generation Contract

Used by Task 11 Step 4 and key-rotation reactivation. Reference: `server.py` `/admin/generate-offline` route.

> ⚠️ **`.lic` provenance — use ONLY the dashboard-generated file.** There are two incompatible ways to produce a `.lic` file: (1) the server dashboard / `/admin/generate-offline` flow — this creates both the signed token **and** the `activated_devices` row on the server; (2) the standalone Python CLI (`python license_guard.py generate <fingerprint>`) — this creates a cryptographically valid signed token but registers **no** device on the server. **Do NOT use `python license_guard.py generate ...` for myPrinter deployments.** A CLI-generated file is signed correctly and will import successfully while offline (signature/fingerprint/expiry pass; heartbeat returns `None` and is skipped). However, it will be deleted on the **first successful heartbeat** because no `activated_devices` row exists — the server returns `valid=false` / `revoked=true` for unknown fingerprints. If strict offline provenance enforcement is required, `ActivateOfflineAsync` must require a successful heartbeat before accepting any `.lic` file.

**Precondition:** fingerprint must NOT exist in `activated_devices` table — server blocks with "Máy này đã được kích hoạt trước đó!" if any row exists.

**Request** (`application/json` — verified against `server.py` which uses `request.get_json()`):
```json
{
  "fingerprint": "<64-char hex>",
  "note": "optional label",
  "days": 365,
  "heartbeat_days": 30
}
```

**Response on success:**
```json
{ "success": true, "license_name": "license_<first12offingerprint>.lic", "license_data": { ...signed license object... } }
```
Dashboard downloads `JSON.stringify(license_data, null, 2)` as the `.lic` file — send this file to the customer.

> ⚠️ **`heartbeat_days` caveat:** The `heartbeat_days` value set during offline generation is stored in the server DB but is **not** embedded in the `license_data` token (which only carries `fingerprint/expiry/signature/version`). If the customer imports the `.lic` while offline, `HeartbeatGraceDays` defaults to 30 until the first **successful heartbeat response from the activation server** syncs the server-configured value. Generic internet connectivity is **not** sufficient — the activation server itself must be reachable and respond successfully to `/heartbeat`. If `ActivateOfflineAsync` cannot get a successful heartbeat response (server down, wrong URL, TLS failure, firewall), the imported license keeps the local default of 30 days regardless of network state. If immediate enforcement of a custom grace period is required, the customer must import while **the activation server is reachable**. **⚠️ Critical edge case: if `heartbeat_days=0` was configured (meaning "no heartbeat required"), an import without a successful heartbeat will still default to 30 until the first server sync — temporarily requiring heartbeats, actively contradicting the server-configured policy. Always import with the activation server reachable when `heartbeat_days=0`.**

**To reset before generating:** use **\"Nhả Key\"** (`POST /admin/reset-key`) to clear the existing device row so `/admin/generate-offline` will accept the fingerprint again. If the row came from an online activation, the original key is also freed for reuse. If the row is an offline device (`activation_key == "OFFLINE_LIC"`), no key is freed — the device row is simply deleted. The per-row \"Nhả Key\" button is hidden in the UI for revoked rows, but you can still reset a revoked device via:
- **Bulk action:** select the revoked device → bulk dropdown → "Nhả Key"
- **Direct POST:** `POST /admin/reset-key` with `{ "fingerprint": "..." }`

> ⚠️ **Do NOT use "Xóa hẳn" (`/revoke` hard delete) to reset before offline generation.** The hard-delete path calls `mark_key_free()` on the `OFFLINE_LIC` sentinel key for offline-activated devices, which corrupts the key pool (see Known Server Bugs).

> **Auth/CSRF:** Admin mutation endpoints (POST/PUT/PATCH/DELETE) require a valid admin session and `X-CSRFToken` header. Admin GET endpoints (e.g. `/admin/list`, `/admin/audit-log`) require auth but not CSRF. These endpoints are for use from the admin dashboard UI.

---

## Known Server Bugs (ACTIVATION_SYSTEM — verified still present as of Round 18)

These bugs exist in `ACTIVATION_SYSTEM/activation_server/server.py` and were confirmed still present after 18 rounds of security hardening. They are documented here for awareness. If they become blocking, submit fixes upstream to ACTIVATION_SYSTEM.

| Bug | Description |
|---|---|
| **"Nhả Key" on revoked rows not server-side guarded** | `/admin/reset-key` has no revoked-state check — it resets any device. The dashboard hides the per-row "Nhả Key" button for revoked rows (UI-only guard), but bulk-action selection includes revoked rows, and direct POST to `/admin/reset-key` works regardless. |
| **`OFFLINE_LIC` sentinel freed on hard delete** | `/revoke` (hard delete) and bulk `hard_revoke` attempt to free any truthy `activation_key`, including the sentinel value `OFFLINE_LIC` stored for offline-activated devices. With the current SQLite implementation (`UPDATE activation_keys SET used=0, fingerprint=NULL WHERE key=?`), passing `"OFFLINE_LIC"` is a no-op if no such key row exists — it does NOT insert garbage data. However, it is an unnecessary operation that could become harmful if the DB schema changes. Avoid hard delete on offline-activated devices; use `/admin/reset-key` instead. |
| **CSRF error format for `/revoke`** | The CSRF error handler only returns the structured admin-JSON error for paths starting with `/admin/`. The hard-delete route is at `/revoke` (no `/admin/` prefix), so CSRF failures on it return the generic JSON `{ "success": false, "error": "Bad Request" }` instead of the richer admin-dashboard message. The dashboard JS can still parse this response and show a toast, but the error detail is minimal. |

---

## Non-Goals

- No frontend (JavaScript) changes — the gate is at the WinForms layer, before WebView2 opens
- No backend (C# API) changes — API routes remain ungated
- No license tier / feature flag system — binary activated/not-activated only
- The activation server (`ACTIVATION_SYSTEM/`) is maintained separately — this spec only covers the C# client integration
- No drag-and-drop for `.lic` files — file-picker import only

---

## Deployment Checklist

| Step | Who | When |
|---|---|---|
| Run `setup_licensing.py` once | Developer | One-time, before first release |
| Deploy activation server to VPS | Developer | One-time, before first release |
| Copy `license_public.pem` to `desktop/Activation/` | Developer | One-time per key rotation |
| **Rebuild and re-publish desktop app** after copying PEM | Developer | Per key rotation — embedded resource requires rebuild |
| ⚠️ **Deploy new private key to activation server and restart server** | Developer | Per key rotation — must happen atomically with desktop app deploy |
| ⚠️ **Warn: key rotation invalidates ALL existing `license.dat` / `.lic` tokens** — plan reactivation: | Developer | Per key rotation |
| — Online reactivation: issue new keys or reset old used keys in admin dashboard. ⚠️ **Issuing a new key alone is NOT enough for soft-revoked devices** — `/activate` rejects any fingerprint whose `activated_devices` row has `revoked=true`, regardless of key. You MUST clear the revoked device row first (`POST /admin/reset-key` or bulk \"Nhả Key\"), then activate with a fresh or freed key. | Admin | Per key rotation |
| — Offline reactivation: use **"Nhả Key"** (`/admin/reset-key`) to clear the device row before generating new `.lic`. Per-row "Nhả Key" button is UI-hidden for revoked rows, but works via bulk action or direct POST. ⚠️ Avoid "Xóa hẳn" (hard delete) on offline-activated devices — the sentinel key `OFFLINE_LIC` is not a real key and hard delete makes an unnecessary DB UPDATE on it (see Known Server Bugs). | Admin | Per key rotation |
| — Global DB revocation is NOT required. Only the target device row must be cleared before generating a new offline `.lic`; server only blocks on that specific fingerprint. | Admin | Per key rotation |
| Update `appsettings.json` `ServerUrl` | Developer | Once |
| **Ship updated `appsettings.json`** with the deployed executable | Developer | Per config change — file is read from output dir at runtime |
| Generate activation keys in admin dashboard | Admin | Per customer |
| Send activation key to customer | Admin | Per customer |
