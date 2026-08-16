# Plan — Vendor TableView 控件 + 修复原生 XAML 泄漏

- **slug**: `vendor-tableview`
- **status**: approved
- **intent**: CLEAR
- **review_required**: false

## 目标
将应用从 NuGet `WinUI.TableView 1.4.1` 切换到仓库内 `FastFluentFilesFolders.TableView` 项目，修复控件源码中的原生 XAML 泄漏（行容器回收、事件处理器清理），使分组↔非分组快速切换泄漏率从 ~2-5MB/轮 降至 ≤1.6MB/轮（与非分组↔非分组干净基线持平）。

## Must-Have
- `dotnet build` ExitCode 0
- 快速切换 Downloads↔Documents 15 轮 ≤ +24MB（1.6MB/轮 × 15）
- 功能无回归：分组文件夹显示时间组头、非分组平铺列表、排序正常、左键双击打开/右键菜单正常

## Must-NOT-Have
- 不破坏现有修复（版本守卫、去重、AllowLiveShaping=false、图标缓存键、单 GroupedFileList）
- 不引入新 NuGet 依赖

---

## Todos

### Phase 1 — 迁移：NuGet → Vendored 项目

- [x] 1. `FastFluentFilesFolders.TableView/FastFluentFilesFolders.TableView.csproj` — 升级 TargetFramework `net8.0`→`net10.0-windows10.0.19041.0`，WindowsAppSDK `2.2.0`→`2.3.1`（与主应用一致）
  - 验证：`dotnet build FastFluentFilesFolders.TableView.csproj` ExitCode 0

- [x] 2. `FastFluentFilesFolders/FastFluentFilesFolders.csproj` — 移除 `PackageReference Include="WinUI.TableView" Version="1.4.1"`，添加 `<ProjectReference Include="..\FastFluentFilesFolders.TableView\FastFluentFilesFolders.TableView.csproj" />`
  - 验证：编译通过（此时会因命名空间不匹配报编译错误——预期，Phase 1 T3-T5 修复）

- [x] 3. `FastFluentFilesFolders/UserControls/LrsTableView.cs` — `using WinUI.TableView;`→`using FastFluentFilesFolders.UserControls.TableView;`，`SD = WinUI.TableView.SortDirection`→`SD = FastFluentFilesFolders.UserControls.TableView.SortDirection`
  - 验证：编译错误减少

- [x] 4. `FastFluentFilesFolders/Views/MiddleFilesView.xaml.cs` — `using WinUI.TableView;`→`using FastFluentFilesFolders.UserControls.TableView;`
  - 验证：编译错误减少

- [x] 5. `FastFluentFilesFolders/Views/MiddleFilesView.xaml` — `xmlns:tv="using:WinUI.TableView"`→`xmlns:tv="using:FastFluentFilesFolders.UserControls.TableView"`
  - 验证：全量编译 ExitCode 0

### Phase 2 — 修复 CollectionView Reset 事件订阅累积

- [x] 6. `FastFluentFilesFolders.TableView/ItemsSource/CollectionView.cs` — `OnSourceCollectionChanged` Reset 分支（约 line 237）：将 `DetachPropertyChangedHandlers(e.OldItems)` 改为 `DetachPropertyChangedHandlers(_attachedItems ?? e.OldItems)`，并在 `AttachPropertyChangedHandlers` 中维护 `_attachedItems` List（去重追加），Reset 后 `_attachedItems?.Clear()`
  - 验证：编译通过；AllowLiveShaping=false 时路径不触发但仍需保证类型安全

### Phase 3 — 回归验证

- [x] 7. `dotnet build` — 全量编译 ExitCode 0

- [x] 8. 快速切换回归：Unpackaged 启动 → PinnedShortcuts Downloads↔Documents 15 轮 → 记录 PrivateMemorySize64
  - 基线：非分组↔非分组 1.59MB/轮（隔离测试）；修复前分组↔非分组 ~2-5MB/轮
  - 目标：≤1.6MB/轮，15 轮总计 ≤24MB，无单调增长

- [x] 9. 功能回归：分组文件夹组头正常 → 非分组文件夹平铺列表正常 → 排序正常 → 双击打开正常
  - 验证：AGENTS.md 立即刷新 + 无回归

## 验收标准
- [ ] F1. dotnet build 0 errors
- [ ] F2. Downloads↔Documents 快速切换 15 轮 ≤24MB，无单调增长
- [ ] F3. 功能无回归（分组/平铺/排序/双击）







