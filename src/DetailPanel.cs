
namespace WingetTui;

/// <summary>
/// Right-side package detail panel. Renders the package's metadata with inline styles —
/// label/value pairs in accent vs primary, URLs in info-blue with underline, and the
/// action shortcuts with the key in accent. Implemented via direct drawing because
/// Terminal.Gui v2 doesn't yet expose a rich-text Span/Line primitive (see feature-gaps.md).
/// </summary>
public sealed class DetailPanel : FrameView
{
    private readonly List<List<Span>> _lines = [];
    private bool _loading;
    private string? _emptyMessage = "Select a package to view details";

    public DetailPanel ()
    {
        Title = " Package Details ";
        BorderStyle = LineStyle.Rounded;
        SchemeName = Theme.FrameUnfocusedSchemeName;
        CanFocus = true;
    }

    public AppMode Mode { get; set; } = AppMode.Installed;

    public void SetDetail (PackageDetail? detail, bool loading)
    {
        _loading = loading;
        _lines.Clear ();

        if (loading)
        {
            Title = " Package Details (loading…) ";
            _emptyMessage = "Loading…";
            SetNeedsDraw ();

            return;
        }

        Title = " Package Details ";

        if (detail is null)
        {
            _emptyMessage = "No package selected";
            SetNeedsDraw ();

            return;
        }

        _emptyMessage = null;

        AddKv ("Name", detail.Name);
        AddKv ("Id", detail.Id, ValueScheme.Info);
        AddKv ("Version", detail.Version);

        if (!string.IsNullOrEmpty (detail.AvailableVersion))
        {
            AddKv ("Available", detail.AvailableVersion, ValueScheme.Success);
        }

        if (!string.IsNullOrEmpty (detail.Publisher))
        {
            AddKv ("Publisher", detail.Publisher);
        }

        // Skip the Source line when both the show output and the list-row context lacked it
        // (rare — typically only ARP/MSIX-only packages with no manifest source).
        if (!string.IsNullOrEmpty (detail.Source))
        {
            AddKv ("Source", detail.Source, detail.Source switch
            {
                "winget" => ValueScheme.Info,
                "msstore" => ValueScheme.Accent,
                _ => ValueScheme.Primary
            });
        }

        if (!string.IsNullOrEmpty (detail.License))
        {
            AddKv ("License", detail.License);
        }

        if (detail.PinState.IsPinned)
        {
            AddSingle ($"\U0001F4CC {detail.PinState.DisplayLabel ()}", Theme.Selection);
        }

        if (!string.IsNullOrEmpty (detail.Homepage))
        {
            AddBlank ();
            AddKv ("Homepage", detail.Homepage, ValueScheme.Link);
        }

        if (!string.IsNullOrEmpty (detail.ReleaseNotesUrl))
        {
            AddKv ("Release notes", detail.ReleaseNotesUrl, ValueScheme.Link);
        }

        if (!string.IsNullOrEmpty (detail.Description))
        {
            AddBlank ();
            AddSingle ("Description:", Theme.Accent, TextStyle.Bold);
            AddParagraph (detail.Description);
        }

        AddBlank ();
        AddSingle ("Actions:", Theme.Accent, TextStyle.Bold);

        switch (Mode)
        {
            case AppMode.Search:
                AddAction ("i", "Install");
                AddAction ("I", "Install specific version");

                break;
            case AppMode.Installed:
                if (!string.IsNullOrEmpty (detail.AvailableVersion))
                {
                    AddAction ("u", "Upgrade");
                }

                AddAction ("x", "Uninstall");
                AddAction ("p", "Pin/Unpin");

                break;
            case AppMode.Upgrades:
                AddAction ("u", "Upgrade");
                AddAction ("x", "Uninstall");
                AddAction ("Spc", "Select");
                AddAction ("a", "Toggle All");
                AddAction ("U", "Upgrade selected");

                break;
        }

        if (!string.IsNullOrEmpty (detail.Homepage))
        {
            AddAction ("o", "Open homepage");
        }

        if (!string.IsNullOrEmpty (detail.ReleaseNotesUrl))
        {
            AddAction ("c", "Open changelog");
        }

        SetNeedsDraw ();
    }

    /// <inheritdoc />
    protected override bool OnDrawingContent (DrawContext? context)
    {
        int width = Viewport.Width;
        int height = Viewport.Height;

        if (_emptyMessage is { })
        {
            SetAttribute (new (Theme.TextSecondary, Theme.Surface));
            Move (1, 0);
            AddStr (_emptyMessage);

            return true;
        }

        int y = 0;

        foreach (List<Span> line in _lines)
        {
            if (y >= height)
            {
                break;
            }

            DrawSpans (line, 1, ref y, width - 2);
        }

        return true;
    }

    private void DrawSpans (IReadOnlyList<Span> spans, int x0, ref int y, int maxWidth)
    {
        int x = x0;

        foreach (Span span in spans)
        {
            if (string.IsNullOrEmpty (span.Text))
            {
                continue;
            }

            SetAttribute (span.Attr);
            string [] words = span.Text.Split (' ');

            for (int i = 0; i < words.Length; i++)
            {
                string word = words [i];
                string toEmit = i == 0 ? word : " " + word;

                if (x + toEmit.Length > x0 + maxWidth && x > x0)
                {
                    y++;

                    if (y >= Viewport.Height)
                    {
                        return;
                    }

                    x = x0;
                    toEmit = word;
                    SetAttribute (span.Attr);
                }

                Move (x, y);
                AddStr (toEmit);
                x += toEmit.Length;
            }
        }

        y++;
    }

    // ------------------------------------------------------------------------
    // Span helpers
    // ------------------------------------------------------------------------

    private enum ValueScheme
    {
        Primary,
        Info,
        Success,
        Accent,
        Link
    }

    private void AddKv (string label, string value, ValueScheme scheme = ValueScheme.Primary)
    {
        _lines.Add ([
            new Span ($"{label}: ", new (Theme.Accent, Theme.Surface, TextStyle.Bold)),
            new Span (value, scheme switch
            {
                ValueScheme.Info => new (Theme.Info, Theme.Surface),
                ValueScheme.Success => new (Theme.Success, Theme.Surface),
                ValueScheme.Accent => new (Theme.Accent, Theme.Surface),
                ValueScheme.Link => new (Theme.Info, Theme.Surface, TextStyle.Underline),
                _ => new (Theme.TextPrimary, Theme.Surface)
            })
        ]);
    }

    private void AddSingle (string text, Color fg, TextStyle style = TextStyle.None) =>
        _lines.Add ([new Span (text, new (fg, Theme.Surface, style))]);

    private void AddBlank () => _lines.Add ([]);

    private void AddParagraph (string text)
    {
        foreach (string paragraph in text.Split ('\n'))
        {
            _lines.Add ([new Span (paragraph, new (Theme.TextPrimary, Theme.Surface))]);
        }
    }

    private void AddAction (string key, string description) =>
        _lines.Add ([
            new Span (" ", new (Theme.TextPrimary, Theme.Surface)),

            // Key rendered as a colored chip: text-on-accent foreground on accent background.
            // Mirrors the [i] Install style in upstream src/ui.rs.
            new Span ($" {key} ", new (Theme.TextOnAccent, Theme.Accent, TextStyle.Bold)),
            new Span ($"  {description}", new (Theme.TextPrimary, Theme.Surface))
        ]);

    private sealed record Span (string Text, Attribute Attr);
}
