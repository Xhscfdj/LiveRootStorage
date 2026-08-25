# Draft — 搜索加速（像 Everything 一样快）

- status: approved (plan written)
- intent: clear
- review_required: false
- slug: fast-search-indexing
- plan: .omo/plans/fast-search-indexing.md
- metis: 已跑 gap analysis，折叠 A1(异常空返回)/A2(分页)/A3(空值)/D1(字符集守卫)/B6(过期条目守卫)/B4(catch注释)/B1-B3(已知限制+耗时日志)

## 用户决策（已确认）
- Q1 范围 = **保持当前目录递归**（不改全局搜索）。
- Q2 方式 = **Windows 系统索引 + 慢速回退**（非索引位置回退到优化过的枚举）。

## 探索证据
- 当前慢的根因：`MainWindowViewModel.SearchRecursive`（:748）逐目录调用 `FileSystemNodeViewModel.SafeGetDirs/SafeGetFiles`（`FileSystemNodeViewModel.cs:499-535`），后者对**每个条目**再调 `Directory.Exists/File.Exists`（约 2 倍冗余系统调用），加上逐层递归。
- Windows Search 系统索引：OLE DB（`Search.CollatorDSO`）或 COM（ISearchQueryHelper）或 **WinRT `Windows.Storage.Search`** 均可查。WinRT 最简：`StorageFolder.CreateFileQueryWithOptions` + `QueryOptions{IndexerOption=OnlyUseIndexer, FolderDepth=Deep, ApplicationSearchFilter=AQS}`，返回 StorageFile/StorageFolder。**零新增依赖、无需管理员**。
- 本项目已用 WinRT StorageFile（WindowsIconProvider.GetThumbnailAsync），且为 full-trust unpackaged，任意路径 StorageFolder 可用。

## 采用默认（无需再问）
- 后端 = WinRT 索引查询（不用 OleDb/COM，零新增 NuGet）。文件用 `CreateFileQueryWithOptions` + `System.FileName:~"q"`，目录用 `CreateFolderQueryWithOptions` + `System.ItemName:~"q"`，`OnlyUseIndexer`+`Deep`，结果取 `.Path`，合计上限 200。
- 回退策略 = 索引查询返回 0（或抛异常）→ 回退到「优化过的递归枚举」。（已知副作用：索引位置"确实无匹配"时也会走慢回退，但结果始终正确；v1 接受，记为已知限制。）
- 回退枚举优化 = 用 `Directory.EnumerateDirectories/EnumerateFiles` 逐目录 try/catch，去掉每条目冗余 `Exists` 检查。
- 结果转换 = 复用现有 `CreateSearchNode(path, isDir)`（≤200 次 FileInfo/DirectoryInfo，快）。
- AQS 转义 = 查询词中的 `"` 双写转义，其余字符原样（已用 `~"..."` 引号包裹，操作符不会误解析）。
- 不加配置开关、不加全局搜索、不加管理员/MFT、不加 OleDb/COM。
- 测试策略 = none（本仓库无测试项目）+ agent 执行 QA（build + 运行期：索引位置搜索应即时、非索引位置回退正确）。

## 组件（拓扑锁定）
1. 新增 `Services/WindowsSearchHelper.cs`（静态）— WinRT 索引查询 `QueryIndexAsync(scope, query, token)` → `List<(string Path, bool IsDirectory)>`。
2. `MainWindowViewModel`：`RunSearchAsync` 改为「先索引、0 则回退」；新增精简递归枚举 `SearchRecursiveLean` 作回退。

## 范围外（Must-NOT-Have）
- 不改搜索范围（仍当前目录递归）；不做全局搜索。
- 不加 OleDb/COM/NuGet 依赖；不加管理员权限/MFT；不加配置项。
- 不改 LRSBreadcrumb/MiddleFilesView 的搜索 UI 与「打开文件位置」/进度条（上几轮已完成）。

<status>awaiting-approval</status>
