# ComBackendSpike — AOT readiness findings

## What this spike answers

> Before writing 350+ LOC of `ComBackend.cs`, can the WinGet COM API
> (`Microsoft.WindowsPackageManager.ComInterop`) be consumed from a
> Native-AOT-published winget-tui-sharp build?

## Methodology

A standalone console project (`spikes/ComBackendSpike/`) that exercises
the same code paths a real backend would touch: `PackageManager` →
`GetPackageCatalogs()` → `ConnectAsync()` → `FindPackagesAsync()` with
a `PackageMatchFilter`, then property access on every returned
`CatalogPackage`. ~60 LOC.

Project settings mirror the main app: `PublishAot=true`,
`InvariantGlobalization=true`, `StripSymbols=true`. Trim/AOT analyzers
are explicitly enabled with `TrimmerSingleWarn=false` so every warning
is surfaced individually.

## What can be verified from a Linux dev box

- ✅ NuGet restore + build for `win-x64` (with `EnableWindowsTargeting=true`)
- ✅ Source-level Roslyn AOT/Trim analyzer pass (`EnableTrimAnalyzer`, `EnableAotAnalyzer`)
- ✅ IL-level trim analysis via `PublishTrimmed=true` (this is the bulk of what AOT cares about)
- ❌ **Native AOT codegen** — `dotnet publish -p:PublishAot=true` fails with
  *"Cross-OS native compilation is not supported"*. Requires a Windows host.
- ❌ **Runtime smoke** — the produced .exe needs the Windows COM runtime
  and an installed App Installer to actually do anything.

So "fully test AOT support" can only be partially answered from this
environment. The cross-compile + trim-analysis pass below catches the
class of issues that historically blocked AOT for CsWinRT-projected
COM APIs; the final ilc step and runtime smoke must be done on Windows.

## Verified findings (from Linux)

### 1. The COM projection's TFM constraint

`Microsoft.WindowsPackageManager.ComInterop` ships its managed projection
under `lib/net8.0-windows10.0.26100.0/Microsoft.Management.Deployment.CsWinRTProjection.dll`.

Consequence: any project consuming it must target `netN.0-windows10.0.26100.0`
(or higher). For the main project, that means a TFM bump from `net10.0` to
`net10.0-windows10.0.26100.0` **on Windows publish profiles only**.
Non-Windows dev iteration with `MockBackend` would need a multi-targeted
csproj or a separate Windows-only project.

### 2. The package rejects `AnyCPU`

`Microsoft.WindowsPackageManager.ComInterop.common.targets` errors with
*"Microsoft.Management.Deployment.dll could not be copied because the
AnyCPU platform is being used"* unless one of `RuntimeIdentifier`,
`Platform=x64|arm64|x86`, or `MicrosoftManagementDeployment-Platform`
is set. Fine for our use — we always publish with a RID — but worth
knowing.

### 3. The `InProcCom` package is huge

`Microsoft.WindowsPackageManager.InProcCom` is 100 MB compressed,
~438 MB extracted. Most of that is debug PDBs (130+ MB per arch). The
runtime native DLLs are ~7–8 MB each. Only needed if you want to bundle
the COM server in-process; if the user already has App Installer
installed (the normal case on Windows 10/11), you can rely on the
registered OOP COM server and skip this package entirely. The spike's
`.csproj` references it as commented-out.

### 4. Trim analyzer results — the key data point

After `dotnet publish -r win-x64 -c Release -p:PublishTrimmed=true`:

| Metric | Count |
|---|---|
| Total ILLink warnings | **35** |
| IL2026 (RequiresUnreferencedCode call — AOT-blocking) | **0** |
| IL3050 (RequiresDynamicCode call — AOT-blocking) | **0** |
| IL2081 (generic-arg DAM mismatch — informational) | **35** |
| Warnings in our spike code | **0** |
| Warnings in `Microsoft.Management.Deployment.*` (the WinGet API surface) | **0** |
| Warnings in `ABI.System.*` / `ABI.Windows.*` / `WinRT.Marshaler<>` (CsWinRT infra) | 35 |

**Interpretation:** the WinGet COM projection itself is clean. Every
warning originates from CsWinRT's marshaler fallback paths (CCW
initialization helpers for `IList<T>`, `IDictionary<K,V>`,
`IEnumerable<T>`, `IAsyncOperation<T>`, `IAsyncOperationWithProgress<T,P>`,
etc.). These are CsWinRT runtime infrastructure, not the WinGet API.

IL2081 is "generic argument does not satisfy
`DynamicallyAccessedMemberTypes.PublicParameterlessConstructor`". It's a
*warning*, not an error — it means the trimmer might remove members the
fallback ABI path would have used. These fallback paths in CsWinRT are
guarded by checks the trimmer can't statically prove, so they emit warnings
even though in practice they're either never invoked or invoked with types
that survived trimming anyway.

The CsWinRT team is aware (long-standing issue in microsoft/CsWinRT).
For our purposes the warnings are noise — but noise we'd want to suppress
in the real backend project to keep the build output readable. A targeted
`<NoWarn>IL2081</NoWarn>` scoped to the AOT publish, or `[UnconditionalSuppressMessage]`
where appropriate, is the standard workaround.

### 5. Trimmed publish size

21 MB for the entire `win-x64/publish/` directory (framework + spike).
Spike .exe itself is 159 KB. Once ilc runs (on Windows) this will collapse
further as native code replaces the JIT-compiled assemblies — expect the
final single-file AOT binary to land around 5–10 MB.

## Runtime findings (from the first Windows AOT run)

The AOT publish **succeeded** and the binary ran. Two of the three runtime
unknowns resolved immediately, and the third surfaced a real, well-understood
CsWinRT-under-AOT issue with a known fix.

```
[spike] querying winget COM for: powertoys
[spike] 3 catalogs available:
Unhandled exception. System.InvalidCastException: Specified cast is not valid.
   at System.Collections.Generic.IReadOnlyListImpl`1.Make_IEnumerableObjRef()
   at System.Collections.Generic.IReadOnlyListImpl`1.GetEnumerator()
   at Program.<<Main>$>d__0.MoveNext()
```

What this proves:

- ✅ **Native AOT codegen works** — the `+ 0xNN` raw native offsets (no IL/line
  info) are the AOT signature; this was a real ilc-compiled binary, not JIT.
- ✅ **COM activation works** — `new PackageManager()` succeeded and
  `GetPackageCatalogs()` returned **3 catalogs**, so `IVectorView<T>.Size`
  (`.Count`) marshals fine.
- ❌ **`foreach` over a projected `IReadOnlyList<T>` throws** — enumeration goes
  through `IIterable<T>`, whose RCW (runtime-callable-wrapper) factory for that
  *generic instantiation* must be generated in the **consuming** app. With no JIT
  to synthesize it on demand, the `IIterable<PackageCatalogReference>` cast fails.

### Root cause

The `ComInterop` package ships the WinRT projection (`WinRT.Runtime` 2.2.0, and
the projection DLL even carries `WinRTExposedTypeAttribute`, so the projection
itself is AOT-aware) — **but it does not bring the CsWinRT build-time source
generator**. A consuming app that wants AOT must reference `Microsoft.Windows.CsWinRT`
itself so the generator emits the marshaling for the generic-collection
instantiations *its own code* touches. Ours didn't, so `IIterable<T>` was never
generated → `.Count` works, `foreach` throws.

### The fix (in `ComBackendSpike.csproj`)

```xml
<PackageReference Include="Microsoft.Windows.CsWinRT" Version="2.2.0" />
<AllowUnsafeBlocks>true</AllowUnsafeBlocks>
<CsWinRTAotOptimizerEnabled>Auto</CsWinRTAotOptimizerEnabled>
<!-- consume the prebuilt projection; don't re-run cswinrt.exe over the winmd -->
<CsWinRTGenerateProjection>false</CsWinRTGenerateProjection>
<!-- generate RCW factories for projected types we consume under AOT -->
<CsWinRTRcwFactoryFallbackGeneratorForceOptIn>true</CsWinRTRcwFactoryFallbackGeneratorForceOptIn>
```

`CsWinRTRcwFactoryFallbackGeneratorForceOptIn` is the by-name knob for exactly
this scenario ("I consume a third-party projection under AOT"); it's off by
default for non-component apps, which is why the bare consumer hit the cast.

### Belt-and-suspenders: indexing instead of `foreach`

The spike's `Probe<T>()` now traverses each projected list **both** ways and
reports which survives AOT. Indexed access (`list[i]` → `IVectorView.GetAt`) uses
the same ABI surface as `.Count`, so it's expected to work even if the config fix
somehow misses an instantiation. **If `foreach` still throws after the fix, the
real backend should consume WinRT vector views with `for (int i…)` loops** — a
cheap, guaranteed-AOT-safe pattern. One Windows run now produces a decision table
instead of a crash.

> ⚠️ Verification limit: this fix was validated only as far as a non-Windows dev
> box allows — restore resolves with no version skew (CsWinRT 2.2.0 matches the
> `WinRT.Runtime` already in the graph), the project compiles, and the
> `WinRT.SourceGenerator` analyzer runs with `…ForceOptIn = true` confirmed
> flowing to the compiler. The runtime behavior must be confirmed by re-running
> `Run-AotSpike.ps1` on Windows.

## What needs a Windows host to finish verifying

1. **Run `dotnet publish -r win-x64 -c Release -p:PublishAot=true`** on
   a Windows machine. Watch for any IL2026/IL3050 the cross-OS trim
   analyzer didn't catch, plus any IL2050 / IL3001 from ilc itself
   (COM-specific codegen warnings). Goal: same 0 count as above.
2. **Smoke-run the produced `com-backend-spike.exe`**. Expected output:
   list of catalogs, "connecting to winget catalog…", a count of matches,
   and up to 5 package rows. If it throws on `new PackageManager()`,
   App Installer isn't registered. If it throws on `ConnectAsync()`,
   the registered COM server version is older than the projection.
3. **Measure binary size** after AOT. Compare to current `win-x64`
   build of `winget-tui-sharp` to budget the cost of bundling the COM
   backend.
4. **(Optional) repeat for `win-arm64`** on an arm64 host or via an
   arm64 ilc cross-target.

## Verdict

**Go — with a known, fixed gotcha.** The static gate passes (0 IL2026/IL3050;
the 35 IL2081 are CsWinRT infra noise). The first Windows run then proved AOT
codegen *and* COM activation both work, and surfaced exactly one real issue:
enumerating projected generic collections under AOT needs the consuming app to
run the CsWinRT source generator and opt into the RCW factory fallback
(`CsWinRTRcwFactoryFallbackGeneratorForceOptIn=true`). That's now wired into the
spike's `.csproj`, with indexed-access as a guaranteed-safe fallback pattern for
the real backend. This was the one issue that historically blocks COM-in-AOT, and
it has a documented fix — not a dead end.

Re-run `Run-AotSpike.ps1` on Windows to confirm the fix at runtime. Remaining
genuine unknowns: final AOT binary size, and clean cancellation through
`IAsyncOperationWithProgress` — neither expected to surprise.

## Reproducing on Linux

```bash
cd spikes/ComBackendSpike
dotnet restore -r win-x64
dotnet publish -r win-x64 -c Release \
    -p:PublishAot=false \
    -p:PublishTrimmed=true \
    -p:TrimmerSingleWarn=false \
    -p:SuppressTrimAnalysisWarnings=false
```

## Reproducing on Windows (the real test)

A self-contained PowerShell wrapper at `Run-AotSpike.ps1` publishes the
spike with `PublishAot=true` and runs the produced binary. All paths
inside the script are anchored on `$PSScriptRoot`, so it works no
matter the current directory:

```powershell
# from anywhere
pwsh C:\path\to\winget-tui-sharp\spikes\ComBackendSpike\Run-AotSpike.ps1
pwsh C:\path\to\winget-tui-sharp\spikes\ComBackendSpike\Run-AotSpike.ps1 -Query "visual studio code"
pwsh C:\path\to\winget-tui-sharp\spikes\ComBackendSpike\Run-AotSpike.ps1 -Rid win-arm64
pwsh C:\path\to\winget-tui-sharp\spikes\ComBackendSpike\Run-AotSpike.ps1 -SkipPublish   # rerun without rebuilding
```

Or the equivalent without the wrapper:

```powershell
dotnet publish C:\path\to\winget-tui-sharp\spikes\ComBackendSpike\ComBackendSpike.csproj -r win-x64 -c Release -p:PublishAot=true
& C:\path\to\winget-tui-sharp\spikes\ComBackendSpike\bin\Release\net10.0-windows10.0.26100.0\win-x64\publish\com-backend-spike.exe
```
