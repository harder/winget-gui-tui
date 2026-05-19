using WingetTui;

if (args.Length > 0 && args [0] is "--dump")
{
    // Diagnostic mode: invoke winget the way the backend would and print the raw output
    // verbatim, plus a hex dump of the bytes immediately around the dash-line separator.
    // Use this on Windows to verify the encoding and figure out why ParseTable is empty:
    //
    //     winget-tui-gui.exe --dump search vscode
    //     winget-tui-gui.exe --dump list
    //     winget-tui-gui.exe --dump upgrade
    string [] cmd = args.Length > 1
                        ? [.. args.Skip (1), "--accept-source-agreements"]
                        : ["list", "--accept-source-agreements"];

    Console.OutputEncoding = System.Text.Encoding.UTF8;
    Console.WriteLine ($"Invoking: winget {string.Join (' ', cmd)}");
    Console.WriteLine ($"Console.OutputEncoding: {Console.OutputEncoding.WebName}");
    Console.WriteLine ();

    (int code, string output) = await WingetCliBackend.RunWithCodeAsync (cmd, CancellationToken.None);
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
        PackageDetail? detail = WingetCliBackend.ParseShowTraced (id, output, Console.Out);
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
        IReadOnlyList<Package> rows = WingetCliBackend.ParseTableTraced (output, hasAvailable: cmd [0] == "upgrade", Console.Out);
        Console.WriteLine ();
        Console.WriteLine ($"--- parsed rows: {rows.Count}");

        foreach (Package row in rows.Take (5))
        {
            Console.WriteLine ($"  Name='{row.Name}' Id='{row.Id}' Version='{row.Version}' Available='{row.AvailableVersion}' Source='{row.Source}'");
        }
    }

    return;
}

bool useMock = args.Any (a => a is "--mock" or "-m") || !IsWingetAvailable ();

if (useMock && !args.Any (a => a is "--mock" or "-m"))
{
    Console.Error.WriteLine ("winget not found on PATH — falling back to mock backend. Run with `winget` available to drive the real CLI.");
}

IBackend backend = useMock ? new MockBackend () : new WingetCliBackend ();

Theme.Register ();

IApplication app = Application.Create ().Init ();
WingetTuiWindow window = new (backend);
app.Run (window);
window.Dispose ();
app.Dispose ();

return;

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
