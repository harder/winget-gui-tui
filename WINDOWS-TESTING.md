# Windows verification checklist — `feat/com-backend`

Everything below can only be confirmed on a real Windows host (Native AOT codegen
can't cross-compile from Linux, and the WinGet COM server + installs need Windows).
Work top-down; **P0** gates everything else.

## Build & run

```powershell
# from the repo root, on Windows
dotnet publish -c Release -f net10.0-windows10.0.26100.0 -r win-x64
$exe = ".\bin\Release\net10.0-windows10.0.26100.0\win-x64\publish\winget-tui-sharp.exe"

# confirm it's a real Native-AOT image (single native exe, no CoreCLR shipped):
Test-Path ".\bin\Release\net10.0-windows10.0.26100.0\win-x64\publish\coreclr.dll"   # expect: False

& $exe            # default backend (COM on Windows)
& $exe --cli      # force the CLI backend
& $exe --mock     # force the mock backend
& $exe --com      # force COM explicitly
```

For quick iteration without AOT: `dotnet run -f net10.0-windows10.0.26100.0`.

---

## P0 — Foundational COM runtime (must pass first)

- [ ] **AOT publish succeeds** and produces a native exe; `coreclr.dll` is absent (true AOT, not self-contained).
- [ ] **No `InvalidCastException` anywhere at runtime.** The whole backend uses indexed `Materialize<T>` instead of `foreach` over projected collections (the spike's AOT rule). Exercise search/list/upgrades/show and confirm none throw the spike's original cast error.
- [ ] **Default backend = COM** on the Windows build with no flags (not CLI). Sanity: search is fast/structured, IDs are never truncated with `…`.
- [ ] **Flag selection** works: `--cli`, `--com`, `--mock` pick the right backend; **`--cli` wins when both `--cli` and `--com` are passed** (precedence `--mock > --cli > --com > default`).
- [ ] **Search** (`/`) returns real catalog results with version + source columns.
- [ ] **Installed** tab lists installed packages with correct installed versions.
- [ ] **Upgrades** tab shows only packages with an available update, with the Available column populated.
- [ ] **Details** panel: selecting a row fetches metadata (publisher, description, homepage, license, release-notes URL).
- [ ] **Source filter** (`f`) cycles All / winget / msstore and re-queries correctly.

## P1 — Operations + the two new features

Operations (pick a small, safe package to install/uninstall, e.g. a CLI tool):

- [ ] **Install** (`i`) via COM succeeds; status shows result; reboot-required note appears when applicable.
- [ ] **Install specific version** (`I`) resolves the chosen version (`PackageVersionId` path).
- [ ] **Upgrade** (`u`) works; a forced failure says **"Upgrade failed"** (not "Install failed").
- [ ] **Uninstall** (`x`) works.
- [ ] **Batch upgrade** (Upgrades tab → space to select → `U`) runs sequentially with per-item status.

**Live progress bar** (the headline feature — also tests `.Progress` delegate marshaling under AOT, the one CCW-callback unknown):

- [ ] During a real COM **install**, the status bar shows a determinate bar that **advances**, moving through **Downloading → Installing** phases with changing percentages (not stuck at 0%/100%).
- [ ] During an **uninstall**, the phase reads **"Uninstalling"** (not "Installing").
- [ ] Progress callbacks don't crash under AOT (the managed→native delegate CCW works — same path the spike's awaited `ConnectAsync` exercised).

**Cancellation** (`Esc`):

- [ ] **Esc during an install cancels it** cooperatively (COM `Cancel()`): status shows "Cancelling…" then **"Cancelled"**, and the list refreshes.
- [ ] **Esc with no op running** still quits the app (unchanged behavior).
- [ ] `q` and `Ctrl+C` **still quit** during an op (only `Esc` cancels).
- [ ] **Batch upgrade + Esc**: the in-flight item cancels and the remaining queue stops.
- [ ] **One-op-at-a-time guard**: triggering a second operation while one is running is ignored (no second progress bar, no crash).

## P2 — Review-flagged real-Windows concerns & measurements

- [ ] **Shared `PackageManager` thread-agility.** The backend reuses one `PackageManager` across operations invoked from background/threadpool (MTA) threads. Watch for `RPC_E_WRONG_THREAD` or intermittent COM errors under rapid search/typing or back-to-back ops. **If seen → switch to a fresh `PackageManager` per operation** (currently shared as a perf choice). *(Open from review pass 2.)*
- [ ] **Unhealthy source + `All`.** Disable/break the `msstore` source, then do a default (All) search. Does the all-or-nothing composite connect fail the whole query? Confirm the documented workaround — pressing `f` to narrow to winget-only — recovers. *(Open from review pass 1.)*
- [ ] **Pinning on the COM backend.** Pin (`p`), unpin, and pin annotations (📌) work — these delegate to `winget.exe`, so they need winget on PATH even on the COM backend. Confirm pin state shows in Installed/Upgrades and pin/unpin succeed.
- [ ] **Same-id-across-catalogs** (rare): if a package id exists in multiple sources, operations resolve the first match. Only worth checking if you hit an odd case.
- [ ] **CLI-backend cancel** (`--cli`, then Esc mid-install): confirm it stops watching but does **not** kill `winget.exe` (the install continues) — documented, lower priority.
- [ ] **Measure the AOT binary size** of the COM (Windows) build and compare to the CLI/mock build, to budget the COM backend's cost. *(Open spike question.)*
- [ ] **(Optional) win-arm64**: repeat the P0 smoke on an arm64 host or arm64 cross-target.

---

### Notes
- Spike repro for the COM-in-AOT mechanics lives in `spikes/ComBackendSpike/` (`Run-AotSpike.ps1`, `SPIKE-RESULTS.md`) — already validated; useful if a low-level COM/AOT question resurfaces.
- This checklist is maintained as the canonical "verify on Windows" list for the COM work; new COM-backend changes that can't be checked from Linux should add an item here.
