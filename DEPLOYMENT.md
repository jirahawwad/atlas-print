# Atlas.Print — Deployment & Redeployment Runbook

Applies to the `Atlas.Print` repo only. Not tied to any feature branch —
this is the standing process for getting any build onto any environment.

**Hosting model**: self-hosted Kestrel, registered as a Windows Service
(`builder.Host.UseWindowsService()`) — **not** IIS-hosted, no `web.config`.
Service name: `Atlas.Print`. Listens on `http://localhost:8085` (per
`appsettings.json`'s `Urls`).

**Environments**: `LOCAL`, `DEV`, `DEMO`, `QA`, `PROD` — driven by the
`ASPNETCORE_ENVIRONMENT` value set in the Windows Service's own registry
`Environment` entry (`HKLM:\SYSTEM\CurrentControlSet\Services\Atlas.Print`),
**not** a system-wide environment variable and **not** `launchSettings.json`
(that file only affects local `dotnet run`/Visual Studio F5, never a
deployed box). The registry `Environment` entry also carries
`PLAYWRIGHT_BROWSERS_PATH`. DEMO shares a host with QA, so no separate DEMO
service install is needed.

**Versioned publish layout**: publish profiles (`Properties\PublishProfiles\`)
publish to `publish\<env>\$(Version)\`, matching `<Version>` in
`Atlas.Print.csproj`. Each release lands in its own version folder
(e.g. `E:\Apps\DataArchive\print\1.0.0\`) rather than overwriting the
previous one in place — redeployment is a **service binPath repoint**, not
a file overwrite, once a version has actually been bumped.

**Known gap**: unlike its Linux sibling `Atlas.Report.Print` (which has
`deploy-service.sh` + versioned `.service` unit files committed to the
repo), `Atlas.Print` has **no deployment automation checked in** — no
install/update script. Everything below is manual until someone builds the
PowerShell equivalent.

---

## Part A — Redeployment (existing service, the common case)

Use this for shipping a new build to a box where `Atlas.Print` is already
installed and running.

### 1. Pre-flight checks (on the target box)

```powershell
Get-Service -Name "Atlas.Print"

# Note current Environment values before touching anything
Get-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\Atlas.Print" -Name Environment

# Note the current binPath — this is what you'll be repointing FROM
Get-CimInstance -ClassName Win32_Service -Filter "Name='Atlas.Print'" | Select-Object Name, PathName, StartMode, State
```

- [ ] Note the current `PathName` (e.g. `E:\Apps\DataArchive\print\1.0.0\Atlas.Print.exe`).
- [ ] Note the current `Environment` values — confirm they're still correct
      after redeploy (should be untouched, since a binPath repoint doesn't
      touch the registry `Environment` entry at all — but confirm rather
      than assume).

### 2. Build

```powershell
dotnet publish Atlas.Print/Atlas.Print.csproj -c <LOCAL|DEV|DEMO|QA|PROD> -o <publish-output-folder>
```

Or via the matching `.pubxml` in Visual Studio — either way, confirm only
the correct `appsettings.<ENV>.json` shipped:

```powershell
Get-ChildItem <publish-output-folder>\appsettings*.json
```
Expect exactly `appsettings.json` + `appsettings.<THIS_ENV>.json` — no
others.

### 3. Unpack the new build into its own versioned folder on the server

```powershell
# E:\Apps\DataArchive\print\<new version>\
```
Do **not** overwrite the currently-running version's folder — this is a
side-by-side deploy, which is what makes rollback a one-line repoint
instead of a file restore.

### 4. Stop the service

```powershell
Stop-Service -Name "Atlas.Print"
Get-Service -Name "Atlas.Print" | Select-Object Status
```
Confirm `Stopped` before continuing. This isn't required to protect the new
version's files (they're in a separate folder), but the service must be
stopped before the binPath repoint in step 6 takes effect cleanly.

### 5. Install matching Chromium for the new version's folder

- [ ] **Confirm whether the `Microsoft.Playwright` package version changed**
      in this release before assuming this step is required.
- [ ] **Playwright prunes unreferenced browser revisions on each install** —
      don't assume an older revision (e.g. from a previous
      `Microsoft.Playwright` version) is still sitting in
      `playwright-browsers\` untouched. If you ever roll back to an older
      package version, re-run this same install step from that old
      version's folder rather than assuming its browser is still cached.

```powershell
# Session-scoped — an interactive PowerShell session does NOT automatically
# inherit the Windows Service's own registry Environment value. Set this
# explicitly every time, unless this box also has a machine-wide
# PLAYWRIGHT_BROWSERS_PATH system variable (confirm which applies here).
$env:PLAYWRIGHT_BROWSERS_PATH = "E:\Apps\DataArchive\print\playwright-browsers"

cd "E:\Apps\DataArchive\print\<new version>"
.\playwright.ps1 install chromium
```

### 6. Repoint the service to the new version

```powershell
sc.exe config "Atlas.Print" binPath= "E:\Apps\DataArchive\print\<new version>\Atlas.Print.exe"
```
Note the exact syntax: a space *after* `binPath=`, none before it — `sc.exe`
fails confusingly if this is backwards.

```powershell
# Verify the repoint took
Get-CimInstance -ClassName Win32_Service -Filter "Name='Atlas.Print'" | Select-Object Name, PathName
```

### 7. Verify config is still intact

```powershell
Get-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\Atlas.Print" -Name Environment
```
Should be unchanged from step 1 — a binPath repoint never touches this.

### 8. Start and verify

```powershell
Start-Service -Name "Atlas.Print"
Get-Service -Name "Atlas.Print" | Select-Object Status
```

- [ ] **Check the log** for a clean startup, no `Fatal` entries — and (once
      the version-logging line is in `BrowserPool.StartAsync`) confirm the
      expected `Microsoft.Playwright` assembly version:
      ```powershell
      Get-Content "<log path for this env>\atlas-print-$(Get-Date -Format yyyy-MM-dd).log" -Tail 50
      ```
      (Log path per environment: DEV/PROD use `E:\Logs\Atlas.Print\`, QA/DEMO
      use `F:\Logs\Atlas.Print\` (DEMO in its own `Atlas.Print.Demo`
      subfolder, since DEMO shares a host with QA), base config uses
      `C:\Logs\Atlas.Print\`. Confirm which applies to this box.)
- [ ] **Smoke test** — confirm the service actually renders a PDF, not just
      that the process is running:
      ```powershell
      $body = @{ htmlPayload = "<html><body><h1>Smoke test</h1></body></html>"; printFormat = "PORTRAIT" } | ConvertTo-Json
      Invoke-RestMethod -Uri "http://localhost:8085/print/generate" -Method Post -Body $body -ContentType "application/json"
      ```
      Expect a `200` with a `base64Document` in the response — not a `500`.
- [ ] **If this box is `LOCAL`/`DEV`**: confirm `/print/preview` and
      `/swagger` are reachable. **If `DEMO`/`QA`/`PROD`**: confirm both
      return `404`.
- [ ] Confirm the mid-tier(s) that call this instance can still reach it —
      confirm nothing about the deploy changed the port/host it listens on.

### 9. Clean up

- [ ] Once confirmed healthy, keep the previous version's folder (and its
      registered binPath, for reference) for a reasonable retention window
      before deleting it — it's your rollback.

---

## Part B — Rollback

Since redeploy is a side-by-side version folder + binPath repoint, rollback
is just repointing back:

```powershell
Stop-Service -Name "Atlas.Print"
sc.exe config "Atlas.Print" binPath= "E:\Apps\DataArchive\print\<previous version>\Atlas.Print.exe"
Get-CimInstance -ClassName Win32_Service -Filter "Name='Atlas.Print'" | Select-Object Name, PathName
Start-Service -Name "Atlas.Print"
```

Then re-run the verification steps from Part A, §8.

- [ ] **If the Playwright package version also changed in the release being
      rolled back**, re-run Part A §5's install step from the *previous*
      version's folder first — its browser revision may have been pruned
      when the newer version was installed (see the note in §5). Don't
      assume it's still there.

The `Environment` registry value never needs to be touched for rollback —
it's independent of which version's binPath is active.

---

## Part C — First-time install (new box / new environment)

For a box where `Atlas.Print` has never been installed. Less likely given
all environments already exist (and DEMO intentionally shares QA's host,
not a separate install), but included for completeness.

### 1. Prerequisites

- [ ] .NET 8 runtime installed on the box (confirm framework-dependent vs.
      self-contained deploy — not confirmed either way yet).
- [ ] Target port (`8085` by default) confirmed free and not blocked by
      firewall from whichever box(es) will call it.
- [ ] `PLAYWRIGHT_BROWSERS_PATH` target folder decided and Chromium
      installed there (Part A, §5).
- [ ] Log directory (per this environment's `appsettings.<ENV>.json`)
      exists, with write permission for whichever account the service runs
      as.

### 2. Build and copy files

Same as Part A, §2–3 — publish to a versioned folder on the target box.

### 3. Register the Windows Service

```powershell
New-Service -Name "Atlas.Print" `
    -DisplayName "Atlas Print Service" `
    -BinaryPathName "E:\Apps\DataArchive\print\<version>\Atlas.Print.exe" `
    -StartupType Automatic
```

### 4. Set per-service environment variables

`New-Service` has no parameter for this — set directly in the registry
immediately after creation:

```powershell
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\Atlas.Print" -Name Environment -Value @(
    "ASPNETCORE_ENVIRONMENT=<LOCAL|DEV|DEMO|QA|PROD>",
    "PLAYWRIGHT_BROWSERS_PATH=<target path from §1>"
)
```

### 5. Start and verify

Same as Part A, §8.

---

## Resolved since this runbook was first written

- `<Configurations>` in the `.csproj` now lists all five environments
  (`Debug;Release;LOCAL;DEV;DEMO;QA;PROD`).
- Solution-level Configuration Manager mappings updated to match (a
  separate `.sln`-level fix beyond the `.csproj` change — both were needed
  for Visual Studio's Publish UI to correctly select non-Debug/Release
  configurations).
- `launchSettings.json`'s `LOCAL` profile now sets
  `ASPNETCORE_ENVIRONMENT=LOCAL` (was incorrectly `Development`, which
  caused `/swagger` and `/print/preview` to 404 under local F5 debugging
  since neither matched the `["LOCAL", "DEV"]` gate).
- `appsettings.DEV.json` and `appsettings.DEMO.json` existed locally but
  were never committed — now tracked in the repo.
- `Properties\PublishProfiles\` now has `DEV`/`QA`/`PROD` `.pubxml` files,
  each publishing to `publish\<env>\$(Version)\`.
- `Microsoft.Playwright` bumped to `1.62.0` (from `1.44.0`), matching
  `Atlas.Report.Print`.

## Open items worth addressing separately (not blockers for a manual deploy)

- No committed install/update script for `Atlas.Print`, unlike
  `Atlas.Report.Print`'s `deploy-service.sh` — worth building a PowerShell
  equivalent of this entire runbook (Part A especially) so redeploys stop
  being manual and error-prone.
- No automated cleanup of old version folders — they accumulate on disk
  over time (intentional, for rollback) but nothing prunes them; a manual
  periodic cleanup is the only mechanism today.
