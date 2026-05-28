using WingetTuiSharp;

if (args.Length > 0 && args [0] is "--dump")
{
    // Diagnostic mode: invoke winget the way the backend would and print the raw output
    // verbatim, plus a hex dump of the bytes immediately around the dash-line separator.
    // Use this on Windows to verify the encoding and figure out why ParseTable is empty:
    //
    //     winget-tui-sharp.exe --dump search vscode
    //     winget-tui-sharp.exe --dump list
    //     winget-tui-sharp.exe --dump upgrade
    string [] cmd = args.Length > 1
                        ? [.. args.Skip (1), "--accept-source-agreements"]
                        : ["list", "--accept-source-agreements"];

    Console.OutputEncoding = System.Text.Encoding.UTF8;
    Console.WriteLine ($"Invoking: winget {string.Join (' ', cmd)}");
    Console.WriteLine ($"Console.OutputEncoding: {Console.OutputEncoding.WebName}");
    Console.WriteLine ();

    (int code, string output) = await CliBackend.RunWithCodeAsync (cmd, CancellationToken.None);
    Console.WriteLine ($"--- exit code: {code}");
    Console.WriteLine ($"--- output length: {output.Length} chars");
    Console.WriteLine ("--- output:");
    Console.WriteLine (output);
    Console.WriteLine ("--- parser trace:");

    if (cmd [0] == "show")
    {
        // Extract the id from --id <X> for the parser's name fallback.
        int idIdx = Array.IndexOf (cmd, "--id");
        string id = idIdx >= 0 && idIdx + 1 < cmd.Length ? cmd [idIdx + 1] : string.Empty;
        PackageDetail? detail = CliBackend.ParseShowTraced (id, output, Console.Out);
        Console.WriteLine ();

        if (detail is null)
        {
            Console.WriteLine ("--- parsed detail: null");
        }
        else
        {
            Console.WriteLine ("--- parsed detail:");
            Console.WriteLine ($"  Name={detail.Name}");
            Console.WriteLine ($"  Id={detail.Id}");
            Console.WriteLine ($"  Version={detail.Version}");
            Console.WriteLine ($"  Publisher={detail.Publisher}");
            Console.WriteLine ($"  Homepage={detail.Homepage}");
            Console.WriteLine ($"  License={detail.License}");
            Console.WriteLine ($"  ReleaseNotesUrl={detail.ReleaseNotesUrl}");
            Console.WriteLine ($"  Description={detail.Description}");
        }
    }
    else
    {
        IReadOnlyList<Package> rows = CliBackend.ParseTableTraced (output, hasAvailable: cmd [0] == "upgrade", Console.Out);
        Console.WriteLine ();
        Console.WriteLine ($"--- parsed rows: {rows.Count}");

        foreach (Package row in rows.Take (5))
        {
            Console.WriteLine ($"  Name='{row.Name}' Id='{row.Id}' Version='{row.Version}' Available='{row.AvailableVersion}' Source='{row.Source}'");
        }
    }

    return;
}

// Backend selection (in priority order):
//   --mock / -m   force the in-memory mock (cross-platform dev/parity)
//   --cli         force the winget.exe CLI parser
//   --com         force the WinGet COM API backend (Windows builds only)
//   (default)     COM on Windows builds, CLI elsewhere; either falls back to mock
//                 if winget isn't usable.
IBackend backend = SelectBackend (args);

Theme.Register ();

IApplication app = Application.Create ().Init ();
App window = new (backend);
app.Run (window);
window.Dispose ();
app.Dispose ();

return;

static IBackend SelectBackend (string [] args)
{
    bool wantMock = args.Any (a => a is "--mock" or "-m");
    bool wantCli = args.Any (a => a is "--cli");
    bool wantCom = args.Any (a => a is "--com");

    if (wantMock)
    {
        return new MockBackend ();
    }

#if WINGET_COM
    // On the Windows build, COM is the default unless the user explicitly asked for the CLI.
    if (wantCom || (!wantCli && OperatingSystem.IsWindows ()))
    {
        try
        {
            return new ComBackend ();
        }
        catch (Exception ex)
        {
            // COM server not registered / activation failed — degrade gracefully rather than crash.
            Console.Error.WriteLine ($"COM backend unavailable ({ex.Message}); falling back to the CLI backend.");
        }
    }
#else
    if (wantCom)
    {
        Console.Error.WriteLine ("--com is only available in the Windows build (net10.0-windows…); using the CLI backend instead.");
    }
#endif

    // CLI path (explicit --cli, or the non-Windows / COM-unavailable default).
    if (!IsWingetAvailable ())
    {
        if (!wantCli)
        {
            Console.Error.WriteLine ("winget not found on PATH — falling back to mock backend. Run with `winget` available to drive the real CLI.");
        }
        else
        {
            Console.Error.WriteLine ("winget not found on PATH — mock backend used despite --cli.");
        }

        return new MockBackend ();
    }

    return new CliBackend ();
}

static bool IsWingetAvailable ()
{
    try
    {
        using System.Diagnostics.Process p = new ()
        {
            StartInfo = new ()
            {
                FileName = "winget",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        p.Start ();
        p.WaitForExit (1500);

        return p.HasExited && p.ExitCode == 0;
    }
    catch
    {
        return false;
    }
}
