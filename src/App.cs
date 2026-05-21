using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace WingetTuiSharp;

/// <summary>
/// Top-level window. Hosts the header (logo + tabs), search/filter input,
/// 60/40 split between package list and detail panel, and the status bar at the bottom.
/// </summary>
public sealed class App : Runnable
{
    /// <summary>Total rows reserved by the logo/header chrome before search or main content.</summary>
    private const int HeaderHeight = Logo.LogoHeight;

    private readonly AppState _state;
    private readonly TabBar _tabBar;
    private readonly Logo _logo;
    private readonly TextField _filterInput;
    private readonly TextField _versionInput;
    private readonly FrameView _listFrame;
    private readonly TableView _packageTable;
    private readonly DetailPanel _detailPanel;
    private readonly StatusBar _statusBar;
    private readonly Label _searchHint;
    private CancellationTokenSource _viewCts = new ();
    private CancellationTokenSource _detailCts = new ();
    private object? _spinnerTimer;
    private bool _initialLoadDone;

    public App (IBackend backend)
    {
        _state = new (backend);
        SchemeName = Theme.AppSchemeName;
        Title = "winget-tui (Terminal.Gui port)";

        // --- Header: logo on the left, tabs to the right, vertically centered against the
        // wordmark. Search/filter lives immediately below the logo header and temporarily
        // pushes the list/detail panes down one row while active. ---
        _logo = new () { X = 1, Y = 0 };
        _tabBar = new () { X = Pos.Right (_logo) + 4, Y = (Logo.LogoHeight - 1) / 2, Width = Dim.Fill (1) };

        // --- Search / filter input (hidden until needed). Lives immediately below the
        // header chrome; the list shifts down another row when search is shown. ---
        _searchHint = new ()
        {
            X = 1,
            Y = HeaderHeight,
            Width = 2,
            Text = "/ ",
            SchemeName = Theme.AccentSchemeName,
            Visible = false
        };
        _filterInput = new ()
        {
            X = Pos.Right (_searchHint),
            Y = HeaderHeight,
            Width = Dim.Fill (1),
            Title = "to search…",
            Visible = false
        };

        // --- Main content split ---
        _listFrame = new ()
        {
            X = 0,
            Y = HeaderHeight,
            Width = Dim.Percent (60),
            Height = Dim.Fill (1),
            Title = " Installed ",
            BorderStyle = LineStyle.Rounded,
            SchemeName = Theme.FrameFocusedSchemeName
        };

        _packageTable = new ()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill (),
            Height = Dim.Fill (),
            FullRowSelect = true,
            SchemeName = Theme.SurfaceSchemeName
        };
        _packageTable.Style.ShowHorizontalHeaderUnderline = true;
        _packageTable.Style.ExpandLastColumn = true;
        _listFrame.Add (_packageTable);

        _detailPanel = new ()
        {
            X = Pos.Right (_listFrame),
            Y = HeaderHeight,
            Width = Dim.Fill (),
            Height = Dim.Fill (1)
        };

        _statusBar = new ()
        {
            X = 0,
            Y = Pos.AnchorEnd (1),
            Width = Dim.Fill ()
        };

        Add (_logo, _tabBar, _searchHint, _filterInput, _listFrame, _detailPanel, _statusBar);

        // --- Version input dialog field (lives inside MessageBox-like popover; we use a separate field) ---
        _versionInput = new ();

        WireEvents ();
        RefreshTable ();
        RefreshStatusBar ();
    }

    private void WireEvents ()
    {
        _tabBar.TabClicked += (_, mode) => SwitchToMode (mode);

        _packageTable.ValueChanged += (_, _) => OnSelectedRowChanged ();

        _filterInput.TextChanged += (_, _) =>
                                    {
                                        if (_state.InputMode == InputMode.LocalFilter)
                                        {
                                            _state.LocalFilter = _filterInput.Text ?? string.Empty;
                                            _state.ApplyFilter ();
                                            RefreshTable ();
                                        }
                                        else if (_state.InputMode == InputMode.Search)
                                        {
                                            _state.SearchQuery = _filterInput.Text ?? string.Empty;
                                        }
                                    };

        _filterInput.Accepted += (_, _) =>
                                 {
                                     if (_state.InputMode == InputMode.Search)
                                     {
                                         TriggerRefresh ();
                                     }

                                     ExitInputMode ();
                                 };

        // Bracketed paste (CSI 2004h) lands in the TextField via the standard Command.Paste
        // pipeline. For Search mode we treat a paste as "intent to search now" — fire the
        // backend immediately rather than waiting for Enter.
        _filterInput.Pasted += (_, _) =>
                               {
                                   if (_state.InputMode == InputMode.Search)
                                   {
                                       _state.SearchQuery = _filterInput.Text ?? string.Empty;
                                       TriggerRefresh ();
                                   }
                               };

        KeyDown += OnKeyDown;
        _packageTable.KeyDown += OnKeyDown;
        _detailPanel.KeyDown += OnKeyDown;
        _detailPanel.LinkActivated += (_, url) => OpenUrl (url);
        _filterInput.KeyDown += OnFilterKeyDown;

        _packageTable.HasFocusChanged += (_, e) => ApplyFocusStyle (_listFrame, e.NewValue);
        _detailPanel.HasFocusChanged += (_, e) => ApplyFocusStyle (_detailPanel, e.NewValue);
    }

    /// <summary>
    /// Swap a frame's scheme AND border line style based on focus. Heavy lines (┏━┓) for the
    /// focused frame, Rounded (╭─╮) for the unfocused one — the same effect upstream gets via
    /// Bold-honoring box drawing in the Ratatui renderer.
    /// </summary>
    private static void ApplyFocusStyle (FrameView frame, bool hasFocus)
    {
        frame.SchemeName = hasFocus ? Theme.FrameFocusedSchemeName : Theme.FrameUnfocusedSchemeName;
        frame.BorderStyle = hasFocus ? LineStyle.Heavy : LineStyle.Rounded;
        frame.SetNeedsDraw ();
    }

    /// <inheritdoc />
    protected override void OnIsRunningChanged (bool newIsRunning)
    {
        base.OnIsRunningChanged (newIsRunning);

        if (newIsRunning && !_initialLoadDone)
        {
            _initialLoadDone = true;
            TriggerRefresh ();
            StartSpinner ();
        }
        else if (!newIsRunning)
        {
            StopSpinner ();
        }
    }

    private void StartSpinner ()
    {
        if (App is null)
        {
            return;
        }

        _spinnerTimer = App.AddTimeout (TimeSpan.FromMilliseconds (100), () =>
                                                                        {
                                                                            _statusBar.Tick++;

                                                                            if (_statusBar.IsLoading)
                                                                            {
                                                                                _statusBar.SetNeedsDraw ();
                                                                            }

                                                                            return true;
                                                                        });
    }

    private void StopSpinner ()
    {
        if (_spinnerTimer is not null && App is not null)
        {
            App.RemoveTimeout (_spinnerTimer);
            _spinnerTimer = null;
        }
    }

    private void TriggerRefresh ()
    {
        _viewCts.Cancel ();
        _viewCts = new ();
        CancellationToken ct = _viewCts.Token;
        int gen = _state.BumpViewGeneration ();
        AppMode mode = _state.Mode;
        SourceFilter src = _state.SourceFilter;
        string query = _state.SearchQuery;

        // Remember the currently-selected package id so we can re-position the cursor on the
        // same package after the refresh, instead of always jumping to row 0. Mirrors
        // upstream's process_messages cursor-anchor behavior.
        string? previousSelectedId = CurrentPackage ()?.Id;

        // Don't hit `winget search` with an empty query — it dumps the entire catalog
        // (~13k packages) which is never what the user wants. Show a placeholder instead.
        if (mode == AppMode.Search && string.IsNullOrWhiteSpace (query))
        {
            _state.Packages = [];
            _state.ApplyFilter ();
            _state.Loading = false;
            _state.StatusMessage = "Press / to search for packages";
            _state.StatusIsError = false;
            RefreshTable ();
            RefreshStatusBar ();
            SyncTabBar ();

            return;
        }

        _state.Loading = true;
        _state.StatusMessage = $"Loading {_state.Mode}…";
        _state.StatusIsError = false;
        RefreshStatusBar ();
        SyncTabBar ();

        Task.Run (async () =>
                  {
                      try
                      {
                          IReadOnlyList<Package> packages = mode switch
                          {
                              AppMode.Search => await _state.Backend.SearchAsync (query, src, ct),
                              AppMode.Upgrades => await _state.Backend.ListUpgradesAsync (src, ct),
                              _ => await _state.Backend.ListInstalledAsync (src, ct)
                          };

                          if (ct.IsCancellationRequested || gen != _state.ViewGeneration)
                          {
                              return;
                          }

                          App?.Invoke (() =>
                                       {
                                           _state.Packages = packages.ToList ();
                                           _state.ApplyFilter ();
                                           _state.Loading = false;
                                           int n = _state.Filtered.Count;
                                           _state.StatusMessage = n == 1 ? "1 package" : $"{n} packages";
                                           RefreshTable ();
                                           RefreshStatusBar ();
                                           RestoreCursorOrSelectFirst (previousSelectedId);
                                       });
                      }
                      catch (OperationCanceledException) { }
                      catch (Exception ex)
                      {
                          string msg = $"Error: {ex.Message}";

                          App?.Invoke (() =>
                                       {
                                           _state.Loading = false;
                                           _state.StatusMessage = msg;
                                           _state.StatusIsError = true;
                                           RefreshStatusBar ();
                                       });
                      }
                  }, ct);
    }

    private void RefreshTable ()
    {
        string title = _state.Mode switch
        {
            AppMode.Search => $" Search ({_state.Filtered.Count}) ",
            AppMode.Upgrades => $" Upgrades ({_state.Filtered.Count} • {_state.BatchSelected.Count} selected) ",
            _ => $" Installed ({_state.Filtered.Count}) "
        };

        if (_state.Mode != AppMode.Search && _state.PinFilter != PinFilter.All)
        {
            title = title.TrimEnd (' ') + $" • {AppState.PinLabel (_state.PinFilter).Trim ()} ";
        }

        _listFrame.Title = title;

        if (_state.Filtered.Count == 0)
        {
            _state.CurrentDetail = null;
            CancelPendingDetailLoad ();
            _detailPanel.SetDetail (null, false);

            _packageTable.Table = new EnumerableTableSource<EmptyRow> ([], new ()
            {
                { _state.Mode == AppMode.Upgrades ? "Name" : "Name", _ => string.Empty }
            });

            RefreshStatusBar ();

            return;
        }

        Dictionary<string, Func<Package, object>> cols;

        if (_state.Mode == AppMode.Upgrades)
        {
            cols = new ()
            {
                [HeaderWithSort ("Name", SortField.Name)] = p =>
                {
                    string marker = _state.BatchSelected.Contains (p.Id) ? "[x] " : "    ";
                    string pin = p.PinState.IsPinned ? "📌 " : string.Empty;

                    return marker + pin + p.Name;
                },
                [HeaderWithSort ("Id", SortField.Id)] = p => p.Id,
                [HeaderWithSort ("Version", SortField.Version)] = p => p.Version,
                ["Available"] = p => p.AvailableVersion ?? string.Empty,
                ["Source"] = p => p.Source
            };
        }
        else
        {
            cols = new ()
            {
                [HeaderWithSort ("Name", SortField.Name)] = p =>
                {
                    string pin = p.PinState.IsPinned ? "📌 " : string.Empty;

                    return pin + p.Name;
                },
                [HeaderWithSort ("Id", SortField.Id)] = p => p.Id,
                [HeaderWithSort ("Version", SortField.Version)] = p => p.Version,
                ["Source"] = p => p.Source
            };
        }

        EnumerableTableSource<Package> raw = new (_state.Filtered, cols);
        MarkedTableSource marked = new (raw);
        _packageTable.Table = marked;

        ApplyColumnStyles (marked);
        OnSelectedRowChanged ();
    }

    /// <summary>
    /// Sets per-column MaxWidth (so long names/ids truncate with a `…` indicator), accent-bold
    /// header coloring, and a per-cell scheme on the Source column to color-code winget vs msstore.
    /// </summary>
    private void ApplyColumnStyles (MarkedTableSource marked)
    {
        _packageTable.Style.ColumnStyles.Clear ();

        // Column 0: the cursor marker — exactly 1 cell wide.
        ColumnStyle markerStyle = _packageTable.Style.GetOrCreateColumnStyle (0);
        markerStyle.MinWidth = 1;
        markerStyle.MaxWidth = 1;
        markerStyle.HeaderColorGetter = _ => new Scheme (_packageTable.GetScheme ())
        {
            Normal = new (Theme.Accent, Theme.Surface)
        };

        for (int i = 1; i < marked.Columns; i++)
        {
            ColumnStyle s = _packageTable.Style.GetOrCreateColumnStyle (i);
            s.HeaderColorGetter = _ => new Scheme (_packageTable.GetScheme ())
            {
                Normal = new (Theme.Accent, Theme.Surface, TextStyle.Bold),
                Focus = new (Theme.Accent, Theme.Surface, TextStyle.Bold)
            };

            // Pin column widths by setting MinWidth = MaxWidth. Otherwise TableView's
            // CalculateMaxCellWidth scans the visible viewport and recomputes widths every
            // frame from the max content width — so when the user presses Down arrow and
            // a new row enters the viewport with different content widths, all columns
            // shift. Fixed widths avoid that visual jump.
            string name = marked.ColumnNames [i];

            if (name.StartsWith ("Name", StringComparison.Ordinal))
            {
                s.MinWidth = 24;
                s.MaxWidth = 24;
            }
            else if (name.StartsWith ("Id", StringComparison.Ordinal))
            {
                s.MinWidth = 28;
                s.MaxWidth = 28;
            }
            else if (name.StartsWith ("Version", StringComparison.Ordinal))
            {
                s.MinWidth = 14;
                s.MaxWidth = 14;
            }
            else if (name == "Available")
            {
                s.MinWidth = 14;
                s.MaxWidth = 14;
            }
            else if (name == "Source")
            {
                s.MinWidth = 8;
                s.MaxWidth = 8;
                s.ColorGetter = args =>
                {
                    string val = args.CellValue?.ToString () ?? string.Empty;
                    Color fg = val switch
                    {
                        "winget" => Theme.Info,
                        "msstore" => Theme.Accent,
                        _ => Theme.TextSecondary
                    };

                    return new Scheme (args.RowScheme)
                    {
                        Normal = new (fg, args.RowScheme.Normal.Background),
                        Focus = new (fg, args.RowScheme.Focus.Background)
                    };
                };
            }
        }
    }

    private string HeaderWithSort (string label, SortField field)
    {
        if (_state.SortField != field)
        {
            return label;
        }

        return label + (_state.SortDir == SortDir.Asc ? " ↑" : " ↓");
    }

    private void SyncTabBar () => _tabBar.Active = _state.Mode;

    /// <summary>
    /// Try to position the cursor on the same package the user had selected before the
    /// refresh (by id). If that package is no longer in the filtered list, fall back to
    /// row 0. If the list is empty, clear the detail panel.
    /// </summary>
    private void RestoreCursorOrSelectFirst (string? previousId)
    {
        if (_state.Filtered.Count == 0)
        {
            _state.CurrentDetail = null;
            CancelPendingDetailLoad ();
            _detailPanel.SetDetail (null, false);
            RefreshStatusBar ();

            return;
        }

        int row = 0;

        if (!string.IsNullOrEmpty (previousId))
        {
            int found = _state.Filtered.FindIndex (p => p.Id.Equals (previousId, StringComparison.OrdinalIgnoreCase));

            if (found >= 0)
            {
                row = found;
            }
        }

        _packageTable.Value = new (new (0, row));
    }

    private void RefreshStatusBar ()
    {
        _statusBar.Mode = _state.Mode;
        _statusBar.InputMode = _state.InputMode;
        _statusBar.SourceFilter = _state.SourceFilter;
        _statusBar.PinFilter = _state.PinFilter;
        _statusBar.Message = _state.StatusMessage;
        _statusBar.IsError = _state.StatusIsError;
        _statusBar.IsLoading = _state.Loading || _state.DetailLoading;
        _statusBar.SetNeedsDraw ();
        _detailPanel.Mode = _state.Mode;
    }

    private void OnSelectedRowChanged ()
    {
        CancelPendingDetailLoad ();

        int row = _packageTable.Value?.SelectedCell.Y ?? -1;

        if (_packageTable.Table is MarkedTableSource ms && ms.CursorRow != row)
        {
            ms.CursorRow = row;
            _packageTable.SetNeedsDraw ();
        }

        Package? p = _state.SelectedPackage (row);

        if (p is null)
        {
            _state.CurrentDetail = null;
            _detailPanel.SetDetail (null, false);
            RefreshStatusBar ();

            return;
        }

        if (_state.DetailCache.TryGetValue (p.Id, out PackageDetail? cached))
        {
            cached.MergeContext (p);
            cached.EnsureDetailHint ();
            _state.CurrentDetail = cached;
            _detailPanel.SetDetail (cached, false);
            RefreshStatusBar ();

            return;
        }

        _detailPanel.SetDetail (null, true);
        CancellationToken ct = _detailCts.Token;
        int gen = _state.BumpDetailGeneration ();
        _state.DetailLoading = true;
        RefreshStatusBar ();

        Task.Run (async () =>
                  {
                      try
                      {
                          PackageDetail? detail = await _state.Backend.ShowAsync (p.Id, ct);

                          if (ct.IsCancellationRequested || gen != _state.DetailGeneration)
                          {
                              return;
                          }

                          App?.Invoke (() =>
                                       {
                                           _state.DetailLoading = false;

                                           // Fall back to a stub detail built from the list-row context when winget show
                                           // can't resolve the package (truncated id, store-only entries with no manifest,
                                           // packages with unusual characters in id like ".115Chrome"). Mirrors upstream's
                                           // "sparse detail fallback" pattern so the panel never goes blank.
                                           PackageDetail final = detail ?? BuildStubDetail (p);
                                           final.MergeContext (p);
                                           final.EnsureDetailHint ();
                                           _state.DetailCache [p.Id] = final;
                                           _state.CurrentDetail = final;
                                           _detailPanel.SetDetail (final, false);
                                           RefreshStatusBar ();
                                       });
                      }
                      catch (OperationCanceledException) { }
                      catch (Exception ex)
                      {
                          App?.Invoke (() =>
                                       {
                                           _state.DetailLoading = false;
                                           _state.StatusMessage = $"Detail error: {ex.Message}";
                                           _state.StatusIsError = true;
                                           RefreshStatusBar ();
                                       });
                       }
                   }, ct);
    }

    private void CancelPendingDetailLoad ()
    {
        _detailCts.Cancel ();
        _detailCts.Dispose ();
        _detailCts = new ();
        _state.DetailLoading = false;
    }

    /// <summary>
    /// Move the package-list cursor by <paramref name="delta"/> rows, clamped to the table.
    /// Used for vim-style j/k navigation and for forwarding navigation keys from the filter
    /// input mode (so the user can scroll through filtered results while typing).
    /// </summary>
    private void MoveListCursor (int delta)
    {
        if (_state.Filtered.Count == 0)
        {
            return;
        }

        int current = _packageTable.Value?.SelectedCell.Y ?? 0;
        int next = Math.Clamp (current + delta, 0, _state.Filtered.Count - 1);

        if (next != current)
        {
            _packageTable.Value = new (new (0, next));
        }
    }

    private static PackageDetail BuildStubDetail (Package p) =>
        new ()
        {
            Id = p.Id,
            Name = p.Name,
            Version = p.Version,
            AvailableVersion = p.AvailableVersion,
            Source = p.Source,
            PinState = p.PinState,
            Description = "winget could not retrieve manifest details for this package. Showing list-view information only."
        };

    private Package? CurrentPackage ()
    {
        int row = _packageTable.Value?.SelectedCell.Y ?? -1;

        return _state.SelectedPackage (row);
    }

    // ------------------------------------------------------------------------
    // Keyboard handling — mirrors src/handler.rs from shanselman/winget-tui.
    // ------------------------------------------------------------------------

    private void OnFilterKeyDown (object? sender, Key key)
    {
        if (key.KeyCode == KeyCode.Esc)
        {
            if (_state.InputMode == InputMode.LocalFilter)
            {
                _state.LocalFilter = string.Empty;
                _filterInput.Text = string.Empty;
                _state.ApplyFilter ();
                RefreshTable ();
            }

            ExitInputMode ();
            key.Handled = true;

            return;
        }

        // Let the user navigate the filtered list while the filter input has focus.
        // Mirrors upstream src/handler.rs:182-212 which forwards Up/Down/PgUp/PgDn/Home/End
        // through to move_selection without closing the input box.
        switch (key.KeyCode)
        {
            case KeyCode.CursorDown:
                MoveListCursor (1);
                key.Handled = true;

                break;
            case KeyCode.CursorUp:
                MoveListCursor (-1);
                key.Handled = true;

                break;
            case KeyCode.PageDown:
                MoveListCursor (10);
                key.Handled = true;

                break;
            case KeyCode.PageUp:
                MoveListCursor (-10);
                key.Handled = true;

                break;
            case KeyCode.Home:
                if (_state.Filtered.Count > 0)
                {
                    _packageTable.Value = new (new (0, 0));
                }

                key.Handled = true;

                break;
            case KeyCode.End:
                if (_state.Filtered.Count > 0)
                {
                    _packageTable.Value = new (new (0, _state.Filtered.Count - 1));
                }

                key.Handled = true;

                break;
        }
    }

    private void OnKeyDown (object? sender, Key key)
    {
        if (_state.InputMode != InputMode.Normal)
        {
            return;
        }

        switch (key.KeyCode)
        {
            case KeyCode.Q:
            case KeyCode.Esc:
                RequestStop ();
                key.Handled = true;

                return;
            case KeyCode.C | KeyCode.CtrlMask:
                RequestStop ();
                key.Handled = true;

                return;
            case KeyCode.D1:
                JumpToTab (AppMode.Search);
                key.Handled = true;

                return;
            case KeyCode.D2:
                JumpToTab (AppMode.Installed);
                key.Handled = true;

                return;
            case KeyCode.D3:
                JumpToTab (AppMode.Upgrades);
                key.Handled = true;

                return;
        }

        // Left/Right arrows cycle modes (Search ↔ Installed ↔ Upgrades) only when focus is
        // not on the list — otherwise the list's column-aware navigation handles them.
        if (key.KeyCode == KeyCode.CursorRight && _packageTable.HasFocus == false)
        {
            _state.CycleMode (true);
            SwitchToMode (_state.Mode);
            key.Handled = true;

            return;
        }

        if (key.KeyCode == KeyCode.CursorLeft && _packageTable.HasFocus == false)
        {
            _state.CycleMode (false);
            SwitchToMode (_state.Mode);
            key.Handled = true;

            return;
        }

        // Tab and Shift+Tab both toggle focus between the package list and the detail panel.
        // (Upstream binds it this way; previously Shift+Tab cycled mode backward which
        // conflicted with the Left-arrow binding.)
        if (key.KeyCode == KeyCode.Tab || key.KeyCode == (KeyCode.Tab | KeyCode.ShiftMask))
        {
            if (_packageTable.HasFocus)
            {
                _detailPanel.SetFocus ();
            }
            else
            {
                _packageTable.SetFocus ();
            }

            key.Handled = true;

            return;
        }

        // Explicit Home/End/PgUp/PgDn for parity (TableView handles them by default but
        // we want to forward them through our handler so generation-counter cancellation
        // wraps any incidental detail loads triggered by selection changes).
        if (key.KeyCode is KeyCode.Home or KeyCode.End or KeyCode.PageUp or KeyCode.PageDown && _packageTable.HasFocus)
        {
            // Let TableView's default command bindings handle the move; do not mark handled.
            return;
        }

        // Character keys
        if (key.AsRune.Value is var rune and > 0)
        {
            char c = (char)rune;

            switch (c)
            {
                case 'j':

                    // Vim-style down. Forward to the table by simulating CursorDown.
                    MoveListCursor (1);
                    key.Handled = true;

                    return;
                case 'k':

                    // Vim-style up.
                    MoveListCursor (-1);
                    key.Handled = true;

                    return;
                case '/':
                case 's':
                    EnterFilterMode ();
                    key.Handled = true;

                    return;
                case 'f':
                    _state.CycleSourceFilter ();
                    TriggerRefresh ();
                    key.Handled = true;

                    return;
                case 'r':
                    TriggerRefresh ();
                    key.Handled = true;

                    return;
                case 'S':
                    _state.CycleSort ();
                    _state.ApplyFilter ();
                    RefreshTable ();
                    key.Handled = true;

                    return;
                case 'P':
                    if (_state.Mode != AppMode.Search)
                    {
                        _state.CyclePinFilter ();
                        _state.ApplyFilter ();
                        RefreshTable ();
                        RefreshStatusBar ();
                    }

                    key.Handled = true;

                    return;
                case '?':
                    ShowHelp ();
                    key.Handled = true;

                    return;
                case 'e':
                    ExportCsv ();
                    key.Handled = true;

                    return;
                case 'o':
                    OpenUrl (_state.CurrentDetail?.Homepage);
                    key.Handled = true;

                    return;
                case 'c':
                    OpenUrl (_state.CurrentDetail?.ReleaseNotesUrl);
                    key.Handled = true;

                    return;
                case 'i':
                    AskInstall (CurrentPackage (), specificVersion: false);
                    key.Handled = true;

                    return;
                case 'I':
                    AskInstall (CurrentPackage (), specificVersion: true);
                    key.Handled = true;

                    return;
                case 'u':
                    AskUpgrade (CurrentPackage ());
                    key.Handled = true;

                    return;
                case 'x':
                    AskUninstall (CurrentPackage ());
                    key.Handled = true;

                    return;
                case 'p':
                    if (_state.Mode != AppMode.Search)
                    {
                        TogglePin (CurrentPackage ());
                        key.Handled = true;
                    }

                    return;
                case ' ':
                    if (_state.Mode == AppMode.Upgrades)
                    {
                        ToggleBatchSelect (CurrentPackage ());
                        key.Handled = true;
                    }

                    return;
                case 'a':
                    if (_state.Mode == AppMode.Upgrades)
                    {
                        ToggleSelectAll ();
                        key.Handled = true;
                    }

                    return;
                case 'U':
                    if (_state.Mode == AppMode.Upgrades)
                    {
                        AskBatchUpgrade ();
                        key.Handled = true;
                    }

                    return;
            }
        }
    }

    private void JumpToTab (AppMode mode)
    {
        if (_state.Mode == mode)
        {
            return;
        }

        SwitchToMode (mode);
    }

    /// <summary>
    /// Centralized view-switch: clears the local filter, batch selection, and any input
    /// mode. Without this, a `/` filter typed in Installed would carry into Search/Upgrades
    /// and silently filter rows the user thinks they're seeing whole.
    /// </summary>
    private void SwitchToMode (AppMode mode)
    {
        _state.Mode = mode;
        _state.LocalFilter = string.Empty;
        _state.BatchSelected.Clear ();

        if (_state.InputMode != InputMode.Normal)
        {
            ExitInputMode ();
        }

        TriggerRefresh ();
    }

    private void EnterFilterMode ()
    {
        _state.InputMode = _state.Mode == AppMode.Search ? InputMode.Search : InputMode.LocalFilter;
        _searchHint.Visible = true;
        _filterInput.Visible = true;
        _filterInput.Title = _state.Mode == AppMode.Search ? "to search…" : "to filter…";
        _filterInput.Text = _state.Mode == AppMode.Search ? _state.SearchQuery : _state.LocalFilter;
        _listFrame.Y = HeaderHeight + 1;
        _detailPanel.Y = HeaderHeight + 1;
        _filterInput.SetFocus ();
        RefreshStatusBar ();
    }

    private void ExitInputMode ()
    {
        _state.InputMode = InputMode.Normal;
        _searchHint.Visible = false;
        _filterInput.Visible = false;
        _listFrame.Y = HeaderHeight;
        _detailPanel.Y = HeaderHeight;
        _packageTable.SetFocus ();
        RefreshStatusBar ();
    }

    // ------------------------------------------------------------------------
    // Confirm + execute helpers
    // ------------------------------------------------------------------------

    /// <summary>
    /// winget sometimes truncates package ids in its tabular output with `…`. Operating on
    /// such an id will fail because winget can't match the literal `…` against the catalog.
    /// Surface a clear message instead of letting winget fail opaquely.
    /// </summary>
    private bool GuardTruncatedId (Package? p, string verb)
    {
        if (p is null || !p.IsTruncated)
        {
            return false;
        }

        _state.StatusMessage = $"Cannot {verb}: id was truncated by winget — pick the same package from another view (e.g. Installed) for the full id.";
        _state.StatusIsError = true;
        RefreshStatusBar ();

        return true;
    }

    private void AskInstall (Package? p, bool specificVersion)
    {
        if (p is null || App is null || GuardTruncatedId (p, "install"))
        {
            return;
        }

        if (!specificVersion)
        {
            if (!Confirm ("Install", $"Install {p.Name}?"))
            {
                return;
            }

            RunOperation ($"Installing {p.Name}", _ => _state.Backend.InstallAsync (p.Id, null, _));

            return;
        }

        string? version = PromptForVersion (p);

        if (string.IsNullOrEmpty (version))
        {
            return;
        }

        RunOperation ($"Installing {p.Name} {version}", _ => _state.Backend.InstallAsync (p.Id, version, _));
    }

    private void AskUpgrade (Package? p)
    {
        if (p is null || App is null || GuardTruncatedId (p, "upgrade"))
        {
            return;
        }

        if (!Confirm ("Upgrade", $"Upgrade {p.Name}?"))
        {
            return;
        }

        RunOperation ($"Upgrading {p.Name}", _ => _state.Backend.UpgradeAsync (p.Id, _));
    }

    private void AskUninstall (Package? p)
    {
        if (p is null || App is null || GuardTruncatedId (p, "uninstall"))
        {
            return;
        }

        if (!Confirm ("Uninstall", $"Uninstall {p.Name}? This cannot be undone."))
        {
            return;
        }

        RunOperation ($"Uninstalling {p.Name}", _ => _state.Backend.UninstallAsync (p.Id, _));
    }

    private void TogglePin (Package? p)
    {
        if (p is null || App is null || GuardTruncatedId (p, "pin"))
        {
            return;
        }

        bool pinned = p.PinState.IsPinned;
        string label = pinned ? "Unpin" : "Pin";

        if (!Confirm (label, $"{label} {p.Name}?"))
        {
            return;
        }

        RunOperation ($"{label}ning {p.Name}", _ => pinned
                                                        ? _state.Backend.UnpinAsync (p.Id, _)
                                                        : _state.Backend.PinAsync (p.Id, _));
    }

    private void ToggleBatchSelect (Package? p)
    {
        if (p is null)
        {
            return;
        }

        if (!_state.BatchSelected.Add (p.Id))
        {
            _state.BatchSelected.Remove (p.Id);
        }

        RefreshTable ();
    }

    private void ToggleSelectAll ()
    {
        if (_state.BatchSelected.Count == _state.Filtered.Count)
        {
            _state.BatchSelected.Clear ();
        }
        else
        {
            foreach (Package p in _state.Filtered)
            {
                _state.BatchSelected.Add (p.Id);
            }
        }

        RefreshTable ();
    }

    private void AskBatchUpgrade ()
    {
        if (_state.BatchSelected.Count == 0 || App is null)
        {
            return;
        }

        if (!Confirm ("Batch Upgrade", $"Upgrade {_state.BatchSelected.Count} selected packages?"))
        {
            return;
        }

        string [] ids = [.. _state.BatchSelected];

        Task.Run (async () =>
                  {
                      foreach (string id in ids)
                      {
                          App?.Invoke (() =>
                                       {
                                           _state.StatusMessage = $"Upgrading {id}…";
                                           _state.Loading = true;
                                           RefreshStatusBar ();
                                       });

                          OpResult result;

                          try
                          {
                              result = await _state.Backend.UpgradeAsync (id, CancellationToken.None);
                          }
                          catch (Exception ex)
                          {
                              result = new ()
                              {
                                  Operation = new () { Kind = OperationKind.Upgrade, PackageId = id },
                                  Success = false,
                                  Message = ex.Message
                              };
                          }

                          App?.Invoke (() =>
                                       {
                                           _state.StatusMessage = result.Success
                                                                      ? $"Upgraded {id}"
                                                                      : $"Failed: {id}";
                                           _state.StatusIsError = !result.Success;
                                           RefreshStatusBar ();
                                       });
                      }

                      App?.Invoke (() =>
                                   {
                                       _state.BatchSelected.Clear ();
                                       _state.Loading = false;
                                       TriggerRefresh ();
                                   });
                  });
    }

    private void RunOperation (string activity, Func<CancellationToken, Task<OpResult>> op)
    {
        _state.StatusMessage = activity;
        _state.Loading = true;
        _state.StatusIsError = false;
        RefreshStatusBar ();

        Task.Run (async () =>
                  {
                      OpResult result;

                      try
                      {
                          result = await op (CancellationToken.None);
                      }
                      catch (Exception ex)
                      {
                          result = new ()
                          {
                              Operation = new () { Kind = OperationKind.Install },
                              Success = false,
                              Message = ex.Message
                          };
                      }

                      App?.Invoke (() =>
                                   {
                                       _state.Loading = false;
                                       _state.StatusMessage = result.Success ? "Done" : result.Message;
                                       _state.StatusIsError = !result.Success;

                                       if (result.Operation.PackageId is { } id)
                                       {
                                           _state.DetailCache.Remove (id);
                                       }

                                       TriggerRefresh ();
                                   });
                  });
    }

    private bool Confirm (string title, string message)
    {
        if (App is null)
        {
            return false;
        }

        int? result = MessageBox.Query (App, title, message, "_Yes", "_No");

        return result == 0;
    }

    private string? PromptForVersion (Package p)
    {
        if (App is null)
        {
            return null;
        }

        VersionInputDialog dlg = new (p.Name);
        App.Run (dlg);
        string? value = dlg.Result as string;
        dlg.Dispose ();

        return value;
    }

    private void ShowHelp ()
    {
        if (App is null)
        {
            return;
        }

        HelpDialog dlg = new ();
        App.Run (dlg);
        dlg.Dispose ();
    }

    private void ExportCsv ()
    {
        try
        {
            string path = Path.Combine (Environment.CurrentDirectory, "winget-tui-export.csv");
            using StreamWriter sw = new (path);
            sw.WriteLine ("Name,Id,Version,Available,Source");

            foreach (Package p in _state.Filtered)
            {
                sw.WriteLine ($"\"{EscapeCsvCell (p.Name)}\",\"{EscapeCsvCell (p.Id)}\",\"{EscapeCsvCell (p.Version)}\",\"{EscapeCsvCell (p.AvailableVersion ?? string.Empty)}\",\"{EscapeCsvCell (p.Source)}\"");
            }

            _state.StatusMessage = $"Exported {_state.Filtered.Count} rows to {path}";
            _state.StatusIsError = false;
        }
        catch (Exception ex)
        {
            _state.StatusMessage = $"Export failed: {ex.Message}";
            _state.StatusIsError = true;
        }

        RefreshStatusBar ();
    }

    private void OpenUrl (string? url)
    {
        if (string.IsNullOrWhiteSpace (url))
        {
            _state.StatusMessage = "No URL available";
            _state.StatusIsError = false;
            RefreshStatusBar ();

            return;
        }

        if (!TryNormalizeOpenableUrl (url, out string normalizedUrl))
        {
            _state.StatusMessage = "Blocked non-http(s) URL";
            _state.StatusIsError = true;
            RefreshStatusBar ();

            return;
        }

        try
        {
            ProcessStartInfo psi = new (normalizedUrl) { UseShellExecute = true };
            Process.Start (psi);
            _state.StatusMessage = $"Opened {normalizedUrl}";
            _state.StatusIsError = false;
        }
        catch (Exception ex)
        {
            _state.StatusMessage = $"Open failed: {ex.Message}";
            _state.StatusIsError = true;
        }

        RefreshStatusBar ();
    }

    internal static string EscapeCsvCell (string value)
    {
        string escaped = LooksLikeCsvFormula (value) ? "'" + value : value;

        return escaped.Replace ("\"", "\"\"");
    }

    internal static bool TryNormalizeOpenableUrl (string? url, out string normalizedUrl)
    {
        normalizedUrl = string.Empty;

        if (string.IsNullOrWhiteSpace (url))
        {
            return false;
        }

        if (!Uri.TryCreate (url.Trim (), UriKind.Absolute, out Uri? parsed))
        {
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        normalizedUrl = parsed.AbsoluteUri;

        return true;
    }

    private static bool LooksLikeCsvFormula (string value)
    {
        if (string.IsNullOrEmpty (value))
        {
            return false;
        }

        string trimmed = value.TrimStart ();

        return trimmed.Length > 0 && trimmed [0] is '=' or '+' or '-' or '@';
    }

    private sealed record EmptyRow ();

    /// <summary>
    /// Wraps an <see cref="EnumerableTableSource{Package}"/> and prepends a column-0 cursor
    /// marker (<c>●</c> on the cursor row, blank otherwise). A redundant visual cue alongside
    /// the row-highlight colors — useful when terminal themes mute the highlight, and for
    /// color-blind accessibility.
    ///
    /// Nested here because it has no consumer outside <see cref="App"/>; pulling
    /// it out as a public top-level type would just clutter the public surface.
    /// </summary>
    private sealed class MarkedTableSource : IEnumerableTableSource<Package>
    {
        private readonly EnumerableTableSource<Package> _inner;
        private readonly string [] _columns;

        public MarkedTableSource (EnumerableTableSource<Package> inner)
        {
            _inner = inner;
            _columns = new [] { " " }.Concat (inner.ColumnNames).ToArray ();
        }

        public int CursorRow { get; set; } = -1;

        public object this [int row, int col]
            => col == 0
                   ? (row == CursorRow ? "●" : " ")
                   : _inner [row, col - 1];

        public int Rows => _inner.Rows;
        public int Columns => _columns.Length;
        public string [] ColumnNames => _columns;
        public IEnumerable<Package> GetAllObjects () => _inner.GetAllObjects ();
        public Package GetObjectOnRow (int row) => _inner.GetObjectOnRow (row);
    }
}
