# Contributing

This is a **proof-of-concept** project benchmarking Terminal.Gui v2 against Ratatui via a port of [shanselman/winget-tui](https://github.com/shanselman/winget-tui). Contributions that close parity gaps against upstream, fix bugs in the existing surface, or sharpen the test suite are welcome. New features beyond what upstream does are usually out of scope — see [README § out of scope](README.md#status--roadmap).

## Dev setup

```bash
git clone https://github.com/harder/winget-gui-tui
cd winget-gui-tui
dotnet test tests/WingetTui.Tests.csproj   # 73 tests, <1s
dotnet run -- --mock                       # UI iteration, any host
```

Building the actual AOT binary requires a **Windows host** with Visual Studio Build Tools (C++ workload). See [README § Building](README.md#building).

## Working on a change

1. **Add a test first** when the change touches parser behavior, model semantics, or anything covered by `tests/ParserTests.cs`. Every existing test is anchored to a real bug — please keep that pattern.
2. **Compare against upstream** when changing winget parsing logic. The Rust source at <https://github.com/shanselman/winget-tui/tree/main/src> is the behavioral spec. Note divergences in [feature-gaps.md](feature-gaps.md).
3. **Run the suite** before opening a PR: `dotnet test`.
4. **Don't expand scope.** Configuration files, packaging, distribution, signing — all out of scope. Focus is parity benchmarking.

## Filing issues

- **Bugs**: include the failing scenario, OS + architecture (x64 vs arm64), and where possible a `--dump` trace (e.g. `winget-tui-gui --dump search vscode > dump.txt`).
- **Parity gaps**: link to the upstream Rust code that does it differently.
- **Terminal.Gui regressions**: include the version you upgraded from and to. The Terminal.Gui compatibility tests in `tests/ParserTests.cs` should ideally catch these — if a regression slipped through, an extra test for it is highly welcome.

## Code style

Mostly follow standard C# / .NET conventions. The project loosely mirrors [Terminal.Gui's style](https://github.com/gui-cs/Terminal.Gui/blob/develop/.claude/rules/formatting.md) — notable points:

- Space before parens: `Method ()`, `array [i]`, `if (...)`.
- Braces on next line (Allman style).
- `var` only for built-in types (`int`, `string`, `bool`, etc.). Explicit type for everything else.
- Blank line before `return` / `break` / `continue`, after control blocks.

These aren't CI-enforced for a POC; just match the surrounding code.

## License

By contributing you agree your work is licensed under the project's [MIT license](LICENSE).
