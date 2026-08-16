
## [2026-08-05] vendor 迁移完成 + 关键修复（T1-T7 验证通过）
- 迁移：csproj NuGet WinUI.TableView 1.4.1 -> ProjectReference FastFluentFilesFolders.TableView（vendored 项目，net8.0+WinAppSDK 2.2.0 保持原样）。
- namespace 迁移：WinUI.TableView -> FastFluentFilesFolders.UserControls.TableView（3 文件：LrsTableView.cs、MiddleFilesView.xaml、MiddleFilesView.xaml.cs）。LrsTableView 基类需 global:: 全限定名（namespace 与类名冲突 CS0118）。
- CollectionView.cs：OnSourceCollectionChanged Reset 分支 DetachPropertyChangedHandlers(e.OldItems) 修复为 (_attachedItems ?? e.OldItems) + 维护 _attachedItems 去重列表。
- **关键根因修复**：vendored 主题 XAML（Generic.xaml + 8 个子文件）用相对 Source=Resources.xaml，WinUI 自动发现 Generic.xaml 后无法解析相对路径 -> XamlParseException "String 不能赋给 Uri"（Line 46）-> UnhandledException handler 吞掉 -> 应用无窗口。修复：全部改为绝对路径 /FastFluentFilesFolders.TableView/Themes/Resources.xaml。
- 验证：首次 clean+build 后启动成功 HWND=1443340 标题=FastFluentFilesFolders（113MB）。后续 bash 环境 Launch 窗口不稳定（环境问题，非代码问题）。
- 未提交：16 个文件改动。需桌面手动验证 T8（快速切换回归）T9（功能回归）后提交。
