---
slug: fix-tableview-render-pipeline
status: drafting
intent: unclear
review_required: true
plan_path: .omo/plans/fix-tableview-render-pipeline.md
plan_sha256: null
review_round_id: null
pending-action: write and review .omo/plans/fix-tableview-render-pipeline.md
review:
  momus:
    status: pending
    workspace_root: null
    runtime_home: null
    target: .omo/plans/fix-tableview-render-pipeline.md
    round_id: null
    plan_sha256: null
    launch_id: null
    session: null
    result: null
  independent:
    status: pending
    workspace_root: null
    runtime_home: null
    target: .omo/plans/fix-tableview-render-pipeline.md
    round_id: null
    plan_sha256: null
    launch_id: null
    session: null
    result: null
approach: <fill: the approach you intend to plan>
---

# Draft: fix-tableview-render-pipeline

## Components (topology ledger)
<!-- Lock the SHAPE before depth. One row per top-level component that can succeed or fail independently. -->
<!-- id | outcome (one line) | status: active|deferred | evidence path -->

## Open assumptions (announced defaults)
<!-- Intent is UNCLEAR: research resolves ambiguity, defaults are adopted (not asked), and each is surfaced in the plan's human TL;DR for veto. -->
<!-- assumption | adopted default | rationale | reversible? -->
| assumption | adopted default | rationale | reversible? |
|---|---|---|---|
| Dominant cause of "one-by-one" perception | `ItemContainerTransitions` (Entrance/Content/AddDelete transitions) animating each re-realized row container | TableView.xaml:64-73 sets them; ListView re-realizes on Reset; user sees row-by-row animation | yes (XAML change) |
| Dominant cause of lag | Per-row `DispatcherQueue.TryEnqueue` in `PrepareContainerForItemOverride` (TableView.cs:118) + O(rows) realization on every Reset | each realized row enqueues separate UI-thread callback | yes (code change) |
| Which surface to change | Prefer app-side `LrsTableView` overrides over editing vendored theme, to keep vendor diff minimal | vendored control is a project reference; app-side subclass override is least invasive | yes |
| Manual QA channel | Launch the WinUI app unpackaged (`dotnet run --project FastFluentFilesFolders`), navigate into Downloads (time-grouped), observe whether rows appear with animation/flicker and whether navigation feels laggy | user-visible behavior; no test project exists | n/a |

## Decisions (with rationale)

- **D1. Disable/remove item-container transitions for the file grid.** Set `ItemContainerTransitions` to an empty/`null` TransitionCollection in `LrsTableView` (code-behind ctor or XAML), removing `EntranceThemeTransition`, `ContentThemeTransition`, `AddDeleteThemeTransition`, `ReorderThemeTransition`. Rationale: these transitions are exactly what makes re-realized rows animate in one-by-one; the file grid should swap content instantly. This is the primary fix for the "one-by-one replacement" perception.
- **D2. Avoid per-row deferred dispatcher work during bulk refresh.** `PrepareContainerForItemOverride` enqueues per-row work via `DispatcherQueue.TryEnqueue`. Options: (a) guard `LrsTableView` to suppress/batch this during `UpdateSource`; (b) accept it but reduce total rows realized by NOT forcing full re-realization on refresh (avoid `ItemsSource=null` churn — reuse pattern already avoids the fresh-allocation worst case). Decision: keep the reuse pattern (already applied) and evaluate whether D1 alone removes the perceptible lag; if not, add a suppression flag in the vendored `PrepareContainerForItemOverride` gated by a public property on TableView that `LrsTableView` sets during `UpdateSource`.
- **D3. Keep `ItemsSource=null` isolation** in `UpdateSource`/`SortBy`/`OnSorting` (already applied) — it ensures RebuildFlat's Clear+Add CollectionChanged events never reach the CollectionView as incremental updates, so the CollectionView fires a single Reset.
- **D4. Do NOT re-introduce fresh GroupedFileList per call** — measured worse (allocation + full re-render of all rows). Reuse pattern stands.

## Scope IN

- `FastFluentFilesFolders/UserControls/LrsTableView.cs` — add `ItemContainerTransitions` removal (ctor or attached), keep reuse UpdateSource + SortBy/OnSorting.
- (If D2-b chosen) `FastFluentFilesFolders.TableView/TableView.cs` — gate the per-row `DispatcherQueue.TryEnqueue` work behind a public suppressible property; `LrsTableView` sets it during `UpdateSource`.
- Manual-QA: launch app, navigate Downloads (time-grouped) + a large flat folder, verify no row-by-row animation/flicker and no noticeable lag.

## Scope OUT (Must NOT have)

- No changes to GroupedFileList grouping semantics, sort logic, or folder content pipeline (MainWindowViewModel / MiddleFilesView) beyond what's already applied.
- No new NuGet packages, no project restructure.
- No functional change to file operations (paste/delete/rename) — only the visual/refresh pipeline.
- No re-introduction of fresh-GroupedFileList-per-call.

## Open questions

(none — research resolved the open unknowns; D2-a vs D2-b is a plan-internal decision resolved by "try D1 first, add D2-b only if lag persists".)

## Approval gate
status: approved
<!-- When exploration is exhausted and unknowns are answered, set status: awaiting-approval. -->
<!-- That durable record is the loop guard: on a later turn read it and resume at the gate instead of re-running exploration. -->
- Approach: D1 (remove ItemContainerTransitions in LrsTableView) as primary fix; keep reuse UpdateSource + ItemsSource=null isolation; optionally D2-b (gate per-row dispatcher work) as a conditional second task gated on D1 QA result.
- **REVIEW RESULTS (dual high-accuracy, run 2026-08-09):**
  - Momus: **APPROVE** — all plan references verified against actual files; root-cause chain supported; D1 concrete & correctly scoped; guardrails sufficient.
  - Oracle: **APPROVE WITH ADJUSTMENTS** — Todo 1 ctor local value beats theme style setter (verified precedence); no risk to selection/keyboard/reorder; **Todo 2 timing corrected** (flag must stay true past ItemsSource re-attach, reset via LOW-priority dispatch to cover the async layout/realization pass); alternative noted (editing theme XAML directly, rejected to keep app-side diff); bonus observation re `ScrollRowIntoView` at TableView.cs:99.
  - Plan updated to incorporate Oracle's Todo 2 timing fix.
- Next workflow action: execution (user ran /start-work → approval granted). Set up Boulder state, register todos, dispatch Todo 1.

## Findings (cited - path:lines)

- **App-side current state (verified directly, 2026-08-07):**
  - `FastFluentFilesFolders/UserControls/LrsTableView.cs:24-37` — `UpdateSource` reuses one `_groupedSource`; `ItemsSource = null` → `_groupedSource.SetItems(items, grouped)` → `ItemsSource = _groupedSource`.
  - `LrsTableView.cs:39-59` — `SortBy` sets `ItemsSource = null` before `SortWithinGroups`.
  - `LrsTableView.cs:82-122` — `OnSorting` sets `ItemsSource = null` before `SortWithinGroups`/`ResetSort`.
  - `LrsTableView.cs:61-66` — `OnFlatListChanged` re-nulls/re-sets ItemsSource (used when GroupedFileList's `FlatListChanged` fires).
  - Prior session notes: fresh-GroupedFileList-per-UpdateSource caused allocation overhead + full re-render → user reported MORE lag ("更卡了一些，问题还是在"). Reverted to reuse pattern.
  - Prior session notes: `ItemsSource=null` isolation alone did NOT fully remove the "one-by-one replacement" perception in time-grouped folders; hypothesis was virtualization container generation (framework-level).
- **vendored TableView (FastFluentFilesFolders.TableView, verified directly):**
  - `TableView.cs:33` — `public partial class TableView : ListView` → inherits WinUI ListView virtualization + transitions infra.
  - `TableView.cs:42` — `private readonly CollectionView _collectionView = [];`
  - `TableView.cs:63` — `base.ItemsSource = _collectionView;` → CollectionView (wrapping GroupedFileList) drives ListView item generation.
  - `TableView.cs:114-138` — `PrepareContainerForItemOverride`: per-row `DispatcherQueue.TryEnqueue(...)` (line 118) runs `EnsureCellsStyle`/`ApplyCellsSelectionState`/`ApplyDetailsPaneState` → **one deferred dispatcher callback per realized row** → O(rows) UI-thread callbacks = lag on large folders.
  - `TableView.cs:816-832` — `ItemsSourceChanged` uses `using var defer = _collectionView.DeferRefresh(); _collectionView.Source = null!;` then reassigns → batching at CollectionView level (ItemsSource/CollectionView.Deffer.cs:18-30 fires ONE Reset on deferral completion).
  - `CollectionView.cs:312-338` — `HandleSourceChanged()`: `_view.Clear()` + `_view.AddRange(Source)` + `OnVectorChanged(Reset)` → single Reset vector change; incremental `ItemInserted`/`ItemRemoved` only for single-item Adds/Removes while bound (lines 195-242).
  - `TableView.Properties.cs:861-869` — `OnItemsSourceChanged` → `ItemsSourceChanged(e)` + `SelectedCellRanges.Clear()` + `OnCellSelectionChanged()`.
  - **`Themes/TableView.xaml:64-73` — `ItemContainerTransitions` includes `AddDeleteThemeTransition`, `ContentThemeTransition`, `ReorderThemeTransition`, `EntranceThemeTransition IsStaggeringEnabled="False"`** → every re-realized row container ANIMATES in → visual "one-by-one replacement".
  - **`Themes/TableView.xaml:74-80` — `ItemsPanel` = `ItemsStackPanel Orientation="Vertical"`** → virtualizing panel, lazily realizes visible rows.
  - `Themes/TableView.xaml:127` — `<ItemsPresenter .../>` inside ScrollViewer.
  - `TableView.Events.cs` — no VectorChanged wiring; ListView infra handles it via CollectionView as ItemsSource.
- **User-reported symptoms consistent with:**
  - Transitions animate each row in → "眼前项一项一项被替换的感觉".
  - Per-row `DispatcherQueue.TryEnqueue` in PrepareContainerForItemOverride → lag scaling with row count.
  - fresh-GroupedFileList → every nav re-realized ALL rows (transition animation + dispatcher work) → MORE lag.

## Pending exploration (2 background agents dispatched 2026-08-07 — BOTH FAILED with [401] Provider returned error; research completed directly by root as read-only)
