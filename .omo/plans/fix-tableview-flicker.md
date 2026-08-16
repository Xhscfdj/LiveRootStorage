# Fix TableView flicker & navigation refresh

## Root cause analysis (updated)

`RebuildFlat()` calls `Clear()` + individual `Add()` on the `GroupedFileList` (an `ObservableCollection`). Two distinct phases cause visual issues:

1. **CollectionChanged events during RebuildFlat**: `Clear()` fires Reset, each `Add()` fires Add. When ItemsSource is bound, the TableView processes these incrementally → visible flicker. **FIX**: `ItemsSource = null` before `SetItems`/`SortWithinGroups`, restore after. The vendored TableView's `ItemsSourceChanged` internally uses `DeferRefresh()` to batch the restore into a single Reset.

2. **Virtualization container generation**: When ItemsSource is restored to a populated collection, the TableView's virtualizing panel creates visual containers for visible rows. This is **framework-level rendering** (ItemsControl/VirtualizingPanel infrastructure) and cannot be eliminated from outside. For time-grouped folders, the group headers + children structure makes this visually perceptible ("one-by-one replacement"). Fresh GroupedFileList allocation made it WORSE (allocation overhead + full re-render).

## Fix: ItemsSource=null isolation (reuse same GroupedFileList)

**File: `UserControls/LrsTableView.cs`**

- [x] Revert `UpdateSource`: **reuse** same `_groupedSource` (no allocation), `ItemsSource=null` before `SetItems`, restore after
- [x] `SortBy`: `ItemsSource=null` BEFORE `SortWithinGroups` (already applied ✅)
- [x] `OnSorting`: `ItemsSource=null` BEFORE `SortWithinGroups`/`ResetSort` (already applied ✅)

### UpdateSource (revert to reuse pattern):
```csharp
public void UpdateSource(ObservableCollection<FileSystemNodeViewModel> items, bool grouped)
{
    if (_groupedSource == null)
    {
        _groupedSource = new GroupedFileList();
        _groupedSource.FlatListChanged += OnFlatListChanged;
        ItemsSource = _groupedSource;
    }
    // ItemsSource=null 将 RebuildFlat 的 CollectionChanged 与 TableView 隔离，
    // 恢复后 TableView 内部 DeferRefresh 做一次完整重建
    ItemsSource = null;
    _groupedSource.SetItems(items, grouped);
    ItemsSource = _groupedSource;
}
```

### SortBy / OnSorting (keep as-is, already applied ✅)

## Already applied (don't change)
- `Configs.cs`: `LastVisitedPath` property
- `MainWindowViewModel.cs`: atomic `CurrentFolderContent` replacement, `!IsReady` guard on save, `ReferenceEquals` force-refresh in `NavigateToPath`/`OpenItem`
- `GroupedFileList.cs`: `_isGrouped` flag
- `FileTreeView.xaml.cs`: `ReferenceEquals` force-refresh
