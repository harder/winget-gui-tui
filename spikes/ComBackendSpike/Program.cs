// Spike: exercise the smallest end-to-end path through the WinGet COM API
// that touches the types we'd use in a real backend. The goal is to make
// the AOT compiler reason about ConnectAsync, FindPackagesAsync, match
// filters, and CatalogPackage property access — if any of those don't
// survive trimming or fail AOT analysis, this is where it shows up.
//
// Layout choices:
//   - No fancy CLI parsing; first arg is an optional query, default "powertoys".
//   - No exception handling around the COM calls — we want the failure to
//     bubble up so the runtime smoke (on Windows) reports it clearly.
//   - All async via .AsTask() so the trim analyzer sees a normal Task<T>
//     and not just the WinRT projection.

using Microsoft.Management.Deployment;

string query = args.Length > 0 ? args [0] : "powertoys";

Console.WriteLine ($"[spike] querying winget COM for: {query}");

PackageManager pm = new ();

// Step 1: enumerate available catalogs. Cheap; verifies COM activation works.
IReadOnlyList<PackageCatalogReference> catalogs = pm.GetPackageCatalogs ();
Console.WriteLine ($"[spike] {catalogs.Count} catalogs available:");

foreach (PackageCatalogReference c in catalogs)
{
    Console.WriteLine ($"  - {c.Info.Name} ({c.Info.Type})");
}

// Step 2: connect to the default winget catalog.
PackageCatalogReference wingetRef = pm.GetPackageCatalogByName ("winget");
Console.WriteLine ("[spike] connecting to winget catalog…");
ConnectResult connect = await wingetRef.ConnectAsync ().AsTask ();

if (connect.Status != ConnectResultStatus.Ok)
{
    Console.Error.WriteLine ($"[spike] connect failed: {connect.Status}");

    return 1;
}

PackageCatalog catalog = connect.PackageCatalog;

// Step 3: find packages. Uses every relevant filter/match type so the
// trimmer sees real usage of FindPackagesOptions / PackageMatchFilter.
FindPackagesOptions opts = new ();
PackageMatchFilter filter = new ()
{
    // Name is one of the broadly-supported field types per the IDL
    // (PackageManager.idl: CatalogDefault, Id, Name, Moniker, Command, Tag, PackageFamilyName, ProductCode).
    Field = PackageMatchField.Name,
    Option = PackageFieldMatchOption.ContainsCaseInsensitive,
    Value = query
};
opts.Filters.Add (filter);

Console.WriteLine ($"[spike] running FindPackagesAsync…");
FindPackagesResult result = await catalog.FindPackagesAsync (opts).AsTask ();
Console.WriteLine ($"[spike] {result.Matches.Count} matches (truncated={result.WasLimitExceeded})");

// Step 4: dereference each CatalogPackage to force the AOT compiler to
// retain InstalledVersion / AvailableVersions / DefaultInstallVersion
// property getters in the trimmed image.
int shown = 0;

foreach (MatchResult m in result.Matches)
{
    CatalogPackage pkg = m.CatalogPackage;
    PackageVersionInfo? installed = pkg.InstalledVersion;
    IReadOnlyList<PackageVersionId> available = pkg.AvailableVersions;
    string installedV = installed?.Version ?? "<not installed>";
    string latestV = available.Count > 0 ? available [0].Version : "<no remote versions>";
    Console.WriteLine ($"  · {pkg.Id}  name={pkg.Name}  installed={installedV}  latest={latestV}");

    if (++shown >= 5)
    {
        Console.WriteLine ($"  · … ({result.Matches.Count - shown} more)");

        break;
    }
}

Console.WriteLine ("[spike] done.");

return 0;
