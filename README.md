# winget-gui-tui

> ⚠️ **Proof of concept — for evaluation only.** This project exists to **benchmark Terminal.Gui v2 against Ratatui**: feature parity, rendering fidelity, performance, and UX. It is not a finished application, is not on a release track, and is not intended for daily use. Run it on a Windows machine with `winget` only if you understand that **install / uninstall / upgrade actions invoke the real `winget` CLI** and will operate on your real package state.

A C# / [Terminal.Gui](https://github.com/gui-cs/Terminal.Gui) v2 reimplementation of [shanselman/winget-tui](https://github.com/shanselman/winget-tui) — a Rust + Ratatui TUI for the Windows Package Manager.

## Origin

This is a from-scratch port. UI layout, keybindings, color palette, table structure, winget output parsing, dedupe / pin-state / locale handling, and the "Found `<name>` [`<id>`]" detail-header convention all follow the [upstream Rust source](https://github.com/shanselman/winget-tui/tree/main/src) directly. No upstream code was copied; the upstream served as the specification.

Differences between the two implementations — including Terminal.Gui feature gaps surfaced while porting — are documented in [feature-gaps.md](feature-gaps.md).

License: MIT (see [LICENSE](LICENSE), upstream attribution in [NOTICE](NOTICE)).

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

## Running

On Windows 10 (21H2+) or Windows 11 with `winget` on `PATH`:

```powershell
dotnet run
```

Force the mock backend (useful on Linux / macOS for parity testing without winget):

```bash
dotnet run -- --mock
```

Diagnose parser issues against real winget output:

```powershell
.\bin\Debug\net10.0\winget-tui-gui.exe --dump search vscode
.\bin\Debug\net10.0\winget-tui-gui.exe --dump list
.\bin\Debug\net10.0\winget-tui-gui.exe --dump upgrade
.\bin\Debug\net10.0\winget-tui-gui.exe --dump show --id Microsoft.VisualStudioCode --exact
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
├── WingetTui.csproj         # PackageReference on Terminal.Gui from NuGet
├── README.md
├── NOTICE                   # Upstream attribution
├── LICENSE                  # MIT
├── feature-gaps.md          # Terminal.Gui parity findings vs upstream
└── src/
    ├── GlobalUsings.cs      # Centralized using directives
    ├── Models.cs            # Package, PackageDetail, enums, OpResult
    ├── IBackend.cs          # Backend interface
    ├── WingetCliBackend.cs  # Shells out to winget; parses table output
    ├── MockBackend.cs       # Fake packages so the UI runs anywhere
    ├── AppState.cs          # Filters, sort, selection, generation counters
    ├── Theme.cs             # Warm-amber palette + Schemes + pixel-art Logo
    ├── TabBar.cs            # Mouse-clickable tab bar
    ├── WingetStatusBar.cs   # Bottom status bar with spinner + chips
    ├── DetailPanel.cs       # Right-side package detail (direct-draw rich text)
    ├── Dialogs.cs           # VersionInputDialog + HelpDialog
    └── WingetTuiWindow.cs   # Main Runnable; nested MarkedTableSource
```

~3,800 LOC. The Rust upstream is comparable in size (~3,500 logical lines across 9 `.rs` files).

## Status & roadmap

This is a POC. Things known to be unfinished or different from upstream are listed in [feature-gaps.md](feature-gaps.md). Not under active development beyond what's needed for parity benchmarking. PRs that close parity gaps are welcome.

Things explicitly **out of scope**:

- Configuration file support (`%APPDATA%\winget-tui\config.toml`)
- Distribution / packaging (Microsoft Store, MSIX, signed releases)
- A test suite beyond `--dump`-style smoke checks

## Related

- **Upstream**: [shanselman/winget-tui](https://github.com/shanselman/winget-tui) (Rust + Ratatui)
- **Terminal.Gui v2**: [gui-cs/Terminal.Gui](https://github.com/gui-cs/Terminal.Gui)
- **winget**: [microsoft/winget-cli](https://github.com/microsoft/winget-cli)
