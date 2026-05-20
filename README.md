# winget-gui-tui


> ⚠️ **Proof of concept** This project exists to **benchmark [Terminal.Gui](https://github.com/gui-cs/Terminal.Gui) v2 against Ratatui**: feature parity, rendering fidelity, performance, and UX. However it is fully operational: Run it on a Windows machine only if you understand that **install / uninstall / upgrade actions invoke the real `winget` CLI** and will operate on your real package state.

A C# / [Terminal.Gui](https://github.com/gui-cs/Terminal.Gui) v2 reimplementation of the wonderful [winget-tui](https://github.com/shanselman/winget-tui) - a Rust + Ratatui based TUI for the [Windows Package Manager (winget)](https://github.com/microsoft/winget-cli).
This application shows what is possible with a .NET terminal UI. Release binaries are Native AOT and self-contained. You do NOT need the .NET runtime to use them.

[![C#](https://img.shields.io/badge/C%23-239120?style=flat&logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?style=flat&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)
[![Terminal.Gui](https://img.shields.io/badge/Terminal.Gui-v2-FF6F00?style=flat&logo=windowsterminal&logoColor=white)](https://github.com/gui-cs/Terminal.Gui)
[![Windows](https://img.shields.io/badge/Windows-x64%20%7C%20arm64-0078D4?style=flat&logo=windows&logoColor=white)](https://www.microsoft.com/windows)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow?style=flat)](LICENSE)

[![CI](https://github.com/harder/winget-gui-tui/actions/workflows/ci.yml/badge.svg)](https://github.com/harder/winget-gui-tui/actions/workflows/ci.yml)
[![Release](https://github.com/harder/winget-gui-tui/actions/workflows/release.yml/badge.svg)](https://github.com/harder/winget-gui-tui/actions/workflows/release.yml)

## Origin & attribution

This is a from-scratch C# / Terminal.Gui port of [**shanselman/winget-tui**](https://github.com/shanselman/winget-tui): Scott Hanselman's Rust + Ratatui TUI for the Windows Package Manager. Winget-tui is copyright © Scott Hanselman, MIT-licensed.

UI layout, keybindings, color palette, table structure, winget output parsing, dedupe / pin-state / locale handling, and the "Found `<name>` [`<id>`]" detail-header convention all follow the [upstream source](https://github.com/shanselman/winget-tui/tree/main/src). **No upstream code was copied** - the upstream served as the behavioral and visual specification.

Differences between the two implementations, including Terminal.Gui feature gaps surfaced while porting, are documented in [feature-gaps.md](feature-gaps.md).

This port is also MIT-licensed; see [LICENSE](LICENSE).

## What's in the box

| Area | Status |
|------|--------|
| Three-tab UI (Search / Installed / Upgrades) | ✅ |
| Pixel-art logo + tab bar header | ✅ (3-row half-block art, mouse-clickable tabs) |
| Package list table (Name, Id, Version, Source / Available) | ✅ |
| Detail panel: publisher, description, homepage, changelog, license | ✅ |
| Status bar: source filter, pin filter, hotkey hints, spinner | ✅ |
| Search mode (`/` or `s`) with deferred backend search | ✅ |
| Local filter for Installed / Upgrades (auto-cleared on view switch) | ✅ |
| Source filter cycling (`f`) | ✅ |
| Pin filter cycling (`P`) | ✅ |
| Sort cycling (`S`) - None → Name↑↓ → Id↑↓ → Version↑↓ | ✅ |
| Install / Install-version / Uninstall / Upgrade / Pin | ✅ (no `--exact` to match upstream behavior) |
| Pin states distinguished: Pinned / Blocking / Gating(version) | ✅ |
| Batch-select (Space / `a`) and batch upgrade (`U`) | ✅ |
| Confirm dialog, version-input dialog, help overlay | ✅ |
| CSV export (`e`) | ✅ |
| Open homepage (`o`) / changelog (`c`) | ✅ |
| Refresh (`r`) with cursor-anchor by package id | ✅ |
| Vim navigation (`j`/`k`) + arrow / PgUp / PgDn / Home / End | ✅ (detail pane scrolls when it has focus) |
| Navigation while filter input has focus | ✅ |
| Truncation guard for ops on `…`-suffixed ids | ✅ |
| Focus-driven border weight: Heavy when focused, Rounded when not | ✅ |
| Rich-text detail panel: inline span styling, accent label, info-blue URLs | ✅ (via direct drawing - Terminal.Gui doesn't ship a span primitive) |
| CJK / display-width column slicing | ✅ |
| Bracketed-paste support on search/version inputs | ✅ (via Terminal.Gui v2 paste pipeline) |
| Warm-amber theme matching upstream `theme.rs` palette | ✅ |
| Mock backend for non-Windows hosts | ✅ |
| Native AOT standalone exe, no .NET runtime needed | ✅ |

## Building

`winget` itself is Windows-only, so the deployed target is Windows. The build uses **.NET Native AOT** to produce a single standalone `.exe` (~10–15 MB) that runs without `dotnet` installed on the target machine.

### Prerequisites

You must build on a **Windows host** - Native AOT does not support cross-OS publish (a Linux/WSL `dotnet publish` for `win-x64` or `win-arm64` will fail with "Cross-OS native compilation is not supported"). Architecture cross-targeting *between* the two Windows RIDs works fine, so you don't need an ARM machine to produce an arm64 build.

- **Windows host** (any architecture, see arch cross-compile note below)
- **.NET 10 SDK** ([install](https://dot.net))
- **Visual Studio Build Tools** with the *Desktop development with C++* workload
  (the AOT linker uses MSVC's `link.exe` and the Windows SDK).
- To produce an **arm64** binary, also install the **MSVC v143 - VS 2022 C++ ARM64 build tools** component (works from x64 or arm64 hosts).

### Build the standalone executable

The architecture you build for must match where the binary will run:

| Target Windows machine | Command |
|------------------------|---------|
| Intel / AMD x64 (most Windows PCs) | `dotnet publish -c Release -r win-x64` |
| ARM64 (Surface Pro X, Snapdragon Copilot+ PCs, Windows Dev Kit) | `dotnet publish -c Release -r win-arm64` |

```powershell
# x64 (Intel/AMD)
dotnet publish -c Release -r win-x64
.\bin\Release\net10.0\win-x64\publish\winget-tui-gui.exe

# arm64
dotnet publish -c Release -r win-arm64
.\bin\Release\net10.0\win-arm64\publish\winget-tui-gui.exe
```

**Cross-architecture compile** (`x64 → arm64` or `arm64 → x64`) works on Windows as long
as the matching VS C++ build tools component is installed. Building on Windows arm64
produces an arm64 exe that runs natively (no x64 emulation).

Copy `winget-tui-gui.exe` anywhere, no other files required.

### Dev iteration on any host (including WSL / macOS / Linux)

For iterating on the code, `dotnet run` is faster than re-publishing AOT each time, and unlike the AOT publish it works on any OS - handy for hacking on the UI from WSL. There's no `winget` to invoke on non-Windows hosts, so use `--mock`:

```bash
dotnet run                  # Windows: hits real winget
dotnet run -- --mock        # any host: mock backend, useful for UI development
```

### Run the test suite

```bash
dotnet test tests/WingetTui.Tests.csproj
```

The xUnit suite under `tests/` covers:

- **Parser pipeline** - table parsing, ANSI/CR handling, display-width column slicing for
  CJK, dedupe with version-first preference, footer stop and secondary-table parsing,
  bad-id rejection, store product ids, ARP\Machine\… ids, truncated ids, digit-prefixed
  package names.
- **`winget show`** - Found-line extraction, locale-independent prefix (German `Gefunden`),
  multi-line description continuation, German keys, bracketed release-notes don't hijack
  the Found-line detector, homepage / publisher_url fallback, release-notes-url
  extraction.
- **CLI argument construction** - install/upgrade-by-id don't include `--exact`,
  upgrade-by-name does, pin add uses `--blocking`, pin remove avoids `--installed`,
  upgrade includes `--include-pinned`, list doesn't.
- **Pin state precedence** - Blocking trumps all, Gating(version), `"latest"` is Pinned
  not Gating, empty inputs degrade to None.
- **Models** - `Package.IsTruncated`, `PinState.DisplayLabel`,
  `PackageDetail.MergeContext`, `EnsureDetailHint`.
- **Version comparison** - numeric vs lexical, longer-prefix-wins, empty handling.
- **Terminal.Gui compatibility** - `Theme.Register` round-trip, every named scheme
  resolves, `Rune.GetColumns()` returns 2 for CJK and 1 for ASCII, `string.GetColumns()`
  walks grapheme clusters correctly, `Logo` instantiates with expected dimensions,
  `TabBar` reports clicks via `TabClicked`, `MarkedTableSource` nested type still exists.
  These catch breakages on Terminal.Gui version upgrades.

Every test is anchored to a real bug found during development or a Terminal.Gui surface
we depend on; **88 tests**, runs in <1 second.

### Diagnose winget parser issues at runtime

The `--dump` mode invokes winget and prints the raw output plus a parser trace. Useful
when real `winget` output doesn't match what the parser expects:

```powershell
winget-tui-gui.exe --dump search vscode
winget-tui-gui.exe --dump list
winget-tui-gui.exe --dump upgrade
winget-tui-gui.exe --dump show --id Microsoft.VisualStudioCode --exact
```

## Keybindings

Mirrors `src/handler.rs` in the upstream:

| Key | Action |
|-----|--------|
| `/` or `s` | Search (Search tab) / local filter |
| `↑`/`k`, `↓`/`j` | Move selection, or scroll the detail pane when it has focus |
| `←`/`→` | Switch tab |
| `1` / `2` / `3` | Jump to Search / Installed / Upgrades |
| `Tab` / `Shift+Tab` | Toggle focus between list and detail |
| `PgUp` / `PgDn`, `Home` / `End` | Page navigation, or page/start/end scroll in the detail pane when it has focus |
| `f` | Cycle source filter (All / Winget / MsStore) |
| `P` | Cycle pin filter |
| `S` | Cycle sort column / direction |
| `r` | Refresh (preserves selection by id) |
| `e` | Export visible list to CSV |
| `i` | Install |
| `I` | Install specific version |
| `u` | Upgrade |
| `U` | Batch upgrade |
| `x` | Uninstall |
| `p` | Pin / unpin |
| `Space` | Toggle batch select (Upgrades) |
| `a` | Toggle select-all (Upgrades) |
| `o` | Open homepage |
| `c` | Open changelog |
| `?` | Toggle help |
| `q` / `Esc` / `Ctrl+C` | Quit |

## Architecture

```
                    ┌──────────┐
                    │   user   │  keyboard, mouse, paste
                    └─────┬────┘
                          ▼
   ┌─────────────────────────────────────────────────────────────┐
   │                            App                              │
   │  ┌────┐ ┌─────────┐  ┌──────────────┐  ┌─────────────────┐  │
   │  │Logo│ │ TabBar  │  │ PackageList  │  │  DetailPanel    │  │
   │  └────┘ └─────────┘  │ (TableView + │  │  (direct-draw   │  │
   │                      │  MarkedTable │  │   span model)   │  │
   │  ┌────────────────┐  │  Source)     │  │                 │  │
   │  │   StatusBar    │  └──────────────┘  └─────────────────┘  │
   │  └────────────────┘  ┌──────────────────────────────────┐   │
   │                      │  Modals: HelpDialog, VersionInput│   │
   │                      └──────────────────────────────────┘   │
   └────────────────────────────────┬────────────────────────────┘
                                    │ reads / mutates
                                    ▼
   ┌─────────────────────────────────────────────────────────────┐
   │                          AppState                           │
   │  Mode (Search/Installed/Upgrades)                           │
   │  Filtered packages, cursor, batch selection                 │
   │  Source filter, pin filter, sort field/dir, local filter    │
   │  DetailCache, view_generation, detail_generation            │
   └────────────────────────────────┬────────────────────────────┘
                                    │ async (CancellationToken,
                                    │        generation guard)
                                    ▼
   ┌─────────────────────────────────────────────────────────────┐
   │                         IBackend                            │
   │   Search · ListInstalled · ListUpgrades · Show              │
   │   Install · Uninstall · Upgrade · Pin · Unpin · ListPins    │
   └─────────────┬───────────────────────────────────┬───────────┘
                 ▼                                   ▼
   ┌──────────────────────────────┐   ┌──────────────────────────┐
   │  CliBackend  (Windows)       │   │  MockBackend  (--mock)   │
   │  ParseTable / ParseShow /    │   │  in-memory fixtures so   │
   │  ParsePins / dedupe /        │   │  the UI runs on any host │
   │  display-width column slice  │   │  for development         │
   └──────────────┬───────────────┘   └──────────────────────────┘
                  ▼
   ┌─────────────────────────────────────────┐
   │   winget.exe  (system, Windows-only)    │
   └─────────────────────────────────────────┘
```

Three layers, top to bottom: **UI** (`App` owns all the widgets in `Ui.cs` plus the
list/detail), **state** (`AppState` is the single source of truth for what's filtered
and selected, with generation counters that invalidate stale async responses), and
**backend** (`IBackend` interface, two implementations). Async results from the
backend flow back through `App.Invoke` on the UI thread, where they pass through
the generation guard before mutating `AppState` and triggering a redraw.

## Project layout

```
winget-gui-tui/
├── Program.cs               # Entry point + winget-detection + --dump diagnostic
├── WingetTui.csproj         # PackageReference on Terminal.Gui; AOT-configured
├── README.md
├── LICENSE                  # MIT
├── feature-gaps.md          # Terminal.Gui parity findings vs upstream
├── code-signing.md          # Code-signing options researched but not adopted (POC)
├── src/
│   ├── GlobalUsings.cs      # Centralized using directives
│   ├── Models.cs            # Package, PackageDetail, enums, OpResult
│   ├── Backend.cs           # IBackend interface
│   ├── CliBackend.cs        # Shells out to winget; parses table output
│   ├── MockBackend.cs       # Fake packages so the UI runs anywhere
│   ├── AppState.cs          # Filters, sort, selection, generation counters
│   ├── Theme.cs             # Warm-amber palette + Schemes + pixel-art Logo
│   ├── Ui.cs                # TabBar, StatusBar, DetailPanel, Dialogs (widgets)
│   └── App.cs               # Main Runnable; state coordination; nested MarkedTableSource
└── tests/
    ├── WingetTui.Tests.csproj
    └── ParserTests.cs       # xUnit suite covering the parser pipeline
```

## Status & roadmap

This is a POC. Things known to be unfinished or different from upstream are listed in [feature-gaps.md](feature-gaps.md). Terminal.Gui is under active development and this application will be upated periodically to reflect improvements, fixes, and new features in that library. PRs that close parity gaps are welcome.

Things explicitly **out of scope**:

- Configuration file support (`%APPDATA%\winget-tui\config.toml`)

## Releases

Tag-driven via `.github/workflows/release.yml`:

```bash
git tag v0.1.0
git push origin v0.1.0
```

Builds AOT executables for `win-x64` and `win-arm64`, packages each as both a bare `.exe` and a portable `.zip` bundle (exe + README + LICENSE + feature-gaps), computes SHA-256 checksums, and publishes a GitHub Release with all artifacts attached.

Manual dispatch is also available from the Actions tab (provide a `version` input).

### Code signing

The released binaries are **not code-signed** yet. This POC doesn't have a Azure Trusted Signing subscription set up, so users will see a Microsoft Defender SmartScreen warning on first run. See [code-signing.md](code-signing.md) for the full breakdown of options researched (Azure Trusted Signing, SignPath.io OSS sponsorship, EV cert via Azure Key Vault, GitHub Attestations) and which I'd adopt first if this graduates from POC.

**Workaround for users on the unsigned binary:**

```powershell
Unblock-File -Path .\winget-tui-gui.exe
```

Or right-click the exe → *Properties* → check *Unblock* → *OK*. On the first run after unblocking, click *More info → Run anyway* and SmartScreen will remember the decision.

## Contributing

Contributions welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).

## Related

- **Upstream**: [shanselman/winget-tui](https://github.com/shanselman/winget-tui) (Rust + Ratatui)
- **Terminal.Gui v2**: [gui-cs/Terminal.Gui](https://github.com/gui-cs/Terminal.Gui)
- **winget**: [microsoft/winget-cli](https://github.com/microsoft/winget-cli)
