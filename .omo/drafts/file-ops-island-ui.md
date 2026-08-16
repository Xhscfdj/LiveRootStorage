# Draft — 文件操作岛 UI 视觉对齐 Files StatusCenter

- status: awaiting-approval
- intent: clear
- review_required: false
- slug: file-ops-island-ui

## 意图判定
CLEAR —— 用户指定参考对象（`Files-main` 的 StatusCenter 文件操作岛），并要求「界面优化」。
已通过提问确定范围：**视觉对齐**（卡片式 + 状态图标/颜色 + 标题栏「文件操作/清除已完成」+ 单项关闭 + 空状态），
**不做** 速度曲线图(SpeedGraph)、展开/折叠 chevron、取消按钮（这三者需为复制操作接入 CancellationToken 与速度采样，改动深入 FileOperator，超出「界面」范畴）。

## 探索证据（源码已读，行号可溯源）

**当前 LRS 实现：**
- `UserControls/FileOperationsButton.xaml` — Button+Flyout → Border → `ItemsControl` 极简 `StackPanel`（Text + ProgressBar + ProgressDisplay 一行）。
- `UserControls/FileOperationsButton.xaml.cs:15-24 SetItems` — 仅设 ItemsSource + CollectionChanged 更新计数/可见性。
- `Models/FileOperationItem.cs` — Text/Progress(double)/FileCount/Process/RemainTime/SizeText + ProgressDisplay（格式 `FileOpProgressFmt`）。
- 宿主：`Views/MiddleFilesView.xaml:45` 工具栏右侧 `<uc:FileOperationsButton x:Name="FileOpsBtn" .../>`；集合 `_fileOperationItems` 归 `MiddleFilesView` 所有（`.xaml.cs:36`）。
- 添加入口：粘贴 `MainWindowViewModel.Paste`（含 `CompleteOperation`/`FailOperation` 辅助，`.cs:239-282`）；删除/彻底删除 `MiddleFilesView.xaml.cs:799/813`（`new FileOperationItem{...Progress=100...}`）；归档 `Extensions/Extensions/ArchivePlugin.cs:119/184`（经 `FileOperationReporter.ReportOperation`）。
- 现有转换器：`MiddleFilesView.xaml.cs:950-976`（BoolToVisibility/InvertBool/InvertVisibility/ExpandGlyph），位于 `FastFluentFilesFolders.Views` 命名空间，且是 Page 级资源（不可跨控件复用）。
- 主题色资源可用：`AccentAAFillColorSecondaryBrush`（MiddleFilesView.xaml:22）、标准 Fluent `SystemFillColorSuccessBrush`/`SystemFillColorCriticalBrush` 等。

**Files-main 参考（`src/Files.App/UserControls/StatusCenter/`）：**
- `StatusCenter.xaml` — 标题栏「Status Center」+「Clear completed」按钮；ListView 卡片项：圆角 Border（CardBackgroundFillColorDefaultBrush + CardStrokeColorDefaultBrush），头部行「32x32 圆形色块 + 16x16 图标 + 标题 + 关闭(X)/chevron」，底部「进度条 + 底部信息（已处理/总量）」；空状态「NoFileOperations」。
- `StatusCenterItem.cs` — `ItemKind`(InProgress/Successful/Error/Canceled) 驱动颜色、`ItemIconKind` 驱动图标。
- `StatusCenterStyles.xaml:54-64` — `StatusCenterStateToBrushConverter`（background/foreground 参数），颜色：InProgress=强调色、Successful=绿、Error=红、Canceled=灰。
- 图标枚举：Copy/Move/Delete/Recycle/Extract/Compress/Successful/Error。

## 决策（采用默认 / 用户已定）
1. **范围 = 视觉对齐**（用户选择）。不做速度图/展开折叠/取消。
2. **保留 Button+Flyout 宿主**，保留 `RootBtn` 在 count==0 时隐藏的现有行为；空状态消息因按钮在 0 项时已隐藏而不可达 → **不新增**空状态文案（记为明确决策）。
3. **继续用 ItemsControl**（不引入 ListView/选中语义）。
4. **模型扩展**：`FileOperationItem` 增加 `FileOperationState` 枚举 + `State` + `IconGlyph`(操作图标) + 计算属性 `DisplayIconGlyph`(按 State 切换成功/失败/取消图标) + `IsCompleted`/`IsInProgress`；保留现有属性不动（Text/Progress/FileCount/Process/RemainTime/SizeText/ProgressDisplay 语义不变）。
5. **颜色经转换器**：新增 `FileOperationStateToBrushConverter`（`background`/`foreground` 布尔参数，模仿 Files），从 `Application.Current.Resources` 取主题刷：InProgress=AccentFillColorDefaultBrush / AccentFillColorSecondaryBrush，Successful=SystemFillColorSuccess(Bg)Brush，Error=SystemFillColorCritical(Bg)Brush，Canceled=TextFillColorSecondaryBrush。
6. **图标 glyph（Segoe Fluent）**：复制 `\uE8C8`、移动/剪切 `\uE8AB`、粘贴 `\uE77F`、删除 `\uE74D`、归档 `\uE7B8`、成功 `\uE73E`、失败 `\uE783`、取消 `\uE711`。
7. **多语言**：新增 `FileOperationsTitle`(文件操作/File Operations)、`ClearCompleted`(清除已完成/Clear completed)。
8. **归档插件**（同程序集）成功/失败点显式设置 `State`；其现有 `FileOperationItem` 构造（Text/FileCount/Progress/Process/RemainTime）保持兼容。

## 改动文件清单（预研）
1. `Models/FileOperationItem.cs` — 加枚举 + State/IconGlyph/DisplayIconGlyph/IsCompleted/IsInProgress。
2. `UserControls/FileOperationStateToBrushConverter.cs`（新增）。
3. `UserControls/FileOperationsButton.xaml` — 卡片模板 + 标题栏 + 关闭按钮 + 转换器资源。
4. `UserControls/FileOperationsButton.xaml.cs` — ClearCompleted/RemoveItem/标题栏本地化刷新。
5. `ViewModels/MainWindowViewModel.cs` — CompleteOperation/FailOperation 设 State；Paste 按 isCut 设操作图标。
6. `Views/MiddleFilesView.xaml.cs` — 删除/彻底删除设 State=Successful+IconGlyph；粘贴项设 State=InProgress+IconGlyph。
7. `Extensions/Extensions/ArchivePlugin.cs` — 成功/失败设 State。
8. `Strings/en.json`、`Strings/zh-Hans.json` — 新增两条。
9. `ViewModels/MultiLanguageStringsViewModel.cs` — 新增两属性 + AllPropertyNames。

## 需要用户确认（无——范围已定，其余为可逆默认）

<status>awaiting-approval</status>
