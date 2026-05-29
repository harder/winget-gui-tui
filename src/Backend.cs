
namespace WingetTuiSharp;

public interface IBackend
{
    Task<IReadOnlyList<Package>> SearchAsync (string query, SourceFilter source, CancellationToken ct);
    Task<IReadOnlyList<Package>> ListInstalledAsync (SourceFilter source, CancellationToken ct);
    Task<IReadOnlyList<Package>> ListUpgradesAsync (SourceFilter source, CancellationToken ct);
    Task<PackageDetail?> ShowAsync (string id, CancellationToken ct);

    // Available versions for a package, newest first. Drives the version picker. Backends that
    // can't enumerate versions (CLI) return an empty list, in which case the UI falls back to a
    // free-text version prompt.
    Task<IReadOnlyList<string>> ListVersionsAsync (string id, CancellationToken ct);

    // What would be installed (installer type / architecture / scope / elevation) for a package,
    // optionally at a specific version. Shown in the install confirm dialog. Returns null when the
    // backend can't resolve it (CLI), in which case the confirm shows no preview line.
    Task<InstallerPreview?> GetInstallerPreviewAsync (string id, string? version, CancellationToken ct);

    // The install/upgrade/uninstall operations optionally report structured progress through
    // `progress`. Backends that can't (CLI) ignore it; the COM backend maps the WinGet COM
    // progress events onto OpProgress; the mock backend synthesizes a download→install ramp.
    Task<OpResult> InstallAsync (string id, string? version, InstallSettings? settings, IProgress<OpProgress>? progress, CancellationToken ct);
    Task<OpResult> UninstallAsync (string id, IProgress<OpProgress>? progress, CancellationToken ct);
    Task<OpResult> UpgradeAsync (string id, IProgress<OpProgress>? progress, CancellationToken ct);

    // Fetch a package's installer to disk without installing it (winget "download"), reusing the
    // same progress reporting as install. Returns the download location in the OpResult message.
    Task<OpResult> DownloadAsync (string id, string? version, IProgress<OpProgress>? progress, CancellationToken ct);
    Task<OpResult> PinAsync (string id, CancellationToken ct);
    Task<OpResult> UnpinAsync (string id, CancellationToken ct);
    Task<IReadOnlyDictionary<string, PinState>> ListPinsAsync (CancellationToken ct);
}
