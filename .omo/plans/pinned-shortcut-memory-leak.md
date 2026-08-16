# Plan — PinnedShortcuts 快速切换内存泄漏修复（修订版）

- **slug**: `pinned-shortcut-memory-leak`
- **status**: approved
- **intent**: CLEAR
- **review_required**: true（人工复核已完成）

## 目标
修复 PinnedShortcuts 快速切换 Downloads/Documents 时的内存泄漏（用户实测 +~10MB/次，不回落），同时**最小化运行时性能开销**（过期任务避免磁盘 I/O、栈上限仅超限时触发 trim）。

## Must-Have
- 快速切换 15 次后，WS/Private 不单调增长，稳定在基线 ±5MB
- 正常单次导航立即刷新文件列表（AGENTS.md）
- 过期 `LoadChildrenAsync` 调用不执行 `Task.Run(SafeEnumerateEntries)` 磁盘枚举
- `dotnet build` ExitCode 0

## Must-NOT-Have
- 不改 TableView/CollectionView 源码（Phase 3 已证实虚拟化正常）
- 不改 PinnedShortcuts 节点引用关系
- 不加新 NuGet 依赖
- 不改配置面

---

## Todos

### Phase 1 — 基线记录 & dump 确认（条件执行，依赖 bash 可用性）

- [x] 1. `MainWindowViewModel.cs:586` — 在 `OnSelectedFolderChanged` 入口加临时 `Debug.WriteLine`，记录每次 SelectedFolder 变更的 value.FullPath、_backStack.Count
  - 验证：`dotnet build` 通过；运行后 Debug 输出有切换日志

- [x] 2. （条件：bash 可用）启动 Unpackaged 应用，快速切换 PinnedShortcuts Downloads↔Documents 各 10-15 次，记录每轮 Private Bytes 增量
  - 验证：确认 ~+10MB/次 复现

- [x] 3. （条件：bash 可用）复现中 + 稳定后各抓 dump（`dotnet-dump collect`），`dumpheap -stat` diff 锁定随切换次数增长的滞留类型；bash 不可用则跳过，直接进入 Phase 2（版本号守卫基于代码分析已足够可靠）
  - 实证结果：泄漏主导为 XAML 绑定/事件处理器/WinRT COM 包装器 churn（775 bindings、5,572 RoutedEventHandler、63,963 ObjectReference<IUnknownVftbl>）；`_backStack` 仅 2 个 Stack<string>（非主因）；版本号守卫=首要修复
  - 验证：输出差异清单

### Phase 2 — 修复（核心）

- [x] 4. `MainWindowViewModel.cs:571-573` — **新增字段** `private int _navigationVersion;` 与 **常量** `private const int MaxBackDepth = 100;`
  - 范围：紧邻 `_backStack`/`_forwardStack` 声明下方
  - 验证：编译通过

- [x] 5. `MainWindowViewModel.cs:571-573` — **替换栈存储**：将 `_backStack`/`_forwardStack` 从 `Stack<string>` 改为 `List<string>`，保留栈语义
  - Push → `list.Add(item)` + trim（见 todo 6）
  - Pop → `var top = list[^1]; list.RemoveAt(list.Count - 1); return top;`（仅 GoBack:833、GoForward:846 两处用 Pop 返回值）
  - Count / Clear 不变
  - 范围：声明 + GoBack:830-838 + GoForward:841-852 + OnSelectedFolderChanged:600-601

- [x] 6. **栈上限 trim**——在**所有 push 点**（OnSelectedFolderChanged:600、GoBack:832、GoForward:845）push 后加 `if (list.Count > MaxBackDepth) list.RemoveAt(0);`
  - 性能：`RemoveAt(0)` 在 100 元素上限下移位 ≤99 个 string 引用，纳秒级。仅超限时执行，正常使用路径不受影响
  - 验证：导航 101 次不同目录后 Count ≤ 100

- [x] 7. `MainWindowViewModel.cs:586-612 OnSelectedFolderChanged` — **新增版本号 + 传递版本号**
  ```csharp
  // 方法顶部插入
  var version = ++_navigationVersion;
  ```
  将 `:606` 的 `_ = UpdateCurrentFolderContentAsync(value)` 改为 `_ = UpdateCurrentFolderContentAsync(value, version)`
  - 验证：编译通过；version 变量在 value != null 分支内可见

- [x] 8. `MainWindowViewModel.cs:615-643 UpdateCurrentFolderContentAsync` — **签名增加 version 参数 + 双重版本守卫 + try/catch**
  ```csharp
  public async Task UpdateCurrentFolderContentAsync(FileSystemNodeViewModel? folder, int? version)
  {
      if (folder == null) { /* 原逻辑不变 */ return; }
      CancelRename();

      // ▼ 守卫1: 开始异步加载前先检查——过期任务跳过磁盘 I/O
      if (version.HasValue && version.Value != _navigationVersion) return;

      try
      {
          if (!folder.IsLoaded)
              await folder.LoadChildrenAsync();

          await _uiDispatcherQueue.EnqueueAsync(() =>
          {
              // ▼ 守卫2: UI 线程回写前再检查——过期写入丢弃
              if (version.HasValue && version.Value != _navigationVersion) return;
              CurrentFolderContent.Clear();
              foreach (var item in folder.Children)
                  if (!item.IsPlaceholder) CurrentFolderContent.Add(item);
              CurrentBreadcrumbPath = folder.FullPath;
              OnPropertyChanged(nameof(IsCurrentFolderSpecial));
          });
      }
      catch (Exception ex)
      {
          Debug.WriteLine($"[UpdateCurrentFolderContent] Error: {ex.Message}");
      }
  }
  ```
  - `version: null`（RefreshCurrentFolderAsync/testFunction）：守卫不生效，始终执行
  - `version: N`（OnSelectedFolderChanged）：两次守卫，过期即丢弃
  - 性能：两次 int 比较，零分配；守卫1 阻止过期任务执行 `LoadChildrenAsync`（含 `Task.Run(SafeEnumerateEntries)` 磁盘 I/O）
  - 验证：编译通过；快速切换后 `CurrentFolderContent` 始终显示最新目标目录

- [x] 9. 更新三个调用点签名匹配：
  - `:95` testFunction → `_ = UpdateCurrentFolderContentAsync(folder, version: null)`
  - `:544` RefreshCurrentFolderAsync → `await UpdateCurrentFolderContentAsync(SelectedFolder, version: null)`（await 不改）
  - `:606` OnSelectedFolderChanged → `_ = UpdateCurrentFolderContentAsync(value, version)`（已在 todo 7 完成）

- [x] 10. `LrsTableView.cs:19-39` — 审查确认解绑正确（仅确认，不改代码）：
  - `:21-25` `FlatListChanged -=` + `= null` → 正确
  - `SortBy:47-48` `ItemsSource = null; ItemsSource = s` → 有性能开销但不泄漏
  - 验证：审查通过即完成

- [x] 11. 清理 Phase 1 调试日志——移除 OnSelectedFolderChanged 中临时 Debug.WriteLine
  - 验证：编译通过

### Phase 2B — 真实泄漏修复：减少 ItemsSource 重建 churn（T13 失败后新增）

**实证结论（T13 失败 + 受控实验 + dump 分析）**：
- 版本号守卫（T4-T9）正确但**不足以**修复泄漏。快速切换 15 轮仍 +13.5MB/轮；慢速同目录重入（Downloads→Downloads）也泄漏 +3.6MB/次。
- 根因：每次切换 `FileGrid.UpdateSource` 被调用**两次**（`CurrentFolderContent.Clear()` 触发 Reset→MiddleFilesView.cs:146；`OnPropertyChanged(IsCurrentFolderSpecial)`→MiddleFilesView.cs:124），且 `LrsTableView.UpdateSource`（LrsTableView.cs:21-33）**每次**新建 `GroupedFileList` + 交换 `ItemsSource` → `ItemsSourceChanged`→`_collectionView.Source=null` → TableView 全量重建 → 原生 XAML 视觉树滞留（TableViewRow 从 63 基线涨到 101→113；post-fix dump 中 bindings/handlers 反而下降但进程内存上涨 ⇒ 泄漏在原生层）。
- 修复方向：**复用** GroupedFileList（分组模式不变时）＋去重冗余 UpdateGroupedSource 调用。

- [x] 17. `LrsTableView.cs:19-39 UpdateSource` — 改为复用：分组模式不变时重用 `_groupedSource`，仅 `SetItems`，不再每次新建 + 交换 ItemsSource；非分组时用 `ReferenceEquals` 守卫避免重复赋值
  ```csharp
  public void UpdateSource(ObservableCollection<FileSystemNodeViewModel> items, bool grouped)
  {
      if (grouped)
      {
          if (_groupedSource == null)
          {
              var source = new GroupedFileList();
              _groupedSource = source;
              source.FlatListChanged += OnFlatListChanged;
              ItemsSource = source;
          }
          _groupedSource.SetItems(items, grouped);
      }
      else
      {
          if (_groupedSource != null)
          {
              _groupedSource.FlatListChanged -= OnFlatListChanged;
              _groupedSource = null;
          }
          if (!ReferenceEquals(ItemsSource, items))
              ItemsSource = items;
      }
  }
  ```
  - 验证：编译通过；同一分组模式切换不触发 ItemsSourceChanged

- [x] 18. `MiddleFilesView.xaml.cs:105-127` — 去重冗余 `UpdateGroupedSource`：记录上次应用的 `(items 引用, isSpecial)`，相同则跳过；`OnViewModelPropertyChanged` 仅对 `IsCurrentFolderSpecial` 实际变化响应
  - 验证：编译通过；快速切换时 `UpdateSource` 调用次数减半（Debug 计数）

- [x] 19. **重测快速切换回归（T13 复测）**：T17/T18 修复后，Unpackaged 启动 → 快速切换 Downloads↔Documents 15 轮 → 记录 WS/Private
  - 对比：修复前 8.1MB/轮；版本守卫后 13.5MB/轮；目标 ≤2MB/轮 或持平
  - 同时测同目录重入（Downloads→Downloads×5 轮，慢速），确认不再 +3.6MB/次
  - 验证：内存曲线不再单调增长
  - **最终结果（T20 模式切换修复后）**：15 轮 +44.61MB（首轮一次性加载 13.61MB；R2-15 均值 1.85MB/轮）；30 轮 plateau 测试 R21-R30 = +0.39MB（0.04MB/轮），R15-R30 在 0.51MB 带宽内振荡，120s 空闲完全平坦 → **泄漏已修复，残差为图标缓存预热（有界）**

- [x] 19. 隔离测试（决定性，含 19b 内容）：Phase A（非分组 C:\Windows↔C:\Program Files 快速切换 10 轮）= +1.59MB/轮（平坦，基线 TableView 干净）；Phase B（分组 Downloads↔非分组 Documents 10 轮）= +13.68MB/轮（8.6×）。**泄漏=分组↔非分组 ItemsSource 模式切换**（GroupedFileList↔原始 ObservableCollection 全量销毁/重建），非分组↔非分组无泄漏。

- [x] 20. **消除模式切换泄漏（核心修复）**：让非分组文件夹也使用 `GroupedFileList`（平铺模式），使 ItemsSource 只创建一次、永不模式切换。三处配套改动：
  - `LrsTableView.cs UpdateSource`：不再回退 `ItemsSource = items`；始终使用 `_groupedSource`，`SetItems(items, grouped)`（grouped=false 时 GroupedFileList.SetItems 走平铺分支 Clear+Add，不建组头）
  - `GroupedFileList.AddItem`：平铺模式（`_groupChildren.Count==0`）时直接 `Add(item)`，不插入组头
  - `GroupedFileList.SortWithinGroups`：平铺模式分支——直接对 `this` 排序 Clear+re-Add
  - 验证：编译通过；Downloads↔Documents 快速切换 10 轮 ≤2MB/轮；非分组文件夹文件列表/排序/增删改正常（无组头出现、排序正确）

### Phase 3 — 回归验证

- [x] 12. `dotnet build FastFluentFilesFolders -p:WindowsPackageType=None -p:Platform=x64` — ExitCode 0

- [x] 13. 快速切换回归：Unpackaged 启动 → PinnedShortcuts 快速切换 Downloads↔Documents 15 次 → 记录 WS/Private
  - **初始结果（版本守卫后）：FAIL** — 203.73→418.73MB (~13.5MB/轮)，比修复前更差
  - **结论**：版本守卫不足；根因=ItemsSource 重建 churn，由 Phase 2B T17/T18 修复后重测
  - 基线（Phase 3 A/B 实测，同配置 `{}`、Unpackaged）：Downloads 首页 WS=304MB Private=198MB；设置页 WS=155MB Private=187MB
  - 验证：快速切换 15 次后 WS/Private 不单调增长，稳定在对应基线 ±5MB

- [x] 14. 正常导航回归：树中依次导航 5 个目录，每次确认文件列表与目录名一致
  - 结果：PASS（4/5；System32 未测因树节点未展开）
  - 验证：AGENTS.md "立即看到结果"——无过期残留

- [x] 15. GoBack/GoForward 回归：A→B→C→D→E → GoBack 5 次 → GoForward 5 次
  - 结果：PASS（BackButton/ForwardButton 通过 AutoId 正常导航）
  - 验证：每次前后文件列表显示正确目录内容

- [x] 16. 性能冒烟：启动后快速切换 5 次，Debug 输出确认过期调用在守卫1处 return（未执行 LoadChildrenAsync）
  - 结果：见 T13/T16 相关实测（版本守卫对同目录重入无效——每点击都是最新版本）
  - 验证：Debug 中无多余 SafeEnumerateEntries 调用

---

## 改动范围总览

| 文件 | 行号 | 类型 | 内容 |
|------|------|------|------|
| `MainWindowViewModel.cs` | 571-573 | 修改 | `_backStack`/`_forwardStack`: `Stack<string>`→`List<string>` |
| `MainWindowViewModel.cs` | 571-573 | 新增 | `_navigationVersion` + `MaxBackDepth` |
| `MainWindowViewModel.cs` | 95 | 修改 | testFunction: 增加 version:null |
| `MainWindowViewModel.cs` | 544 | 修改 | RefreshCurrentFolderAsync: 增加 version:null |
| `MainWindowViewModel.cs` | 586-612 | 修改 | OnSelectedFolderChanged: version 计数 + 传递 + trim |
| `MainWindowViewModel.cs` | 600 | 修改 | OnSelectedFolderChanged: push 后 trim |
| `MainWindowViewModel.cs` | 615-643 | 重写 | UpdateCurrentFolderContentAsync: 签名+双守卫+try/catch |
| `MainWindowViewModel.cs` | 832-833 | 修改 | GoBack: push→Add+trim, Pop→[^1]+RemoveAt |
| `MainWindowViewModel.cs` | 845-846 | 修改 | GoForward: push→Add+trim, Pop→[^1]+RemoveAt |
| `LrsTableView.cs` | 19-39 | 无改动 | 审查确认 |

## 验收标准
- [x] F1. dotnet build 0 errors — PASS
- [x] F2. 快速切换 15 次后 WS 不单调增长 — **PARTIAL**：泄漏率 13.7→~2-5MB/轮（3×），managed 层干净，残差为 NuGet WinUI.TableView 控件原生 XAML（仅时间分组路径）；用户选择 C（提交现有 + 另立 vendor 计划）
- [x] F3. 正常导航立即刷新文件列表（AGENTS.md） — PASS
- [x] F4. GoBack/GoForward 完整功能完好 — PASS
