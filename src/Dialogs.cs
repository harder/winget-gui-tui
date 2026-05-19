namespace WingetTui;

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

        TextView content = new ()
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill (1),
            Height = Dim.Fill (1),
            ReadOnly = true,
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
