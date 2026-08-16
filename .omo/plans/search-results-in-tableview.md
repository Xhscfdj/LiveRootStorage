# search-results-in-tableview - Work Plan

## TL;DR (For humans)
把当前 `LRSBreadcrumb` 里「Flyout + ListView」的弹窗搜索，改为用主界面 `LrsTableView`（`MiddleFilesView` 的文件表格）显示搜索结果。搜索框内联到面包屑地址栏（点放大镜按钮后地址栏切换为搜索框，Esc 恢复）；结果加一列「位置」（父目录路径），仅搜索模式可见。搜索逻辑从面包屑代码搬进 `MainWindowViewModel`，沿用现有搜索行为（当前目录递归、名字匹配、上限 200、取消旧任务）。

- **你会得到**：一个不再依赖弹窗的 TableView 搜索；搜索结果可排序、带图标、双击目录跳转/双击文件打开；中英文都支持「位置」列头。
- **为什么这么做**：面包屑(TopView)与文件列表(MiddleFilesView)都绑定 `App.SharedViewModel`，走 VM 联动最干净；结果复用 `FileSystemNodeViewModel` 可零成本复用图标/4 列/双击/Enter。
- **不会做**：不做全盘/全局搜索（保持当前目录递归）、不改 `FastFluentFilesFolders.TableView` 库源码、不加 NuGet 依赖、不改配置面、不给搜索结果做独立右键/批量操作。
- **工作量**：约 6 个文件（MainWindowViewModel / LRSBreadcrumb.xaml+.cs / MiddleFilesView.xaml+.cs / MultiLanguageStringsViewModel / 两个 strings json / CHANGELOG.md），12 个实现任务 + 4 个终验任务。
- **风险**：工作树存在未提交改动（脏工作树）——只做增量编辑、不还原不覆盖无关改动；`TableViewColumn` 是 DependencyObject 无 DataContext，列可见性必须在 code-behind 切换（已规避）。
- **决策**：搜索范围=当前目录递归；结果模型=复用 FileSystemNodeViewModel；「位置」列值=父目录路径（`Path.GetDirectoryName`）；结果上限 200（目录+文件合计，修正旧实现只限文件不目录的不一致）；测试策略=none（本仓库无测试项目）+ agent 执行 QA。

## Scope
**IN（改动文件）**
- `FastFluentFilesFolders/ViewModels/MainWindowViewModel.cs` — 搜索状态 + 逻辑
- `FastFluentFilesFolders/UserControls/LRSBreadcrumb.xaml` + `.xaml.cs` — 内联搜索框 + 删除旧弹窗搜索
- `FastFluentFilesFolders/Views/MiddleFilesView.xaml` + `.xaml.cs` — 「位置」列 + 源切换
- `FastFluentFilesFolders/ViewModels/MultiLanguageStringsViewModel.cs` — 新增字符串属性
- `FastFluentFilesFolders/Strings/zh-Hans.json` + `en.json` — 新增字符串
- `CHANGELOG.md` — 按 AGENTS.md 写变更日志

**OUT（不碰）**
- `FastFluentFilesFolders.TableView/`（vendored 副本，运行时实际用的是 NuGet `WinUI.TableView` v1.4.1，两者都不改）
- `LrsTableView.cs` / `GroupedFileList.cs`（现有平铺模式已满足搜索展示，无需改）
- 配置面、NuGet 依赖、插件系统

## Verification strategy
- 本仓库无测试项目（AGENTS.md 明确），测试策略 = **none**，以 agent 执行的构建 + 运行期验证为主。
- 每次逻辑任务后跑 `dotnet build`（见 Success criteria 的准确命令）。
- 运行期验证：Unpackaged 启动，验证搜索/导航/退出搜索/列显隐（详见各 todo 的 QA 场景与 F3）。
- 所有 UI 更新走 `DispatcherQueue`（AGENTS.md 硬约束），后台枚举用 `Task.Run`。

## Execution strategy
- 单 worker 会话顺序执行（`$start-work search-results-in-tableview`）；文件间无并行安全收益（共享 VM/UI 线程）。
- Wave 1（VM）→ Wave 2（面包屑）→ Wave 3（MiddleFilesView）→ Wave 4（i18n + CHANGELOG + 构建）。
- 每改完一个文件即跑 `dotnet build` 确认可编译；源生成器属性（`[ObservableProperty]`）改动后必须重新 build 再检查生成代码。
- 脏工作树：只做增量编辑，`git diff` 确认未覆盖无关改动。

## Todos

### Wave 1 — ViewModel 搜索状态与逻辑

- [ ] 1. `MainWindowViewModel.cs` 新增搜索状态字段
  - References: 现有 `[ObservableProperty]` 区块（`MainWindowViewModel.cs:666-677`）；`_uiDispatcherQueue` 字段（:701）；`SelectedFolder`（:702）。
  - 在 `_isReady` 附近新增：
    ```csharp
    [ObservableProperty] private bool _isSearchMode = false;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private ObservableCollection<FileSystemNodeViewModel> _searchResults = new();
    private CancellationTokenSource? _searchCts;
    ```
  - Acceptance: `dotnet build` 通过；生成 `IsSearchMode/SearchText/SearchResults` 三个公开属性 + 各自的 `OnXxxChanged` partial。
  - QA happy: build ExitCode 0。
  - QA failure: 属性命名与已有 `_searchCts` 等冲突 → 改名；编译报 CS0101 重复 partial → 检查是否已存在同名 partial。
  - Commit: 不提交（工作树增量）。

- [ ] 2. `MainWindowViewModel.cs` 新增 `OnSearchTextChanged` 触发搜索 + `EnterSearchMode`/`ExitSearchMode`
  - References: `[ObservableProperty] string _searchText`（todo 1 新增）；旧搜索触发逻辑 `LRSBreadcrumb.xaml.cs:609-688 OnSearchTextChanged`。
  - 实现：
    ```csharp
    partial void OnSearchTextChanged(string value) => TriggerSearch(value);

    public void EnterSearchMode()
    {
        SearchResults.Clear();
        SearchText = string.Empty;
        IsSearchMode = true;
    }

    public void ExitSearchMode()
    {
        _searchCts?.Cancel();
        IsSearchMode = false;
        SearchText = string.Empty;
        SearchResults.Clear();
    }

    private void TriggerSearch(string query)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        query = query.Trim();
        if (query.Length == 0) { _uiDispatcherQueue.TryEnqueue(() => SearchResults.Clear()); return; }
        var scope = SelectedFolder?.FullPath ?? CurrentBreadcrumbPath;
        if (string.IsNullOrEmpty(scope) || !Directory.Exists(scope)) return;
        _ = Task.Run(() => RunSearchAsync(scope, query, token), token);
    }
    ```
  - Acceptance: 输入变化即触发搜索（取消旧任务）；`ExitSearchMode` 幂等可重复调用。
  - QA happy: `dotnet build` 通过；快速连续输入不抛 `ObjectDisposedException`（CTS 已 cancel）。
  - QA failure: `partial void OnSearchTextChanged` 签名与生成器不匹配（CS0260 缺 partial 修饰 / 参数类型不一致）→ 对齐 `partial void OnSearchTextChanged(string value)`。
  - Commit: 不提交。

- [ ] 3. `MainWindowViewModel.cs` 实现 `RunSearchAsync` + `CreateSearchNode`
  - References: 旧枚举逻辑 `LRSBreadcrumb.xaml.cs:633-664`；`FileSystemNodeViewModel` 构造 + `ApplyMetadata`（`FileSystemNodeViewModel.cs:155-210, 335-345`）；`OpenWithDefaultProgram`（`MainWindowViewModel.cs:878`）。
  - 实现（后台线程）：
    ```csharp
    private async Task RunSearchAsync(string scope, string query, CancellationToken token)
    {
        var results = new List<FileSystemNodeViewModel>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(scope, "*", SearchOption.AllDirectories))
            {
                if (token.IsCancellationRequested) break;
                if (Path.GetFileName(dir).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                { results.Add(CreateSearchNode(dir, true)); if (results.Count >= 200) break; }
            }
            if (results.Count < 200 && !token.IsCancellationRequested)
            foreach (var file in Directory.EnumerateFiles(scope, "*", SearchOption.AllDirectories))
            {
                if (token.IsCancellationRequested) break;
                if (Path.GetFileName(file).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                { results.Add(CreateSearchNode(file, false)); if (results.Count >= 200) break; }
            }
        }
        catch { /* 无权限目录：保留已收集的部分结果 */ }
        if (token.IsCancellationRequested) return;
        await _uiDispatcherQueue.EnqueueAsync(() =>
        {
            if (token.IsCancellationRequested) return;
            SearchResults.Clear();
            foreach (var r in results) SearchResults.Add(r);
            OnPropertyChanged(nameof(SearchResults));
        });
    }

    private FileSystemNodeViewModel CreateSearchNode(string path, bool isDir)
    {
        var node = new FileSystemNodeViewModel(path, isDir, false, AppConfigs!, _uiDispatcherQueue, true);
        try
        {
            if (isDir) { var d = new DirectoryInfo(path); node.ApplyMetadata(true, 0, d.LastWriteTimeUtc, d.CreationTimeUtc); }
            else { var f = new FileInfo(path); node.ApplyMetadata(false, f.Length, f.LastWriteTimeUtc, f.CreationTimeUtc); }
        }
        catch { }
        return node;
    }
    ```
  - Acceptance: 搜索在当前目录递归、按名匹配（OrdinalIgnoreCase）、结果合计上限 200、后台枚举不阻塞 UI、结果回填在 UI 线程且带 `OnPropertyChanged(nameof(SearchResults))` 通知。
  - QA happy: 搜索存在条目，`SearchResults` 数量 >0 且每个节点 `Name/LastModifiedTimeString/VisualSize` 非空。
  - QA failure: 无权限子目录导致整次搜索中断 → 已用 `catch { }` 包裹整体枚举（保留部分结果）；枚举在 UI 线程卡顿 → 确认在 `Task.Run` 内。
  - Commit: 不提交。

- [ ] 4. `MainWindowViewModel.cs` 集成搜索模式到 `OpenItem` + 导航退出搜索
  - References: `OpenItem`（`MainWindowViewModel.cs:782-809`）；`NavigateToPath`（:928-946）；`OnSelectedFolderChanged`（:706）。
  - 在 `OpenItem` 最顶部加：
    ```csharp
    if (IsSearchMode)
    {
        if (item.IsDirectory) { ExitSearchMode(); NavigateToPath(item.FullPath); }
        else OpenWithDefaultProgram(item.FullPath);
        return;
    }
    ```
  - 在 `NavigateToPath(string path)` 方法体最顶部加：`if (IsSearchMode) ExitSearchMode();`
  - Acceptance: 搜索模式下双击目录结果 → 退出搜索并导航到该目录；双击文件 → 打开并保持搜索；后退/前进/上级/主页/面包屑点击 → 退出搜索。
  - QA happy: 搜索 → 双击目录结果 → 地址栏恢复面包屑、文件列表显示该目录内容。
  - QA failure: 双击目录结果后列表仍是搜索结果 → 确认 `ExitSearchMode()` 先于 `NavigateToPath` 执行、MiddleFilesView 已响应 `IsSearchMode=false`（依赖 todo 8/9）。
  - Commit: 不提交。

### Wave 2 — 面包屑内联搜索框

- [ ] 5. `LRSBreadcrumb.xaml` 在 `AddressBarArea` 内新增 `SearchTextBox`
  - References: `AddressBarArea`（`LRSBreadcrumb.xaml:90-161`）；`PathTextBox`（:96-106，作为样式参照）。
  - 在 `PathTextBox` 之后、`BreadcrumbScrollViewer` 之前加入隐藏的搜索框（样式对齐 PathTextBox：透明背景、无边框、CornerRadius 16、`Visibility="Collapsed"`）：
    ```xml
    <TextBox x:Name="SearchTextBox"
             VerticalAlignment="Stretch" CornerRadius="16"
             Background="Transparent" BorderThickness="0"
             Visibility="Collapsed"
             PlaceholderText="{x:Bind local:App.ML.SearchPlaceholder}"  <!-- 若 x:Bind 不可用则用代码设置 -->
             KeyDown="OnSearchTextBoxKeyDown" />
    ```
  - 注：`PlaceholderText` 用代码设置更稳（`SearchTextBox.PlaceholderText = App.ML.SearchPlaceholder`，与现有 `RefreshTooltips` 模式一致）；若 XAML 里用 `{x:Bind}` 需确保命名空间引用正确，否则改代码设置。
  - Acceptance: 编译通过；SearchTextBox 初始隐藏，不影响现有地址栏/面包屑显示。
  - QA happy: 启动后地址栏正常显示面包屑，无多余文本框。
  - QA failure: XAML 解析错误（XamlParseException）→ 检查 `x:Name`/事件处理函数名匹配 `.cs`。
  - Commit: 不提交。

- [ ] 6. `LRSBreadcrumb.xaml.cs` 接线搜索模式 UI 切换 + 绑定 VM
  - References: `OnSearchButtonClick`（:591-607）；`EnterEditMode`/`ExitEditMode`（:325-351）；`OnBreadcrumbKeyDown`（:378-389）；构造器订阅 `App.LocalizationService.PropertyChanged`（:157-161）；`App.SharedViewModel`（`App.xaml.cs:24`）。
  - 实现：
    1. 字段 `private bool _isSearchMode;`
    2. 构造器里 `SearchTextBox.PlaceholderText = App.ML.SearchPlaceholder;` 并在 `RefreshTooltips()` 里同步；绑定 `SearchTextBox.SetBinding(TextBox.TextProperty, new Binding { Source = App.SharedViewModel, Path = new PropertyPath(nameof(MainWindowViewModel.SearchText)), Mode = TwoWay, UpdateSourceTrigger = PropertyChanged });`
    3. 订阅 `App.SharedViewModel.PropertyChanged`（构造器）→ 当 `e.PropertyName == nameof(MainWindowViewModel.IsSearchMode)` 时 `SyncSearchUi()`。
    4. `OnSearchButtonClick` 改为切换：`if (_isSearchMode) ExitSearch(); else EnterSearch();`，其中 `EnterSearch()` 调 `App.SharedViewModel.EnterSearchMode()` + 显示 SearchTextBox/隐藏 BreadcrumbScrollViewer/确保 `ExitEditMode()`、聚焦 SearchTextBox；`ExitSearch()` 调 `App.SharedViewModel.ExitSearchMode()` + 恢复。
    5. `SyncSearchUi()`：依据 `App.SharedViewModel.IsSearchMode` 同步 `_isSearchMode` 与 SearchTextBox/BreadcrumbScrollViewer 可见性。
    6. `OnSearchTextBoxKeyDown`：Esc → `ExitSearch()`，`e.Handled=true`。
    7. 守卫：`OnAddressBarAreaPointerPressed`（:283）与 `OnPathTextBoxGotFocus`（:303）在 `_isSearchMode` 时直接 return（避免进入编辑模式与搜索框冲突）。
  - Acceptance: 点搜索按钮 → 地址栏切换为搜索框并聚焦；输入实时出结果；Esc/再次点按钮 → 恢复地址栏；双击目录结果（VM 退出搜索）时面包屑自动恢复。
  - QA happy: 搜索→Esc→地址栏恢复面包屑；搜索→双击目录结果→地址栏恢复。
  - QA failure: 点搜索按钮无反应 → 确认 `OnSearchButtonClick` 仍是 XAML `Click` 目标；输入不出结果 → 确认 `SearchTextBox.Text` 双向绑定到 `SearchText`（TwoWay + PropertyChanged trigger）。
  - Commit: 不提交。

- [ ] 7. `LRSBreadcrumb.xaml.cs` 删除旧弹窗搜索代码
  - References: 旧字段 `:125-133`（`_searchCts/_searchResults/_searchFlyout/_searchTextBox/_searchResultsList/_searchStatusText/_searchContentGrid/_searchFlyoutBuilt`）；`BuildSearchFlyout`（:217-281）；`OnSearchButtonClick` 旧体（:591-607）；`OnSearchTextChanged`（:609-688）；`OnSearchTextBoxKeyDown`（:690-697）；`OnSearchFlyoutClosing`（:699-702）；`OnSearchResultItemClick`（:704-714）；`SearchResultItem` 类（:757-764）。
  - 删除以上全部（含 `SearchResultItem` 类与 `_searchResults` 字段）；保留/替换 `OnSearchButtonClick` 与 `OnSearchTextBoxKeyDown` 为 todo 6 的新实现。移除不再使用的 `using`（如 `Microsoft.UI.Xaml.Controls.Primitives` 若 Flyout 不再使用需确认，`Flyout` 类型仅搜索用）。
  - Acceptance: `dotnet build` 通过；无对已删符号的引用；`SearchResultItem` 全工程无残留引用。
  - QA happy: build 0 error；`grep SearchResultItem` 无结果（除本计划外）。
  - QA failure: 编译报未定义 `_searchFlyout` 等 → 确认引用点全部清除（含 XAML 事件）。
  - Commit: 不提交。

### Wave 3 — TableView 显示搜索结果 + 「位置」列

- [ ] 8. `MiddleFilesView.xaml` 新增 `ColLocation` 列 + 转换器资源
  - References: 现有列 `MiddleFilesView.xaml:60-157`；`Page.Resources`（:16-34）；`xmlns:uc`（:11）。
  - 在 `Page.Resources` 加 `<local:ParentDirectoryConverter x:Key="ParentDirectoryConverter"/>`（转换器类在 todo 9 定义）。
  - 在 `ColName` 之后新增列（`Visibility="Collapsed"`，`CanSort="False"`，避免分组列表对 `FullPath` 排序失效）：
    ```xml
    <tv:TableViewTemplateColumn x:Name="ColLocation" Width="2*" CanSort="False" Visibility="Collapsed">
        <tv:TableViewTemplateColumn.CellTemplate>
            <DataTemplate x:DataType="vm:FileSystemNodeViewModel">
                <TextBlock DoubleTapped="OnRowDoubleTapped"
                           Text="{Binding FullPath, Converter={StaticResource ParentDirectoryConverter}}"
                           VerticalAlignment="Center"
                           TextTrimming="CharacterEllipsis"/>
            </DataTemplate>
        </tv:TableViewTemplateColumn.CellTemplate>
    </tv:TableViewTemplateColumn>
    ```
  - Acceptance: 编译通过；列初始隐藏，正常浏览不显示「位置」。
  - QA happy: build 通过；普通浏览 4 列无「位置」列。
  - QA failure: XAML 解析失败 → 确认 `ParentDirectoryConverter` 在 `.cs` 里已 public 定义、`x:DataType` 引用 `vm` 命名空间正确。
  - Commit: 不提交。

- [ ] 9. `MiddleFilesView.xaml.cs` 新增 `ParentDirectoryConverter` + 源切换 + 列显隐
  - References: `OnViewModelPropertyChanged`（:107-115）；`UpdateGroupedSource`（:117-137）；`_watchedCollection`/`OnCurrentFolderCollectionChanged`（:34, 127-159）；现有转换器类（:952-978）；`ColLocation`（todo 8）。
  - 实现：
    1. 新增转换器：
       ```csharp
       public class ParentDirectoryConverter : Microsoft.UI.Xaml.Data.IValueConverter
       {
           public object Convert(object value, Type targetType, object parameter, string language)
               => value is string s ? (System.IO.Path.GetDirectoryName(s) ?? string.Empty) : string.Empty;
           public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
       }
       ```
    2. `OnViewModelPropertyChanged` 增补：`IsSearchMode`、`SearchResults` 两个属性变化时也调用 `UpdateGroupedSource(vm)`。
    3. `UpdateGroupedSource(vm)` 顶部加搜索分支：
       ```csharp
       if (vm.IsSearchMode)
       {
           if (_watchedCollection != null) { _watchedCollection.CollectionChanged -= OnCurrentFolderCollectionChanged; _watchedCollection = null; }
           _lastAppliedGroupedSource = null;
           ColLocation.Visibility = Visibility.Visible;
           FileGrid.UpdateSource(vm.SearchResults, grouped: false);
           return;
       }
       ColLocation.Visibility = Visibility.Collapsed;
       // ... 现有 normal 逻辑不变（含 _watchedCollection 订阅与 dedup）
       ```
  - Acceptance: 进入搜索 → 表格显示 `SearchResults`（平铺）、「位置」列可见；退出搜索 → 恢复 `CurrentFolderContent`、列隐藏；`SearchResults` 回填（带 `OnPropertyChanged`）触发一次 `UpdateSource`。
  - QA happy: 搜索后表格行数与 `SearchResults.Count` 一致；「位置」列显示父目录。
  - QA failure: 搜索后表格仍显示旧目录 → 确认 `OnViewModelPropertyChanged` 已加 `SearchResults` 分支；「位置」列不显示 → 确认 `ColLocation.Visibility` 在 code-behind 切换（`TableViewColumn` 无 DataContext，不能用 XAML `{Binding}`）。
  - Commit: 不提交。

- [ ] 10. 回归确认搜索/普通浏览切换无泄漏或陈旧残留
  - References: `LrsTableView.UpdateSource`（`LrsTableView.cs:24-34`，已复用 `_groupedSource`）；`GroupedFileList.SetItems` 平铺分支（`GroupedFileList.cs:36-48`）。
  - 确认：搜索模式用 `UpdateSource(SearchResults, false)` 走 `_groupedSource.SetItems(items, false)` 平铺分支（Clear+Add，无组头）；退出搜索恢复 `UpdateSource(CurrentFolderContent, IsCurrentFolderSpecial)`。不新增 `ItemsSource` 交换、不新建 `GroupedFileList`。
  - Acceptance: 多次进出搜索模式，`ItemsSource` 始终是同一个 `_groupedSource`（无 churn）；排序/双击在搜索结果上正常。
  - QA happy: 搜索→退出→搜索→退出 ×5，内存不单调增长（Debug 观察）；搜索结果双击文件可打开、双击目录可跳转。
  - QA failure: 搜索排序点击无反应 → 确认搜索结果非分组时 `GroupedFileList.SortWithinGroups` 平铺分支生效（已有，无需改）。
  - Commit: 不提交。

### Wave 4 — 多语言 + CHANGELOG + 构建

- [ ] 11. 多语言：新增 `ColumnLocation` 等字符串
  - References: `Strings/zh-Hans.json`、`Strings/en.json`；`MultiLanguageStringsViewModel.cs` 的 `AllPropertyNames`（:11-57）与属性区（:92 附近 `ColumnSize` 之后）。
  - 新增键：
    - `ColumnLocation`：zh-Hans = "位置"，en = "Location"
    - （可选，仅当搜索框加清除按钮时）`SearchClearTooltip`：zh-Hans = "清除搜索"，en = "Clear search"
  - 在 `MultiLanguageStringsViewModel` 的 `AllPropertyNames` 加 `nameof(ColumnLocation)`（及可选 `SearchClearTooltip`），并新增属性 `public string ColumnLocation => _loc.GetString("ColumnLocation");`
  - Acceptance: `dotnet build` 通过；中英文下「位置」列头分别显示「位置」/「Location」。
  - QA happy: 切换语言后列头文本更新（走 `RefreshAll` 的 `OnPropertyChanged` 链路）。
  - QA failure: 语言切换后列头仍是旧文本 → 确认属性名加入 `AllPropertyNames`。
  - Commit: 不提交。

- [ ] 12. `CHANGELOG.md` 按 AGENTS.md 格式写变更日志
  - References: `AGENTS.md` CHANGELOG 段（格式 `## <Version>` + `- [ChangeType Date] Changelog ...`）；`CHANGELOG.md` 当前为空。
  - 追加：
    ```
    ## 1.0.2
    - [Feature 2026-08-16] 搜索改为 TableView 显示结果：搜索框内联至面包屑地址栏（Esc 恢复），新增「位置」列（仅搜索时显示）
    ```
  - Acceptance: `CHANGELOG.md` 含上述条目，格式与 AGENTS.md 一致。
  - QA happy: 打开文件确认条目存在且格式正确。
  - QA failure: 版本号与仓库现行版本约定不符 → 按 `Package.appxmanifest`/窗口副标题的现行版本递增（此处取 1.0.2）。
  - Commit: 不提交（除非用户明确要求提交）。

## Final verification wave

- [ ] F1. 计划符合性审计：逐条核对 12 个实现 todo 是否全部落地，改动文件与 Scope 一致，无越界改动（尤其未触碰 `FastFluentFilesFolders.TableView/`、`LrsTableView.cs`、`GroupedFileList.cs`、配置面）。
  - 证据：`git diff --stat` 仅含 Scope 内文件。

- [ ] F2. 代码质量审查：`lsp_diagnostics` 对 6 个改动 `.cs`/`.xaml` 无 error；UI 更新均在 `DispatcherQueue`；无 `as any`/`@ts-ignore` 等价反模式、无空 catch 吞异常（搜索枚举的 `catch { }` 需保留注释说明为「保留部分结果」）。
  - 证据：diagnostics 输出 + 人工 review。

- [ ] F3. 运行期手动 QA（Unpackaged 启动）：
  1. 点搜索按钮 → 地址栏变搜索框 → 输入关键词 → 表格显示匹配结果、带图标、含「位置」列。
  2. 双击目录结果 → 退出搜索并导航；双击文件 → 用默认程序打开且仍停留搜索。
  3. Esc → 恢复地址栏；无结果时表格为空（无残留）。
  4. 切换中/英文 → 「位置」列头语言正确。
  - 证据：逐项记录 PASS/FAIL。

- [ ] F4. 范围保真：确认未新增 NuGet 依赖、未改配置、未实现全局搜索、未给结果做独立批量操作（Must-NOT-Have 全部满足）。
  - 证据：`git diff` 复查 + F1 联动。

## Commit strategy
- 不自动提交。所有改动留在工作树，提交仅在用户明确要求时进行。
- 若用户要求提交，用中文消息，例如 `搜索改为 TableView 显示结果`，仅暂存 Scope 内文件（含 CHANGELOG.md），不夹带工作树中其它无关改动。

## Success criteria
- `dotnet build FastFluentFilesFolders -p:WindowsPackageType=None -p:Platform=x64` ExitCode 0（本仓库标准构建命令）。
- 搜索功能：点搜索按钮 → 地址栏内联搜索框 → 输入实时出结果（当前目录递归、名字匹配、≤200）→ TableView 显示（含「位置」列）→ 双击目录跳转/双击文件打开 → Esc 恢复地址栏。
- 退出搜索后文件列表立即恢复当前目录内容（AGENTS.md「立即看到结果」）。
- 中英文「位置」列头正确。
- `CHANGELOG.md` 已按 AGENTS.md 写入条目。
- 不触碰 Scope 外文件；不新增依赖。
