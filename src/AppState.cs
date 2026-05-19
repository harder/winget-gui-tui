
namespace WingetTui;

/// <summary>
/// In-memory state for the running app. Mirrors src/app.rs in the Rust original.
/// </summary>
public sealed class AppState
{
    public AppState (IBackend backend) => Backend = backend;

    public IBackend Backend { get; }

    public AppMode Mode { get; set; } = AppMode.Installed;
    public InputMode InputMode { get; set; } = InputMode.Normal;
    public Focus Focus { get; set; } = Focus.PackageList;

    public List<Package> Packages { get; set; } = [];
    public List<Package> Filtered { get; private set; } = [];
    public Dictionary<string, PinState> Pins { get; } = new (StringComparer.OrdinalIgnoreCase);
    public HashSet<string> BatchSelected { get; } = new (StringComparer.OrdinalIgnoreCase);
    public PackageDetail? CurrentDetail { get; set; }
    public Dictionary<string, PackageDetail> DetailCache { get; } = new (StringComparer.OrdinalIgnoreCase);

    public string SearchQuery { get; set; } = string.Empty;
    public string LocalFilter { get; set; } = string.Empty;
    public SourceFilter SourceFilter { get; set; } = SourceFilter.All;
    public PinFilter PinFilter { get; set; } = PinFilter.All;
    public SortField SortField { get; set; } = SortField.None;
    public SortDir SortDir { get; set; } = SortDir.Asc;
    public bool Loading { get; set; }
    public bool DetailLoading { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
    public bool StatusIsError { get; set; }

    public int ViewGeneration { get; private set; }
    public int DetailGeneration { get; private set; }

    public int BumpViewGeneration () => ++ViewGeneration;
    public int BumpDetailGeneration () => ++DetailGeneration;

    /// <summary>
    /// Recomputes Filtered based on LocalFilter, PinFilter, sort. Preserves selection by id when possible.
    /// </summary>
    public void ApplyFilter ()
    {
        IEnumerable<Package> q = Packages;

        if (!string.IsNullOrEmpty (LocalFilter))
        {
            string filter = LocalFilter;

            q = q.Where (p => p.Name.Contains (filter, StringComparison.OrdinalIgnoreCase)
                              || p.Id.Contains (filter, StringComparison.OrdinalIgnoreCase));
        }

        if (Mode != AppMode.Search)
        {
            q = PinFilter switch
            {
                PinFilter.PinnedOnly => q.Where (p => p.PinState.IsPinned),
                PinFilter.UnpinnedOnly => q.Where (p => !p.PinState.IsPinned),
                _ => q
            };
        }

        if (SortField != SortField.None)
        {
            IComparer<string> cmp = StringComparer.OrdinalIgnoreCase;

            Func<Package, string> key = SortField switch
            {
                SortField.Name => p => p.Name,
                SortField.Id => p => p.Id,
                SortField.Version => p => p.Version,
                _ => _ => string.Empty
            };

            q = SortDir == SortDir.Asc
                    ? q.OrderBy (key, cmp)
                    : q.OrderByDescending (key, cmp);
        }

        Filtered = q.ToList ();
    }

    public Package? SelectedPackage (int selected)
    {
        if (selected < 0 || selected >= Filtered.Count)
        {
            return null;
        }

        return Filtered [selected];
    }

    public void CycleSort ()
    {
        // None -> Name asc -> Name desc -> Id asc -> Id desc -> Version asc -> Version desc -> None
        (SortField, SortDir) = (SortField, SortDir) switch
        {
            (SortField.None, _) => (SortField.Name, SortDir.Asc),
            (SortField.Name, SortDir.Asc) => (SortField.Name, SortDir.Desc),
            (SortField.Name, SortDir.Desc) => (SortField.Id, SortDir.Asc),
            (SortField.Id, SortDir.Asc) => (SortField.Id, SortDir.Desc),
            (SortField.Id, SortDir.Desc) => (SortField.Version, SortDir.Asc),
            (SortField.Version, SortDir.Asc) => (SortField.Version, SortDir.Desc),
            _ => (SortField.None, SortDir.Asc)
        };
    }

    public void CycleSourceFilter ()
    {
        SourceFilter = SourceFilter switch
        {
            SourceFilter.All => SourceFilter.Winget,
            SourceFilter.Winget => SourceFilter.MsStore,
            _ => SourceFilter.All
        };
    }

    public void CyclePinFilter ()
    {
        PinFilter = PinFilter switch
        {
            PinFilter.All => PinFilter.PinnedOnly,
            PinFilter.PinnedOnly => PinFilter.UnpinnedOnly,
            _ => PinFilter.All
        };
    }

    public void CycleMode (bool forward)
    {
        Mode = forward
                   ? Mode switch
                   {
                       AppMode.Search => AppMode.Installed,
                       AppMode.Installed => AppMode.Upgrades,
                       _ => AppMode.Search
                   }
                   : Mode switch
                   {
                       AppMode.Search => AppMode.Upgrades,
                       AppMode.Installed => AppMode.Search,
                       _ => AppMode.Installed
                   };
    }

    public static string SourceLabel (SourceFilter f)
        => f switch
        {
            SourceFilter.Winget => " Winget ",
            SourceFilter.MsStore => " MsStore ",
            _ => " All "
        };

    public static string PinLabel (PinFilter f)
        => f switch
        {
            PinFilter.PinnedOnly => " \U0001F4CC only ",
            PinFilter.UnpinnedOnly => " \U0001F4CC hide ",
            _ => " \U0001F4CC all "
        };
}
