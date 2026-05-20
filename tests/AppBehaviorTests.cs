namespace WingetTui.Tests;

public class AppBehaviorTests
{
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
}
