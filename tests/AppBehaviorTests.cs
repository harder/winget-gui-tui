namespace WingetTui.Tests;

public class AppBehaviorTests
{
    [Fact]
    public void DetailPanel_SetDetail_WithLongContentEnablesVerticalScrolling ()
    {
        DetailPanel panel = CreateDetailPanel ();

        panel.SetDetail (CreateLongDetail (), loading: false);

        Assert.True (panel.ViewportSettings.HasFlag (ViewportSettingsFlags.HasVerticalScrollBar));
        Assert.True (panel.GetContentHeight () > panel.Viewport.Height);
        Assert.True (panel.VerticalScrollBar.Visible);
    }

    [Fact]
    public void DetailPanel_OnMouseWheel_ScrollsViewport ()
    {
        DetailPanel panel = CreateDetailPanel ();
        panel.SetDetail (CreateLongDetail (), loading: false);

        InvokeMouse (panel, MouseFlags.WheeledDown);
        int afterWheelDown = panel.Viewport.Y;

        InvokeMouse (panel, MouseFlags.WheeledUp);

        Assert.True (afterWheelDown > 0);
        Assert.Equal (0, panel.Viewport.Y);
    }

    [Fact]
    public void DetailPanel_OnKeyDown_ScrollsAndCanReturnHome ()
    {
        DetailPanel panel = CreateDetailPanel ();
        panel.SetDetail (CreateLongDetail (), loading: false);

        InvokeKeyDown (panel, KeyCode.End);
        int afterEnd = panel.Viewport.Y;

        InvokeKeyDown (panel, KeyCode.Home);

        Assert.True (afterEnd > 0);
        Assert.Equal (0, panel.Viewport.Y);
    }

    [Fact]
    public void DetailPanel_SetDetail_ResetsScrollPositionForNewSelection ()
    {
        DetailPanel panel = CreateDetailPanel ();
        panel.SetDetail (CreateLongDetail (), loading: false);
        InvokeKeyDown (panel, KeyCode.End);

        panel.SetDetail (CreateLongDetail ("Second package"), loading: false);

        Assert.Equal (0, panel.Viewport.Y);
    }

    [Fact]
    public void AppState_ApplyFilter_SortsVersionsNumericallyAscending ()
    {
        AppState state = new (new MockBackend ())
        {
            Packages =
            [
                new () { Id = "pkg.one", Name = "One", Version = "2.0.0", Source = "winget" },
                new () { Id = "pkg.two", Name = "Two", Version = "10.0.0", Source = "winget" },
                new () { Id = "pkg.three", Name = "Three", Version = "1.9.0", Source = "winget" }
            ],
            SortField = SortField.Version,
            SortDir = SortDir.Asc
        };

        state.ApplyFilter ();

        Assert.Equal (["1.9.0", "2.0.0", "10.0.0"], state.Filtered.Select (p => p.Version));
    }

    [Fact]
    public void AppState_ApplyFilter_SortsVersionsNumericallyDescending ()
    {
        AppState state = new (new MockBackend ())
        {
            Packages =
            [
                new () { Id = "pkg.one", Name = "One", Version = "2.0.0", Source = "winget" },
                new () { Id = "pkg.two", Name = "Two", Version = "10.0.0", Source = "winget" },
                new () { Id = "pkg.three", Name = "Three", Version = "1.9.0", Source = "winget" }
            ],
            SortField = SortField.Version,
            SortDir = SortDir.Desc
        };

        state.ApplyFilter ();

        Assert.Equal (["10.0.0", "2.0.0", "1.9.0"], state.Filtered.Select (p => p.Version));
    }

    [Theory]
    [InlineData ("=2+5", "'=2+5")]
    [InlineData (" -hidden formula", "' -hidden formula")]
    [InlineData ("@cmd", "'@cmd")]
    [InlineData ("plain text", "plain text")]
    public void EscapeCsvCell_PrefixesSpreadsheetFormulaVectors (string input, string expected)
    {
        Assert.Equal (expected, App.EscapeCsvCell (input));
    }

    [Theory]
    [InlineData ("https://example.invalid/foo", true)]
    [InlineData ("http://example.invalid/foo", true)]
    [InlineData ("file:///etc/passwd", false)]
    [InlineData ("javascript:alert(1)", false)]
    [InlineData ("not a url", false)]
    public void TryNormalizeOpenableUrl_AllowsOnlyHttpSchemes (string input, bool expected)
    {
        bool result = App.TryNormalizeOpenableUrl (input, out string normalized);

        Assert.Equal (expected, result);

        if (expected)
        {
            Assert.NotEmpty (normalized);
        }
        else
        {
            Assert.Equal (string.Empty, normalized);
        }
    }

    private static DetailPanel CreateDetailPanel ()
    {
        DetailPanel panel = new ()
        {
            Frame = new (0, 0, 28, 8),
            Viewport = new (0, 0, 26, 6)
        };

        return panel;
    }

    private static PackageDetail CreateLongDetail (string name = "Long package")
        => new ()
        {
            Id = $"pkg.{name.Replace (" ", string.Empty, StringComparison.OrdinalIgnoreCase)}",
            Name = name,
            Version = "1.0.0",
            Source = "winget",
            Description = string.Join (
                ' ',
                Enumerable.Range (1, 80).Select (i => $"detail-line-{i:00} wraps through the panel to force scrolling")),
            Homepage = "https://example.invalid/home",
            ReleaseNotesUrl = "https://example.invalid/releases"
        };

    private static void InvokeMouse (DetailPanel panel, MouseFlags flags)
    {
        Mouse mouse = new ()
        {
            Position = new (1, 1),
            Flags = flags
        };

        System.Reflection.MethodInfo onMouse = typeof (DetailPanel).GetMethod (
            "OnMouseEvent",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        Assert.NotNull (onMouse);
        _ = onMouse.Invoke (panel, [mouse]);
    }

    private static void InvokeKeyDown (DetailPanel panel, KeyCode keyCode)
    {
        Key key = new (keyCode);

        System.Reflection.MethodInfo onKeyDown = typeof (DetailPanel).GetMethod (
            "OnKeyDown",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        Assert.NotNull (onKeyDown);
        _ = onKeyDown.Invoke (panel, [key]);
    }
}
