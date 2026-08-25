# fast-search-indexing - Work Plan

## TL;DR (For humans)
把「当前目录递归枚举」的慢搜索，改成「Windows 系统索引 + 慢速回退」：在已索引位置（用户目录/库，如 Downloads/Documents）用 WinRT `Windows.Storage.Search` 直接查系统索引，毫秒级出结果；非索引位置（C:\、外置盘）或索引查询无结果/失败时，回退到「优化过的递归枚举」（去掉每条目冗余的 `Directory.Exists/File.Exists` 检查）。范围仍为当前目录递归，不做全局搜索。

- **你会得到**：主流位置（用户目录/库）搜索近乎即时；其他位置比现在快但仍非即时；结果展示/图标/双击/「打开文件位置」/进度条不变。
- **为什么这么做**：WinRT `QueryOptions.IndexerOption=OnlyUseIndexer` 是 WinUI3 查系统索引的最简方式，**零新增 NuGet、无需管理员**（用户已选此方案）；回退枚举是兜底、永远正确。
- **不会做**：不做全局搜索、不加管理员/MFT、不加 OleDb/COM/NuGet、不加配置项、不改搜索 UI。
- **工作量**：2 个文件（新增 `Services/WindowsSearchHelper.cs` + 改 `MainWindowViewModel.cs`），3 个实现任务 + 4 个终验任务。
- **风险与已知限制**：① 索引快路径可能落后于索引器（新建文件尚未入索引、隐藏/系统文件、未索引类型、OneDrive 占位符）→ 结果可能不完整，但**结果不保证与递归完全一致**（v1 接受）；② 索引位置「确实无匹配」会多走一次慢回退（结果仍正确）；③ 快路径与回退的结果顺序可能不同（索引器顺序 ≠ DFS 顺序）；④ 工作树有未提交改动（只做增量编辑）。
- **决策**：范围=当前目录递归；后端=WinRT 系统索引；回退=优化递归枚举；AQS 名字匹配 `System.ItemName:~"q"`（目录）/`System.FileName:~"q"`（文件）；查询词含文件名非法字符（`" * ? : < > |`）时跳过索引直接回退；结果复用 `FileSystemNodeViewModel` + `CreateSearchNode`；测试策略=none + agent QA。

## Scope
**IN（改动文件）**
- `FastFluentFilesFolders/Services/WindowsSearchHelper.cs`（**新增**）— WinRT 索引查询
- `FastFluentFilesFolders/ViewModels/MainWindowViewModel.cs` — `RunSearchAsync` 编排 + `SearchRecursive` 精简回退 + 两个安全枚举辅助方法

**OUT（不碰）**
- `LRSBreadcrumb.*`、`MiddleFilesView.*`、`LrsTableView.cs`、`GroupedFileList.cs`、`FileSystemNodeViewModel.cs`（`SafeGetDirs/SafeGetFiles` 仍被面包屑/树/计算大小共用，不改其语义）、`FastFluentFilesFolders.TableView/`、配置面、NuGet 依赖、插件系统。

## Verification strategy
- 本仓库无测试项目（AGENTS.md 明确），测试策略 = **none**，以 agent 执行的构建 + 运行期验证为主。
- `dotnet build FastFluentFilesFolders/FastFluentFilesFolders.csproj -p:Platform=x64 -p:WindowsPackageType=None` ExitCode 0。
- 运行期：Unpackaged 启动冒烟（进程存活 + 窗口句柄）；`QueryIndexAsync` 内 `Debug.WriteLine` 记录耗时，索引位置应 <~500ms；回退结果与旧递归一致。
- 所有 UI 更新走 `DispatcherQueue`；索引查询与回退枚举都在 `Task.Run` 后台线程。

## Execution strategy
- 单 worker 会话顺序执行；先新增 helper，再改 VM（helper 被 VM 引用，需先落地）。
- 每改完跑 `dotnet build`；脏工作树只做增量编辑。

## Todos

### Wave 1 — 索引查询后端 + VM 编排

- [x] 1. `Services/WindowsSearchHelper.cs`（新增）— WinRT 系统索引查询 `QueryIndexAsync`
  - References: 项目已用 WinRT `StorageFile`（`Services/WindowsIconProvider.cs`）；静态 helper 模式参照 `Services/NativeContextMenuHelper.cs`、`ShellIconHelper.cs`；`MainWindowViewModel.cs:732 RunSearchAsync`。
  - 新增 `namespace FastFluentFilesFolders.Services` 下 `public static class WindowsSearchHelper`，单一公开方法（已含 A1 空返回、A2 分页、A3 空值、D1 字符集守卫、B6 过期条目守卫）：
    ```csharp
    public static async Task<List<(string Path, bool IsDirectory)>> QueryIndexAsync(
        string scope, string query, CancellationToken token)
    {
        // 查询词含文件名非法字符（也是 AQS 特殊字符）→ 不用索引，由调用方回退到递归枚举
        if (query.IndexOfAny(new[] { '"', '*', '?', ':', '<', '>', '|' }) >= 0)
            return new List<(string Path, bool IsDirectory)>();

        var result = new List<(string Path, bool IsDirectory)>();
        try
        {
            var folder = await StorageFolder.GetFolderFromPathAsync(scope);
            var escaped = query.Replace("\"", "\"\"");

            // 目录（先目录后文件，与递归回退顺序一致）；分页拉取，最多 200
            var folderOptions = new QueryOptions
            {
                FolderDepth = FolderDepth.Deep,
                IndexerOption = IndexerOption.OnlyUseIndexer,
                ApplicationSearchFilter = $"System.ItemName:~\"{escaped}\""
            };
            var dirs = await folder.CreateFolderQueryWithOptions(folderOptions).GetFoldersAsync(0, 200);
            foreach (var d in dirs)
            {
                if (token.IsCancellationRequested || result.Count >= 200) break;
                if (Directory.Exists(d.Path)) result.Add((d.Path ?? string.Empty, true)); // 跳过索引过期条目
            }

            // 文件
            if (!token.IsCancellationRequested && result.Count < 200)
            {
                var fileOptions = new QueryOptions
                {
                    FolderDepth = FolderDepth.Deep,
                    IndexerOption = IndexerOption.OnlyUseIndexer,
                    ApplicationSearchFilter = $"System.FileName:~\"{escaped}\""
                };
                var files = await folder.CreateFileQueryWithOptions(fileOptions)
                    .GetFilesAsync(0, (uint)(200 - result.Count));
                foreach (var f in files)
                {
                    if (token.IsCancellationRequested || result.Count >= 200) break;
                    if (File.Exists(f.Path)) result.Add((f.Path ?? string.Empty, false)); // 跳过索引过期条目
                }
            }
        }
        catch
        {
            // 索引不可用/路径不可访问/已取消 → 返回空（绝不返回部分结果），由调用方回退到递归枚举
            return new List<(string Path, bool IsDirectory)>();
        }
        return result;
    }
    ```
  - `using` 需要：`Windows.Storage`、`Windows.Storage.Search`、`System.Collections.Generic`、`System.IO`、`System.Threading`、`System.Threading.Tasks`。
  - Acceptance: `dotnet build` 通过；任何异常/字符集不符都返回**空**（绝不返回部分结果）；分页拉取（单次 ≤200，不再物化全量）；跳过不存在的过期条目。
  - QA happy: 在已索引目录（`C:\Users\<user>\Downloads`）搜索存在的文件名，返回非空且含该路径；查询词含 `*` 时返回空（回退）。
  - QA failure: `GetFoldersAsync(0, 200)` 参数类型不匹配（需 uint）→ `0`/`200` 为常量可隐式转换、`(uint)(200 - result.Count)` 显式转换；`d.Path` 为空导致 CS8601 → 已用 `?? string.Empty`；部分结果泄漏 → 已统一空返回。
  - Commit: 不提交（工作树增量）。

- [x] 2. `MainWindowViewModel.cs` — 新增安全枚举辅助 + `SearchRecursive` 改为精简回退
  - References: 现有 `SearchRecursive`（`MainWindowViewModel.cs:748-765`，当前用 `FileSystemNodeViewModel.SafeGetDirs/SafeGetFiles`）；`SafeGetDirs/SafeGetFiles`（`FileSystemNodeViewModel.cs:499-535`，含每条目冗余 `Directory.Exists/File.Exists`）。
  - 在 `SearchRecursive` 附近新增两个 private static 辅助（`ToList` 使异常在 try 内完整捕获；每条 catch 保留注释）：
    ```csharp
    private static List<string> EnumerateDirsSafe(string dir)
    {
        try { return Directory.EnumerateDirectories(dir).ToList(); }
        catch { return new List<string>(); } // 无权限/不存在 → 返回空，由调用方跳过
    }
    private static List<string> EnumerateFilesSafe(string dir)
    {
        try { return Directory.EnumerateFiles(dir).ToList(); }
        catch { return new List<string>(); } // 无权限/不存在 → 返回空，由调用方跳过
    }
    ```
  - 将 `SearchRecursive` 体内的 `FileSystemNodeViewModel.SafeGetDirs(dir)` 替换为 `EnumerateDirsSafe(dir)`、`FileSystemNodeViewModel.SafeGetFiles(dir)` 替换为 `EnumerateFilesSafe(dir)`；其余逻辑（200 上限、`Path.GetFileName(...).IndexOf(query, OrdinalIgnoreCase)` 匹配、`CreateSearchNode`、递归）不变。
  - Acceptance: `dotnet build` 通过；`SearchRecursive` 仍递归当前目录、名字匹配、≤200；搜索路径不再调用 `SafeGetDirs/SafeGetFiles`（面包屑/树/计算大小仍照旧用）。
  - QA happy: 非索引目录搜索仍能正确返回匹配项（结果与旧实现一致）。
  - QA failure: 无权限子目录导致整次搜索中断 → 逐目录 `EnumerateDirsSafe/EnumerateFilesSafe` 各自 try/catch 跳过；`grep SafeGetDirs` 确认搜索路径不再引用。
  - Commit: 不提交。

- [x] 3. `MainWindowViewModel.cs` — `RunSearchAsync` 改为「先索引、0 则回退」
  - References: 现有 `RunSearchAsync`（`MainWindowViewModel.cs:732-746`）；`TriggerSearch`（:705-730，已置 `IsSearching`/清空结果）；`CreateSearchNode`（:767-778）。
  - 将 `RunSearchAsync` 体改为：
    ```csharp
    private async Task RunSearchAsync(string scope, string query, CancellationToken token)
    {
        var results = new List<FileSystemNodeViewModel>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var indexed = await WindowsSearchHelper.QueryIndexAsync(scope, query, token);
        sw.Stop();
        System.Diagnostics.Debug.WriteLine($"[Search] index query {sw.ElapsedMilliseconds}ms, hits={indexed.Count}");
        if (token.IsCancellationRequested) return;
        if (indexed.Count > 0)
        {
            foreach (var (path, isDir) in indexed)
                results.Add(CreateSearchNode(path, isDir));
        }
        else
        {
            SearchRecursive(scope, query, results, token); // 索引无结果/失败/非索引位置 → 回退
        }
        if (token.IsCancellationRequested) return;
        await _uiDispatcherQueue.EnqueueAsync(() =>
        {
            if (token.IsCancellationRequested) return;
            SearchResults.Clear();
            foreach (var r in results) SearchResults.Add(r);
            OnPropertyChanged(nameof(SearchResults));
            OnPropertyChanged(nameof(DisplayedItemCount));
            IsSearching = false;
        });
    }
    ```
  - 注意：`RunSearchAsync` 已在 `TriggerSearch` 的 `Task.Run` 后台线程上，`QueryIndexAsync` 的 WinRT await 与 `SearchRecursive` 都在该线程；结果在 `EnqueueAsync` 回 UI 线程。`CreateSearchNode` 用 `lazyLoad:true` + `ApplyMetadata`，线程安全。
  - Acceptance: `dotnet build` 通过；索引命中走快路径（不触发递归）；索引空/异常走 `SearchRecursive` 回退；`IsSearching` 生命周期不变；结果顺序=索引器顺序（与回退的 DFS 顺序不同，属已知差异）。
  - QA happy: 索引位置搜索即时（Debug 日志 <~500ms）；非索引位置正确回退；连续输入/清空正常（`IsSearching`/进度条不受影响）；取消后不残留结果。
  - QA failure: 索引命中但结果为空却仍回退 → 确认 `indexed.Count > 0` 分支；快路径卡 UI → 确认在 `Task.Run` 内 await；取消后仍回填 → 已有 `token.IsCancellationRequested` 双重守卫。
  - Commit: 不提交。

## Final verification wave

- [x] F1. 计划符合性审计：3 个实现 todo 全部落地；改动仅 `WindowsSearchHelper.cs`（新增）+ `MainWindowViewModel.cs`；未触碰 `FileSystemNodeViewModel.cs` 的 `SafeGetDirs/SafeGetFiles`、面包屑/表格/库源码、配置、csproj。
  - 证据：`git diff --stat` 仅含上述 2 文件；`grep SafeGetDirs` 确认搜索路径不再引用。

- [x] F2. 代码质量审查：`dotnet build` 0 错误 + 逐行人工审查（csharp LSP 未安装时）；索引查询与回退均在后台线程、回填在 `DispatcherQueue`；两处 `catch { }`（`WindowsSearchHelper` 兜底、`EnumerateDirsSafe/FilesSafe`）均有注释；无 TODO/占位符；查询词字符集守卫与过期条目守卫就位。
  - 证据：build 输出 + 人工 review。

- [x] F3. 运行期手动 QA（Unpackaged 启动）：
  1. 启动冒烟：进程存活 + 窗口句柄有效。
  2. 在已索引目录（Downloads/Documents）搜索 → Debug 日志显示 index query <~500ms 且出结果。
  3. 在非索引目录（如 C:\ 根或外置盘）搜索 → 回退枚举仍正确返回结果（与旧递归一致）。
  4. 查询词含 `*`/`?` 等 → 走回退不报错。
  5. 结果双击/「打开文件位置」/进度条/清空 均正常。
  - 证据：冒烟 PASS 记录 + Debug 耗时日志 + 用户主观确认即时性。

- [x] F4. 范围保真：未新增 NuGet 依赖（WinRT 用 Windows SDK 内置）、未加配置项、未做全局搜索、未加管理员/MFT/OleDb/COM。
  - 证据：`git diff` 复查 + F1 联动。

## Commit strategy
- 不自动提交。所有改动留在工作树，提交仅在用户明确要求时进行。
- 若用户要求提交，用中文消息（如 `搜索加速：Windows 索引 + 慢速回退`），仅暂存 Scope 内文件。

## Success criteria
- `dotnet build FastFluentFilesFolders -p:WindowsPackageType=None -p:Platform=x64` ExitCode 0。
- 已索引位置（用户目录/库）搜索近乎即时（快路径，Debug 日志 <~500ms）；非索引位置正确回退并返回结果。
- 结果展示、图标、双击、Enter、「打开文件位置」、进度条、清空行为与之前一致。
- 搜索范围仍为当前目录递归；无新增依赖/配置/管理员权限。
- 已知限制（接受）：索引快路径可能落后于索引器（新建文件/隐藏文件/未索引类型），结果不保证与递归逐项一致；快路径与回退结果顺序可能不同；索引位置「确实无匹配」会走一次慢回退。
