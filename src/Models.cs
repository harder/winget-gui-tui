namespace WingetTuiSharp;

public enum AppMode
{
    Search,
    Installed,
    Upgrades
}

public enum InputMode
{
    Normal,
    Search,
    LocalFilter,
    VersionInput
}

public enum Focus
{
    PackageList,
    DetailPanel
}

public enum SortField
{
    None,
    Name,
    Id,
    Version
}

public enum SortDir
{
    Asc,
    Desc
}

public enum SourceFilter
{
    All,
    Winget,
    MsStore
}

public enum PinFilter
{
    All,
    PinnedOnly,
    UnpinnedOnly
}

public enum PinStateKind
{
    None,
    Pinned,
    Blocking,
    Gating
}

public readonly record struct PinState (PinStateKind Kind, string? GatingVersion = null)
{
    public bool IsPinned => Kind != PinStateKind.None;

    public string DisplayLabel ()
        => Kind switch
        {
            PinStateKind.Pinned => "Pinned",
            PinStateKind.Blocking => "Blocking",
            PinStateKind.Gating => $"Gating {GatingVersion}",
            _ => string.Empty
        };

    public static readonly PinState Unpinned = new (PinStateKind.None);
}

public sealed class Package
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Version { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string? AvailableVersion { get; init; }
    public PinState PinState { get; set; } = PinState.Unpinned;

    public bool IsTruncated => Id.EndsWith ('…') || Id.EndsWith ("...", StringComparison.Ordinal);
}

public sealed class PackageDetail
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Version { get; set; } = string.Empty;
    public string? AvailableVersion { get; set; }
    public string Source { get; set; } = string.Empty;
    public PinState PinState { get; set; } = PinState.Unpinned;
    public string? Publisher { get; init; }
    public string? Description { get; set; }
    public string? Homepage { get; init; }
    public string? License { get; init; }
    public string? ReleaseNotesUrl { get; init; }

    /// <summary>
    /// True when <see cref="Description"/> holds a synthesized "couldn't fetch / nothing available"
    /// note rather than real manifest copy. Used by the detail panel to dim/annotate the line so
    /// it's not mistaken for the real package description.
    /// </summary>
    public bool IsDescriptionDegraded { get; set; }

    /// <summary>
    /// Merge in fields known from the originating <see cref="Package"/> list row that
    /// `winget show` doesn't always emit: Source, Version (installed), AvailableVersion.
    /// Mirrors upstream src/cli_backend.rs's `merge_over` behavior.
    /// </summary>
    public void MergeContext (Package context)
    {
        if (string.IsNullOrEmpty (Source))
        {
            Source = context.Source;
        }

        // `winget show` reports the latest manifest version. The installed version is on the
        // list row, so prefer that for the detail header.
        if (!string.IsNullOrEmpty (context.Version))
        {
            Version = context.Version;
        }

        if (string.IsNullOrEmpty (AvailableVersion) && !string.IsNullOrEmpty (context.AvailableVersion))
        {
            AvailableVersion = context.AvailableVersion;
        }

        if (!PinState.IsPinned && context.PinState.IsPinned)
        {
            PinState = context.PinState;
        }
    }

    /// <summary>
    /// When the manifest-derived fields are all empty (no Publisher, Description, Homepage,
    /// License), synthesize a one-line note in <see cref="Description"/> explaining that
    /// detail isn't available — so the panel doesn't render a row of empty values.
    /// Mirrors upstream src/app.rs::ensure_detail_hint.
    /// </summary>
    public void EnsureDetailHint ()
    {
        if (!string.IsNullOrEmpty (Description))
        {
            return;
        }

        bool sparse = string.IsNullOrEmpty (Publisher)
                      && string.IsNullOrEmpty (Homepage)
                      && string.IsNullOrEmpty (License)
                      && string.IsNullOrEmpty (ReleaseNotesUrl);

        if (sparse)
        {
            Description = "Additional metadata is not available for this package in any configured winget source.";
            IsDescriptionDegraded = true;
        }
    }
}

public enum OperationKind
{
    Install,
    Uninstall,
    Upgrade,
    Pin,
    Unpin,
    BatchUpgrade
}

public sealed class Operation
{
    public required OperationKind Kind { get; init; }
    public string? PackageId { get; init; }
    public string? Version { get; init; }
    public IReadOnlyList<string>? Ids { get; init; }
}

public sealed class OpResult
{
    public required Operation Operation { get; init; }
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}
