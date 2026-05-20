
namespace WingetTui;

/// <summary>
/// Mock backend used when winget is not available (e.g., running this on Linux/macOS for parity testing).
/// </summary>
public sealed class MockBackend : IBackend
{
    private static readonly Package [] _installed =
    [
        new () { Id = "Microsoft.VisualStudioCode", Name = "Microsoft Visual Studio Code", Version = "1.95.0", Source = "winget" },
        new () { Id = "Git.Git", Name = "Git", Version = "2.46.0", Source = "winget", AvailableVersion = "2.47.0" },
        new () { Id = "GitHub.cli", Name = "GitHub CLI", Version = "2.55.0", Source = "winget" },
        new () { Id = "Microsoft.PowerShell", Name = "PowerShell", Version = "7.4.5", Source = "winget", AvailableVersion = "7.5.0" },
        new () { Id = "9NKSQGP7F2NH", Name = "WhatsApp Desktop", Version = "2.2412.10.0", Source = "msstore" },
        new () { Id = "Notepad++.Notepad++", Name = "Notepad++", Version = "8.6.9", Source = "winget" },
        new () { Id = "Mozilla.Firefox", Name = "Mozilla Firefox", Version = "131.0.3", Source = "winget", AvailableVersion = "132.0.1" },
        new () { Id = "Microsoft.WindowsTerminal", Name = "Windows Terminal", Version = "1.21.3231.0", Source = "winget" },
        new () { Id = "Python.Python.3.12", Name = "Python 3.12", Version = "3.12.7", Source = "winget" },
        new () { Id = "Docker.DockerDesktop", Name = "Docker Desktop", Version = "4.34.0", Source = "winget", AvailableVersion = "4.35.1" }
    ];

    private static readonly Package [] _searchResults =
    [
        new () { Id = "Microsoft.VisualStudioCode", Name = "Visual Studio Code", Version = "1.95.0", Source = "winget" },
        new () { Id = "Microsoft.VisualStudioCode.Insiders", Name = "Visual Studio Code Insiders", Version = "1.96.0", Source = "winget" },
        new () { Id = "Anthropic.Claude", Name = "Claude", Version = "0.7.5", Source = "winget" },
        new () { Id = "JetBrains.Rider", Name = "JetBrains Rider", Version = "2024.2.7", Source = "winget" },
        new () { Id = "Neovim.Neovim", Name = "Neovim", Version = "0.10.2", Source = "winget" }
    ];

    private readonly Dictionary<string, PinState> _pins = new (StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<Package>> SearchAsync (string query, SourceFilter source, CancellationToken ct)
    {
        IEnumerable<Package> q = _searchResults
                                 .Concat (_installed)
                                 .Where (p => string.IsNullOrEmpty (query)
                                              || p.Name.Contains (query, StringComparison.OrdinalIgnoreCase)
                                              || p.Id.Contains (query, StringComparison.OrdinalIgnoreCase));

        q = source switch
        {
            SourceFilter.Winget => q.Where (p => p.Source == "winget"),
            SourceFilter.MsStore => q.Where (p => p.Source == "msstore"),
            _ => q
        };

        return Task.FromResult<IReadOnlyList<Package>> (q.ToArray ());
    }

    public Task<IReadOnlyList<Package>> ListInstalledAsync (SourceFilter source, CancellationToken ct)
    {
        Package [] q = source switch
        {
            SourceFilter.Winget => _installed.Where (p => p.Source == "winget").ToArray (),
            SourceFilter.MsStore => _installed.Where (p => p.Source == "msstore").ToArray (),
            _ => _installed
        };

        foreach (Package p in q)
        {
            if (_pins.TryGetValue (p.Id, out PinState ps))
            {
                p.PinState = ps;
            }
        }

        return Task.FromResult<IReadOnlyList<Package>> (q);
    }

    public Task<IReadOnlyList<Package>> ListUpgradesAsync (SourceFilter source, CancellationToken ct)
    {
        Package [] q = _installed.Where (p => p.AvailableVersion is not null).ToArray ();

        q = source switch
        {
            SourceFilter.Winget => q.Where (p => p.Source == "winget").ToArray (),
            SourceFilter.MsStore => q.Where (p => p.Source == "msstore").ToArray (),
            _ => q
        };

        foreach (Package p in q)
        {
            if (_pins.TryGetValue (p.Id, out PinState ps))
            {
                p.PinState = ps;
            }
        }

        return Task.FromResult<IReadOnlyList<Package>> (q);
    }

    public Task<PackageDetail?> ShowAsync (string id, CancellationToken ct)
    {
        Package? p = _installed.Concat (_searchResults).FirstOrDefault (x => x.Id == id);

        if (p is null)
        {
            return Task.FromResult<PackageDetail?> (null);
        }

        PackageDetail detail = new ()
        {
            Id = p.Id,
            Name = p.Name,
            Version = p.Version,
            AvailableVersion = p.AvailableVersion,
            Source = p.Source,
            PinState = _pins.GetValueOrDefault (p.Id, PinState.Unpinned),
            Publisher = $"{p.Name.Split (' ') [0]} Team",
            Description = $"{p.Name} is a placeholder description for the mock backend. "
                          + "When running on Windows with winget installed, real manifest data is fetched here. "
                          + "This clone exists to drive parity testing between the Rust winget-tui and a Terminal.Gui port.",
            Homepage = $"https://example.invalid/{p.Id}",
            License = "MIT",
            ReleaseNotesUrl = $"https://example.invalid/{p.Id}/releases"
        };

        return Task.FromResult<PackageDetail?> (detail);
    }

    public Task<OpResult> InstallAsync (string id, string? version, CancellationToken ct)
        => Task.FromResult (new OpResult
        {
            Operation = new () { Kind = OperationKind.Install, PackageId = id, Version = version },
            Success = true,
            Message = $"[mock] Installed {id}" + (version is null ? string.Empty : $" v{version}")
        });

    public Task<OpResult> UninstallAsync (string id, CancellationToken ct)
        => Task.FromResult (new OpResult
        {
            Operation = new () { Kind = OperationKind.Uninstall, PackageId = id },
            Success = true,
            Message = $"[mock] Uninstalled {id}"
        });

    public Task<OpResult> UpgradeAsync (string id, CancellationToken ct)
        => Task.FromResult (new OpResult
        {
            Operation = new () { Kind = OperationKind.Upgrade, PackageId = id },
            Success = true,
            Message = $"[mock] Upgraded {id}"
        });

    public Task<OpResult> PinAsync (string id, CancellationToken ct)
    {
        _pins [id] = new (PinStateKind.Blocking);

        return Task.FromResult (new OpResult
        {
            Operation = new () { Kind = OperationKind.Pin, PackageId = id },
            Success = true,
            Message = $"[mock] Pinned {id}"
        });
    }

    public Task<OpResult> UnpinAsync (string id, CancellationToken ct)
    {
        _pins.Remove (id);

        return Task.FromResult (new OpResult
        {
            Operation = new () { Kind = OperationKind.Unpin, PackageId = id },
            Success = true,
            Message = $"[mock] Unpinned {id}"
        });
    }

    public Task<IReadOnlyDictionary<string, PinState>> ListPinsAsync (CancellationToken ct)
        => Task.FromResult<IReadOnlyDictionary<string, PinState>> (_pins);
}
