
namespace WingetTui;

/// <summary>
/// Bottom status bar showing the source filter badge, pin filter badge, status message,
/// and right-aligned context-aware hotkey hints. Replicates ui.rs::render_status_bar.
/// </summary>
public sealed class WingetStatusBar : View
{
    public WingetStatusBar ()
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
