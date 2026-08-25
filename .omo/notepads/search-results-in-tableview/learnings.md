# Learnings — search-results-in-tableview

Conventions, patterns, and successful approaches discovered during work on this plan.

_Auto-scaffolded by /start-work. Append new entries below - never overwrite._

---

## Wave 1 (Todos 1-4) — search state & logic in MainWindowViewModel.cs

- `FileSystemNodeViewModel` 的构造签名为 `(string fullPath, bool isDirectory, bool isPlaceholder, Configs configs, DispatcherQueue uiDispatcherQueue, bool lazyLoad)`（6 参），与 `CreateSearchNode` 中使用的 `new FileSystemNodeViewModel(path, isDir, false, AppConfigs!, _uiDispatcherQueue, true)` 完全匹配。
- `ApplyMetadata(bool isDirectory, long size, DateTime lastWriteUtc, DateTime creationUtc)`、`SafeGetDirs(string)`、`SafeGetFiles(string)` 均为 public（同命名空间 `FastFluentFilesFolders.ViewModels`），无需额外 using。
- `[ObservableProperty]` 新字段（_isSearchMode/_searchText/_searchResults）会产生与其他既有字段一致的 MVVMTK0045 警告，属项目既有模式，非本次新增问题。
- 所有 UI 回写均通过 `_uiDispatcherQueue`（TryEnqueue/EnqueueAsync）；后台搜索在 `TriggerSearch` 的 `Task.Run` 中执行，避免阻塞 UI 线程。
- 构建：`dotnet build FastFluentFilesFolders/FastFluentFilesFolders.csproj -p:Platform=x64 -p:WindowsPackageType=None` 通过（0 错误，179 条均为既有 nullable/ObservableProperty 警告）。

## Wave 2 (Todo 11) — ColumnLocation i18n key

- 新增 `ColumnLocation` 本地化键：zh-Hans.json = "位置"、en.json = "Location"，均插在 `"ColumnSize"` 之后、`"CalculateSize"` 之前（JSON 第 39 行）。
- `MultiLanguageStringsViewModel.cs`：属性 `public string ColumnLocation => _loc.GetString("ColumnLocation");` 紧跟 `ColumnSize` 属性（第 96 行）；`nameof(ColumnLocation)` 追加进 `AllPropertyNames` 数组（`nameof(ColumnSize)` 之后，第 19 行），确保语言切换时 `RefreshAll()` 会重新触发该属性的 PropertyChanged。
- 验证：两个 JSON 均通过 `ConvertFrom-Json` 解析；构建 0 错误退出码 0。后续 MiddleFilesView 波次可直接使用 `ML.ColumnLocation`。

## Wave 3 (Todos 8-10) — search results display in MiddleFilesView

- `MiddleFilesView.xaml`：`Page.Resources` 新增 `ParentDirectoryConverter`（key `ParentDirectoryConverter`）；在 `ColName` 之后、`ColModifiedDate` 之前插入 `ColLocation` 列（`Width="2*" CanSort="False" Visibility="Collapsed"`，CellTemplate 绑定 `FullPath` + 转换器，`DoubleTapped="OnRowDoubleTapped"`）；状态栏计数绑定由 `CurrentFolderContent.Count` 改为 `DisplayedItemCount`。
- `MiddleFilesView.xaml.cs`：新增 `ParentDirectoryConverter : IValueConverter`（`Path.GetDirectoryName(s) ?? string.Empty`），与既有 BoolToVisibilityConverter 等同模式。`OnViewModelPropertyChanged` 扩展 `IsSearchMode`/`SearchResults` 分支。`UpdateGroupedSource` 顶部新增搜索分支：先取消 `_watchedCollection` 订阅并置 null，`_lastAppliedGroupedSource = null`（绕过普通 dedup），`ColLocation.Visibility = Visible`，`FileGrid.UpdateSource(vm.SearchResults, grouped:false)` 后 return；普通路径顶部置 `ColLocation.Visibility = Collapsed`。`RefreshHeaders` 追加 `ColLocation.Header = ML.ColumnLocation`。`OnFileGridContextRequested` 与 `GetSelectedItems` 顶部加 `IsSearchMode` 只读守卫。
- **Todo-10 验证（仅代码审查，未改动 LrsTableView.cs / GroupedFileList.cs）**：`LrsTableView.UpdateSource` 仅在 `_groupedSource == null` 时创建 `GroupedFileList` 并赋 `ItemsSource`（一次性），之后每次调用 `_groupedSource.SetItems(items, grouped)`；`SetItems` 的 `!grouped` 分支走 `Clear()`+`Add()` 平铺路径，不新建 GroupedFileList、不换 ItemsSource —— 满足“FileGrid.ItemsSource 引用稳定”。`_watchedCollection` 在搜索分支里先 `-=` 再置 null，普通分支也是先 `-=` 再重新 `+=`，重复进出搜索不会累积重复订阅。
- 构建：`dotnet build FastFluentFilesFolders/FastFluentFilesFolders.csproj -p:Platform=x64 -p:WindowsPackageType=None` 0 错误、181 条既有警告（MVVMTK0045/MVVMTK0034、PRI 等，均为项目既有），退出码 0。

