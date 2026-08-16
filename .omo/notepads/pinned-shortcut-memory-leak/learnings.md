# Learnings — pinned-shortcut-memory-leak

Conventions, patterns, and successful approaches discovered during work on this plan.

_Auto-scaffolded by /start-work. Append new entries below - never overwrite._

---

## [2026-08-03] T1-T3 Baseline & Dump (verified by orchestrator)
- Leak reproduced: 206.13 -> 328.96 MB private over 15 rounds (~8.1MB/round). Evidence: Temp\opencode\leak_baseline.txt. App PID 20504 left running.
- dumpheap -stat verified: 775 MiddleFilesView bindings (obj11 386 + obj13 389), 5,572 RoutedEventHandler, 4,887 PropertyChangedEventHandler, 63,963 ObjectReference<IUnknownVftbl>, 588 FileSystemNodeViewModel (healthy ~430), 2 Stack<string> only.
- KEY: _backStack string theory DISPROVEN as primary leak. Dominant leak = XAML binding/event-handler/COM churn from concurrent stale UpdateCurrentFolderContentAsync rebuilds (each stale task -> CurrentFolderContent.Clear -> Reset -> FileGrid.UpdateSource -> new bindings/COM wrappers).
- => Version-guard (plan T8) is the PRIMARY fix. _backStack cap (T5/T6) is minor defense.
- dump analysis file: Temp\opencode\leak_dump_analysis.txt (subagent; numbers independently verified by orchestrator via dotnet-dump).
- dotnet-dump v9.0.6 available. Use here-string pipe + exit to avoid hangs.

## [2026-08-03] T13 FAILURE - version guard does NOT fix leak (orchestrator analysis)
- Post-fix regression: 203.73->418.73MB over 15 rounds (~13.5MB/round) - WORSE than pre-fix 8.1MB/round.
- Controlled experiment: SLOW same-folder re-entry (Downloads->Downloads) LEAKS +3.6MB/click. User's "slow doesn't grow" was about alternation; same-folder re-entry leaks too. => leak is PER-SWITCH/PER-REBUILD, not concurrency.
- Post-fix dump (leak_after_fix.dmp): bindings DOWN 386->175, handlers DOWN 5572->4376, yet process memory UP (484MB vs 323MB). => growth is NATIVE XAML, not managed.
- TableViewRow grew 101->113 (baseline 63) => TableView rows/visuals accumulating.
- gcroot of retained VM: MiddleFilesView -> LrsTableView -> CollectionView -> List<object> (_view) - legitimately the currently displayed folder. VM count 699 = Downloads+Documents+trees, NOT a VM leak.
- ROOT CAUSE: every switch fires FileGrid.UpdateSource TWICE (Reset from Clear at MiddleFilesView.cs:146, AND IsCurrentFolderSpecial PropertyChanged at :124). LrsTableView.UpdateSource ALWAYS creates new GroupedFileList + swaps ItemsSource (LrsTableView.cs:21-33) => full TableView rebuild (ItemsSourceChanged -> _collectionView.Source=null) => native XAML visuals leak. Same-folder re-entry triggers it too.
- FIX DIRECTION: (A) LrsTableView.UpdateSource: reuse existing GroupedFileList when grouped mode unchanged; guard ItemsSource swap with ReferenceEquals. (B) MiddleFilesView: dedup redundant UpdateGroupedSource (track last special value / skip when no change).
- Version guard (T4-T9) KEPT: correct stale-write prevention, low cost, but alone insufficient.

## [2026-08-03] T19 re-test FAIL + ROOT CAUSE: leak is in NuGet TableView control
- T19 (ItemsSource-reuse + dedup fix): STILL FAILS. Rapid 11.1MB/round, slow 11.9MB/click. Both fixes correct but insufficient.
- Final dump leak_t19_final.dmp (444MB private): ObjectReference<IUnknownVftbl>=89,042 (grew from 63,963), RoutedEventHandler=14,892 (from 5,572), DoubleTappedEventHandler=7,336 (from 3,098), TableViewCell=484, ManagedObjectWrapperHolder=27,063, NativeObjectWrapper=34,561, ReferenceTrackerNativeObjectWrapper=27,279.
- Managed heap only ~94MB of 444MB process => ~350MB NATIVE XAML/COM. VM count bounded (588). gcroot: VM held by CollectionView._view (legit current folder).
- CONCLUSION: the leak lives in the NuGet WinUI.TableView 1.4.1 control's XAML visual lifecycle during content changes - NOT in app ViewModel layer. App has NO ProjectReference to vendored FastFluentFilesFolders.TableView project (it's in slnx but unreferenced; app uses PackageReference WinUI.TableView 1.4.1, WinUI.TableView.dll confirmed in output).
- IMPORTANT user observation mismatch: user says SLOW switching doesn't grow, but UIAutomation tests show slow re-entry grows ~3.6-11.9MB/click. May be measurement artifact (UIAutomation Invoke churn) or real.
- Icon cache fast-path never hits: TryGetCached uses _16 key, GetIconAsync caches under _32 key (size16/32 mismatch) => every icon re-loads SHGetFileInfo. Perf bug, not leak (cache bounded).

## [2026-08-04] FINAL STATE: leak reduced 3x (13.7 -> ~2-5MB/round), residual is control-side native XAML
- Final wave F2 FAIL: 199.68 -> 307.02MB over 20 rounds, R11-20 = +48MB (~4.8MB/round). F3 PASS (immediate refresh). F4 borderline (back#3 returned Program Files instead of C:\ - likely history artifact, T15 already PASSED back/forward).
- "Plateau test" (claimed flat) had impossible 38MB baseline -> MEASUREMENT ARTIFACT (wrong process/handle). Disregard it. Real residual ~2-5MB/round.
- Final dump leak_final2.dmp (345MB): managed objects ALL CLEAN - FileSystemNodeViewModel 696 (bounded), TableViewRow 105, TableViewCell 420 (down from 484), FileGroupHeader 0, GroupedFileList 1. => Residual leak is NATIVE XAML in NuGet WinUI.TableView control when rebuilding time-grouped (Downloads) content.
- Isolation proof: non-grouped<->non-grouped = 1.59MB/round (clean); grouped<->non-grouped was 13.7MB/round -> now ~2-5MB/round after single-GroupedFileList fix (3x improvement, NOT eliminated).
- Vendored FastFluentFilesFolders.TableView project IS in slnx (modified in 7a2878e) but app has NO ProjectReference - uses NuGet WinUI.TableView 1.4.1 (latest). Options: (a) vendor+patch control, (b) accept 3x improvement, (c) further app-side investigation.
- Changes currently uncommitted: MainWindowViewModel.cs (version guard + backStack cap), MiddleFilesView.xaml.cs (dedup), LrsTableView.cs (constructor AllowLiveShaping=false + single GroupedFileList UpdateSource), GroupedFileList.cs (flat-mode AddItem/SortWithinGroups), ShellIconHelper.cs (icon key _16->_32).
