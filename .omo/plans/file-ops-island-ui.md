# Plan — 文件操作岛 UI 视觉对齐 Files StatusCenter

- **slug**: `file-ops-island-ui`
- **status**: approved
- **intent**: CLEAR
- **review_required**: false

## 目标
将 `FileOperationsButton`（当前 Button+Flyout 里极简「文字 + 进度条 + 一行信息」）改造成模仿 Files `StatusCenter` 的**卡片式**操作岛：圆形状态图标+颜色、标题栏「文件操作 / 清除已完成」、单项关闭(X)、进行中显示进度条+底部信息。**纯界面层改动**，不改文件复制/进度逻辑（上一轮已让粘贴上报真实个数/进度/大小）。

**测试策略**：本项目无测试工程（AGENTS.md）。采用 **tests-after + 全 agent 执行 QA**：`dotnet build` + 静态一致性 grep + 运行时冒烟，零人工干预。

## Must-Have
- 粘贴 / 删除 / 彻底删除 / 归档（压缩/解压）四类操作在操作岛均以卡片显示：状态图标 + 标题 + （进行中）进度条 + 底部信息。
- 状态颜色正确：进行中=强调色、成功=绿、失败=红、取消=灰（经 `{ThemeResource}` 主题刷，深浅色自适应）。
- 标题栏「文件操作」+「清除已完成」按钮可用；单项关闭(X) 仅对已完成项显示且可移除。
- 新增多语言键 `FileOperationsTitle`、`ClearCompleted`，中英双语齐全且 `MultiLanguageStringsViewModel` 注册（`AllPropertyNames` + 属性）。
- `dotnet build` ExitCode 0（.NET 10.0.400 已就绪）。
- AGENTS.md「每次文件操作后立即看到结果」不受影响（本计划不改数据流）。

## Must-NOT-Have
- 不做速度曲线图(SpeedGraph)、展开/折叠 chevron、取消按钮（用户已选「视觉对齐」）。
- 不新增 NuGet 依赖、不改 `FastFluentFilesFolders.csproj`（复用现有 `FileOperationsButton.xaml` 的 `<Page Update>` 条目）。
- 不引入 ListView / 选中语义，继续用 `ItemsControl`。
- 不改 `FileOperator` / `IFileOperator` 的复制/移动签名与实现。
- 保留「0 项时按钮自动隐藏」现有行为；**不**新增空状态文案（0 项时按钮已隐藏，空状态不可达——已记为明确决策）。
- 不删改 `FileOperationItem` 现有属性（Text/Progress/FileCount/Process/RemainTime/SizeText/ProgressDisplay 语义与绑定保持兼容，避免破坏 `ArchivePlugin` 既有用法）。

---

## Todos

### Phase 1 — 模型 & 转换器

- [x] 1. `Models/FileOperationItem.cs` — 新增枚举与状态/图标属性
  - 新增 `public enum FileOperationState { InProgress, Successful, Error, Canceled }`（同文件、类外）。
  - 类内新增字段+属性：
    - `State`（默认 `InProgress`；setter 变更时 `Notify()` 自身 + `nameof(DisplayIconGlyph)` + `nameof(IsCompleted)` + `nameof(IsInProgress)`）。
    - `IconGlyph`（string，操作图标；setter 变更时 `Notify()` 自身 + `nameof(DisplayIconGlyph)`）。
    - 计算属性 `DisplayIconGlyph`：`Successful→"\uE73E"`、`Error→"\uE783"`、`Canceled→"\uE711"`、`_→IconGlyph`。
    - 计算属性 `IsCompleted => State != InProgress`、`IsInProgress => State == InProgress`。
  - 验证：`dotnet build` 通过；`grep -n "FileOperationState" Models/FileOperationItem.cs` 命中枚举+State 定义。

- [x] 2. `UserControls/FileOperationStateToBrushConverter.cs`（新增）— 状态→主题刷转换器
  - `public sealed partial class FileOperationStateToBrushConverter : DependencyObject, IValueConverter`，8 个 `DependencyProperty`（`SolidColorBrush` 类型）：`InProgressBrush/InProgressBackgroundBrush`、`SuccessfulBrush/SuccessfulBackgroundBrush`、`ErrorBrush/ErrorBackgroundBrush`、`CanceledBrush/CanceledBackgroundBrush`。
  - `Convert`：`value` 为 `FileOperationState`；`parameter` 为字符串 `"Background"` 时返回背景刷，否则返回前景刷；映射 InProgress/Successful/Error/默认(Canceled)。`ConvertBack` 抛 `NotImplementedException`。
  - 刷子值在 XAML 里经 `{ThemeResource}` 注入（见 todo 3），转换器自身不做运行时资源查找。
  - 验证：`dotnet build` 通过；`grep -n "DependencyProperty" UserControls/FileOperationStateToBrushConverter.cs` 命中 8 个。

### Phase 2 — 控件 UI

- [x] 3. `UserControls/FileOperationsButton.xaml` — 重写 Flyout 内容为卡片式
  - 命名空间新增：`xmlns:uc="using:FastFluentFilesFolders.UserControls"`、`xmlns:views="using:FastFluentFilesFolders.Views"`。
  - `UserControl.Resources` 声明：
    - `<uc:FileOperationStateToBrushConverter x:Key="StateToBrushConverter" InProgressBrush="{ThemeResource AccentFillColorDefaultBrush}" InProgressBackgroundBrush="{ThemeResource AccentFillColorSecondaryBrush}" SuccessfulBrush="{ThemeResource SystemFillColorSuccessBrush}" SuccessfulBackgroundBrush="{ThemeResource SystemFillColorSuccessBackgroundBrush}" ErrorBrush="{ThemeResource SystemFillColorCriticalBrush}" ErrorBackgroundBrush="{ThemeResource SystemFillColorCriticalBackgroundBrush}" CanceledBrush="{ThemeResource TextFillColorSecondaryBrush}" CanceledBackgroundBrush="{ThemeResource SubtleFillColorSecondaryBrush}"/>`
    - `<views:BoolToVisibilityConverter x:Key="BoolToVis"/>`（复用 `MiddleFilesView.xaml.cs` 中的公开类）。
  - Flyout 内 `Border` 改 `MinWidth="340"`，内部 `Grid` 两行：
    - 行 0 标题栏：`TextBlock x:Name="HeaderTitleText"`（FontSize 14 / SemiBold）+ 右侧 `Button x:Name="ClearCompletedBtn"`（`Click="ClearCompleted_Click"`，`Style="{StaticResource SubtleButtonStyle}"`，FontSize 12）。
    - 行 1 `ItemsControl x:Name="OpsList"`，`ItemTemplate` 改为卡片：
      - 外层 `Grid`（Padding 8，`Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"`，`BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"`，`BorderThickness="1"`，`CornerRadius="8"`，Margin 0,2）两行：
        - 行 0 头部：3 列 —— `Border`(32x32，`CornerRadius=16`，`Background="{Binding State, Converter={StaticResource StateToBrushConverter}, ConverterParameter=Background}"`) 内 `FontIcon`(`Glyph="{Binding DisplayIconGlyph}"`，FontSize 16，`Foreground="{Binding State, Converter={StaticResource StateToBrushConverter}}"`)；列 1 `TextBlock`(`Text="{Binding Text}"`，SemiBold，`TextTrimming="CharacterEllipsis"`，`ToolTipService.ToolTip="{Binding Text}"`)；列 2 `Button`(`Width/Height=32`，`Background="Transparent"`，`BorderThickness="0"`，`Visibility="{Binding IsCompleted, Converter={StaticResource BoolToVis}}"`，`Click="CloseItem_Click"`) 内含 `FontIcon Glyph="&#xE711;"` FontSize 12。
        - 行 1 进度区（仅进行中）：`StackPanel`(`Visibility="{Binding IsInProgress, Converter={StaticResource BoolToVis}}"`) 内 `ProgressBar`(`Value="{Binding Progress}"`，`Maximum="100"`，Height 4，`Foreground="{ThemeResource AccentFillColorDefaultBrush}"`) + `TextBlock`(FontSize 11，`Foreground="{ThemeResource TextFillColorSecondaryBrush}"`，`Text="{Binding ProgressDisplay}"`，`TextTrimming="CharacterEllipsis"`)。
  - 触发按钮 `RootBtn`/`CountText` 保持不变。
  - 验证：`dotnet build` 通过（XAML 编译）；`grep -n "StateToBrushConverter\|DisplayIconGlyph\|IsCompleted\|IsInProgress" UserControls/FileOperationsButton.xaml` 命中。

- [x] 4. `UserControls/FileOperationsButton.xaml.cs` — 增加移除/清除/本地化
  - 新增 `using System.ComponentModel;`（PropertyChangedEventArgs）。
  - 字段 `private ObservableCollection<FileOperationItem>? _items;`。
  - `SetItems`：保存 `_items`、设 `OpsList.ItemsSource`、订阅 `CollectionChanged → UpdateCount()`、首次 `UpdateCount()`。
  - `UpdateCount()`：`RootBtn.Visibility = count>0 ? Visible : Collapsed`；`CountText.Text = count.ToString()`。
  - `RemoveItem(FileOperationItem item)`：`_items?.Remove(item)`。
  - `ClearCompleted()`：倒序移除 `IsCompleted` 项。
  - `CloseItem_Click`：`if (sender is FrameworkElement { DataContext: FileOperationItem item }) RemoveItem(item);`
  - `ClearCompleted_Click`：`ClearCompleted()`。
  - 本地化：构造函数里 `App.ML.PropertyChanged += OnMLPropertyChanged; ApplyLocalizedStrings();`；`ApplyLocalizedStrings()` 设 `HeaderTitleText.Text = App.ML.FileOperationsTitle; ClearCompletedBtn.Content = App.ML.ClearCompleted;`；`OnMLPropertyChanged` 调 `ApplyLocalizedStrings()`。
  - 验证：`dotnet build` 通过；`grep -n "ClearCompleted\|RemoveItem\|FileOperationsTitle" UserControls/FileOperationsButton.xaml.cs` 命中。

### Phase 3 — 接线（添加入口补 State+图标）

- [x] 5. `ViewModels/MainWindowViewModel.cs` — 完成/失败设置状态；粘贴按 isCut 设图标
  - `CompleteOperation`（~:257）：闭包内增加 `op.State = FileOperationState.Successful;`。
  - `FailOperation`（~:273）：闭包内增加 `op.State = FileOperationState.Error;`。
  - `Paste`（~:147）：在取得 `(paths, isCut)` 且非空后，`_uiDispatcherQueue.TryEnqueue(() => { if (op != null) op.IconGlyph = isCut ? "\uE8AB" : "\uE8C8"; });`（剪切→移动图标、复制→复制图标）。
  - 验证：`dotnet build` 通过；`grep -n "FileOperationState.Successful\|FileOperationState.Error\|IconGlyph" ViewModels/MainWindowViewModel.cs` 命中。

- [x] 6. `Views/MiddleFilesView.xaml.cs` — 三处添加入口补 State+IconGlyph
  - 粘贴 `OnPasteClick`（~:697 初始化器）：加 `State = FileOperationState.InProgress, IconGlyph = "\uE77F"`。
  - 删除 `OnDeleteClick`（~:805 初始化器）：加 `State = FileOperationState.Successful, IconGlyph = "\uE74D"`。
  - 彻底删除 `OnPermanentDeleteClick`（~:819 初始化器）：加 `State = FileOperationState.Successful, IconGlyph = "\uE74D"`。
  - 验证：`dotnet build` 通过；`grep -n "FileOperationState" Views/MiddleFilesView.xaml.cs` 命中 ≥3 处。

- [x] 7. `Extensions/Extensions/ArchivePlugin.cs` — 归档成功/失败设置 State
  - `CompressTo`（~:119 创建）与 `ExtractArchive`（~:184 创建）：初始化器加 `IconGlyph = "\uE7B8"`（State 默认即 InProgress，无需显式）。
  - 两处成功回调（~:162-167 与 ~:214-219）：加 `opItem.State = FileOperationState.Successful;`。
  - 两处失败回调（~:177 与 ~:228）：加 `opItem.State = FileOperationState.Error;`。
  - 验证：`dotnet build` 通过；`grep -n "FileOperationState" Extensions/Extensions/ArchivePlugin.cs` 命中 ≥4 处。

### Phase 4 — 多语言

- [x] 8. `Strings/en.json` + `Strings/zh-Hans.json` + `ViewModels/MultiLanguageStringsViewModel.cs` — 新增两条文案
  - `en.json`（`FileOpFailed` 之后）：`"FileOperationsTitle": "File operations"`、`"ClearCompleted": "Clear completed"`。
  - `zh-Hans.json`（`FileOpFailed` 之后）：`"FileOperationsTitle": "文件操作"`、`"ClearCompleted": "清除已完成"`。
  - `MultiLanguageStringsViewModel.cs`：`AllPropertyNames` 在 `nameof(FileOpFailed)` 后加 `nameof(FileOperationsTitle), nameof(ClearCompleted)`；类内新增 `public string FileOperationsTitle => _loc.GetString("FileOperationsTitle");`、`public string ClearCompleted => _loc.GetString("ClearCompleted");`。
  - 验证：`dotnet build` 通过；两 JSON 经 `ConvertFrom-Json` 解析无误；`grep -n "FileOperationsTitle\|ClearCompleted" Strings/*.json ViewModels/MultiLanguageStringsViewModel.cs` 命中。

## Final Verification Wave

- [x] F1. `dotnet build FastFluentFilesFolders/FastFluentFilesFolders.csproj -nologo` — ExitCode 0（.NET 10.0.400 已装）。
- [x] F2. 静态一致性：每个 `{Binding X}` 目标属性（`Text`/`Progress`/`ProgressDisplay`/`State`/`DisplayIconGlyph`/`IsCompleted`/`IsInProgress`）均在 `FileOperationItem` 存在；两个转换器 key（`StateToBrushConverter`/`BoolToVis`）在 XAML 资源已声明；`App.ML.FileOperationsTitle`/`ClearCompleted` 属性存在。
- [x] F3. 运行时冒烟：`dotnet run --project FastFluentFilesFolders`（unpackaged）启动，依次执行 ①粘贴 1 个文件、②删除到回收站、③归档压缩一个文件 → 断言：无异常/崩溃（进程存活）、操作岛按钮出现且打开 Flyout 可见卡片（进行中=强调色进度条、成功后=绿色 ✓ 图标 + 出现 X 按钮）；点「清除已完成」后已完成卡片清空、按钮在 0 项时隐藏。（可用 `/visual-qa` 或 `/playwright` 做视觉佐证；若无法自动化取景则退化为「无崩溃 + 日志无异常」判定。）

---

## 改动范围总览

| 文件 | 类型 | 内容 |
|------|------|------|
| `Models/FileOperationItem.cs` | 修改 | 加 `FileOperationState` 枚举 + `State`/`IconGlyph`/`DisplayIconGlyph`/`IsCompleted`/`IsInProgress` |
| `UserControls/FileOperationStateToBrushConverter.cs` | 新增 | 状态→主题刷转换器（8 个 DependencyProperty，background/foreground） |
| `UserControls/FileOperationsButton.xaml` | 重写 | 标题栏 + 卡片 ItemTemplate + 转换器资源 |
| `UserControls/FileOperationsButton.xaml.cs` | 修改 | `_items` 持有、`RemoveItem`/`ClearCompleted`/`UpdateCount`/本地化刷新 |
| `ViewModels/MainWindowViewModel.cs` | 修改 | `CompleteOperation`/`FailOperation` 设 `State`；`Paste` 按 isCut 设 `IconGlyph` |
| `Views/MiddleFilesView.xaml.cs` | 修改 | 粘贴/删除/彻底删除初始化器补 `State`+`IconGlyph` |
| `Extensions/Extensions/ArchivePlugin.cs` | 修改 | 压缩/解压成功失败设 `State`，创建设 `IconGlyph` |
| `Strings/en.json` / `Strings/zh-Hans.json` | 修改 | 新增 `FileOperationsTitle`、`ClearCompleted` |
| `ViewModels/MultiLanguageStringsViewModel.cs` | 修改 | 新增两属性 + `AllPropertyNames` 注册 |

## 决策记录（已定，无需再问）
1. 范围=视觉对齐（用户选定）；速度图/展开折叠/取消不做。
2. 保留按钮「0 项自动隐藏」；不新增空状态文案（不可达）。
3. 继续 ItemsControl；颜色经转换器从 `{ThemeResource}` 注入（主题自适应）。
4. 图标 glyph：复制 `\uE8C8`、移动/剪切 `\uE8AB`、粘贴 `\uE77F`、删除 `\uE74D`、归档 `\uE7B8`、成功 `\uE73E`、失败 `\uE783`、取消 `\uE711`。
