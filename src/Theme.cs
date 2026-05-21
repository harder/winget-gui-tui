namespace WingetTuiSharp;

/// <summary>
/// Warm-amber color palette and the registered <see cref="Scheme"/>s for the app, plus the
/// pixel-art "winget" wordart rendered as a <see cref="Logo"/> view. Mirrors the constants
/// and shapes in shanselman/winget-tui's src/theme.rs.
/// </summary>
public static class Theme
{
    public static readonly Color Accent = new (238, 201, 141);
    public static readonly Color AccentDim = new (137, 130, 112);
    public static readonly Color TextPrimary = new (232, 220, 183);
    public static readonly Color TextSecondary = new (158, 158, 158);
    public static readonly Color TextOnAccent = new (30, 30, 30);
    public static readonly Color Surface = new (45, 45, 45);
    public static readonly Color Bg = new (30, 30, 30);
    public static readonly Color Success = new (86, 185, 127);
    public static readonly Color Danger = new (231, 72, 86);
    public static readonly Color Info = new (97, 175, 239);
    public static readonly Color Selection = new (198, 120, 221);

    public const string AppSchemeName = "WingetTuiSharp.App";
    public const string SurfaceSchemeName = "WingetTuiSharp.Surface";
    public const string FrameFocusedSchemeName = "WingetTuiSharp.FrameFocused";
    public const string FrameUnfocusedSchemeName = "WingetTuiSharp.FrameUnfocused";
    public const string NavbarActiveSchemeName = "WingetTuiSharp.NavbarActive";
    public const string NavbarInactiveSchemeName = "WingetTuiSharp.NavbarInactive";
    public const string StatusSchemeName = "WingetTuiSharp.Status";
    public const string AccentSchemeName = "WingetTuiSharp.Accent";
    public const string AccentDimSchemeName = "WingetTuiSharp.AccentDim";
    public const string InfoSchemeName = "WingetTuiSharp.Info";
    public const string DangerSchemeName = "WingetTuiSharp.Danger";
    public const string SuccessSchemeName = "WingetTuiSharp.Success";

    public static void Register ()
    {
        SchemeManager.AddScheme (AppSchemeName, new ()
        {
            Normal = new (TextPrimary, Bg),
            Focus = new (TextPrimary, Bg),
            Active = new (TextOnAccent, Accent, TextStyle.Bold),
            HotNormal = new (Accent, Bg),
            HotFocus = new (Accent, Bg),
            Disabled = new (TextSecondary, Bg)
        });

        SchemeManager.AddScheme (SurfaceSchemeName, new ()
        {
            Normal = new (TextPrimary, Surface),

            // TableView uses Focus for the selected row when focused, Active when not.
            // Focus must be the brightest (selected + focused). Active must still be
            // visible (selected without focus) so the highlight persists.
            Focus = new (TextOnAccent, Accent, TextStyle.Bold),
            Active = new (TextPrimary, AccentDim, TextStyle.Bold),
            HotNormal = new (Accent, Surface),
            HotFocus = new (TextOnAccent, Accent, TextStyle.Bold),
            Disabled = new (TextSecondary, Surface)
        });

        SchemeManager.AddScheme (NavbarActiveSchemeName, new ()
        {
            Normal = new (TextOnAccent, Accent, TextStyle.Bold),
            Focus = new (TextOnAccent, Accent, TextStyle.Bold),
            HotNormal = new (TextOnAccent, Accent, TextStyle.Bold),
            HotFocus = new (TextOnAccent, Accent, TextStyle.Bold)
        });

        SchemeManager.AddScheme (NavbarInactiveSchemeName, new ()
        {
            Normal = new (AccentDim, Bg),
            Focus = new (TextPrimary, Bg),
            HotNormal = new (AccentDim, Bg),
            HotFocus = new (TextPrimary, Bg)
        });

        SchemeManager.AddScheme (StatusSchemeName, new ()
        {
            Normal = new (TextPrimary, Surface),
            Focus = new (TextPrimary, Surface),
            HotNormal = new (Accent, Surface),
            HotFocus = new (Accent, Surface)
        });

        SchemeManager.AddScheme (AccentSchemeName, new ()
        {
            Normal = new (Accent, Bg, TextStyle.Bold),
            Focus = new (Accent, Bg, TextStyle.Bold),
            HotNormal = new (Accent, Bg, TextStyle.Bold),
            HotFocus = new (Accent, Bg, TextStyle.Bold)
        });

        SchemeManager.AddScheme (AccentDimSchemeName, new ()
        {
            Normal = new (AccentDim, Bg),
            Focus = new (AccentDim, Bg),
            HotNormal = new (AccentDim, Bg),
            HotFocus = new (AccentDim, Bg)
        });

        // Schemes used by FrameView containers ONLY (not by their inner content). The Border
        // renders its lines and title using VisualRole.Normal, so we swap these schemes on
        // the frame based on whether its content has focus. Inner content keeps its own
        // SchemeName so data colors are unaffected.
        SchemeManager.AddScheme (FrameFocusedSchemeName, new ()
        {
            Normal = new (Accent, Surface, TextStyle.Bold),
            Focus = new (Accent, Surface, TextStyle.Bold)
        });

        SchemeManager.AddScheme (FrameUnfocusedSchemeName, new ()
        {
            Normal = new (AccentDim, Surface),
            Focus = new (AccentDim, Surface)
        });

        SchemeManager.AddScheme (InfoSchemeName, new ()
        {
            Normal = new (Info, Bg, TextStyle.Underline),
            Focus = new (Info, Bg, TextStyle.Underline),
            HotNormal = new (Info, Bg, TextStyle.Underline),
            HotFocus = new (Info, Bg, TextStyle.Underline)
        });

        SchemeManager.AddScheme (DangerSchemeName, new ()
        {
            Normal = new (Danger, Bg),
            Focus = new (Danger, Bg),
            HotNormal = new (Danger, Bg),
            HotFocus = new (Danger, Bg)
        });

        SchemeManager.AddScheme (SuccessSchemeName, new ()
        {
            Normal = new (Success, Bg),
            Focus = new (Success, Bg),
            HotNormal = new (Success, Bg),
            HotFocus = new (Success, Bg)
        });
    }
}

/// <summary>
/// Block-art "WINGET TUI #" wordmark rendered directly in 5 text rows for better legibility.
/// </summary>
public sealed class Logo : View
{
    // "WINGET TUI #" rendered as a compact 51×5 block wordmark with a 1-column gap
    // between letters and a 2-column gap between words.
    private static readonly string [] _lines =
    [
        "█   █ ███ █  █  ██  ████ ████  ████ █  █ ███   █ █ ",
        "█   █  █  ██ █ █    █     █     █   █  █  █   █████",
        "█ █ █  █  █ ██ █ ██ ███   █     █   █  █  █    █ █ ",
        "██ ██  █  █  █ █  █ █     █     █   █  █  █   █████",
        "█   █ ███ █  █  ███ ████  █     █    ██  ███   █ █ "
    ];

    public const int LogoWidth = 51;
    public const int LogoHeight = 5;

    public Logo ()
    {
        Width = LogoWidth;
        Height = LogoHeight;
        CanFocus = false;
        SchemeName = Theme.AccentSchemeName;
    }

    /// <inheritdoc />
    protected override bool OnDrawingContent (DrawContext? context)
    {
        SetAttribute (new (Theme.Accent, Theme.Bg, TextStyle.Bold));

        for (int y = 0; y < _lines.Length && y < Viewport.Height; y++)
        {
            Move (0, y);
            AddStr (_lines [y]);
        }

        return true;
    }
}
