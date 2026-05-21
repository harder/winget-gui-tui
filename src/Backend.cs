
namespace WingetTuiSharp;

public interface IBackend
{
    Task<IReadOnlyList<Package>> SearchAsync (string query, SourceFilter source, CancellationToken ct);
    Task<IReadOnlyList<Package>> ListInstalledAsync (SourceFilter source, CancellationToken ct);
    Task<IReadOnlyList<Package>> ListUpgradesAsync (SourceFilter source, CancellationToken ct);
    Task<PackageDetail?> ShowAsync (string id, CancellationToken ct);
    Task<OpResult> InstallAsync (string id, string? version, CancellationToken ct);
    Task<OpResult> UninstallAsync (string id, CancellationToken ct);
    Task<OpResult> UpgradeAsync (string id, CancellationToken ct);
    Task<OpResult> PinAsync (string id, CancellationToken ct);
    Task<OpResult> UnpinAsync (string id, CancellationToken ct);
    Task<IReadOnlyDictionary<string, PinState>> ListPinsAsync (CancellationToken ct);
}
