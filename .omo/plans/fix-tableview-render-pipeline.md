# fix-tableview-render-pipeline - Work Plan

## TL;DR (For humans)

**What you'll get:** 文件网格在导航/刷新/排序时不再有"一行一行跳进来"的动画和卡顿——文件夹内容瞬间整体切换。

**Why this approach:** 已定位真正根因：vendored TableView 的主题给每行容器配了入场动画（Entrance/Content/AddDelete transitions），且每次导航时每行都单独排入 UI 线程调度。方案是在应用侧 LrsTableView 里关掉这些动画，并（如仍有卡顿）给 vendored 的行准备逻辑加一个可抑制开关。

**What it will NOT do:** 不改分组/排序/文件操作逻辑；不重新引入"每次新建 GroupedFileList"（实测更卡）；不动 MainWindowViewModel/MiddleFilesView 的内容管线。

**Effort:** Short
**Risk:** Low - 应用侧覆盖主题 + 一个 vendored 开关，均有回退点
**Decisions I made for you:** D1 关闭 ItemContainerTransitions 为主修复；D2 仅在 D1 后仍有卡顿时启用 vendored 行准备抑制开关；保留已应用的 ItemsSource=null 隔离与复用模式。

Your next move: approve this plan, then run a high-accuracy review (already requested for this UNCLEAR route). Full execution detail follows below.

---

> TL;DR (machine): Short effort, Low risk - remove TableView ItemContainerTransitions + optional per-row dispatcher suppression to eliminate row-by-row animation & lag in the file grid.

## Scope
### Must have
- Remove item-container transition animations on the file grid so rows swap instantly (no Entrance/Content/AddDelete animation) — implemented app-side in `LrsTableView`.
- Keep the applied `ItemsSource = null` isolation in `UpdateSource`/`SortBy`/`OnSorting` (reuse `_groupedSource`, no fresh allocation).
- If a Manual-QA pass shows navigation still laggy on a large/time-grouped folder, add a vendored suppression flag so `PrepareContainerForItemOverride`'s per-row `DispatcherQueue.TryEnqueue` work is batched/skipped during `UpdateSource`.
- Manual QA: launch the app unpackaged, navigate Downloads (time-grouped) + a large flat folder, confirm no row-by-row animation/flicker and no noticeable lag.

### Must NOT have (guardrails, anti-slop, scope boundaries)
- No changes to GroupedFileList grouping semantics, sort logic, or folder content pipeline (MainWindowViewModel / MiddleFilesView) beyond what's already applied.
- No re-introduction of fresh-GroupedFileList-per-call.
- No new NuGet packages, no project restructure.
- No functional change to file operations (paste/delete/rename/context menus) — only the visual/refresh pipeline.

## Verification strategy
> Zero human intervention - all verification is agent-executed.
- Test decision: none (repo has no test projects) + `dotnet build` as the automated gate; Manual-QA via launching the WinUI app.
- Evidence: `.omo/evidence/` (subdirectory per task).

## Execution strategy
### Parallel execution waves
> Wave 1: Todo 1 (LrsTableView transitions removal) — single-file app-side change. Wave 2 (conditional): Todo 2 (vendored suppression) only if Wave 1 QA shows residual lag. Wave 3: Final verification wave.

### Dependency matrix
| Todo | Depends on | Blocks | Can parallelize with |
| --- | --- | --- | --- |
| 1. LrsTableView ItemContainerTransitions removal | none | 2 (if needed), F1-F4 | — |
| 2. (conditional) Vendored PrepareContainerForItemOverride suppression | 1 (QA shows lag) | F1-F4 | — |
| F1-F4 Final verification wave | 1 (and 2 if taken) | — | with each other |

## Todos
> Implementation + Test = ONE todo. Never separate.
- [x] 1. Remove item-container transitions on the file grid
  What to do / Must NOT do: In `FastFluentFilesFolders/UserControls/LrsTableView.cs`, disable the vendored TableView's `ItemContainerTransitions` (Entrance/Content/AddDelete/Reorder) so re-realized row containers swap without animation. Preferred: set `ItemContainerTransitions = null` (or a `new TransitionCollection()`) in the `LrsTableView` constructor. Must NOT edit the vendored theme XAML for this todo (D1 is app-side only); must NOT change UpdateSource/SortBy/OnSorting behavior; must NOT touch any other file.
  Parallelization: Wave 1 | Blocked by: none | Blocks: 2 (if needed), F1-F4
  References: `FastFluentFilesFolders/UserControls/LrsTableView.cs:15-22` (ctor), `FastFluentFilesFolders.TableView/Themes/TableView.xaml:64-73` (ItemContainerTransitions source), `FastFluentFilesFolders.TableView/TableView.cs:33` (TableView : ListView)
  Acceptance criteria (agent-executable): `dotnet build` in repo root exits 0; `ItemContainerTransitions` is null/empty on a constructed `LrsTableView` (assert via code inspection / a debug print).
  QA scenarios: Manual - `dotnet run --project FastFluentFilesFolders`, navigate to Downloads (time-grouped): rows must swap instantly with NO entrance/transition animation. Happy: instant swap. Failure: if rows still animate, todo fails. Evidence `.omo/evidence/task-1-lrs-tableview-transitions.txt`.
  Commit: N | refactor(LrsTableView): disable item container transitions

- [~] 2. (conditional) Gate per-row dispatcher work during bulk refresh
  What to do / Must NOT do: ONLY if Todo 1 QA shows navigation still laggy on a large/time-grouped folder. BLOCKED: trigger is F3 Manual-QA showing residual lag — user must run the app and confirm lag persists before this todo is enabled. Add a public bool property `SuppressItemPreparation` (default false) on `FastFluentFilesFolders.TableView/TableView.cs`; in `PrepareContainerForItemOverride` (TableView.cs:114-138) `if (SuppressItemPreparation) return;` AFTER `base.PrepareContainerForItemOverride(element, item)` and BEFORE the `DispatcherQueue.TryEnqueue(...)` block (line 118). CRITICAL TIMING (per Oracle review): `PrepareContainerForItemOverride` fires during the LAYOUT pass that happens AFTER `UpdateSource` returns, so the flag must stay true past the ItemsSource re-attach and be reset on a LOW-priority dispatch so it covers realization then resets before user interaction:
  ```csharp
  // in LrsTableView.UpdateSource, around the swap:
  ItemsSource = null;
  ((TableView)this).SuppressItemPreparation = true;
  _groupedSource.SetItems(items, grouped);
  ItemsSource = _groupedSource;
  // reset AFTER the layout/realization pass, before user input
  DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
      ((TableView)this).SuppressItemPreparation = false);
  ```
  Must NOT change any other behavior; must NOT touch GroupedFileList/MiddleFilesView/MainWindowViewModel; default must remain false (no behavior change when not set).
  Parallelization: Wave 2 | Blocked by: 1 (QA shows lag) | Blocks: F1-F4
  References: `FastFluentFilesFolders.TableView/TableView.cs:114-138` (PrepareContainerForItemOverride + DispatcherQueue.TryEnqueue at 118; `GetContainerForItemOverride` already sets row.TableView + _rows, so the TryEnqueue block is only visual polish: EnsureCellsStyle/ApplyCellsSelectionState/ApplyDetailsPaneState/ApplyCurrentCellState), `FastFluentFilesFolders/UserControls/LrsTableView.cs:24-37` (UpdateSource)
  Acceptance criteria (agent-executable): `dotnet build` exits 0; code inspection confirms the flag gates the per-row block; default is false; the LOW-priority reset dispatch is present.
  QA scenarios: Manual - same `dotnet run` navigation scenario: lag must be gone on large folders. Happy: no perceptible lag. Failure: lag persists. Evidence `.omo/evidence/task-2-prep-suppression.txt`.
  Commit: N | perf(TableView): allow suppressing per-row container prep during bulk refresh

## Final verification wave
> Runs in parallel after ALL todos. ALL must APPROVE. Surface results and wait for the user's explicit okay before declaring complete.
- [x] F1. Plan compliance audit - every todo done, plan reads back consistently, no scope creep
- [x] F2. Code quality review - `dotnet build` 0 errors; changed code reviewed line-by-line (no stubs/placeholders)
- [~] F3. Real manual QA - BLOCKED: user must launch the app (`dotnet run --project FastFluentFilesFolders`), navigate Downloads (time-grouped) + large flat folder, and visually confirm no row-by-row animation and no perceptible lag. Human perception test — cannot be agent-executed from this environment.
- [x] F4. Scope fidelity - only the planned files changed; no changes to grouping/sort/file-op logic; no fresh-GroupedFileList reintroduced

## Commit strategy
- Commit per verified todo (type refactor/perf as noted on each todo). Do NOT commit unverified changes.
- The repo tracks many uncommitted changes from prior sessions; only stage files touched by THIS plan's todos.

## Success criteria
- `dotnet build` (repo root) exits 0.
- Manual QA on the running WinUI app: navigating to Downloads (time-grouped) and a large flat folder shows an INSTANT content swap — no one-by-one row animation, no perceptible navigation lag.
- `UpdateSource` still reuses one `_groupedSource` with `ItemsSource = null` isolation (no fresh allocation).
- If Todo 2 was taken: per-row container-prep dispatcher work is suppressed during bulk refresh, gated by a default-false public flag.
