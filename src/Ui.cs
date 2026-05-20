// Widget views and modal dialogs. Combined into one file so the project layout reads
// closer to upstream's src/ui.rs. Each widget is a sealed class self-contained in its
// own #region for navigation.

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
    private readonly List<List<Span>> _wrappedLines = [];
    private int _wrappedWidth = -1;
    private string? _emptyMessage = "Select a package to view details";

    public DetailPanel ()
    {
        Title = " Package Details ";
        BorderStyle = LineStyle.Rounded;
        SchemeName = Theme.FrameUnfocusedSchemeName;
        CanFocus = true;
        ViewportSettings |= ViewportSettingsFlags.HasVerticalScrollBar;
        VerticalScrollBar.VisibilityMode = ScrollBarVisibilityMode.Auto;
        VerticalScrollBar.Increment = 3;
    }

    public AppMode Mode { get; set; } = AppMode.Installed;

    public void SetDetail (PackageDetail? detail, bool loading)
    {
        _lines.Clear ();
        _wrappedLines.Clear ();
        _wrappedWidth = -1;
        ScrollToStart ();

        if (loading)
        {
            Title = " Package Details (loading…) ";
            _emptyMessage = "Loading…";
            SetContentHeight (1);
            SetNeedsDraw ();

            return;
        }

        Title = " Package Details ";

        if (detail is null)
        {
            _emptyMessage = "No package selected";
            SetContentHeight (1);
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

        UpdateWrappedLines ();
        SetNeedsDraw ();
    }

    /// <inheritdoc />
    protected override bool OnDrawingContent (DrawContext? context)
    {
        UpdateWrappedLines ();

        int height = Viewport.Height;
        int width = Math.Max (1, Viewport.Width - 2);

        if (_emptyMessage is { })
        {
            SetAttribute (new (Theme.TextSecondary, Theme.Surface));
            Move (1, 0);
            AddStr (_emptyMessage);

            return true;
        }

        int start = Math.Clamp (Viewport.Y, 0, Math.Max (0, _wrappedLines.Count - 1));
        int end = Math.Min (start + height, _wrappedLines.Count);

        for (int index = start; index < end; index++)
        {
            DrawWrappedLine (_wrappedLines [index], 1, index - start, width);
        }

        return true;
    }

    /// <inheritdoc />
    protected override bool OnKeyDown (Key key)
    {
        switch (key.KeyCode)
        {
            case KeyCode.CursorDown:
                ScrollBy (1);
                key.Handled = true;

                return true;
            case KeyCode.CursorUp:
                ScrollBy (-1);
                key.Handled = true;

                return true;
            case KeyCode.PageDown:
                ScrollPage (1);
                key.Handled = true;

                return true;
            case KeyCode.PageUp:
                ScrollPage (-1);
                key.Handled = true;

                return true;
            case KeyCode.Home:
                ScrollToStart ();
                key.Handled = true;

                return true;
            case KeyCode.End:
                ScrollToEnd ();
                key.Handled = true;

                return true;
        }

        if (key.AsRune.Value is var rune and > 0)
        {
            char c = (char)rune;

            switch (c)
            {
                case 'j':
                    ScrollBy (1);
                    key.Handled = true;

                    return true;
                case 'k':
                    ScrollBy (-1);
                    key.Handled = true;

                    return true;
            }
        }

        return base.OnKeyDown (key);
    }

    /// <inheritdoc />
    protected override bool OnMouseEvent (Mouse mouse)
    {
        if (mouse.Flags.HasFlag (MouseFlags.WheeledDown))
        {
            SetFocus ();
            ScrollBy (3);
            mouse.Handled = true;

            return true;
        }

        if (mouse.Flags.HasFlag (MouseFlags.WheeledUp))
        {
            SetFocus ();
            ScrollBy (-3);
            mouse.Handled = true;

            return true;
        }

        if (mouse.IsSingleClicked == true)
        {
            SetFocus ();
        }

        return base.OnMouseEvent (mouse);
    }

    public void ScrollBy (int delta)
    {
        UpdateWrappedLines ();

        if (delta != 0 && ScrollVertical (delta) == true)
        {
            SetNeedsDraw ();
        }
    }

    public void ScrollPage (int pages)
    {
        int pageSize = Math.Max (1, Viewport.Height);
        ScrollBy (pages * pageSize);
    }

    public void ScrollToStart ()
    {
        if (Viewport.Y == 0)
        {
            return;
        }

        Viewport = new (Viewport.X, 0, Viewport.Width, Viewport.Height);
        SetNeedsDraw ();
    }

    public void ScrollToEnd ()
    {
        UpdateWrappedLines ();

        int maxScroll = Math.Max (0, GetContentHeight () - Viewport.Height);

        if (Viewport.Y == maxScroll)
        {
            return;
        }

        Viewport = new (Viewport.X, maxScroll, Viewport.Width, Viewport.Height);
        SetNeedsDraw ();
    }

    private void UpdateWrappedLines ()
    {
        int maxWidth = Math.Max (1, Viewport.Width - 2);

        if (_wrappedWidth == maxWidth)
        {
            return;
        }

        _wrappedLines.Clear ();

        if (_emptyMessage is not null)
        {
            _wrappedLines.Add ([]);
            SetContentHeight (1);
            _wrappedWidth = maxWidth;

            return;
        }

        foreach (List<Span> line in _lines)
        {
            _wrappedLines.AddRange (WrapLine (line, maxWidth));
        }

        if (_wrappedLines.Count == 0)
        {
            _wrappedLines.Add ([]);
        }

        SetContentHeight (_wrappedLines.Count);
        _wrappedWidth = maxWidth;
    }

    private static List<List<Span>> WrapLine (IReadOnlyList<Span> spans, int maxWidth)
    {
        List<List<Span>> wrapped = [];
        List<Span> current = [];
        int currentWidth = 0;

        foreach (Span span in spans)
        {
            if (string.IsNullOrEmpty (span.Text))
            {
                continue;
            }

            string [] words = span.Text.Split (' ');

            for (int i = 0; i < words.Length; i++)
            {
                string word = words [i];
                string token = i == 0 ? word : " " + word;

                if (string.IsNullOrEmpty (token))
                {
                    continue;
                }

                int tokenWidth = token.GetColumns ();

                if (currentWidth + tokenWidth > maxWidth && currentWidth > 0)
                {
                    wrapped.Add (current);
                    current = [];
                    currentWidth = 0;

                    if (token.StartsWith (' '))
                    {
                        token = token.TrimStart ();
                        tokenWidth = token.GetColumns ();
                    }
                }

                if (string.IsNullOrEmpty (token))
                {
                    continue;
                }

                current.Add (new (token, span.Attr));
                currentWidth += tokenWidth;
            }
        }

        wrapped.Add (current);

        return wrapped;
    }

    private void DrawWrappedLine (IReadOnlyList<Span> spans, int x0, int y, int maxWidth)
    {
        int x = x0;

        foreach (Span span in spans)
        {
            if (string.IsNullOrEmpty (span.Text))
            {
                continue;
            }

            string text = span.Text;
            int width = text.GetColumns ();

            if (x + width > x0 + maxWidth)
            {
                text = TrimToWidth (text, Math.Max (0, x0 + maxWidth - x));
                width = text.GetColumns ();
            }

            if (string.IsNullOrEmpty (text))
            {
                continue;
            }

            SetAttribute (span.Attr);
            Move (x, y);
            AddStr (text);
            x += width;
        }
    }

    private static string TrimToWidth (string text, int maxWidth)
    {
        if (maxWidth <= 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new ();
        int width = 0;

        foreach (Rune rune in text.EnumerateRunes ())
        {
            int runeWidth = rune.GetColumns ();

            if (width + runeWidth > maxWidth)
            {
                break;
            }

            builder.Append (rune.ToString ());
            width += runeWidth;
        }

        return builder.ToString ();
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

/// <summary>
/// Custom tab bar widget that renders 3 mutually-exclusive tabs with mouse hit-testing.
/// Mirrors the navbar in src/ui.rs.
/// </summary>
public sealed class TabBar : View
{
    private static readonly (AppMode Mode, string Label) [] _tabs =
    [
        (AppMode.Search, "1 ◇ Search"),
        (AppMode.Installed, "2 ▣ Installed"),
        (AppMode.Upgrades, "3 △ Upgrades")
    ];

    private AppMode _active = AppMode.Installed;

    public TabBar ()
    {
        CanFocus = false;
        Height = 1;
        SchemeName = Theme.NavbarInactiveSchemeName;
    }

    public AppMode Active
    {
        get => _active;
        set
        {
            if (_active == value)
            {
                return;
            }

            _active = value;
            SetNeedsDraw ();
        }
    }

    public event EventHandler<AppMode>? TabClicked;

    /// <inheritdoc />
    protected override bool OnDrawingContent (DrawContext? context)
    {
        SetAttribute (new (Theme.AccentDim, Theme.Bg));

        for (int x = 0; x < Viewport.Width; x++)
        {
            Move (x, 0);
            AddStr (" ");
        }

        int cursor = 0;

        foreach ((AppMode mode, string label) in _tabs)
        {
            string padded = $" {label} ";

            if (cursor + padded.Length > Viewport.Width)
            {
                break;
            }

            Attribute attr = mode == _active
                                 ? new Attribute (Theme.TextOnAccent, Theme.Accent, TextStyle.Bold)
                                 : new Attribute (Theme.AccentDim, Theme.Bg);
            SetAttribute (attr);
            Move (cursor, 0);
            AddStr (padded);
            cursor += padded.Length + 1;
        }

        return true;
    }

    /// <inheritdoc />
    protected override bool OnMouseEvent (Mouse mouse)
    {
        if (!mouse.IsSingleClicked || mouse.Position is null)
        {
            return false;
        }

        int x = mouse.Position.Value.X;
        int cursor = 0;

        foreach ((AppMode mode, string label) in _tabs)
        {
            string padded = $" {label} ";

            if (x >= cursor && x < cursor + padded.Length)
            {
                Active = mode;
                TabClicked?.Invoke (this, mode);
                mouse.Handled = true;

                return true;
            }

            cursor += padded.Length + 1;
        }

        return false;
    }
}

/// <summary>
/// Bottom status bar showing the source filter badge, pin filter badge, status message,
/// and right-aligned context-aware hotkey hints. Replicates ui.rs::render_status_bar.
/// </summary>
public sealed class StatusBar : View
{
    public StatusBar ()
    {
        Height = 1;
        CanFocus = false;
        SchemeName = Theme.StatusSchemeName;
    }

    public AppMode Mode { get; set; } = AppMode.Installed;
    public InputMode InputMode { get; set; } = InputMode.Normal;
    public SourceFilter SourceFilter { get; set; } = SourceFilter.All;
    public PinFilter PinFilter { get; set; } = PinFilter.All;
    public string Message { get; set; } = string.Empty;
    public bool IsError { get; set; }
    public bool IsLoading { get; set; }
    public int Tick { get; set; }

    private static readonly char [] _spinner = ['⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏'];

    /// <inheritdoc />
    protected override bool OnDrawingContent (DrawContext? context)
    {
        SetAttribute (new (Theme.TextPrimary, Theme.Surface));

        for (int x = 0; x < Viewport.Width; x++)
        {
            Move (x, 0);
            AddStr (" ");
        }

        int x0 = 0;

        // Source filter badge
        string srcLabel = AppState.SourceLabel (SourceFilter);
        Attribute srcAttr = SourceFilter switch
        {
            SourceFilter.Winget => new (Theme.TextOnAccent, Theme.Info, TextStyle.Bold),
            SourceFilter.MsStore => new (Theme.TextOnAccent, Theme.Accent, TextStyle.Bold),
            _ => new (Theme.TextOnAccent, Theme.TextSecondary, TextStyle.Bold)
        };
        SetAttribute (srcAttr);
        Move (x0, 0);
        AddStr (srcLabel);
        x0 += srcLabel.Length + 1;

        // Pin filter badge (skip in Search mode)
        if (Mode != AppMode.Search)
        {
            string pinLabel = AppState.PinLabel (PinFilter);
            Attribute pinAttr = PinFilter switch
            {
                PinFilter.PinnedOnly => new (Theme.TextOnAccent, Theme.Selection, TextStyle.Bold),
                PinFilter.UnpinnedOnly => new (Theme.TextOnAccent, Theme.TextSecondary, TextStyle.Bold),
                _ => new (Theme.TextOnAccent, Theme.AccentDim, TextStyle.Bold)
            };
            SetAttribute (pinAttr);
            Move (x0, 0);
            AddStr (pinLabel);
            x0 += pinLabel.Length + 1;
        }

        // Hotkey hints right-aligned. Reserve at least 8 columns for the status message; if the
        // hints don't fit in the remaining width, drop lowest-priority pairs from the left first.
        string hints = ComposeHints ();
        int hintsAvailable = Viewport.Width - x0 - 8;

        if (hints.Length > hintsAvailable)
        {
            hints = TruncateHints (hints, hintsAvailable);
        }

        int hintsStart = Math.Max (x0 + 1, Viewport.Width - hints.Length);

        // Status message between filters and hints
        string msg = Message;

        if (IsLoading)
        {
            char spin = _spinner [Tick % _spinner.Length];
            msg = $"{spin} {msg}";
        }

        int msgMax = hintsStart - x0 - 1;

        if (msg.Length > msgMax)
        {
            msg = msgMax > 1 ? msg [..(msgMax - 1)] + "…" : string.Empty;
        }

        Attribute msgAttr = IsError
                                ? new Attribute (Theme.Danger, Theme.Surface, TextStyle.Bold)
                                : IsLoading
                                    ? new Attribute (Theme.Accent, Theme.Surface)
                                    : new Attribute (Theme.TextPrimary, Theme.Surface);
        SetAttribute (msgAttr);
        Move (x0, 0);
        AddStr (msg);

        SetAttribute (new (Theme.TextPrimary, Theme.Surface));
        Move (hintsStart, 0);
        AddStr (hints);

        return true;
    }

    private static string TruncateHints (string hints, int available)
    {
        if (available <= 1)
        {
            return string.Empty;
        }

        // Hints are space-separated "key label" pairs joined by "  ". Drop pairs from the
        // left (lowest priority first; the right end has q Quit / ? Help) until they fit.
        // Then prepend "…" so the reader knows hints were elided.
        string [] pairs = hints.Split (new [] { "  " }, StringSplitOptions.None);
        int start = 0;

        while (start < pairs.Length - 1)
        {
            string candidate = "… " + string.Join ("  ", pairs [start..]);

            if (candidate.Length <= available)
            {
                return candidate;
            }

            start++;
        }

        // Even the last pair is too long; hard-cut.
        return available > 1 ? hints [^Math.Max (1, available - 1)..].Insert (0, "…") : "…";
    }

    private string ComposeHints ()
    {
        return InputMode switch
        {
            InputMode.Search => "Esc Cancel  Enter Search",
            InputMode.LocalFilter => "Esc Clear  Enter Done  Bksp Del",
            InputMode.VersionInput => "Esc Cancel  Enter Confirm  Bksp Del",
            _ => Mode == AppMode.Search
                     ? "/ Search  f Source  r Refresh  e Export  ? Help  q Quit"
                     : "/ Filter  f Source  p Pin  P Pins  r Refresh  e Export  ? Help  q Quit"
        };
    }
}
/// <summary>
/// Modal dialog asking for a specific version string before install.
/// Returns the string via <see cref="Runnable{TResult}.Result"/>; null if cancelled.
/// </summary>
public sealed class VersionInputDialog : Runnable<string?>
{
    private readonly TextField _input;

    public VersionInputDialog (string packageName)
    {
        Title = $" Install specific version of {packageName} ";
        BorderStyle = LineStyle.Rounded;
        Width = 60;
        Height = 8;
        X = Pos.Center ();
        Y = Pos.Center ();
        SchemeName = Theme.SurfaceSchemeName;
        Arrangement = ViewArrangement.Movable;

        Label prompt = new () { X = 1, Y = 1, Text = "Version:" };
        _input = new () { X = Pos.Right (prompt) + 1, Y = 1, Width = Dim.Fill (1) };

        Button confirm = new () { X = Pos.Center () - 8, Y = 4, Text = "_Confirm", IsDefault = true };
        Button cancel = new () { X = Pos.Center () + 2, Y = 4, Text = "Cancel" };

        confirm.Accepting += (_, e) =>
                             {
                                 Result = _input.Text;
                                 RequestStop ();
                                 e.Handled = true;
                             };

        cancel.Accepting += (_, e) =>
                            {
                                Result = null;
                                RequestStop ();
                                e.Handled = true;
                            };

        Add (prompt, _input, confirm, cancel);
        _input.SetFocus ();
    }
}

/// <summary>
/// Help overlay shown by pressing <c>?</c>. Mirrors the contents from src/ui.rs::render_help.
/// </summary>
public sealed class HelpDialog : Runnable
{
    public HelpDialog ()
    {
        Title = " Help ";
        BorderStyle = LineStyle.Rounded;
        Width = Dim.Percent (60);
        Height = Dim.Percent (70);
        X = Pos.Center ();
        Y = Pos.Center ();
        SchemeName = Theme.SurfaceSchemeName;
        Arrangement = ViewArrangement.Movable;

        Code content = new ()
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill (1),
            Height = Dim.Fill (1),
            Text = HelpText
        };
        Add (content);

        Button close = new ()
        {
            X = Pos.Center (),
            Y = Pos.AnchorEnd (1),
            Text = "_Close",
            IsDefault = true
        };
        close.Accepting += (_, _) => RequestStop ();
        Add (close);

        KeyDown += (_, key) =>
                   {
                       if (key.KeyCode is KeyCode.Esc or KeyCode.Q)
                       {
                           RequestStop ();
                           key.Handled = true;
                       }

                       if (key.AsRune.Value == '?')
                       {
                           RequestStop ();
                           key.Handled = true;
                       }
                   };
    }

    private const string HelpText =
        """
        Navigation
          up / k        Move up
          dn / j        Move down
          PgUp / PgDn   Page navigation
          Home / End    Jump to start / end
          lt / rt       Switch tabs (Search/Installed/Upgrades)
          Tab / S-Tab   Toggle focus between list and detail
          /  or  s      Search (Search tab) / local filter
          f             Cycle source filter
          r             Refresh

        Actions
          i             Install
          I             Install specific version
          u             Upgrade
          x             Uninstall
          p             Pin / Unpin
          Space         Toggle batch select (Upgrades only)
          a             Select / deselect all (Upgrades only)
          U             Batch upgrade selected
          e             Export visible list to CSV
          P             Cycle pin filter
          S             Cycle sort columns (Name / Id / Version, ↑/↓)
          o             Open package homepage
          c             Open changelog / release notes

        Mouse
          Click tab to switch view
          Click row to select
          Scroll wheel to navigate

        General
          ?             Toggle this help
          q / Esc       Quit
          Ctrl+C        Quit
        """;
}
