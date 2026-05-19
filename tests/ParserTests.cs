using Xunit;

namespace WingetTui.Tests;

/// <summary>
/// Unit tests for the CliBackend parsing pipeline. Equivalent to the
/// <c>#[cfg(test)] mod tests</c> block inside upstream's <c>src/cli_backend.rs</c>.
///
/// Every test in here is anchored to a real bug that was found while porting:
/// the bare-CR spinner overwrite that ate the header, the CJK display-width
/// column misalignment that corrupted Chinese package names, the missing
/// "Found &lt;name&gt; [&lt;id&gt;]" handling that left the detail panel empty,
/// and the pin-state collapse that lost Blocking vs Gating distinction.
/// </summary>
public class ParserTests
{
    // ──────────────────────────────────────────────────────────────────────
    // ParseTable — the core table parser
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseTable_ExtractsRowsFromStandardEnglishOutput ()
    {
        const string output = """
            Name                Id                            Version    Source
            -------------------------------------------------------------
            Visual Studio Code  Microsoft.VisualStudioCode    1.95.0     winget
            Git                 Git.Git                       2.46.0     winget
            """;

        IReadOnlyList<Package> rows = CliBackend.ParseTable (output, hasAvailable: false);

        Assert.Equal (2, rows.Count);
        Assert.Equal ("Visual Studio Code", rows [0].Name);
        Assert.Equal ("Microsoft.VisualStudioCode", rows [0].Id);
        Assert.Equal ("1.95.0", rows [0].Version);
        Assert.Equal ("winget", rows [0].Source);
        Assert.Equal ("Git.Git", rows [1].Id);
    }

    [Fact]
    public void ParseTable_HandlesBareCarriageReturnSpinnerOverwrites ()
    {
        // Regression: winget emits a progress spinner that overwrites its line via bare \r
        // (CR without LF). When stdout is captured, those frames pile up *before* the real
        // header. SplitLines must honor the last \r per line so only the final state survives.
        const string output =
            "   -    \r   \\    \r   |    \r   /    \r"
            + "Name                Id                            Version    Source\r\n"
            + "-------------------------------------------------------------\r\n"
            + "Git                 Git.Git                       2.46.0     winget\r\n";

        IReadOnlyList<Package> rows = CliBackend.ParseTable (output, hasAvailable: false);

        Assert.Single (rows);
        Assert.Equal ("Git", rows [0].Name);
        Assert.Equal ("Git.Git", rows [0].Id);
    }

    [Fact]
    public void ParseTable_StripsAnsiCsiSequences ()
    {
        // Some winget builds emit ANSI color codes even when piped. The CSI stripper must
        // remove them before the dash-separator scan, or row data shifts left by 4–7 bytes
        // and SliceColumn lands on the wrong field.
        const string output = """
            Name                Id                            Version    Source
            -------------------------------------------------------------
            \x1B[32mGit\x1B[0m                 Git.Git                       2.46.0     winget
            """;

        string withEsc = output.Replace ("\\x1B", "\x1B");
        IReadOnlyList<Package> rows = CliBackend.ParseTable (withEsc, hasAvailable: false);

        Assert.Single (rows);
        Assert.Equal ("Git", rows [0].Name);
    }

    [Fact]
    public void ParseTable_StopsAtFooterDoesNotSpillIntoSecondaryTable ()
    {
        // winget upgrade --include-pinned appends a second mini-table after the footer.
        // We must stop at the footer and treat the secondary table as a separate parse.
        const string output = """
            Name      Id          Version      Available    Source
            ------------------------------------------------------
            Git       Git.Git     2.46.0       2.47.0       winget
            VS Code   Microsoft   1.94.0       1.95.0       winget
            2 upgrades available.

            Name             Id              Version  Available  Source
            -----------------------------------------------------------
            PinnedApp        Foo.Pinned      1.0      1.1        winget
            """;

        IReadOnlyList<Package> rows = CliBackend.ParseTable (output, hasAvailable: true);

        Assert.Equal (3, rows.Count);
        Assert.Contains (rows, p => p.Id == "Foo.Pinned");
    }

    [Fact]
    public void ParseTable_RejectsRowsWithMalformedIds ()
    {
        // Localized footer text occasionally lands in the id column. Valid ids contain '.',
        // '\', or have no spaces. Reject rows where id has a space but no '.' or '\'.
        const string output = """
            Name              Id                       Version    Source
            -----------------------------------------------------------
            Real Package      Real.Package             1.0.0      winget
            Footer text leak  some footer notice text  whatever   winget
            """;

        IReadOnlyList<Package> rows = CliBackend.ParseTable (output, hasAvailable: false);

        Assert.Single (rows);
        Assert.Equal ("Real.Package", rows [0].Id);
    }

    [Fact]
    public void ParseTable_DropsRowsWithEmptyIds ()
    {
        const string output = """
            Name              Id                       Version    Source
            -----------------------------------------------------------
            Real Package      Real.Package             1.0.0      winget

            Another One       Another.One              2.0.0      winget
            """;

        IReadOnlyList<Package> rows = CliBackend.ParseTable (output, hasAvailable: false);

        Assert.Equal (2, rows.Count);
    }

    [Fact]
    public void ParseTable_HandlesCjkColumnAlignmentByDisplayWidth ()
    {
        // Regression: a Chinese package name like "360极速浏览器" uses 4 chars but ~10
        // display columns. A char-index slice would land in the middle of the next
        // column and corrupt Id/Version. SliceColumn must walk by Rune width.
        const string output =
            "Name                Id           Version    Source\r\n"
            + "------------------------------------------------\r\n"
            + "360极速浏览器          66Chrome     1233.0     winget\r\n"
            + "115浏览器              .115Chrome   0.0        winget\r\n";

        IReadOnlyList<Package> rows = CliBackend.ParseTable (output, hasAvailable: false);

        Assert.Equal (2, rows.Count);
        Assert.Equal ("360极速浏览器", rows [0].Name);
        Assert.Equal ("66Chrome", rows [0].Id);
        Assert.Equal ("1233.0", rows [0].Version);
        Assert.Equal ("115浏览器", rows [1].Name);
        Assert.Equal (".115Chrome", rows [1].Id);
    }

    [Fact]
    public void ParseTable_ReturnsEmptyWhenNoSeparatorFound ()
    {
        Assert.Empty (CliBackend.ParseTable ("garbage output\nwith no dashes", hasAvailable: false));
        Assert.Empty (CliBackend.ParseTable (string.Empty, hasAvailable: false));
    }

    // ──────────────────────────────────────────────────────────────────────
    // ParseShow — winget show output parser
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseShow_ExtractsNameAndIdFromFoundLine ()
    {
        // Regression: ParseShow used to require a "Name:" key, but `winget show` never
        // emits one — the package name lives on the first line as "Found <name> [<id>]".
        const string output = """
            Found Microsoft Visual Studio Code [Microsoft.VisualStudioCode]
            Version: 1.95.0
            Publisher: Microsoft Corporation
            Homepage: https://code.visualstudio.com
            License: MIT
            """;

        PackageDetail? detail = CliBackend.ParseShow ("Microsoft.VisualStudioCode", output);

        Assert.NotNull (detail);
        Assert.Equal ("Microsoft Visual Studio Code", detail.Name);
        Assert.Equal ("Microsoft.VisualStudioCode", detail.Id);
        Assert.Equal ("1.95.0", detail.Version);
        Assert.Equal ("Microsoft Corporation", detail.Publisher);
        Assert.Equal ("https://code.visualstudio.com", detail.Homepage);
        Assert.Equal ("MIT", detail.License);
    }

    [Fact]
    public void ParseShow_LocaleIndependentFoundLine_German ()
    {
        // Upstream behavior: detect the "Prefix <name> [<id>]" pattern by trailing ] +
        // matching [, not by the English word "Found". German prefix is "Gefunden".
        const string output = """
            Gefunden Google Chrome [Google.Chrome]
            Version: 118.0.5993.118
            """;

        PackageDetail? detail = CliBackend.ParseShow ("Google.Chrome", output);

        Assert.NotNull (detail);
        Assert.Equal ("Google Chrome", detail.Name);
        Assert.Equal ("Google.Chrome", detail.Id);
    }

    [Fact]
    public void ParseShow_PublisherUrlFallsBackToHomepageWhenEmpty ()
    {
        // Regression: empty-string "Homepage:" used to block the publisher_url fallback
        // because GetValueOrDefault returns "" (not null) for present-but-empty keys.
        const string output = """
            Found Foo [Acme.Foo]
            Homepage:
            Publisher Url: https://acme.example
            """;

        PackageDetail? detail = CliBackend.ParseShow ("Acme.Foo", output);

        Assert.NotNull (detail);
        Assert.Equal ("https://acme.example", detail.Homepage);
    }

    [Fact]
    public void ParseShow_ContinuationLinesAppendToDescription ()
    {
        const string output = """
            Found Foo [Acme.Foo]
            Description: First line of the description.
              Second line continuation.
              Third line.
            Homepage: https://acme.example
            """;

        PackageDetail? detail = CliBackend.ParseShow ("Acme.Foo", output);

        Assert.NotNull (detail);
        Assert.Equal ("First line of the description. Second line continuation. Third line.", detail.Description);
    }

    [Fact]
    public void ParseShow_LocaleAwareKeys_GermanPublisherAndDescription ()
    {
        const string output = """
            Gefunden Foo [Acme.Foo]
            Version: 1.0
            Herausgeber: ACME GmbH
            Beschreibung: Eine Beschreibung.
            Lizenz: GPL-3.0
            """;

        PackageDetail? detail = CliBackend.ParseShow ("Acme.Foo", output);

        Assert.NotNull (detail);
        Assert.Equal ("ACME GmbH", detail.Publisher);
        Assert.Equal ("Eine Beschreibung.", detail.Description);
        Assert.Equal ("GPL-3.0", detail.License);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Dedupe — (id, source) collision handling
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Dedupe_KeepsNewerVersionWhenIdAndSourceMatch ()
    {
        List<Package> rows =
        [
            new () { Id = "Acme.Foo", Name = "Foo", Version = "1.0.0", Source = "winget" },
            new () { Id = "Acme.Foo", Name = "Foo", Version = "2.0.0", Source = "winget" }
        ];

        List<Package> deduped = CliBackend.DedupePackages (rows);

        Assert.Single (deduped);
        Assert.Equal ("2.0.0", deduped [0].Version);
    }

    [Fact]
    public void Dedupe_OlderVersionDoesNotEvictNewer ()
    {
        List<Package> rows =
        [
            new () { Id = "Acme.Foo", Name = "Foo", Version = "2.0.0", Source = "winget" },
            new () { Id = "Acme.Foo", Name = "Foo", Version = "1.0.0", Source = "winget" }
        ];

        List<Package> deduped = CliBackend.DedupePackages (rows);

        Assert.Single (deduped);
        Assert.Equal ("2.0.0", deduped [0].Version);
    }

    [Fact]
    public void Dedupe_EqualVersionsBreakTieByAvailableVersion ()
    {
        // Same (id, source) collision key + same version → richer-metadata row wins.
        // Dedupe key includes Source, so Source itself can't differ between the two
        // candidates (different Sources are different packages).
        List<Package> rows =
        [
            new () { Id = "Acme.Foo", Name = "Foo", Version = "1.0.0", Source = "winget", AvailableVersion = null },
            new () { Id = "Acme.Foo", Name = "Foo", Version = "1.0.0", Source = "winget", AvailableVersion = "1.1.0" }
        ];

        List<Package> deduped = CliBackend.DedupePackages (rows);

        Assert.Single (deduped);
        Assert.Equal ("1.1.0", deduped [0].AvailableVersion);
    }

    [Fact]
    public void Dedupe_SourceComparisonIsCaseInsensitive ()
    {
        // "Winget" and "winget" are the same source for dedupe purposes.
        List<Package> rows =
        [
            new () { Id = "Acme.Foo", Name = "Foo", Version = "1.0.0", Source = "Winget" },
            new () { Id = "Acme.Foo", Name = "Foo", Version = "2.0.0", Source = "winget" }
        ];

        List<Package> deduped = CliBackend.DedupePackages (rows);

        Assert.Single (deduped);
        Assert.Equal ("2.0.0", deduped [0].Version);
    }

    [Fact]
    public void Dedupe_DifferentSourcesAreNotDeduplicated ()
    {
        // Same id but different source = different package (e.g. winget vs msstore).
        List<Package> rows =
        [
            new () { Id = "Acme.Foo", Name = "Foo", Version = "1.0.0", Source = "winget" },
            new () { Id = "Acme.Foo", Name = "Foo", Version = "1.0.0", Source = "msstore" }
        ];

        List<Package> deduped = CliBackend.DedupePackages (rows);

        Assert.Equal (2, deduped.Count);
    }

    [Fact]
    public void Dedupe_PreservesInsertionOrderForUniquePackages ()
    {
        List<Package> rows =
        [
            new () { Id = "A.A", Name = "A", Source = "winget" },
            new () { Id = "B.B", Name = "B", Source = "winget" },
            new () { Id = "C.C", Name = "C", Source = "winget" }
        ];

        List<Package> deduped = CliBackend.DedupePackages (rows);

        Assert.Equal (new [] { "A.A", "B.B", "C.C" }, deduped.Select (p => p.Id).ToArray ());
    }

    // ──────────────────────────────────────────────────────────────────────
    // CompareVersionsLike — version-aware ordering
    // ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData ("1.0.0", "1.0.0", 0)]
    [InlineData ("1.119.0", "1.118.0", 1)]
    [InlineData ("1.118.0", "1.119.0", -1)]
    [InlineData ("1.2", "1.2.0", 0)]            // missing segments treated as 0
    [InlineData ("2.0.0", "10.0.0", -1)]        // numeric comparison, not lexical
    [InlineData ("1.0.0-beta", "1.0.0-alpha", 1)]
    public void CompareVersionsLike_OrdersAsExpected (string a, string b, int expectedSign)
    {
        int actual = CliBackend.CompareVersionsLike (a, b);
        Assert.Equal (expectedSign, Math.Sign (actual));
    }

    [Fact]
    public void CompareVersionsLike_EmptyStringsAreEqual ()
    {
        Assert.Equal (0, CliBackend.CompareVersionsLike (string.Empty, string.Empty));
    }

    [Fact]
    public void CompareVersionsLike_EmptyIsLessThanNonEmpty ()
    {
        Assert.True (CliBackend.CompareVersionsLike (string.Empty, "1.0.0") < 0);
        Assert.True (CliBackend.CompareVersionsLike ("1.0.0", string.Empty) > 0);
    }

    // ──────────────────────────────────────────────────────────────────────
    // ParsePinState — Blocking / Gating(version) / Pinned / None precedence
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void ParsePinState_BlockingTrumpsAllOtherSignals ()
    {
        // Blocking wins even if a pinned-version is also present.
        PinState ps = CliBackend.ParsePinState ("Blocking", "1.0.0");

        Assert.Equal (PinStateKind.Blocking, ps.Kind);
    }

    [Fact]
    public void ParsePinState_GatingTypeWithVersionIsGating ()
    {
        PinState ps = CliBackend.ParsePinState ("Gating", "2.45.*");

        Assert.Equal (PinStateKind.Gating, ps.Kind);
        Assert.Equal ("2.45.*", ps.GatingVersion);
    }

    [Fact]
    public void ParsePinState_NonEmptyVersionWithUnknownTypeIsGating ()
    {
        // If the version column has content other than "latest", treat it as Gating
        // regardless of type column text.
        PinState ps = CliBackend.ParsePinState ("Something else", "1.0.0");

        Assert.Equal (PinStateKind.Gating, ps.Kind);
        Assert.Equal ("1.0.0", ps.GatingVersion);
    }

    [Fact]
    public void ParsePinState_LatestVersionIsPinnedNotGating ()
    {
        // The literal "latest" in the version column means "any latest version is fine" —
        // that's a regular pin, not a gating constraint.
        PinState ps = CliBackend.ParsePinState ("Pin", "latest");

        Assert.Equal (PinStateKind.Pinned, ps.Kind);
    }

    [Fact]
    public void ParsePinState_GatingTypeWithEmptyVersionDegradesToPinned ()
    {
        PinState ps = CliBackend.ParsePinState ("Gating", string.Empty);

        Assert.Equal (PinStateKind.Pinned, ps.Kind);
    }

    [Fact]
    public void ParsePinState_PinTypeIsPinned ()
    {
        Assert.Equal (PinStateKind.Pinned, CliBackend.ParsePinState ("Pin", string.Empty).Kind);
    }

    [Fact]
    public void ParsePinState_EmptyTypeEmptyVersionIsNone ()
    {
        PinState ps = CliBackend.ParsePinState (string.Empty, string.Empty);

        Assert.Equal (PinStateKind.None, ps.Kind);
    }

    // ──────────────────────────────────────────────────────────────────────
    // PackageDetail — context merging + sparse hint
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void PackageDetail_MergeContext_FillsSourceFromListRow ()
    {
        // Regression: winget show often omits "Source:" entirely (especially for VCRedist
        // and similar). The list-row context (which knows the source) must fill it in.
        PackageDetail detail = new () { Id = "Acme.Foo", Name = "Foo", Source = string.Empty };
        Package row = new () { Id = "Acme.Foo", Name = "Foo", Source = "winget" };

        detail.MergeContext (row);

        Assert.Equal ("winget", detail.Source);
    }

    [Fact]
    public void PackageDetail_MergeContext_PrefersInstalledVersionFromListRow ()
    {
        // winget show reports the *latest* manifest version. The installed version is on
        // the list row, so MergeContext must overwrite Version with the list-row's.
        PackageDetail detail = new () { Id = "Acme.Foo", Name = "Foo", Version = "2.0.0" };
        Package row = new () { Id = "Acme.Foo", Name = "Foo", Version = "1.5.0" };

        detail.MergeContext (row);

        Assert.Equal ("1.5.0", detail.Version);
    }

    [Fact]
    public void PackageDetail_MergeContext_KeepsAvailableVersionWhenContextHasIt ()
    {
        PackageDetail detail = new () { Id = "Acme.Foo", Name = "Foo", AvailableVersion = null };
        Package row = new () { Id = "Acme.Foo", Name = "Foo", AvailableVersion = "1.1.0" };

        detail.MergeContext (row);

        Assert.Equal ("1.1.0", detail.AvailableVersion);
    }

    [Fact]
    public void PackageDetail_MergeContext_AdoptsContextPinStateWhenDetailUnpinned ()
    {
        PackageDetail detail = new () { Id = "Acme.Foo", Name = "Foo" };
        Package row = new () { Id = "Acme.Foo", Name = "Foo", PinState = new (PinStateKind.Blocking) };

        detail.MergeContext (row);

        Assert.Equal (PinStateKind.Blocking, detail.PinState.Kind);
    }

    [Fact]
    public void EnsureDetailHint_SynthesizesDescriptionWhenAllFieldsEmpty ()
    {
        PackageDetail detail = new () { Id = "Acme.Foo", Name = "Foo" };

        detail.EnsureDetailHint ();

        Assert.NotNull (detail.Description);
        Assert.Contains ("not available", detail.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureDetailHint_LeavesDescriptionAloneWhenAlreadyPopulated ()
    {
        PackageDetail detail = new () { Id = "Acme.Foo", Name = "Foo", Description = "Real description." };

        detail.EnsureDetailHint ();

        Assert.Equal ("Real description.", detail.Description);
    }

    [Fact]
    public void EnsureDetailHint_LeavesDescriptionAloneWhenAnyOtherFieldPopulated ()
    {
        // Having Publisher (or any other manifest field) means winget show returned real
        // data — the synthetic hint would mislead.
        PackageDetail detail = new () { Id = "Acme.Foo", Name = "Foo", Publisher = "ACME" };

        detail.EnsureDetailHint ();

        Assert.Null (detail.Description);
    }

    // ──────────────────────────────────────────────────────────────────────
    // ParsePins — winget pin list parser
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void ParsePins_HandlesNoPinsConfiguredMessage ()
    {
        IReadOnlyDictionary<string, PinState> pins = CliBackend.ParsePins ("No pins configured.\n");

        Assert.Empty (pins);
    }

    [Fact]
    public void ParsePins_ExtractsBlockingAndGatingFromTable ()
    {
        const string output = """
            Name           Id              Version      Pinned Version   Type
            ----------------------------------------------------------------
            Foo            Acme.Foo        1.0.0                         Blocking
            Bar            Acme.Bar        2.0.0        2.45.*           Gating
            """;

        IReadOnlyDictionary<string, PinState> pins = CliBackend.ParsePins (output);

        Assert.Equal (2, pins.Count);
        Assert.Equal (PinStateKind.Blocking, pins ["Acme.Foo"].Kind);
        Assert.Equal (PinStateKind.Gating, pins ["Acme.Bar"].Kind);
        Assert.Equal ("2.45.*", pins ["Acme.Bar"].GatingVersion);
    }
}
