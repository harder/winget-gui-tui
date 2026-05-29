
namespace WingetTuiSharp;

public interface IBackend
{
    Task<IReadOnlyList<Package>> SearchAsync (string query, SourceFilter source, CancellationToken ct);
    Task<IReadOnlyList<Package>> ListInstalledAsync (SourceFilter source, CancellationToken ct);
    Task<IReadOnlyList<Package>> ListUpgradesAsync (SourceFilter source, CancellationToken ct);
    Task<PackageDetail?> ShowAsync (string id, CancellationToken ct);

    // The install/upgrade/uninstall operations optionally report structured progress through
    // `progress`. Backends that can't (CLI) ignore it; the COM backend maps the WinGet COM
    // progress events onto OpProgress; the mock backend synthesizes a download→install ramp.
    Task<OpResult> InstallAsync (string id, string? version, IProgress<OpProgress>? progress, CancellationToken ct);
    Task<OpResult> UninstallAsync (string id, IProgress<OpProgress>? progress, CancellationToken ct);
    Task<OpResult> UpgradeAsync (string id, IProgress<OpProgress>? progress, CancellationToken ct);
    Task<OpResult> PinAsync (string id, CancellationToken ct);
    Task<OpResult> UnpinAsync (string id, CancellationToken ct);
    Task<IReadOnlyDictionary<string, PinState>> ListPinsAsync (CancellationToken ct);
}
