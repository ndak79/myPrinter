#!/usr/bin/env python3
"""
OpenCode Antigravity Auth - Automated Setup & Fix Script
=========================================================
Fixes all known issues with opencode-antigravity-auth plugin on Windows.

Usage:
    python fix_antigravity.py --project-id YOUR_PROJECT_ID
    python fix_antigravity.py                               (will prompt)
    python fix_antigravity.py --skip-install --project-id X  (skip npm)

Issues this script fixes:
  1. Plugin not installed globally
  2. Plugin cache holding stale beta version
  3. proper-lockfile ESM import crash ("Missing 'default' export")
  4. Stale Google API key in auth.json causing "Invalid API key"
  5. antigravity-accounts.json empty (0 accounts) after OAuth
  6. Missing projectId causing "The caller does not have permission" (403)
  7. projectId not synced into auth.json refresh token
  8. opencode.json missing model definitions
"""

import json
import os
import sys
import subprocess
import shutil
import argparse
import platform
import time
import io
from pathlib import Path

# Fix Windows console encoding
if platform.system() == "Windows":
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")


# ── Helpers ──────────────────────────────────────────────────

def home():
    return Path.home()

def config_dir():
    return home() / ".config" / "opencode"

def data_dir():
    return home() / ".local" / "share" / "opencode"

def cache_dir():
    return home() / ".cache" / "opencode"

def run(cmd, desc="", timeout=120):
    print(f"  > {desc or cmd}")
    try:
        r = subprocess.run(cmd, shell=True, capture_output=True, text=True, timeout=timeout)
        return r.returncode == 0, r.stdout.strip(), r.stderr.strip()
    except subprocess.TimeoutExpired:
        return False, "", "timeout"
    except Exception as e:
        return False, "", str(e)

def header(n, title):
    print(f"\n{'='*60}\n  Step {n}: {title}\n{'='*60}")

def ok(msg):   print(f"  [OK]    {msg}")
def warn(msg): print(f"  [WARN]  {msg}")
def err(msg):  print(f"  [ERROR] {msg}")
def info(msg): print(f"  [INFO]  {msg}")

def read_json(path):
    try:
        return json.loads(Path(path).read_text(encoding="utf-8-sig"))
    except Exception:
        return None

def write_json(path, data):
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    Path(path).write_text(json.dumps(data, indent=2, ensure_ascii=False), encoding="utf-8")


# ── Step 1: npm install ─────────────────────────────────────

def step_install():
    header(1, "Install / Update OpenCode + Plugin")

    for name, pkg in [("opencode-ai", "opencode-ai@latest"),
                      ("plugin", "opencode-antigravity-auth@latest")]:
        good, out, stderr = run(f"npm install -g {pkg}",
                                f"npm install -g {pkg}", timeout=300)
        if good:
            ok(f"{name} installed")
        else:
            warn(f"{name}: {stderr[:200]}")

    good, ver, _ = run("opencode --version", "opencode --version")
    if good:
        ok(f"opencode {ver}")


# ── Step 2: Nuke plugin cache ───────────────────────────────

def step_clean_cache():
    header(2, "Clean Plugin Cache")
    d = cache_dir()
    if d.exists():
        try:
            shutil.rmtree(d)
            ok(f"Removed {d}")
        except Exception:
            if platform.system() == "Windows":
                run(f'rmdir /s /q "{d}"', "rmdir via cmd")
            else:
                run(f'rm -rf "{d}"', "rm -rf")
    else:
        info("Cache already clean")


# ── Step 3: Write opencode.json ─────────────────────────────

MODELS = {
    "antigravity-gemini-3-pro": {
        "name": "Gemini 3 Pro (Antigravity)",
        "limit": {"context": 1048576, "output": 65535},
        "modalities": {"input": ["text","image","pdf"], "output": ["text"]},
        "variants": {"low": {"thinkingLevel":"low"}, "high": {"thinkingLevel":"high"}}
    },
    "antigravity-gemini-3.1-pro": {
        "name": "Gemini 3.1 Pro (Antigravity)",
        "limit": {"context": 1048576, "output": 65535},
        "modalities": {"input": ["text","image","pdf"], "output": ["text"]},
        "variants": {"low": {"thinkingLevel":"low"}, "high": {"thinkingLevel":"high"}}
    },
    "antigravity-gemini-3-flash": {
        "name": "Gemini 3 Flash (Antigravity)",
        "limit": {"context": 1048576, "output": 65536},
        "modalities": {"input": ["text","image","pdf"], "output": ["text"]},
        "variants": {"minimal": {"thinkingLevel":"minimal"}, "low": {"thinkingLevel":"low"},
                     "medium": {"thinkingLevel":"medium"}, "high": {"thinkingLevel":"high"}}
    },
    "antigravity-claude-sonnet-4-6": {
        "name": "Claude Sonnet 4.6 (Antigravity)",
        "limit": {"context": 200000, "output": 64000},
        "modalities": {"input": ["text","image","pdf"], "output": ["text"]}
    },
    "antigravity-claude-opus-4-6-thinking": {
        "name": "Claude Opus 4.6 Thinking (Antigravity)",
        "limit": {"context": 200000, "output": 64000},
        "modalities": {"input": ["text","image","pdf"], "output": ["text"]},
        "variants": {"low": {"thinkingConfig":{"thinkingBudget":8192}},
                     "max": {"thinkingConfig":{"thinkingBudget":32768}}}
    },
    "gemini-2.5-flash": {
        "name": "Gemini 2.5 Flash (Gemini CLI)",
        "limit": {"context": 1048576, "output": 65536},
        "modalities": {"input": ["text","image","pdf"], "output": ["text"]}
    },
    "gemini-2.5-pro": {
        "name": "Gemini 2.5 Pro (Gemini CLI)",
        "limit": {"context": 1048576, "output": 65536},
        "modalities": {"input": ["text","image","pdf"], "output": ["text"]}
    },
    "gemini-3-flash-preview": {
        "name": "Gemini 3 Flash Preview (Gemini CLI)",
        "limit": {"context": 1048576, "output": 65536},
        "modalities": {"input": ["text","image","pdf"], "output": ["text"]}
    },
    "gemini-3-pro-preview": {
        "name": "Gemini 3 Pro Preview (Gemini CLI)",
        "limit": {"context": 1048576, "output": 65535},
        "modalities": {"input": ["text","image","pdf"], "output": ["text"]}
    },
    "gemini-3.1-pro-preview": {
        "name": "Gemini 3.1 Pro Preview (Gemini CLI)",
        "limit": {"context": 1048576, "output": 65535},
        "modalities": {"input": ["text","image","pdf"], "output": ["text"]}
    },
    "gemini-3.1-pro-preview-customtools": {
        "name": "Gemini 3.1 Pro Preview Custom Tools (Gemini CLI)",
        "limit": {"context": 1048576, "output": 65535},
        "modalities": {"input": ["text","image","pdf"], "output": ["text"]}
    },
}

def step_write_config():
    header(3, "Write opencode.json")
    p = config_dir() / "opencode.json"

    cfg = {
        "$schema": "https://opencode.ai/config.json",
        "plugin": ["opencode-antigravity-auth@latest"],
        "provider": {"google": {"models": MODELS}},
        "model": "google/antigravity-claude-opus-4-6-thinking"
    }

    # preserve extra keys from existing config
    if p.exists():
        old = read_json(p)
        if old:
            for k, v in old.items():
                if k not in cfg:
                    cfg[k] = v

    write_json(p, cfg)
    ok(f"Written {p}")


# ── Step 4: Remove stale API key ────────────────────────────

def step_clean_api_key():
    header(4, "Remove Stale Google API Key")
    p = data_dir() / "auth.json"
    if not p.exists():
        info("No auth.json yet")
        return

    data = read_json(p)
    if not data:
        return

    g = data.get("google", {})
    changed = False

    if "key" in g:
        del g["key"]
        changed = True
    if g.get("type") == "api":
        del data["google"]
        changed = True

    if changed:
        write_json(p, data)
        ok("Removed stale API key")
    else:
        ok("No stale key found")


# ── Step 5: Patch proper-lockfile ────────────────────────────

def step_patch_lockfile():
    header(5, "Patch proper-lockfile ESM Import Bug")

    storage = (cache_dir() / "node_modules" / "opencode-antigravity-auth"
               / "dist" / "src" / "plugin" / "storage.js")

    if not storage.exists():
        info("Plugin not cached yet - will patch after first 'opencode auth login'")
        info("Re-run this script if you see 'Missing default export ... proper-lockfile'")
        return

    txt = storage.read_text(encoding="utf-8")
    old = 'import lockfile from "proper-lockfile";'
    if old not in txt:
        ok("Already patched")
        return

    new = ('import * as properLockfile from "proper-lockfile";\n'
           'const lockfile = properLockfile.default ?? properLockfile;')
    txt = txt.replace(old, new)
    storage.write_text(txt, encoding="utf-8")
    ok(f"Patched {storage.name}")


# ── Step 6: Set projectId ───────────────────────────────────

def step_set_project(pid):
    header(6, f"Set Project ID → {pid}")

    # 6a. antigravity-accounts.json
    ap = config_dir() / "antigravity-accounts.json"
    if ap.exists():
        data = read_json(ap)
        if data and data.get("accounts"):
            for acc in data["accounts"]:
                acc["projectId"] = pid
            write_json(ap, data)
            ok(f"Updated accounts file")

    # 6b. auth.json refresh token
    auth_p = data_dir() / "auth.json"
    if auth_p.exists():
        data = read_json(auth_p)
        if data:
            g = data.get("google", {})
            if g.get("type") == "oauth" and g.get("refresh"):
                parts = str(g["refresh"]).split("|")
                if len(parts) == 1:
                    parts.append(pid)
                else:
                    parts[1] = pid
                g["refresh"] = "|".join(parts[:3])
                data["google"] = g
                write_json(auth_p, data)
                ok("Updated auth.json refresh token")


# ── Step 7: Seed account ────────────────────────────────────

def step_seed_account(pid):
    header(7, "Seed Antigravity Account from OAuth")

    auth_p = data_dir() / "auth.json"
    acc_p  = config_dir() / "antigravity-accounts.json"

    if not auth_p.exists():
        info("No auth.json - run 'opencode auth login' first")
        return

    data = read_json(auth_p)
    if not data:
        return

    g = data.get("google", {})
    if g.get("type") != "oauth" or not g.get("refresh"):
        info("No Google OAuth credential - run 'opencode auth login' first")
        return

    # Already have accounts?
    if acc_p.exists():
        acc = read_json(acc_p)
        if acc and acc.get("accounts") and len(acc["accounts"]) > 0:
            a0 = acc["accounts"][0]
            if a0.get("projectId") == pid:
                ok("Account already exists with correct projectId")
                return
            a0["projectId"] = pid
            write_json(acc_p, acc)
            ok("Updated existing account projectId")
            return

    # Seed new account
    refresh_token = str(g["refresh"]).split("|")[0]
    if not refresh_token:
        warn("Empty refresh token")
        return

    now = int(time.time() * 1000)
    acc_data = {
        "version": 4,
        "accounts": [{
            "refreshToken": refresh_token,
            "projectId": pid,
            "addedAt": now,
            "lastUsed": now,
            "enabled": True,
        }],
        "activeIndex": 0,
        "activeIndexByFamily": {"claude": 0, "gemini": 0},
    }
    write_json(acc_p, acc_data)
    ok(f"Seeded account in {acc_p}")


# ── Step 8: Verify ──────────────────────────────────────────

def step_verify():
    header(8, "Verify Setup")
    issues = []

    # opencode.json
    cfg = read_json(config_dir() / "opencode.json")
    if cfg:
        plugins = cfg.get("plugin", [])
        if any("opencode-antigravity-auth" in p for p in plugins):
            ok("opencode.json has plugin")
        else:
            issues.append("opencode.json missing plugin entry")
    else:
        issues.append("opencode.json missing or unreadable")

    # auth.json
    auth = read_json(data_dir() / "auth.json")
    if auth:
        g = auth.get("google", {})
        if g.get("type") == "oauth" and g.get("refresh"):
            ok("auth.json has Google OAuth")
        elif g.get("key") or g.get("type") == "api":
            issues.append("auth.json still has API key!")
        else:
            issues.append("No Google OAuth - need 'opencode auth login'")
    else:
        issues.append("auth.json missing - need 'opencode auth login'")

    # antigravity-accounts.json
    acc = read_json(config_dir() / "antigravity-accounts.json")
    if acc:
        accs = acc.get("accounts", [])
        if len(accs) > 0:
            ok(f"{len(accs)} account(s)")
            pid = accs[0].get("projectId", "")
            if pid:
                ok(f"projectId = {pid}")
            else:
                issues.append("Account missing projectId")
        else:
            issues.append("0 accounts - need 'opencode auth login'")
    else:
        issues.append("antigravity-accounts.json missing")

    # lockfile patch
    storage = (cache_dir() / "node_modules" / "opencode-antigravity-auth"
               / "dist" / "src" / "plugin" / "storage.js")
    if storage.exists():
        if 'import lockfile from "proper-lockfile"' in storage.read_text(encoding="utf-8"):
            issues.append("proper-lockfile NOT patched")
        else:
            ok("proper-lockfile patch OK")

    print()
    if issues:
        for i in issues:
            err(i)
        return False
    ok("All checks passed!")
    return True


# ── Step 9: Next steps ──────────────────────────────────────

def step_next(pid, needs_login):
    header(9, "Next Steps")
    print(f"""
  1. ENABLE API (one-time, in browser):
     https://console.cloud.google.com/apis/library/cloudaicompanion.googleapis.com?project={pid}
     -> Click "Enable"
""")
    if needs_login:
        print(f"""  2. LOGIN:
     opencode auth login
     -> Google -> OAuth with Google (Antigravity)
     -> Project ID: {pid}
     -> Complete OAuth in browser
     -> Must see "1 account(s)" not "0 account(s)"
     -> If plugin crashes, re-run: python fix_antigravity.py --project-id {pid}
""")
    print("""  3. TEST:
     opencode
     -> Model: google/antigravity-gemini-3-flash  (or any below)
     -> Send: hello

  4. AVAILABLE MODELS:
     google/antigravity-gemini-3-flash
     google/antigravity-gemini-3-pro
     google/antigravity-gemini-3.1-pro
     google/antigravity-claude-sonnet-4-6
     google/antigravity-claude-opus-4-6-thinking  (--variant=max)
     google/gemini-2.5-flash
     google/gemini-2.5-pro
     google/gemini-3-flash-preview
     google/gemini-3-pro-preview
     google/gemini-3.1-pro-preview
""")


# ── Main ─────────────────────────────────────────────────────

def main():
    print("=" * 60)
    print("  OpenCode Antigravity Auth - Setup & Fix Script")
    print("=" * 60)

    ap = argparse.ArgumentParser()
    ap.add_argument("--project-id", type=str, help="Google Cloud Project ID")
    ap.add_argument("--skip-install", action="store_true", help="Skip npm install")
    args = ap.parse_args()

    pid = args.project_id
    if not pid:
        print("\n  You need a Google Cloud Project ID.")
        print("  Find yours at: https://console.cloud.google.com/")
        print("  Format example: brilliant-rhino-492313-c1\n")
        pid = input("  Enter Project ID: ").strip()
        if not pid:
            err("Project ID required!")
            sys.exit(1)

    info(f"Project ID: {pid}")

    if not args.skip_install:
        step_install()

    step_clean_cache()
    step_write_config()
    step_clean_api_key()

    # rebuild cache so we can patch
    info("Rebuilding plugin cache...")
    run("opencode auth list --print-logs", "opencode auth list", timeout=30)

    step_patch_lockfile()
    step_seed_account(pid)
    step_set_project(pid)

    needs_login = True
    auth = read_json(data_dir() / "auth.json")
    if auth:
        g = auth.get("google", {})
        if g.get("type") == "oauth" and g.get("refresh"):
            needs_login = False

    all_ok = step_verify()
    step_next(pid, needs_login)

    if all_ok:
        print("  Setup complete! Follow the next steps above.\n")
    else:
        print("  Partially complete. Fix issues above, then re-run.\n")


if __name__ == "__main__":
    main()