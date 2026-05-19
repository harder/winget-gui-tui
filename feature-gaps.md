# Terminal.Gui Feature Gaps vs. shanselman/winget-tui (Rust + Ratatui)

This document tracks parity issues identified while porting `winget-tui` to Terminal.Gui v2.
Each entry is the kind of thing that came up while writing the clone in `Examples/WingetTui/`.
The list is meant to drive Terminal.Gui issues / PRs rather than to be exhaustive.

## A. TableView gaps

### A1. Per-cell foreground color in standard table source
Ratatui's `Cell::new(...).style(...)` lets the upstream app color the selection marker, pin
emoji, source ("winget" in blue vs "msstore" in amber), and version cells independently.
Terminal.Gui's `TableView` exposes `CellColorGetter`/`RowColorGetter` only via `ColumnStyle`,
and they return a whole `Scheme`, not a per-grapheme attribute. Inline coloring of mixed-style
text within a cell (e.g. a pinned package showing 📌 in accent color followed by the name in
normal color) requires writing a custom `ITableSource` + drawing override.

> Suggested: add a per-cell `Attribute[]` representation (or accept a `Markup` string) so
> hosts can paint partial-cell styles without subclassing TableView.

### A2. No built-in column sort indicator
Upstream renders `↑` / `↓` next to the sorted column header by reconstructing the header
string. Terminal.Gui has no first-class "sort by column" concept on `TableView`. We do the
same string-suffix hack here, but a `ColumnStyle.SortIndicator` (with click-to-sort) is the
natural fix.

### A3. Header click → sort
Ratatui sample dispatches header-region clicks to cycle the sort column. Terminal.Gui's
`TableView` does not raise a click event scoped to the header row; you have to override
`OnMouseEvent` and compute column boundaries yourself.

### A4. Multi-select markers (checkbox column)
Upstream renders `[x]` / `[ ]` prefixes in the Name column for batch selection. Terminal.Gui
has `CheckBoxTableSourceWrapper`, but it commandeers an entire leading column and re-flows
selection semantics. A "multi-select set" overlaid on existing rows (without taking column 0)
would be more useful.

### A5. Pin/state glyph rendering width
Ratatui counts emoji 📌 as width 2 cells and aligns columns accordingly. Terminal.Gui's
table column widths reserve `ColumnStyle.MaxWidth` in display cells, but the table source
returns a `string`/object — measurement happens via `string.GetColumns()` only on render,
which can desync from manually computed widths if you split cells yourself.

### A6. `ColumnStyle.RepresentationGetter` lacks row context
The lambda receives only the cell value (`Func<object, string>`). To prefix the cursor row
with a `●` marker (like upstream does for accessibility / color-blind users), we had to
write a custom `ITableSource` (`MarkedTableSource`) that injects a column-0 marker and track
the cursor row externally via `ValueChanged`. A `Func<CellRepresentationArgs, string>`
overload exposing `RowIndex`, `ColumnIndex`, and `IsCursorRow` would have made this a
one-liner without a wrapper type.

### A7. No `TableStyle.HeaderColorGetter` (only per-column)
`ColumnStyle.HeaderColorGetter` exists for per-column overrides, but the very common case
— "render every header bold accent" — has to be applied by looping over every column after
the table source is set, in every refresh. A table-level `HeaderColorGetter` fallback
(consulted when the column-level one is null) would make this trivial and would let users
set header style once at construction.

## B. Layout / styling gaps

### B1. Tabs widget API mismatch
`Terminal.Gui.Views.Tabs` is **focus-driven** — the focused subview is the active tab. That
clashes with Ratatui-style tabs where the active tab is a logical state independent of focus
(you want focus to stay on the package list while the active tab is "Installed"). We had to
hand-roll a `TabBar` widget for winget-tui-style behavior.

> Suggested: a `TabSelector` (or `Tabs.Mode = Active|Focused`) for headless tabs that drive
> a *content swap* in another view rather than swapping focus.

### B2. View `Border.SchemeName` not assignable
You can pass `BorderStyle = LineStyle.Rounded` and the border picks up the parent view's
scheme — there is no `Border.SchemeName` property to tint borders independently. Upstream
focused vs unfocused panels have different border colors; we'd need to override the
adornment's draw to mirror that.

### B3. No semantic "FrameView with title-aligned right text"
Upstream's panel title is `" Installed (42) • 📌 only "` with the count right-aligned. The
`FrameView.Title` API is a single string with `TitleAlignment` — no inline split.

### B4. No first-class status bar with right-aligned shortcut hints
We wrote `WingetStatusBar` from scratch. Terminal.Gui has `StatusBar` and `Shortcut`, but
they assume each shortcut is a focusable widget tied to a Command. The Ratatui status bar is
purely informational with colored badges (filter, pin, message, hints) and right-alignment
fall-off. Building that requires custom drawing.

### B5. Inline color "badge" (chip) primitive missing
Source-filter / pin-filter chips ( ` Winget `, ` 📌 only `) are everywhere in the app. A
`Chip` / `Badge` widget that renders padded text with a Scheme would dedupe ~80 LOC across
status bar, panel titles, and detail panel.

## C. Input gaps

### C1. Modifier-aware single keystroke dispatch
The Rust app distinguishes `p` vs `P` purely by the `KeyEvent.code`. In Terminal.Gui, `KeyCode.P`
plus `ShiftMask` is correct, but the routing through `KeyDown` mixes the rune (via
`Key.AsRune.Value`) with the `KeyCode` enum — we ended up using `Key.AsRune.Value` for the
letter actions because it's the only reliable case-sensitive path. A dedicated
`Key.Character` property (case-correct) would help.

### C2. Vim navigation aliases (`j`/`k`)
The upstream binds `j`/`k` *in addition to* arrow keys for list navigation. Terminal.Gui's
`TableView` only takes arrow keys / `Command.Up`/`Down` — `j`/`k` either need
`AddKeyBinding (Key.J, Command.Down)` plumbing on the table itself or a top-level keyboard
hook that mutates `TableView.Value`. We left this as a TODO in the port.

### C3. `Esc` ambiguity
Ratatui's input handler decides whether `Esc` clears a filter, closes the help overlay, or
quits, based on a single state machine. Terminal.Gui routes Esc to focus-up navigation and
modal-dismiss by default; suppressing those defaults across nested views (TextField → list →
window) is non-trivial.

## D. Async + threading

### D1. No bundled tokio-style channel for backend results
The Rust app uses a `tokio::mpsc` channel and drains it inside the render loop. Terminal.Gui's
equivalent is `Application.Invoke(Action)` which puts work on the UI thread. That's fine, but
there's no built-in "generation counter" pattern for cancelling stale fetches; we re-implemented
it in `AppState`. A `Task<T>` extension `WithGeneration(Func<bool>)` or `IRunnable.RunBound`
helper would prevent the foot-gun.

### D2. `IApplication.Invoke` re-entrance guarantees
We rely on `App?.Invoke(...)` from background threads. The documentation is light on whether
nested Invokes from inside an existing Invoke are safe / queued / synchronous. For an app
this size it works, but a clearer contract (e.g. "always queued to next iteration") would
ease porting from Ratatui's pull model.

## E. Performance hypotheses to validate

These are unmeasured but were noticeable while building the port — they're the items to put
under a benchmark:

1. **Full table redraw on filter change.** Ratatui re-renders only changed rows. Terminal.Gui's
   `TableView.Update()` invalidates the whole content area. With 5,000 installed packages and
   active sort, scroll responsiveness should be measured side by side.
2. **Status-bar spinner timer at 100 ms.** We invalidate the entire status bar each tick.
   Ratatui only repaints the spinner glyph cell. Worth measuring CPU at idle.
3. **Process+parse cost.** Both apps spend most time inside `winget`. Make sure the .NET
   `Process` invocation doesn't add startup overhead vs Rust's `std::process::Command`.
4. **Mouse-hit testing in TabBar.** Hand-rolled linear scan; cheap, but compare to whatever
   Ratatui does in its tabs widget.
5. **Emoji / wide-grapheme rendering.** Each cell with 📌 forces a `string.GetColumns()` call
   per draw. Cache column widths per row in `EnumerableTableSource`?

## F. Missing features in this port (not gaps in Terminal.Gui)

For honesty about parity:

- Pin-state introspection from `winget pin list` is parsed but pin metadata for `Blocking`
  vs `Gating(version)` is simplified to `Blocking`.
- The footer-after-table pattern in `winget upgrade --include-pinned` is parsed only for the
  primary table; pinned-only secondary table is not surfaced as a separate section.
- No persisted config (`%APPDATA%\winget-tui\config.toml`) yet.
- Detail-panel "stub fallback" for ARP/MSIX/truncated packages is not implemented.
- Scrollable details — we set `TextView` with scrolling, but the upstream's explicit "scroll
  detail" key (PgUp/PgDn while detail-focused) isn't bound to our text view's commands yet.

## G. Wishlist items that would have made the port shorter

- **`View.SetBorderScheme(Scheme)` / `Border.Scheme`** — for tinting frames.
- **`Markup`-like rich-text spans** in `Label`/`TextView` so we can colorize the detail panel
  inline (link text in `Info`, accent labels, etc.) without painting graphemes by hand.
- **`Chip` / `Badge` widget** as a one-liner.
- **A "headless tabs" widget** (active tab tracked independently of focus, with click
  handlers per tab).
- **`TableView.OnHeaderClicked(int column)`** + built-in sort cycling.
- **`Key.Character` / `KeySymbol`** abstraction that distinguishes `p` from `P` cleanly.
- **Cancellation token aware `App.Invoke`** that wraps a generation guard.
