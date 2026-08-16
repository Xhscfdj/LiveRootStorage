# Learnings — file-ops-island-ui

Conventions, patterns, and successful approaches discovered during work on this plan.

_Auto-scaffolded by /start-work. Append new entries below - never overwrite._

---
## Task 8 (localization strings) - done
- Added FileOperationsTitle + ClearCompleted to en.json (File operations / Clear completed) and zh-Hans.json (文件操作 / 清除已完成), right after FileOpFailed.
- Added properties + AllPropertyNames registration in MultiLanguageStringsViewModel.cs (tab-indented, after FileOpFailed).
- JSON parse verified via ConvertFrom-Json; grep hits in all 3 files. No dotnet build (orchestrator builds later).

## Task 1 (FileOperationItem model) - done
- Added FileOperationState enum (InProgress/Successful/Error/Canceled) in FastFluentFilesFolders.Models.
- Added State (guarded, notifies DisplayIconGlyph/IsCompleted/IsInProgress), IconGlyph (guarded, notifies DisplayIconGlyph), DisplayIconGlyph (state switch: Successful=\uE73E, Error=\uE783, Canceled=\uE711, else IconGlyph), IsCompleted, IsInProgress.
- Existing properties and Notify helper untouched. No build run (orchestrator builds after wave).
## Task 2: FileOperationStateToBrushConverter (DONE)
- Created FastFluentFilesFolders/UserControls/FileOperationStateToBrushConverter.cs: 8 DependencyProperties (InProgress/Successful/Error/Canceled x Foreground/Background), parameter string "Background" selects background variant, ConvertBack throws NotImplementedException, no runtime resource lookup.
## Task 5 (MainWindowViewModel op state/icon) - done
- CompleteOperation: added `op.State = FileOperationState.Successful;` inside the existing TryEnqueue closure (after RemainTime).
- FailOperation: added `op.State = FileOperationState.Error;` inside the existing TryEnqueue closure (after RemainTime).
- Paste: added `_uiDispatcherQueue.TryEnqueue(() => { if (op != null) op.IconGlyph = isCut ? "\uE8AB" : "\uE8C8"; });` right after the null/empty paths guard (isCut known there). \uE8AB=move (cut), \uE8C8=copy.
- using FastFluentFilesFolders.Models already present (line 9); FileOperationState resolves. No build run (orchestrator builds after wave).
## Task 6 (MiddleFilesView op item State/IconGlyph) - done
- OnPasteClick (~line 697): pasteOp initializer gained `State = FileOperationState.InProgress, IconGlyph = "\uE77F"` (paste glyph; VM refines to copy/move \uE8C8/\uE8AB later).
- OnDeleteClick (~line 801) and OnPermanentDeleteClick (~line 815): single-line initializers gained `State = FileOperationState.Successful, IconGlyph = "\uE74D"` (delete glyph; DisplayIconGlyph overrides to green ✓ \uE73E on the card).
- using FastFluentFilesFolders.Models already present (line 4); FileOperationState resolves. No build run (orchestrator builds after wave).
## Task 7 (ArchivePlugin op state/icon) - done
- ArchivePlugin.cs: CompressTo + ExtractArchive opItem initializers gained `IconGlyph = "\uE7B8"` (zip/archive glyph) after FileCount.
- CompressTo success closure (after RemainTime) gained `opItem.State = FileOperationState.Successful;`; failure closure gained `opItem.State = FileOperationState.Error;` (same for ExtractArchive).
- State NOT set at creation (default InProgress → blue accent + archive icon via ReportOperation).
- Success/failure closures are textually identical between the two methods — disambiguated edits with preceding NotifyItemCreatedAsync(outputPath,false)/NotifyItemCreatedAsync(destDir,true) and Debug.WriteLine Compress/Extract lines.
- using FastFluentFilesFolders.Models already present (line 2); FileOperationState resolves. Verified: 2 IconGlyph + 4 FileOperationState usages. No build run (orchestrator builds after wave).
## Task 3 (FileOperationsButton.xaml card rewrite) - done
- Rewrote FastFluentFilesFolders/UserControls/FileOperationsButton.xaml: flyout Border MinWidth 300→340, kept CardBackground/CardStroke/CornerRadius 8/Padding 12.
- Added xmlns:uc (UserControls) + xmlns:views (Views) on root; UserControl.Resources holds exactly 2 converters: StateToBrushConverter (FileOperationStateToBrushConverter with all 8 brush DPs wired to ThemeResource) and BoolToVis (BoolToVisibilityConverter).
- RootBtn trigger button + CountText + &#xE8B7; icon untouched.
- Border now hosts Grid (Auto,Auto): Row0 header (HeaderTitleText FontSize14 SemiBold + ClearCompletedBtn right-aligned SubtleButtonStyle FontSize12 Padding 8,0 Click=ClearCompleted_Click), Row1 ScrollViewer MaxHeight=420 wrapping OpsList.
- DataTemplate card: outer Grid Margin 0,2 Padding 8 CardBackground CardStroke BorderThickness 1 CornerRadius 8, rows Auto,Auto; header row 3 cols Auto,*,Auto = 32x32 corner16 Border (State→Background brush) + FontIcon 16 (State→foreground brush), title TextBlock SemiBold CharacterEllipsis+ToolTip, close Button 32x32 transparent Visibility={IsCompleted BoolToVis} Click=CloseItem_Click glyph &#xE711;; progress row StackPanel Visibility={IsInProgress BoolToVis} = ProgressBar 4 accent + ProgressDisplay TextBlock FontSize11.
- All bindings plain {Binding} (no x:Bind/x:DataType). Click handlers referenced but code-behind lands in Task 4. No build run (orchestrator builds after wave).
## Task 4 (FileOperationsButton.xaml.cs code-behind) - done
- Rewrote FastFluentFilesFolders/UserControls/FileOperationsButton.xaml.cs (65 lines, 4-space indent): added using System.ComponentModel; + `private ObservableCollection<FileOperationItem>? _items;`.
- SetItems stores _items, sets OpsList.ItemsSource, subscribes CollectionChanged → UpdateCount(), calls UpdateCount() once (replaces the old inline lambda).
- UpdateCount(): null-guard, RootBtn visible only when count > 0, CountText = count.
- RemoveItem(item) = _items?.Remove(item); ClearCompleted() iterates BACKWARD removing IsCompleted items (backward loop avoids index shift).
- Click handlers match XAML exactly: CloseItem_Click (pattern-matches sender DataContext as FileOperationItem → RemoveItem), ClearCompleted_Click → ClearCompleted().
- Localization: ctor subscribes App.ML.PropertyChanged += OnMLPropertyChanged and calls ApplyLocalizedStrings() (HeaderTitleText.Text = App.ML.FileOperationsTitle; ClearCompletedBtn.Content = App.ML.ClearCompleted) — live re-localizes on language switch since FileOperationsTitle/ClearCompleted are in AllPropertyNames. No build run (orchestrator builds after wave).
## Flyout glitch fix (button always visible) - done
- Root cause: UpdateCount() collapsed RootBtn when count hit 0. Clearing completed items while the Flyout was open removed the anchor → flyout glitch. Fix = never collapse the button.
- XAML: removed `Visibility="Collapsed"` from RootBtn (now always Visible); added EmptyText TextBlock (Grid.Row="1", centered, Foreground=TextFillColorSecondaryBrush, Visibility=Collapsed) as sibling of the ScrollViewer so it overlays/centers the same Grid cell when OpsList is empty. ScrollViewer + OpsList untouched.
- code-behind UpdateCount(): RootBtn.Visibility=Visible always; `var count = _items.Count;` CountText.Visibility = count>0 ? Visible:Collapsed, CountText.Text = count, EmptyText.Visibility = count>0 ? Collapsed:Visible.
- ApplyLocalizedStrings() gains `EmptyText.Text = App.ML.NoFileOperations;`.
- Localization: added NoFileOperations to en.json ("No file operations") / zh-Hans.json ("无文件操作") after ClearCompleted; added `nameof(NoFileOperations)` to AllPropertyNames (after ClearCompleted) + `public string NoFileOperations => _loc.GetString("NoFileOperations");` after ClearCompleted property.
- dotnet build FastFluentFilesFolders\FastFluentFilesFolders.csproj -p:Platform=x64 → 0 errors.

## 2026-08-15 Follow-up fix
- 用户反馈弹窗抽风: 根因是 count==0 时 RootBtn 被 Collapsed, 弹窗开着时锚点消失 -> Flyout 抽搐. 改为按钮常驻(RootBtn 永远 Visible), 计数徽标 count>0 才显示, 新增空状态文案 NoFileOperations(无文件操作).
- 构建 0 错误; 解包启动冒烟通过.
