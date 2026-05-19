# winget-gui-tui

> ⚠️ **Proof of concept — for evaluation only.** This project exists to **benchmark Terminal.Gui v2 against Ratatui**: feature parity, rendering fidelity, performance, and UX. It is not a finished application, is not on a release track, and is not intended for daily use. Run it on a Windows machine with `winget` only if you understand that **install / uninstall / upgrade actions invoke the real `winget` CLI** and will operate on your real package state.

A C# / [Terminal.Gui](https://github.com/gui-cs/Terminal.Gui) v2 reimplementation of [shanselman/winget-tui](https://github.com/shanselman/winget-tui) — a Rust + Ratatui TUI for the Windows Package Manager.

## Origin & attribution

This is a from-scratch C# / Terminal.Gui v2 port of [**shanselman/winget-tui**](https://github.com/shanselman/winget-tui) — Scott Hanselman's Rust + Ratatui TUI for the Windows Package Manager. Copyright © Scott Hanselman, MIT-licensed.

UI layout, keybindings, color palette, table structure, winget output parsing, dedupe / pin-state / locale handling, and the "Found `<name>` [`<id>`]" detail-header convention all follow the [upstream source](https://github.com/shanselman/winget-tui/tree/main/src). **No upstream code was copied** — the upstream served as the behavioral and visual specification.

Differences between the two implementations — including Terminal.Gui feature gaps surfaced while porting — are documented in [feature-gaps.md](feature-gaps.md).

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
| Sort cycling (`S`) — None → Name↑↓ → Id↑↓ → Version↑↓ | ✅ |
| Install / Install-version / Uninstall / Upgrade / Pin | ✅ (no `--exact` to match upstream behavior) |
| Pin states distinguished: Pinned / Blocking / Gating(version) | ✅ |
| Batch-select (Space / `a`) and batch upgrade (`U`) | ✅ |
| Confirm dialog, version-input dialog, help overlay | ✅ |
| CSV export (`e`) | ✅ |
| Open homepage (`o`) / changelog (`c`) | ✅ |
| Refresh (`r`) with cursor-anchor by package id | ✅ |
| Vim navigation (`j`/`k`) + arrow / PgUp / PgDn / Home / End | ✅ |
| Navigation while filter input has focus | ✅ |
| Truncation guard for ops on `…`-suffixed ids | ✅ |
| Focus-driven border weight: Heavy when focused, Rounded when not | ✅ |
| Rich-text detail panel: inline span styling, accent label, info-blue URLs | ✅ (via direct drawing — Terminal.Gui doesn't ship a span primitive) |
| CJK / display-width column slicing | ✅ |
| Bracketed-paste support on search/version inputs | ✅ (via Terminal.Gui v2 paste pipeline) |
| Warm-amber theme matching upstream `theme.rs` palette | ✅ |
| Mock backend for non-Windows hosts | ✅ |
| Native AOT — standalone exe, no .NET runtime needed | ✅ |

## Building

`winget` itself is Windows-only, so the deployed target is Windows. The build uses **.NET
Native AOT** to produce a single standalone `.exe` (~10–15 MB) that runs without `dotnet`
installed on the target machine — matching the upstream Rust binary's deployment story.

### Prerequisites

- **.NET 10 SDK** ([install](https://dot.net))
- **Visual Studio Build Tools** with the *Desktop development with C++* workload
  (the AOT linker uses MSVC's `link.exe` and the Windows SDK)

### Build the standalone executable

```powershell
dotnet publish -c Release -r win-x64
.\bin\Release\net10.0\win-x64\publish\winget-tui-gui.exe
```

Copy `winget-tui-gui.exe` anywhere — no other files required.

### Dev iteration (Windows or any host, slower start)

For iterating on the code, `dotnet run` is faster than re-publishing AOT each time, but
requires the .NET 10 runtime on the machine running it. On non-Windows hosts there's no
`winget` to invoke, so use `--mock`:

```bash
dotnet run                  # Windows: hits real winget
dotnet run -- --mock        # any host: mock backend, useful for UI development
```

### Run the test suite

```bash
dotnet test tests/WingetTui.Tests.csproj
```

The xUnit suite under `tests/` covers the parser pipeline — table parsing, ANSI/CR handling,
display-width column slicing, dedupe, `winget show` parsing including locale variants,
pin-state precedence, version comparison, and PackageDetail merging. Every test is anchored
to a real bug found during development; 43 tests, runs in <1 second.

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
| `↑`/`k`, `↓`/`j` | Move selection |
| `←`/`→` | Switch tab |
| `1` / `2` / `3` | Jump to Search / Installed / Upgrades |
| `Tab` / `Shift+Tab` | Toggle focus between list and detail |
| `PgUp` / `PgDn`, `Home` / `End` | Page navigation |
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

## Project layout

```
winget-gui-tui/
├── Program.cs               # Entry point + winget-detection + --dump diagnostic
├── WingetTui.csproj         # PackageReference on Terminal.Gui; AOT-configured
├── README.md
├── LICENSE                  # MIT
├── feature-gaps.md          # Terminal.Gui parity findings vs upstream
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

9 source files in `src/` matching the upstream Rust project's `src/` layout. ~3,800 LOC total
(upstream is ~3,500 logical lines across 9 `.rs` files).

## Status & roadmap

This is a POC. Things known to be unfinished or different from upstream are listed in [feature-gaps.md](feature-gaps.md). Not under active development beyond what's needed for parity benchmarking. PRs that close parity gaps are welcome.

Things explicitly **out of scope**:

- Configuration file support (`%APPDATA%\winget-tui\config.toml`)
- Distribution / packaging (Microsoft Store, MSIX, signed releases)

## Related

- **Upstream**: [shanselman/winget-tui](https://github.com/shanselman/winget-tui) (Rust + Ratatui)
- **Terminal.Gui v2**: [gui-cs/Terminal.Gui](https://github.com/gui-cs/Terminal.Gui)
- **winget**: [microsoft/winget-cli](https://github.com/microsoft/winget-cli)
