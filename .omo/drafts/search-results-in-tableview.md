# Draft — 搜索从弹窗改为 TableView 显示搜索结果

- status: awaiting-approval
- intent: clear
- review_required: false
- slug: search-results-in-tableview

## 意图判定
CLEAR —— 把搜索（当前 `LRSBreadcrumb` 的 Flyout+ListView 弹窗）改为用主界面 `LrsTableView` 显示搜索结果。

## 用户决策（已确认）
- Q1 搜索输入框位置 = **面包屑地址栏内联**：点搜索按钮后地址栏切换成搜索框，Esc 恢复地址栏。
- Q2 结果列 = **加一列「位置」，仅搜索时显示**。

## 探索证据（源码已读，行号可溯源）
- 当前搜索全在 `UserControls/LRSBreadcrumb.xaml.cs`（`:591 OnSearchButtonClick` / `:217 BuildSearchFlyout` / `:609 OnSearchTextChanged` / `:704 OnSearchResultItemClick`），Flyout 里 TextBox+ListView+TextBlock，递归当前目录、名字匹配、上限 200。
- 主表格 `Views/MiddleFilesView.xaml:48` `LrsTableView`（`UserControls/LrsTableView.cs` 继承 `WinUI.TableView.TableView`），4 列 Name/Modified/Created/Size，数据源 `FileSystemNodeViewModel` 集合，`UpdateSource(items, grouped)` → `GroupedFileList.SetItems`；`grouped=false` 走平铺分支（`GroupedFileList.cs:42-48`）。
- `MiddleFilesView.xaml.cs:107 OnViewModelPropertyChanged` 监听 `CurrentFolderContent`/`IsCurrentFolderSpecial`；`:117 UpdateGroupedSource` 调 `FileGrid.UpdateSource`。
- 面包屑(TopView)与中间列表(MiddleFilesView)都是 `App.SharedViewModel`(MainWindowViewModel) 的兄弟控件。
- `TableViewColumn` 有 `Visibility` DP（`FastFluentFilesFolders.TableView/Columns/TableViewColumn.cs:295,595`）→ 可绑定「位置」列可见性。
- `FileSystemNodeViewModel`：构造 `(fullPath,isDir,isPlaceholder,configs,dispatcher,lazyLoad)`；`ApplyMetadata(isDir,size,lastWrite,creation)`；`Icon` 懒加载；`OpenItem` 是双击/Enter 统一入口。
- 多语言 `Strings/zh-Hans.json`+`en.json` + `MultiLanguageStringsViewModel`（`AllPropertyNames`+属性）+ `App.ML`。现有搜索键 SearchPlaceholder/SearchStartHint/SearchLoading/SearchNoResults/SearchInProgress。

## 组件（拓扑锁定）
1. 搜索状态迁移进 `MainWindowViewModel`（SearchText / IsSearchMode / SearchResults / 取消 / EnterSearchMode / ExitSearchMode / OpenSearchResult）
2. 面包屑内联搜索框（地址栏切换 + Esc 恢复）
3. TableView 显示搜索结果（MiddleFilesView 源切换 + 平铺非分组 + 「位置」列仅搜索时可见）
4. 结果交互（双击目录=退出搜索并导航；文件=打开并保持搜索）
5. 多语言新增键（ColumnLocation 等）

## 决策（采用默认，无需再问）
- 搜索范围 = 当前目录递归（沿用现行为，`SelectedFolder?.FullPath ?? CurrentBreadcrumbPath`）。
- 结果模型 = 复用 `FileSystemNodeViewModel`（图标/4 列/双击/Enter 全复用）。
- 结果元数据 = 递归枚举按名匹配（≤200）后，用 `FileInfo`/`DirectoryInfo` 取 LastWriteTime/CreationTime/Size，`ApplyMetadata` 填入。
- 「位置」列值 = `Path.GetDirectoryName(FullPath)`（父目录），用转换器实现（沿用 MiddleFilesView 已有 converter 模式）。
- 「位置」列可见性 = 绑定 `IsSearchMode`（复用 `BoolToVisibilityConverter`）。
- 搜索触发 = `OnSearchTextChanged` 里 CancellationTokenSource 取消旧任务（沿用现机制，不加额外防抖）。
- 导航（后退/前进/上级/主页/面包屑点击）时退出搜索模式。
- 结果右键/文件操作 = 不加特殊处理（现有 context menu/rename/delete 会自然作用于真实路径，非本次目标）。
- 测试策略 = **none**（AGENTS.md 明确无测试项目）+ agent-executed QA（`dotnet build` + 运行期 Debug/手动验证）。

## 范围外（Must-NOT-Have）
- 不改 `FastFluentFilesFolders.TableView` 库源码。
- 不新增 NuGet 依赖、不改配置面。
- 不引入全盘/全局搜索（保持当前目录递归）。
- 不给搜索结果做独立 context menu/批量操作。

<status>awaiting-approval</status>
