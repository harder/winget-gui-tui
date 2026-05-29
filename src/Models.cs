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
    BatchUpgrade,
    Download
}

public enum InstallScopePref
{
    Default,
    User,
    Machine
}

public enum InstallModePref
{
    Default,
    Silent,
    Interactive
}

public enum InstallArchPref
{
    Default,
    X64,
    X86,
    Arm64
}

/// <summary>
/// User-chosen advanced install options, gathered from the advanced-install panel and passed to
/// <see cref="IBackend.InstallAsync"/>. The COM backend maps these onto <c>InstallOptions</c>
/// (scope / mode / AllowedArchitectures / AdditionalInstallerArguments); the CLI backend maps them
/// onto winget flags; the mock ignores them. A null settings object means "backend defaults".
/// </summary>
public sealed class InstallSettings
{
    public InstallScopePref Scope { get; init; } = InstallScopePref.Default;
    public InstallModePref Mode { get; init; } = InstallModePref.Default;
    public InstallArchPref Architecture { get; init; } = InstallArchPref.Default;
    public string? CustomArgs { get; init; }

    /// <summary>True when nothing was customized — callers normalize this to null ("backend defaults").</summary>
    public bool IsDefault
        => Scope == InstallScopePref.Default
           && Mode == InstallModePref.Default
           && Architecture == InstallArchPref.Default
           && string.IsNullOrWhiteSpace (CustomArgs);
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

/// <summary>
/// Coarse phase of a long-running install/upgrade/uninstall, backend-agnostic. The COM
/// backend maps the WinGet <c>InstallProgress</c>/<c>UninstallProgress</c> states onto these;
/// the mock backend synthesizes them; the CLI backend can't report progress so it reports none.
/// </summary>
public enum OpPhase
{
    Queued,
    Downloading,
    Installing,
    Uninstalling,
    Finalizing,
    Done
}

/// <summary>
/// A single progress sample for an in-flight operation. <see cref="Fraction"/> is 0..1 within
/// the current <see cref="Phase"/> (or overall once installing). Reported via
/// <see cref="System.IProgress{T}"/> through the backend operation methods.
/// </summary>
public readonly record struct OpProgress (OpPhase Phase, double Fraction)
{
    /// <summary>Human-readable label for the status bar.</summary>
    public string Label
        => Phase switch
        {
            OpPhase.Queued => "Queued",
            OpPhase.Downloading => "Downloading",
            OpPhase.Installing => "Installing",
            OpPhase.Uninstalling => "Uninstalling",
            OpPhase.Finalizing => "Finalizing",
            OpPhase.Done => "Done",
            _ => string.Empty
        };
}

/// <summary>
/// What would actually be installed for a package, shown in the install confirm dialog. Sourced
/// from the WinGet COM API's applicable-installer resolution (type/arch/scope/elevation). Note:
/// the COM API exposes no installer download size, so size is intentionally absent. Backends that
/// can't resolve this (CLI) return null; the mock fills representative values.
/// </summary>
public sealed class InstallerPreview
{
    /// <summary>Friendly installer type, e.g. "MSI", "EXE", "MSIX", "Store".</summary>
    public string? InstallerType { get; init; }

    /// <summary>Friendly architecture, e.g. "x64", "arm64", "x86".</summary>
    public string? Architecture { get; init; }

    /// <summary>Install scope: "machine", "user", or null when unknown.</summary>
    public string? Scope { get; init; }

    /// <summary>True when the installer requires elevation (admin).</summary>
    public bool RequiresElevation { get; init; }

    /// <summary>The version this installer resolves to, when known.</summary>
    public string? Version { get; init; }

    /// <summary>One-line summary like <c>MSI · x64 · machine · admin</c> for the confirm dialog.</summary>
    public string Summary
    {
        get
        {
            List<string> parts = [];

            if (!string.IsNullOrWhiteSpace (InstallerType))
            {
                parts.Add (InstallerType);
            }

            if (!string.IsNullOrWhiteSpace (Architecture))
            {
                parts.Add (Architecture);
            }

            if (!string.IsNullOrWhiteSpace (Scope))
            {
                parts.Add (Scope);
            }

            if (RequiresElevation)
            {
                parts.Add ("admin");
            }

            return string.Join (" · ", parts);
        }
    }
}
