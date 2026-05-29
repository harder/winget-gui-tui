# Handoff — Windows COM-backend verification (`feat/com-backend`)

**Date:** 2026-05-29 · **Branch:** `feat/com-backend` · **Host:** Windows 11 on **ARM64**, App Installer `Microsoft.DesktopAppInstaller 1.29.140.0` (Arm64), winget `v1.29.140-preview`.

This was a human-in-the-loop Windows verification run against `WINDOWS-TESTING.md`. A human drove the
interactive TUI; the agent handled builds, non-interactive checks, diagnosis, and fixes.

> **⚠️ If you are running on WSL / Linux:** you **cannot** run the Windows verification here.
> Native AOT codegen can't cross-compile from Linux, and the WinGet COM server + installs need
> Windows. On Linux you CAN: read/edit source, run the cross-platform build/tests
> (`dotnet build -f net10.0` / `dotnet test`), run the spike's Linux trim-analysis
> (`spikes/ComBackendSpike/SPIKE-RESULTS.md` → "Reproducing on Linux"), and **prepare** the `#17`
> fix — but the actual pass/fail confirmation resumes on the Windows host. Don't check Windows-only
> boxes from Linux.

---

## TL;DR

- **P0 (foundational COM-on-AOT runtime): ✅ 9/9 PASS.** The headline AOT risk (`InvalidCastException`
  from enumerating WinRT-projected collections) does **not** occur in the real app — the indexed
  `Materialize<T>` pattern holds.
- **P1: in progress.** One **confirmed bug (`#17` richer detail panel)**, root cause not yet pinned.
  Two **fixes already applied** (debounce, contrast). The rest of P1 (install/version-picker/
  download/advanced/verify/progress/cancellation) is **not yet tested** — all need a healthy COM server.
- **P2:** not started; one perf observation already folded into a fix (see debounce).
- **Self-inflicted blocker:** the agent wedged the WinGet out-of-proc COM server with too many rapid,
  crash-prone diagnostic processes. **Recovery: re-register App Installer or reboot** (details below).

---

## How to BUILD on Windows (non-obvious — ARM64 host)

Plain `dotnet publish` fails at the ILC native-link step (`MSB3073 … link.exe … code 123`,
`'vswhere.exe' is not recognized`). ILC 10.0.8 calls a bare `vswhere.exe` not on PATH. Run the
publish inside the VS Dev Shell for the x64 cross-target **with the VS Installer dir on PATH**:

```powershell
$installer = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
$root = & "$installer\vswhere.exe" -latest -products * -property installationPath
Import-Module (Join-Path $root "Common7\Tools\Microsoft.VisualStudio.DevShell.dll")
Enter-VsDevShell -VsInstallPath $root -SkipAutomaticLocation -DevCmdArguments "-arch=x64 -host_arch=arm64" | Out-Null
$env:PATH = "$installer;$env:PATH"   # Enter-VsDevShell does NOT add this; ILC needs bare vswhere
dotnet publish -c Release -f net10.0-windows10.0.26100.0 -r win-x64
```

- `dotnet build` (managed) and `dotnet run` do NOT need the Dev Shell — only AOT `publish` (native link) does.
- Intermittent publish exit 1 at link/copy = a still-running `winget-tui-sharp.exe` holding the output exe; stop it first.
- Output exe: `bin\Release\net10.0-windows10.0.26100.0\win-x64\publish\winget-tui-sharp.exe`.
- A clean AOT build = **~23.3 MB** exe, **no `coreclr.dll`** (true AOT). `.pdb` ~69 MB is symbols only.

---

## Results so far

### P0 — ✅ 9/9 (all boxes checked in WINDOWS-TESTING.md)
| # | Item | Result |
|---|------|--------|
| 1 | AOT publish, native exe, no `coreclr.dll` | ✅ 23.3 MB, coreclr.dll absent |
| 2 | No `InvalidCastException` (search/list/upgrades/show) | ✅ clean everywhere |
| 3 | Default backend = COM, full IDs, fast | ✅ no fallback note; structured |
| 4 | Flag precedence `--mock > --cli > --com > default` | ✅ verified both decision points |
| 5 | Search returns catalog results + version/source cols | ✅ 18–19 results, winget+msstore |
| 6 | Installed tab + correct versions | ✅ 248 packages |
| 7 | Upgrades tab (subset, Available col) | ✅ |
| 8 | Details panel (publisher/desc/homepage/license/notes) | ✅ |
| 9 | Source filter `f` cycles All/winget/msstore, re-queries | ✅ functional (+ cosmetic notes below) |

Also verified non-interactively: the **spike** (`spikes/ComBackendSpike/`) AOT-publishes (3.85 MB) and
activates COM, returning 3 catalogs + 3 matches for "powertoys" with full property access — re-confirming
the indexed pattern on this host.

### P1 — partial
| Item | Result |
|------|--------|
| #17 Richer detail panel (Tags/ProductCode/FamilyName/Support/Docs) | ❌ **CONFIRMED BUG** (see below). Absent-field omission half ✅. |
| #11 Core ops (install/version/upgrade/uninstall/batch) | ⬜ not tested |
| #12 Install preview dialog (`i`) | ⬜ not tested |
| #13 Version picker (`I`) | ⬜ not tested |
| #14 Download-only (`d`) | ⬜ not tested |
| #15 Advanced install (`A`) | ⬜ not tested |
| #16 Verify install (`V`) | ⬜ not tested |
| #18 Live progress bar | ⬜ not tested |
| #19 Cancellation (`Esc`) | ⬜ not tested |

**Chosen test package for the destructive P1 ops: `ajeetdsouza.zoxide`** (small, native arm64, silent install/uninstall).

### P2 — not started (#20). One observation already addressed by the debounce fix (below).

---

## Code changes made this session (UNCOMMITTED working-tree edits unless you committed)

1. **`src/App.cs` — detail-load debounce (REAL FIX, keep).** `DetailLoadDebounceMs = 200`; `OnSelectedRowChanged`
   now `await Task.Delay(DetailLoadDebounceMs, ct)` before the backend `ShowAsync`. Prevents a fast list-scroll
   from firing a COM detail fetch per row (which throttled/wedged the COM server → 30–60s detail stalls).
   Each selection change cancels `_detailCts`, so passed-over rows never hit the backend. **Compiles; NOT yet
   verified at runtime on Windows.**
2. **`src/App.cs` — msstore Source-cell contrast fix (REAL FIX, keep).** In `ApplyColumnStyles`'s Source
   `ColorGetter`, removed the `Focus`/selected-row foreground override so a selected msstore row (whose
   background is `Theme.Accent`) no longer renders Accent-on-Accent (invisible). Color-coding kept for
   non-selected rows. **Compiles; NOT yet verified at runtime.**
3. **`spikes/ComBackendSpike/Program.cs` — metadata probe (DIAGNOSTIC, keep).** Step 5 now probes
   `GetCatalogPackageMetadata()` fields. This is what proved the COM data for `#17` exists (see below).
   A composite-path Step 6 was attempted and reverted (it threw `E_ILLEGAL_STATE_CHANGE`; see note in file).
4. **`Program.cs` — `--comshow` diagnostic was added and REVERTED.** It's gone from the tree; the snippet to
   re-add it is in the appendix below.
5. **`WINDOWS-TESTING.md`** — P0 boxes checked with notes; `#17` marked ❌ with detail.
6. **Memory files** under `~/.claude/projects/.../memory/` — build-env + COM-wedge lessons.

---

## THE OPEN BUG: `#17` richer detail panel (Tags / Product code / Family name / Support / Documentation)

**Symptom:** None of these fields ever render in the detail panel, for any package, including
`Microsoft.PowerToys` which definitely has them.

**Evidence gathered:**
- `winget show --id Microsoft.PowerToys` (via the app's `--dump show`) shows the manifest HAS:
  Publisher Support Url, Documentation (Wiki), and 10 Tags.
- The **spike** (`--query powertoys`, Step 5) fetched `Microsoft.PowerToys`'s metadata from a **single
  `winget`-catalog** connect and got: `meta.Publisher` ✓, `meta.PublisherSupportUrl` = the URL ✓,
  `meta.Tags` = 10 items (foreach even works) ✓, `meta.Documentations` = 1 item (indexed access works) ✓,
  `ProductCodes`/`PackageFamilyNames` = 0 (legitimately — PowerToys is a `burn` installer, not MSI/MSIX).
- So the **COM data exists and is readable** via the exact indexed pattern `ComBackend` uses.
- The detail PANEL renders all five fields conditionally (verified in `DetailPanel.SetDetail`), and
  `MergeContext`/`EnsureDetailHint` (`Models.cs`) do NOT touch them (they're `init`-only).
- Therefore **`ComBackend.ShowAsync` must be returning Tags/SupportUrl/Documentation as null/empty.**

**Leading hypothesis:** the difference between the working spike and the app is that the spike used a
**single-catalog** connect, while `ComBackend.ShowAsync` resolves the package via `FindByIdAsync` over a
**composite `All` catalog** (`CreateCompositePackageCatalog` of winget+msstore,
`RemotePackagesFromRemoteCatalogs`). Suspect: a composite-catalog package's
`DefaultInstallVersion.GetCatalogPackageMetadata()` returns the scalar fields (Publisher/Description/
Homepage/License/ReleaseNotes — which the user DOES see) but not the richer ones. NOT yet confirmed —
the puzzle is why scalar `PublisherSupportUrl` would differ from scalar `Publisher` on the same `meta`.

**Decisive next step (Windows, healthy COM):** re-add the `--comshow` diagnostic (appendix) and run
`winget-tui-sharp.exe --comshow Microsoft.PowerToys` ONCE.
- If `Tags = <null>`, `SupportUrl = <null>`, `Docs = <null>` → confirmed: fix `ShowAsync` to fetch metadata
  via the package's own source (e.g. resolve/connect the single source catalog the package came from, or
  re-find by id in a single-source catalog) before `GetCatalogPackageMetadata()`.
- If they're populated → bug is in the TUI render/cache path, not `ShowAsync` — investigate
  `OnSelectedRowChanged` / `DetailCache` / `DetailPanel` instead.

Relevant code: `src/ComBackend.cs` `ShowAsync` (~L162), `FindByIdAsync` (~L806), `StringVector`/`DocLinks`
helpers; `src/DetailPanel.cs` `SetDetail` (L60–135); `src/App.cs` `OnSelectedRowChanged` (~L535).

---

## Cosmetic follow-ups found (not blockers)

- **Source column blank under single-source filter** (`f` → winget): in `All` mode rows show `winget`,
  but filtered-to-winget rows show a blank Source cell (and the detail Source line is then omitted via
  MergeContext). Low value (you already filtered to that source); root-cause needs live COM. Deferred.
- **Search box doesn't auto-open on the Search tab** — pressing `1` then needing `/` to type. UX polish:
  auto-focus the filter input when switching to Search with an empty query. Deferred (user-requested nicety).
- **Source column clipped at narrow terminal width** — `ExpandLastColumn`; reappears when widened. Acceptable.

---

## The COM-server wedge (READ before running ANY COM diagnostic)

The agent spawned ~8 short-lived processes that each `new PackageManager()`; several **crashed mid-COM-op**
(an `E_ILLEGAL_STATE_CHANGE` in a flawed composite spike). After that, COM activation fails **everywhere**
(both background tasks AND the user's interactive session) with:
`COMException 0x80073D54` = Win32 `APPMODEL_ERROR_NO_PACKAGE` (15700). winget CLI still works.

**Recovery (lightest first):**
1. `Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe` then retry.
2. If still wedged, **reboot** (reliable fix for a wedged WinGet OOP COM server).

**Lesson (also in memory):** do NOT spawn many rapid/short-lived COM-activating processes, and never let
one crash mid-COM-operation. For introspection prefer ONE long-lived process or the interactive TUI. The
in-app version of this same hammering (fast scroll → per-row detail fetch) is what the **debounce fix**
addresses.

---

## Resume plan (next Windows session, COM healthy)

1. Recover COM (re-register App Installer or reboot); confirm `winget-tui-sharp.exe` launches with no
   "COM backend unavailable" fallback and Search returns structured results.
2. **Verify the two applied fixes** on the rebuilt binary:
   - Debounce: hold ↓ to scroll the list fast — detail panel should NOT freeze for tens of seconds;
     settles within ~0.2s on the row you stop on.
   - Contrast: highlight the msstore row in a powertoys search — Source cell text readable when selected.
3. **Root-cause + fix `#17`** via the `--comshow` dump (appendix), then rebuild + re-verify on PowerToys.
4. **Run the rest of P1** with `ajeetdsouza.zoxide` (do the non-destructive dialog checks first —
   `i` preview→Cancel, `I` version list→Cancel, `A` options→Cancel, `V` positive — then the real
   install/download/uninstall/upgrade, progress bar, and `Esc` cancellation; finally batch upgrade).
5. **P2 (#20):** shared-`PackageManager` thread-agility (watch RPC_E_WRONG_THREAD); unhealthy-source + `All`
   (break msstore, confirm `f`→winget recovers); pinning (`p`/`P`, needs winget on PATH); AOT vs CLI binary
   size; optional arm64.
6. Check off boxes in `WINDOWS-TESTING.md` as items pass; **commit the checklist + the `#17` fix**; remove
   `--comshow` again before final commit.

---

## Appendix — the `--comshow` diagnostic to re-add to `Program.cs`

Insert right after `using WingetTuiSharp;` (gated so it only affects the `--comshow` arg). Build the
win-x64 AOT exe (Dev Shell) and run it from an interactive, COM-healthy session.

```csharp
#if WINGET_COM
// TEMP DIAGNOSTIC (remove before commit): dump exactly what ComBackend.ShowAsync returns.
if (args.Length > 1 && args [0] is "--comshow")
{
    ComBackend be = new ();
    PackageDetail? d = await be.ShowAsync (args [1], CancellationToken.None);
    if (d is null) { Console.WriteLine ("ShowAsync returned null"); return; }
    Console.WriteLine ($"Name        = {d.Name}");
    Console.WriteLine ($"Publisher   = {d.Publisher}");
    Console.WriteLine ($"Homepage    = {d.Homepage}");
    Console.WriteLine ($"License     = {d.License}");
    Console.WriteLine ($"RelNotesUrl = {d.ReleaseNotesUrl}");
    Console.WriteLine ($"SupportUrl  = {d.SupportUrl}");
    Console.WriteLine ($"Tags        = {(d.Tags is null ? "<null>" : string.Join (" | ", d.Tags))}");
    Console.WriteLine ($"Docs        = {(d.Documentation is null ? "<null>" : string.Join (" | ", d.Documentation.Select (x => $"{x.Label}:{x.Url}")))}");
    Console.WriteLine ($"ProductCodes= {(d.ProductCodes is null ? "<null>" : string.Join (" | ", d.ProductCodes))}");
    Console.WriteLine ($"FamilyNames = {(d.PackageFamilyNames is null ? "<null>" : string.Join (" | ", d.PackageFamilyNames))}");
    return;
}
#endif
```
