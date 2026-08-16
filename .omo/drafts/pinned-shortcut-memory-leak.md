# Draft — PinnedShortcuts 快速切换内存泄漏

- status: approved
- intent: clear
- review_required: true
- slug: pinned-shortcut-memory-leak

## 意图判定
CLEAR —— 具体可复现的 bug（PinnedShortcuts 快速切换 Downloads/Documents 导致 +10MB/次、不回落）。
目标明确：修复该泄漏并符合 AGENTS.md「文件操作后立即看到结果」。

## 探索证据（源码已读，行号可溯源）
触发链：
1. `PinnedShortcuts.xaml.cs:22` → `VM.SelectedFolder = node`（每次点击都赋值）
2. `MainWindowViewModel.cs:586 OnSelectedFolderChanged` — 每次切换：
   - **无条件** `_backStack.Push(_previousPath)`（:598-601）——`_backStack` 随点击次数**无界增长**（切换不同目录必然 push 一个字符串）
   - `_ = UpdateCurrentFolderContentAsync(value)`（:606）—— **fire-and-forget**，不 await、无取消、无过期保护
3. `UpdateCurrentFolderContentAsync`（:615）：`if (!folder.IsLoaded) await folder.LoadChildrenAsync()` 后 `EnqueueAsync → CurrentFolderContent.Clear() + Add`
   - **并发陈旧写入竞态**：快速切换会同时有多个 async 任务在飞行；先点的任务在 await 后仍会把已过期目录内容写回 `CurrentFolderContent`（覆盖用户当前正在看的目录）
4. `MiddleFilesView.xaml.cs:105/115/124` `UpdateGroupedSource` → `FileGrid.UpdateSource`：每次切换新建/重建分组源与绑定。

三个可证实缺陷：
- **A. 导航栈无界增长**（`_backStack`），切换不同路径每次 +1 entry，永不限流/pop，除非 GoBack。
- **B. fire-and-forget 陈旧任务竞态**：快速切换时旧的 `UpdateCurrentFolderContentAsync` 在 await 完成、用户已换目录后才回写 UI → 过期结果覆盖当前目录 + 产生无主异步对象滞留。
- **C. 每次切换全量重建分组源绑定**，需确认 `FileGrid.UpdateSource` 正确解绑旧源、无 `GroupedFileList`/事件委托滞留。

> 已在 Phase 3 实证：TableView 虚拟化正常（TableViewRow=63），VM 虚拟化正确，内存大头在 VM 层全量创建 —— 但**快速切换的瞬时泄漏**与导航栈无界增长、陈旧任务竞争直接相关。10MB/次 量级需 dump 实证具体滞留类型（候选：FileSystemNodeViewModel、BitMспutImage/图标、分组源、TableViewRow）。

## 决策（采用默认）
- 采用「导航版本号 + 取消陈旧写入」方案（最小改动、可验证，不违反 AGENTS.md 即时刷新）。
- 对 `_backStalk`/`_forwardStack` 增加上限保护（确定性，暂不涉及 GoBack 深度行为变更）。
- 修复后必须满足 AGENTS.md：受控并发下仍立即显示新目录。

## 需要确认的步骤（fork）——无需用户决策，采用默认
- 修复同时给 `UpdateCurrentFolderContentAsync` 加 try/catch + 版本号护栏（返回 void 或丢弃陈旧）。
- 不引入依赖、不改配置面。

<status>awaiting-approval</status>