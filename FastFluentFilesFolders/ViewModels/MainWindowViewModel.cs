//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI;
using FastFluentFilesFolders.Models;
using FastFluentFilesFolders.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using Windows.System;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Runtime.InteropServices;

namespace FastFluentFilesFolders.ViewModels
{
	public partial class MainWindowViewModel : ViewModelBase
	{
		private IFileOperator _fileOperator;
		public MultiLanguageStringsViewModel ML { get; }
		public MainWindowViewModel(IIconProvider iconProvider, Microsoft.UI.Dispatching.DispatcherQueue uiDispatcherQueue, Configs configs, IFileOperator fileOperator, MultiLanguageStringsViewModel ml)
		{
			AppConfigs = configs;
			_fileOperator = fileOperator;
			CurrentBreadcrumbPath = configs.HomePageFullPath;
			_uiDispatcherQueue = uiDispatcherQueue;
			_iconProvider = iconProvider;
			ML = ml;

			// 初始标签页
			var firstTab = new ExplorerTab { Title = GetTabTitle(configs.HomePageFullPath) };
			firstTab.SearchResults = SearchResults;
			Tabs.Add(firstTab);
			SelectedTab = firstTab;

			NavigateToPathCommand = new RelayCommand<string>(NavigateToPath);
			NavigateToSubFolderCommand = new RelayCommand<string>(NavigateToPath);
			GoBackCommand = new RelayCommand(GoBack);
			GoForwardCommand = new RelayCommand(GoForward);
			GoUpCommand = new RelayCommand(GoUp);
			// 把图标解码并发上限接入实际生效的信号量：0 = 自动（安全上限 16，避免并发过高偶发失败），
			// >0 = 按配置限流；避免大文件夹进入时图标逐项“轮流替换”造成拖沓感
			FileSystemNodeViewModel.ConfigureIconLoadConcurrency(configs.IconParallelLoadingCount);
			if (configs.IconParallelLoadingCount > 0)
				IconLoadSemaphore = new(configs.IconParallelLoadingCount, configs.IconParallelLoadingCount);

			foreach (var drive in DriveInfo.GetDrives())
			{
				if (drive.IsReady)
				{
					RootDirectories.Add(new FileSystemNodeViewModel(drive.RootDirectory.FullName, true, false, configs, uiDispatcherQueue, true));
				}
			}

			if (RootDirectories.Count > 0)
				SelectedFolder = RootDirectories[0];
		}

		public async Task DeferredInitializeAsync()
		{
			try
			{
				var pinnedPaths = await Task.Run(() => GetQuickAccessPinnedFolders());
				await _uiDispatcherQueue.EnqueueAsync(() =>
				{
					foreach (var path in pinnedPaths)
					{
						if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
						{
							PinnedShortcuts.Add(new FileSystemNodeViewModel(path, true, false, AppConfigs, _uiDispatcherQueue, true));
						}
					}

					var startPath = GetStartupPath();
					if (!string.IsNullOrEmpty(startPath) && Directory.Exists(startPath))
						NavigateToPath(startPath);
				});
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[DeferredInit] Failed: {ex.Message}");
			}
			finally
			{
				await _uiDispatcherQueue.EnqueueAsync(() => IsReady = true);
			}
		}

		/// <summary>
		/// 获取启动时应导航到的路径：优先上次访问路径，回退到首页路径
		/// </summary>
		private string GetStartupPath()
		{
			if (!string.IsNullOrEmpty(AppConfigs.LastVisitedPath) && Directory.Exists(AppConfigs.LastVisitedPath))
				return AppConfigs.LastVisitedPath;
			if (!string.IsNullOrEmpty(AppConfigs.HomePageFullPath))
				return AppConfigs.HomePageFullPath;
			return string.Empty;
		}
		[RelayCommand]
		private void testFunction()
		{
			Debug.WriteLine("[DebugButton] Pressed.");
			var folder = RootDirectories.FirstOrDefault(); // 取第一个驱动器
			_ = UpdateCurrentFolderContentAsync(folder, version: null);
			TestString = "Modified by testFunction.";
			Debug.WriteLine($"[DebugButton] CurrentFolderContent count is {CurrentFolderContent.Count}");
			foreach (var item in CurrentFolderContent)
			{
				Debug.WriteLine($"[DebugButton]Item: {item.Name}, Type is {item.NodeTypeName}");
			}
		}

		[RelayCommand]
		private async Task Copy(IReadOnlyList<FileSystemNodeViewModel>? items)
		{
			if (items == null || items.Count == 0) return;
			ClearCutPending();
			await _fileOperator.CopyToClipBoard(items.Select(i => i.FullPath));
		}

		[RelayCommand]
		private async Task Cut(IReadOnlyList<FileSystemNodeViewModel>? items)
		{
			if (items == null || items.Count == 0) return;
			ClearCutPending();
			_cutItems = items.ToList();
			foreach (var item in _cutItems)
				item.IsCutPending = true;
			await _fileOperator.CopyToClipBoard(items.Select(i => i.FullPath), true);
		}

		private List<FileSystemNodeViewModel> _cutItems = new();

		private void ClearCutPending()
		{
			foreach (var item in _cutItems)
				item.IsCutPending = false;
			_cutItems.Clear();
		}

		[RelayCommand]
		private async Task Paste(FileOperationItem? op)
		{
			await _pasteLock.WaitAsync();
			try
			{
				var (paths, isCut) = await _fileOperator.PasteClipboardFiles();
				if (paths == null || !paths.Any())
				{
					CompleteOperation(op, 0, 0);
					return;
				}

				_uiDispatcherQueue.TryEnqueue(() => { if (op != null) op.IconGlyph = isCut ? "\uE8AB" : "\uE8C8"; });

				var pathList = paths.ToList();
				var destDir = SelectedFolder?.FullPath ?? CurrentBreadcrumbPath;

				// 统计待粘贴项的文件总数与总大小，让操作岛显示真实的文件个数与大小
				var (totalFiles, totalBytes) = await _fileOperator.GetTransferStatsAsync(pathList);
				UpdateOperationProgress(op, 0, totalFiles, 0, totalBytes);

				if (isCut && _cutItems.Count > 0)
				{
					var srcDir = Path.GetDirectoryName(_cutItems[0].FullPath) ?? "";
					if (string.Equals(srcDir, destDir, StringComparison.OrdinalIgnoreCase))
					{
						await _uiDispatcherQueue.EnqueueAsync(() =>
						{
							foreach (var item in _cutItems)
								item.IsCutPending = false;
							_cutItems.Clear();
						});
						CompleteOperation(op, totalFiles, totalBytes);
						return;
					}

					await _uiDispatcherQueue.EnqueueAsync(() =>
					{
						foreach (var item in _cutItems)
						{
							CurrentFolderContent.Remove(item);
							SelectedFolder?.Children.Remove(item);
						}
						ClearCutPending();
					});
				}

				var newNodes = new List<(FileSystemNodeViewModel Node, string SourcePath)>();
				foreach (var srcPath in pathList)
				{
					var name = Path.GetFileName(srcPath);
					var destPath = Path.Combine(destDir, name);
					if (!isCut)
						destPath = GenerateUniquePath(destPath);
					if (isCut)
						await _fileOperator.MoveAsync(srcPath, destPath);
					else
						await _fileOperator.CopyToAsync(srcPath, destPath, false,
							p => UpdateOperationProgress(op, p.CompletedFiles, totalFiles, p.CompletedBytes, totalBytes));

					bool isDir = Directory.Exists(destPath);
					var node = new FileSystemNodeViewModel(destPath, isDir, false, AppConfigs, _uiDispatcherQueue, false);
					_ = node.InitAsync(node.FullPath, isDir);
					PrepareNodeForGroupedView(node);
					newNodes.Add((node, srcPath));
				}

				CompleteOperation(op, totalFiles, totalBytes);

				await _uiDispatcherQueue.EnqueueAsync(() =>
				{
					foreach (var (node, _) in newNodes)
					{
						CurrentFolderContent.Add(node);
						SelectedFolder?.Children.Add(node);
					}
				});

				BreadcrumbRefreshRequested?.Invoke();
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[Paste] 粘贴失败: {ex}");
				FailOperation(op);
			}
			finally
			{
				_pasteLock.Release();
			}
		}

		/// <summary>
		/// 将累积进度映射到操作岛显示：文件个数、进度百分比、已传输/总大小。
		/// </summary>
		private void UpdateOperationProgress(FileOperationItem? op, int completedFiles, int totalFiles, long completedBytes, long totalBytes)
		{
			if (op == null) return;
			double percent = totalBytes > 0
				? (double)completedBytes / totalBytes * 100.0
				: (totalFiles > 0 ? (double)completedFiles / totalFiles * 100.0 : 100.0);
			_uiDispatcherQueue.TryEnqueue(() =>
			{
				op.Progress = Math.Clamp(percent, 0.0, 100.0);
				op.Process = $"{(int)percent}%";
				op.FileCount = totalFiles;
				op.SizeText = $"{FileSystemNodeViewModel.FormatFileSize(completedBytes)} / {FileSystemNodeViewModel.FormatFileSize(totalBytes)}";
			});
		}

		/// <summary>
		/// 标记操作完成：进度 100%，大小显示为总量。
		/// </summary>
		private void CompleteOperation(FileOperationItem? op, int totalFiles, long totalBytes)
		{
			if (op == null) return;
			_uiDispatcherQueue.TryEnqueue(() =>
			{
				op.Progress = 100;
				op.Process = "100%";
				op.FileCount = totalFiles;
				op.SizeText = $"{FileSystemNodeViewModel.FormatFileSize(totalBytes)} / {FileSystemNodeViewModel.FormatFileSize(totalBytes)}";
				op.RemainTime = "0";
				op.State = FileOperationState.Successful;
			});
		}

		/// <summary>
		/// 标记操作失败。
		/// </summary>
		private void FailOperation(FileOperationItem? op)
		{
			if (op == null) return;
			_uiDispatcherQueue.TryEnqueue(() =>
			{
				op.Progress = 0;
				op.Process = ML.FileOpFailed;
				op.RemainTime = "0";
				op.State = FileOperationState.Error;
			});
		}

		private static string GenerateUniquePath(string destPath)
		{
			if (!File.Exists(destPath) && !Directory.Exists(destPath))
				return destPath;

			var dir = Path.GetDirectoryName(destPath) ?? "";
			var name = Path.GetFileNameWithoutExtension(destPath);
			var ext = Path.GetExtension(destPath);

			int index = 1;
			string newPath;
			do
			{
				newPath = Path.Combine(dir, $"{name} ({index}){ext}");
				index++;
			}
			while (File.Exists(newPath) || Directory.Exists(newPath));

			return newPath;
		}

		[RelayCommand]
		private async Task Delete(IReadOnlyList<FileSystemNodeViewModel>? items)
		{
			if (items == null || items.Count == 0) return;
			foreach (var item in items)
				await _fileOperator.DeleteToRecycleBinAsync(item.FullPath);
			await _uiDispatcherQueue.EnqueueAsync(() =>
			{
				foreach (var item in items)
				{
					if (IsSearchMode)
						SearchResults.Remove(item);
					else
					{
						CurrentFolderContent.Remove(item);
						SelectedFolder?.Children.Remove(item);
					}
				}
				if (IsSearchMode)
				{
					OnPropertyChanged(nameof(SearchResults));
					OnPropertyChanged(nameof(DisplayedItemCount));
				}
			});
		}

		[RelayCommand]
		private async Task PermanentDelete(IReadOnlyList<FileSystemNodeViewModel>? items)
		{
			if (items == null || items.Count == 0) return;
			foreach (var item in items)
				await _fileOperator.DeleteAsync(item.FullPath);
			await _uiDispatcherQueue.EnqueueAsync(() =>
			{
				foreach (var item in items)
				{
					if (IsSearchMode)
						SearchResults.Remove(item);
					else
					{
						CurrentFolderContent.Remove(item);
						SelectedFolder?.Children.Remove(item);
					}
				}
				if (IsSearchMode)
				{
					OnPropertyChanged(nameof(SearchResults));
					OnPropertyChanged(nameof(DisplayedItemCount));
				}
			});
		}

		[RelayCommand]
		private async Task Rename(FileSystemNodeViewModel? item)
		{
			if (item == null) return;
			CancelRename();
			await _uiDispatcherQueue.EnqueueAsync(() =>
			{
				item.IsRenaming = true;
				_renamingItem = item;
				RenameFocusRequested?.Invoke(item);
			});
		}

		private FileSystemNodeViewModel? _renamingItem;

		public event Action<FileSystemNodeViewModel>? RenameFocusRequested;

		public void CancelRename()
		{
			if (_renamingItem != null)
			{
				_renamingItem.IsRenaming = false;
				_renamingItem = null;
			}
		}

		public event Action? BreadcrumbRefreshRequested;
		public event Action<FileSystemNodeViewModel>? SelectItemRequested;

		public void RequestBreadcrumbRefresh() => BreadcrumbRefreshRequested?.Invoke();

		public async Task CommitRenameAsync(FileSystemNodeViewModel item, string newName)
		{
			if (string.IsNullOrEmpty(newName)) return;
			try
			{
				var oldDir = Path.GetDirectoryName(item.FullPath) ?? "";
				var newPath = Path.Combine(oldDir, newName);
				await _fileOperator.RenameAsync(item.FullPath, newName);
				item.FullPath = newPath;
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[Rename] Failed: {ex.Message}");
			}
			_renamingItem = null;
		}

		public void AddItemToCurrentView(string fullPath, bool isDirectory)
		{
			var node = new FileSystemNodeViewModel(fullPath, isDirectory, false, _appConfigs, _uiDispatcherQueue, false);
			_ = node.InitAsync(node.FullPath, isDirectory);
			PrepareNodeForGroupedView(node);
			_uiDispatcherQueue.TryEnqueue(() =>
			{
				CurrentFolderContent.Add(node);
				SelectedFolder?.Children.Add(node);
			});
		}

		[RelayCommand]
		private async Task CopyPath(IReadOnlyList<FileSystemNodeViewModel>? items)
		{
			if (items == null || items.Count == 0) return;
			var text = string.Join(Environment.NewLine, items.Select(i => i.FullPath));
			var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
			dataPackage.SetText(text);
			Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
			await Task.CompletedTask;
		}

		[RelayCommand]
		private async Task OpenWith(FileSystemNodeViewModel? item)
		{
			if (item == null || item.IsDirectory) return;
			await Task.Run(() => ShowOpenWithDialog(item.FullPath));
		}

		internal static void ShowOpenWithDialog(string filePath)
		{
			if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
				return;

			var info = new OPENASINFO
			{
				pcszFile = Marshal.StringToHGlobalUni(filePath),
				pcszClass = IntPtr.Zero,
				oaifInFlags = OPEN_AS_INFO_FLAGS.OAIF_ALLOW_REGISTRATION | OPEN_AS_INFO_FLAGS.OAIF_EXEC
			};
			try
			{
				SHOpenWithDialog(IntPtr.Zero, ref info);
			}
			finally
			{
				Marshal.FreeHGlobal(info.pcszFile);
			}
		}

		[RelayCommand]
		private async Task Properties(FileSystemNodeViewModel? item)
		{
			if (item == null) return;
			await _uiDispatcherQueue.EnqueueAsync(() => ShowPropertiesDialog(item));
		}

		private async void ShowPropertiesDialog(FileSystemNodeViewModel item)
		{
			var panel = new StackPanel { Spacing = 12, Width = 420, Margin = new Thickness(0, 0, 0, 8) };

			var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
			if (item.Icon != null)
				header.Children.Add(new Image { Source = item.Icon, Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Center });
			header.Children.Add(new TextBlock
			{
				Text = item.Name,
				FontSize = 18,
				FontWeight = FontWeights.SemiBold,
				TextTrimming = TextTrimming.CharacterEllipsis,
				VerticalAlignment = VerticalAlignment.Center
			});
			panel.Children.Add(header);

			panel.Children.Add(new Border { Height = 1, Background = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"] });

			var propsGrid = new Grid { ColumnSpacing = 16, RowSpacing = 10 };
			propsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
			propsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

			int row = 0;
			void AddRow(string label, string value)
			{
				propsGrid.RowDefinitions.Add(new RowDefinition());
				var lbl = new TextBlock
				{
					Text = label,
					Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
					FontSize = 13,
					VerticalAlignment = VerticalAlignment.Center,
					Margin = new Thickness(0, 0, 4, 0)
				};
				var val = new TextBlock
				{
					Text = value,
					Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
					FontSize = 13,
					TextWrapping = TextWrapping.Wrap,
					VerticalAlignment = VerticalAlignment.Center
				};
				Grid.SetRow(lbl, row); Grid.SetColumn(lbl, 0);
				Grid.SetRow(val, row); Grid.SetColumn(val, 1);
				propsGrid.Children.Add(lbl);
				propsGrid.Children.Add(val);
				row++;
			}

			var typeDesc = item.IsDirectory ? App.ML.PropertiesFolder
				: item.Extension.Length > 0 ? $"{item.Extension.TrimStart('.')} {App.ML.PropertiesFile}"
				: App.ML.PropertiesFile;
			AddRow(App.ML.PropertiesType, typeDesc);
			AddRow(App.ML.PropertiesPath, item.FullPath);
			var sizeText = item.IsDirectory ? item.VisualSize : $"{item.VisualSize} ({string.Format(App.ML.PropertiesBytesFmt, item.ExactSize.ToString("N0"))})";
			AddRow(App.ML.PropertiesSize, sizeText);
			AddRow(App.ML.PropertiesModified, item.LastModifiedTimeString);
			AddRow(App.ML.PropertiesCreated, item.FirstCreatedTimeString);

			// 使用进程（仅对文件显示）
			if (!item.IsDirectory)
			{
				var processInfo = ProcessHelper.GetProcessesUsingFile(item.FullPath);
				AddRow(App.ML.PropertiesProcesses, string.IsNullOrEmpty(processInfo) ? "-" : processInfo);
			}

			panel.Children.Add(propsGrid);

			var dialog = new ContentDialog
			{
				Title = App.ML.PropertiesTitle,
				Content = panel,
				CloseButtonText = App.ML.PropertiesClose,
				DefaultButton = ContentDialogButton.Close,
				XamlRoot = App.MainWindow.Content.XamlRoot
			};

			panel.IsTabStop = true;
			panel.UseSystemFocusVisuals = false;
			dialog.Opened += (_, _) => panel.Focus(FocusState.Programmatic);

			void OnDialogKeyDown(object _, KeyRoutedEventArgs args)
			{
				if (args.Key == VirtualKey.C)
				{
					dialog.Hide();
					args.Handled = true;
				}
			}
			dialog.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnDialogKeyDown), true);

			_ = dialog.ShowAsync();
		}

		[RelayCommand]
		private async Task NewFolder()
		{
			var destDir = SelectedFolder?.FullPath ?? CurrentBreadcrumbPath;
			await AddNewItemToViewAsync(destDir, App.ML.NewFolderDefault, isDirectory: true);
			BreadcrumbRefreshRequested?.Invoke();
		}

		[RelayCommand]
		private async Task NewTextDocument()
		{
			var destDir = SelectedFolder?.FullPath ?? CurrentBreadcrumbPath;
			await AddNewItemToViewAsync(destDir, App.ML.NewTextDocumentDefault, isDirectory: false);
			BreadcrumbRefreshRequested?.Invoke();
		}

		[RelayCommand]
		private async Task NewShortcut()
		{
			var destDir = SelectedFolder?.FullPath ?? CurrentBreadcrumbPath;
			await AddNewItemToViewAsync(destDir, App.ML.NewShortcutDefault, isDirectory: false);
			BreadcrumbRefreshRequested?.Invoke();
		}

		[RelayCommand]
		private async Task NewFile()
		{
			var destDir = SelectedFolder?.FullPath ?? CurrentBreadcrumbPath;
			await AddNewItemToViewAsync(destDir, App.ML.NewFileDefault, isDirectory: false);
			BreadcrumbRefreshRequested?.Invoke();
		}

		[RelayCommand]
		private async Task NewExcelSpreadsheet()
		{
			var destDir = SelectedFolder?.FullPath ?? CurrentBreadcrumbPath;
			await AddNewItemToViewAsync(destDir, App.ML.NewExcelDefault, isDirectory: false);
			BreadcrumbRefreshRequested?.Invoke();
		}

		[RelayCommand]
		private async Task NewWordDocument()
		{
			var destDir = SelectedFolder?.FullPath ?? CurrentBreadcrumbPath;
			await AddNewItemToViewAsync(destDir, App.ML.NewWordDefault, isDirectory: false);
			BreadcrumbRefreshRequested?.Invoke();
		}

		[RelayCommand]
		private async Task NewPowerPointPresentation()
		{
			var destDir = SelectedFolder?.FullPath ?? CurrentBreadcrumbPath;
			await AddNewItemToViewAsync(destDir, App.ML.NewPPTDefault, isDirectory: false);
			BreadcrumbRefreshRequested?.Invoke();
		}

		// 若当前文件夹按时间分组，需在加入视图前同步补齐 LastModifiedTime 并设置分组键，
		// 否则元数据尚未异步加载完毕，条目会被错误地归入“很久以前”而看不到刷新效果。
		private void PrepareNodeForGroupedView(FileSystemNodeViewModel node)
		{
			if (SelectedFolder?.WillSplitToDifferentSorts != true || node.IsPlaceholder)
				return;
			node.LoadMetadataSync();
			node.SortByTime = Helpers.GroupedFileList.GetTimeGroup(node.LastModifiedTime);
		}

		private async Task AddNewItemToViewAsync(string destDir, string defaultName, bool isDirectory)
		{
			var newPath = GenerateUniquePath(Path.Combine(destDir, defaultName));
			if (isDirectory)
				Directory.CreateDirectory(newPath);
			else
				File.Create(newPath).Dispose();

			var node = new FileSystemNodeViewModel(newPath, isDirectory, false, AppConfigs, _uiDispatcherQueue, false);
			PrepareNodeForGroupedView(node);
			await _uiDispatcherQueue.EnqueueAsync(() =>
			{
				CurrentFolderContent.Add(node);
				SelectedFolder?.Children.Add(node);
			});
			_ = node.InitAsync(node.FullPath, isDirectory);
		}

		private void AddNewItemToView(string destDir, string defaultName, bool isDirectory)
		{
			_ = AddNewItemToViewAsync(destDir, defaultName, isDirectory);
		}

		public async Task RefreshCurrentFolderAsync()
		{
			if (SelectedFolder != null)
			{
				// 强制刷新：清空展示标记，使 UpdateCurrentFolderContentAsync 重建列表
				_displayedFolderNode = null;
				await SelectedFolder.ReloadChildrenAsync();
				await UpdateCurrentFolderContentAsync(SelectedFolder, version: null);
				BreadcrumbRefreshRequested?.Invoke();
			}
		}

		private void DebounceSaveLastVisitedPath(string path)
		{
			// 初始化完成前不保存（构造函数中 SelectedFolder=C:\ 会误触发）
			if (AppConfigs == null || !IsReady) return;
			AppConfigs.LastVisitedPath = path;
			_saveConfigCts?.Cancel();
			_saveConfigCts = new CancellationTokenSource();
			var token = _saveConfigCts.Token;
			_ = Task.Run(async () =>
			{
				try
				{
					await Task.Delay(2000, token);
					if (!token.IsCancellationRequested)
					{
						await _uiDispatcherQueue.EnqueueAsync(() =>
						{
							if (!token.IsCancellationRequested)
								AppConfigs?.SaveConfig();
						});
					}
				}
				catch (TaskCanceledException) { }
			}, token);
		}

		[ObservableProperty] private string _testString = "hasn't changed";
		[ObservableProperty] private ObservableCollection<FileSystemNodeViewModel> _pinnedShortcuts = new();
		// 文件系统相关的属性和方法
		private readonly IIconProvider _iconProvider;
		//[ObservableProperty] private string[] _PathsForBreadcrumbBar = ["C:\\"];
		[ObservableProperty] private ObservableCollection<FileSystemNodeViewModel> _currentFolderContent = new();
		[ObservableProperty] private string _currentBreadcrumbPath = "C:\\";
		[ObservableProperty] private Configs? _appConfigs = null;
		[ObservableProperty] private bool _canGoBack;
		[ObservableProperty] private bool _canGoForward;
		[ObservableProperty] private bool _isSettingsOpen;
		[ObservableProperty] private bool _isReady;
		[ObservableProperty] private bool _isSearchMode = false;
		[ObservableProperty] private string _searchText = string.Empty;
		[ObservableProperty] private ObservableCollection<FileSystemNodeViewModel> _searchResults = new();
		[ObservableProperty] private bool _isSearching = false;
		private CancellationTokenSource? _searchCts;

		public int DisplayedItemCount => IsSearchMode ? SearchResults.Count : CurrentFolderContent.Count;
		partial void OnIsSearchModeChanged(bool value) => OnPropertyChanged(nameof(DisplayedItemCount));

		partial void OnSearchTextChanged(string value) => TriggerSearch(value);

		// ===== 多标签页 =====
		public ObservableCollection<ExplorerTab> Tabs { get; } = new();
		[ObservableProperty] private ExplorerTab? _selectedTab;
		public ExplorerTab? CurrentTab => SelectedTab;
		private bool _isRestoringTab;

		[RelayCommand]
		private void NewTab()
		{
			var path = GetStartupPath();
			var tab = new ExplorerTab();
			if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
			{
				tab.Path = path;
				tab.Title = GetTabTitle(path);
			}
			else
			{
				tab.Title = ML.NavExplorer;
			}
			Tabs.Add(tab);
			SwitchToTab(tab);
		}

		public void CloseCurrentTab()
		{
			if (SelectedTab != null) CloseTab(SelectedTab);
		}

		public void CloseTab(ExplorerTab tab)
		{
			if (tab == null || Tabs.Count <= 1) return; // 至少保留一个标签页
			var index = Tabs.IndexOf(tab);
			var wasActive = ReferenceEquals(tab, SelectedTab);

			if (wasActive)
			{
				SaveTabState(tab);
				var next = index + 1 < Tabs.Count ? Tabs[index + 1] : Tabs[index - 1];
				SelectedTab = next;
				ActivateTab(next);
				tab.SearchCts?.Cancel();
				Tabs.Remove(tab);
			}
			else
			{
				tab.SearchCts?.Cancel();
				Tabs.Remove(tab);
			}
		}

		public void SwitchToTab(ExplorerTab tab)
		{
			if (tab == null || ReferenceEquals(tab, SelectedTab)) return;
			if (SelectedTab != null)
			{
				SaveTabState(SelectedTab);
				SelectedTab.SearchCts?.Cancel();
			}
			SelectedTab = tab;
			ActivateTab(tab);
		}

		private void SaveTabState(ExplorerTab tab)
		{
			if (tab == null) return;
			tab.Path = SelectedFolder?.FullPath ?? CurrentBreadcrumbPath;
			tab.FolderNode = SelectedFolder;
			tab.IsSearchMode = IsSearchMode;
			tab.SearchText = SearchText;
			tab.IsSearching = IsSearching;
			tab.SearchResults = SearchResults;
			tab.SearchCts = _searchCts;
		}

		private void ActivateTab(ExplorerTab tab)
		{
			if (tab == null) return;

			// 恢复搜索状态（恢复过程中不重复触发搜索）
			var searchText = tab.SearchText;
			var restartSearch = tab.IsSearching;
			_isRestoringTab = true;
			try
			{
				SearchResults = tab.SearchResults;
				SearchText = searchText;
				IsSearchMode = tab.IsSearchMode;
				IsSearching = tab.IsSearching;
			}
			finally
			{
				_isRestoringTab = false;
			}
			OnPropertyChanged(nameof(SearchResults));
			OnPropertyChanged(nameof(DisplayedItemCount));

			// 恢复导航状态
			var path = string.IsNullOrEmpty(tab.Path) ? GetStartupPath() : tab.Path;
			CurrentBreadcrumbPath = path;
			tab.IsNavigatingFromHistory = true;
			try
			{
				var target = tab.FolderNode
					?? FindNodeByPath(path)
					?? CreateStandaloneNode(path);
				if (target != null)
				{
					tab.FolderNode = target;
					SelectedFolder = target;
				}
				else
				{
					CurrentFolderContent.Clear();
				}
			}
			finally
			{
				tab.IsNavigatingFromHistory = false;
			}

			CanGoBack = tab.BackStack.Count > 0;
			CanGoForward = tab.ForwardStack.Count > 0;

			// 切回标签页时，若上次离开时搜索仍在进行，则重新发起搜索
			if (restartSearch && tab.IsSearchMode && !string.IsNullOrEmpty(searchText))
				TriggerSearch(searchText);

			RequestBreadcrumbRefresh();
		}

		private FileSystemNodeViewModel? CreateStandaloneNode(string path)
		{
			if (string.IsNullOrEmpty(path)) return null;
			if (ArchiveHelper.IsArchiveVirtualPath(path, out var archiveFile, out var relative))
			{
				var node = FileSystemNodeViewModel.CreateArchiveDirectory(archiveFile, relative, AppConfigs!, _uiDispatcherQueue);
				node.IsStandalone = true;
				return node;
			}
			if (!Directory.Exists(path)) return null;
			// lazyLoad: true —— 恢复标签页时同样避免重复枚举，子项由 UpdateCurrentFolderContentAsync 加载一次
			var newNode = new FileSystemNodeViewModel(path, true, false, AppConfigs!, _uiDispatcherQueue, true);
			newNode.IsStandalone = true;
			return newNode;
		}

		private static string GetTabTitle(string path)
		{
			if (string.IsNullOrEmpty(path)) return string.Empty;
			if (ArchiveHelper.IsArchiveVirtualPath(path, out var archiveFile, out var relative))
				path = string.IsNullOrEmpty(relative) ? archiveFile : relative;
			var name = Path.GetFileName(path.TrimEnd('\\'));
			return string.IsNullOrEmpty(name) ? path.TrimEnd('\\') : name;
		}

		public void EnterSearchMode()
		{
			// 已是搜索模式时（例如切换标签页恢复搜索 UI）不清空已有结果
			if (!IsSearchMode)
			{
				SearchResults.Clear();
				SearchText = string.Empty;
				IsSearchMode = true;
			}
			if (CurrentTab != null) CurrentTab.IsSearchMode = true;
		}

		public void ExitSearchMode()
		{
			_searchCts?.Cancel();
			IsSearchMode = false;
			SearchText = string.Empty;
			SearchResults.Clear();
			if (CurrentTab != null)
			{
				CurrentTab.IsSearchMode = false;
				CurrentTab.SearchText = string.Empty;
			}
		}

		private void TriggerSearch(string query)
		{
			if (_isRestoringTab) return;
			var tab = CurrentTab;
			_searchCts?.Cancel();
			_searchCts = new CancellationTokenSource();
			var token = _searchCts.Token;
			if (tab != null)
			{
				tab.SearchCts = _searchCts;
				tab.SearchText = query;
			}
			query = query.Trim();
			if (query.Length == 0)
			{
				IsSearching = false;
				if (tab != null) tab.IsSearching = false;
				SearchResults.Clear();
				OnPropertyChanged(nameof(SearchResults));
				OnPropertyChanged(nameof(DisplayedItemCount));
				return;
			}
			var scope = SelectedFolder?.FullPath ?? CurrentBreadcrumbPath;
			if (string.IsNullOrEmpty(scope) || !Directory.Exists(scope))
			{
				IsSearching = false;
				if (tab != null) tab.IsSearching = false;
				return;
			}
			IsSearching = true;
			if (tab != null) tab.IsSearching = true;
			SearchResults.Clear();
			OnPropertyChanged(nameof(SearchResults));
			OnPropertyChanged(nameof(DisplayedItemCount));
			_ = Task.Run(() => RunSearchAsync(scope, query, token), token);
		}

		private async Task RunSearchAsync(string scope, string query, CancellationToken token)
		{
			var results = new List<FileSystemNodeViewModel>();
			var sw = System.Diagnostics.Stopwatch.StartNew();
			var indexed = await Services.WindowsSearchHelper.QueryIndexAsync(scope, query, token);
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
				if (CurrentTab != null) CurrentTab.IsSearching = false;
			});
		}

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

		private void SearchRecursive(string dir, string query, List<FileSystemNodeViewModel> results, CancellationToken token)
		{
			if (token.IsCancellationRequested || results.Count >= 200) return;
			foreach (var sub in EnumerateDirsSafe(dir))
			{
				if (token.IsCancellationRequested || results.Count >= 200) break;
				if (Path.GetFileName(sub).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
					results.Add(CreateSearchNode(sub, true));
				SearchRecursive(sub, query, results, token);
			}
			if (token.IsCancellationRequested || results.Count >= 200) return;
			foreach (var file in EnumerateFilesSafe(dir))
			{
				if (token.IsCancellationRequested || results.Count >= 200) break;
				if (Path.GetFileName(file).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
					results.Add(CreateSearchNode(file, false));
			}
		}

		private FileSystemNodeViewModel CreateSearchNode(string path, bool isDir)
		{
			var node = new FileSystemNodeViewModel(path, isDir, false, AppConfigs!, _uiDispatcherQueue, true);
			try
			{
				if (isDir) { var d = new DirectoryInfo(path); node.ApplyMetadata(true, 0, d.LastWriteTimeUtc, d.CreationTimeUtc); }
				else { var f = new FileInfo(path); node.ApplyMetadata(false, f.Length, f.LastWriteTimeUtc, f.CreationTimeUtc); }
			}
			// 元数据读取失败时保留默认值
			catch { }
			return node;
		}

		public Microsoft.UI.Xaml.Visibility FileTableVisibility => IsSettingsOpen ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
		public Microsoft.UI.Xaml.Visibility SettingsVisibility => IsSettingsOpen ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
		partial void OnIsSettingsOpenChanged(bool value)
		{
			OnPropertyChanged(nameof(FileTableVisibility));
			OnPropertyChanged(nameof(SettingsVisibility));
		}
		public SemaphoreSlim IconLoadSemaphore = new(30, 30); // 最多30个并发
		private readonly SemaphoreSlim _pasteLock = new(1, 1);
		private const int MaxBackDepth = 100;
		private CancellationTokenSource? _saveConfigCts;
		private ObservableCollection<FileSystemNodeViewModel> _rootDirectories = new();
		public ObservableCollection<FileSystemNodeViewModel> RootDirectories
		{
			get => _rootDirectories;
			set => _rootDirectories = value;
		}
		private Microsoft.UI.Dispatching.DispatcherQueue _uiDispatcherQueue;
		[ObservableProperty] private FileSystemNodeViewModel? _selectedFolder;
		// 当前表格正在展示其内容的文件夹节点：重复进入同一文件夹时跳过无谓的重建
		private FileSystemNodeViewModel? _displayedFolderNode;

		public bool IsCurrentFolderSpecial => SelectedFolder?.WillSplitToDifferentSorts ?? false;

		partial void OnSelectedFolderChanged(FileSystemNodeViewModel? value)
		{
			var tab = CurrentTab;
			var version = tab != null ? ++tab.NavigationVersion : 1;
			if (tab != null && tab.FolderToRelease != null && tab.FolderToRelease != value)
			{
				// 释放子项缓存并重置加载状态，避免退回该文件夹时显示为空
				tab.FolderToRelease.ReleaseChildren();
				tab.FolderToRelease = null;
			}
			Debug.WriteLine($"\n----Selected:{value?.Name}\n");
			Debug.WriteLine($"OnSelectedFolderChanged called with value: {value?.FullPath ?? "null"}");
			Debug.WriteLine($"Is UI thread? {_uiDispatcherQueue.HasThreadAccess}");
			if (value != null)
			{
				if (tab != null && !tab.IsNavigatingFromHistory && tab.PreviousPath != null && tab.PreviousPath != value.FullPath)
				{
					tab.BackStack.Add(tab.PreviousPath);
					if (tab.BackStack.Count > MaxBackDepth) tab.BackStack.RemoveAt(0);
					tab.ForwardStack.Clear();
				}
				if (tab != null)
				{
					tab.PreviousPath = value.FullPath;
					tab.Path = value.FullPath;
					tab.FolderNode = value;
					tab.Title = GetTabTitle(value.FullPath);
				}
				CanGoBack = tab != null && tab.BackStack.Count > 0;
				CanGoForward = tab != null && tab.ForwardStack.Count > 0;
				_ = UpdateCurrentFolderContentAsync(value, version);
				// 保存上次访问路径（防抖，避免频繁写入磁盘）
				DebounceSaveLastVisitedPath(value.FullPath);
			}
			else
			{
				CurrentFolderContent.Clear();
			}
		}

		public async Task UpdateCurrentFolderContentAsync(FileSystemNodeViewModel? folder, int? version)
		{
			if (folder == null)
			{
				_uiDispatcherQueue.TryEnqueue(() => CurrentFolderContent.Clear());
				return;
			}

			CancelRename();

			// 守卫0: 表格已在展示同一个文件夹且子项已加载（且无待选中项）时，
			// 内容与 Children 保持一致，跳过 Clear+Add 重建，避免点击当前目录等场景卡顿
			if (ReferenceEquals(folder, _displayedFolderNode) && folder.IsLoaded && CurrentTab?.PendingSelectPath == null)
				return;

			// 守卫1: 开始异步加载前先检查——过期任务跳过磁盘 I/O
			if (version.HasValue && version.Value != (CurrentTab?.NavigationVersion ?? -1)) return;

			try
			{
				// 确保子项已加载（同步等待，确保 Children 已填充）
				if (!folder.IsLoaded)
				{
					await folder.LoadChildrenAsync();
				}

				// 整表替换：构建新集合一次性赋值（新集合无订阅者，构建零事件开销），
				// 由 PropertyChanged → UpdateGroupedSource → UpdateSource 做一次整表刷新，
				// 避免旧实现“先 Clear 清空再逐条 Add”造成的替换感与分组模式 O(N²) 插入。
				await _uiDispatcherQueue.EnqueueAsync(() =>
				{
					// 守卫2: UI 线程回写前再检查——过期写入丢弃
					if (version.HasValue && version.Value != (CurrentTab?.NavigationVersion ?? -1)) return;
					var newContent = new ObservableCollection<FileSystemNodeViewModel>();
					foreach (var item in folder.Children)
					{
						if (!item.IsPlaceholder)
							newContent.Add(item);
					}
					CurrentFolderContent = newContent;
					CurrentBreadcrumbPath = folder.FullPath;
					_displayedFolderNode = folder;
					OnPropertyChanged(nameof(IsCurrentFolderSpecial));
					var tab = CurrentTab;
					if (tab?.PendingSelectPath != null)
					{
						var pending = tab.PendingSelectPath;
						tab.PendingSelectPath = null;
						var target = CurrentFolderContent.FirstOrDefault(n => string.Equals(n.FullPath, pending, StringComparison.OrdinalIgnoreCase));
						if (target != null)
							SelectItemRequested?.Invoke(target);
					}
				});
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[UpdateCurrentFolderContent] Error: {ex.Message}");
			}
		}

		public void OpenFileLocation(FileSystemNodeViewModel item)
		{
			var parent = Path.GetDirectoryName(item.FullPath);
			if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) return;
			if (CurrentTab != null) CurrentTab.PendingSelectPath = item.FullPath;
			ExitSearchMode();
			NavigateToPath(parent);
		}

		public void OpenItem(FileSystemNodeViewModel item)
		{
			if (IsSearchMode)
			{
				if (item.IsDirectory) { ExitSearchMode(); NavigateToPath(item.FullPath); }
				else OpenWithDefaultProgram(item.FullPath);
				return;
			}
			if (item.IsDirectory)
			{
				// 相同引用时 [ObservableProperty] 会跳过通知，需手动强制刷新
				if (ReferenceEquals(item, SelectedFolder))
				{
					_ = UpdateCurrentFolderContentAsync(item, version: null);
					return;
				}
				if (SelectedFolder?.IsStandalone == true && CurrentTab != null)
					CurrentTab.FolderToRelease = SelectedFolder;
				SelectedFolder = item;

			}
			else if (TryOpenAsArchive(item))
			{
				// 已作为压缩包预览进入
			}
			else if (item.IsArchiveEntry)
			{
				_ = OpenArchiveEntryFileAsync(item);
			}
			else if (!item.IsDirectory)
			{
				OpenWithDefaultProgram(item.FullPath);
			}
		}

		// 若为受支持的压缩包文件，则进入其内部预览（地址栏变为 xxx.zip\）
		private bool TryOpenAsArchive(FileSystemNodeViewModel item)
		{
			if (item.IsArchiveEntry) return false;
			if (item.IsDirectory) return false;
			if (!ArchiveHelper.IsArchiveExtension(item.Extension)) return false;

			var browser = App.PluginManager?.GetArchiveBrowsers()
				.FirstOrDefault(b => b.CanBrowse(item.FullPath));
			if (browser == null) return false;

			var node = FileSystemNodeViewModel.CreateArchiveRoot(item.FullPath, AppConfigs!, _uiDispatcherQueue);
			SelectedFolder = node;
			return true;
		}

		// 打开压缩包内部的文件：先解压到临时目录再用默认程序打开
		private async Task OpenArchiveEntryFileAsync(FileSystemNodeViewModel item)
		{
			try
			{
				var browser = App.PluginManager?.GetArchiveBrowsers()
					.FirstOrDefault(b => b.CanBrowse(item.ArchiveFilePath));
				if (browser == null) return;

				var tempPath = await browser.ExtractEntryToTempAsync(item.ArchiveFilePath, item.ArchiveRelativePath);
				if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
				{
					OpenWithDefaultProgram(tempPath);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[OpenArchiveEntryFile] Failed: {ex.Message}");
			}
		}
		/// <summary>
		/// 使用 Windows 默认关联程序打开指定路径的文件
		/// </summary>
		/// <param name="filePath">要打开的文件的完整路径</param>
		/// <exception cref="ArgumentNullException">路径为空或 null</exception>
		/// <exception cref="FileNotFoundException">文件不存在</exception>
		/// <exception cref="InvalidOperationException">打开文件时发生其他错误</exception>
		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct OPENASINFO
		{
			public IntPtr pcszFile;
			public IntPtr pcszClass;
			public OPEN_AS_INFO_FLAGS oaifInFlags;
		}

		[Flags]
		private enum OPEN_AS_INFO_FLAGS
		{
			OAIF_ALLOW_REGISTRATION = 0x00000001,
			OAIF_REGISTER_EXT = 0x00000002,
			OAIF_EXEC = 0x00000004,
			OAIF_FORCE_REGISTRATION = 0x00000008,
			OAIF_HIDE_REGISTRATION = 0x00000020,
			OAIF_URL_PROTOCOL = 0x00000040,
			OAIF_DEFAULT = 0x00000080,
			OAIF_FILE_IS_URI = 0x00000100
		}

		[DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern int SHOpenWithDialog(IntPtr hwndParent, ref OPENASINFO poainfo);

		public static void OpenWithDefaultProgram(string filePath)
		{
			if (string.IsNullOrWhiteSpace(filePath))
				throw new ArgumentNullException(nameof(filePath));

			if (!File.Exists(filePath))
				throw new FileNotFoundException($"文件不存在: {filePath}");

			try
			{
				// 先尝试默认打开
				Process.Start(new ProcessStartInfo
				{
					FileName = filePath,
					UseShellExecute = true
				});
			}
			catch (Win32Exception ex) when (ex.NativeErrorCode == 1155) // 无关联程序
			{
				// 弹出“打开方式”对话框
				OPENASINFO info = new OPENASINFO
				{
					pcszFile = Marshal.StringToHGlobalUni(filePath),
					pcszClass = IntPtr.Zero,
					oaifInFlags = OPEN_AS_INFO_FLAGS.OAIF_EXEC
				};
				try
				{
					int hr = SHOpenWithDialog(IntPtr.Zero, ref info);
					if (hr < 0) // 失败
					{
						throw new InvalidOperationException($"无法显示“打开方式”对话框，错误码: {hr}");
					}
				}
				finally
				{
					Marshal.FreeHGlobal(info.pcszFile);
				}
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException($"打开文件失败: {ex.Message}", ex);
			}
		}
		public ICommand NavigateToPathCommand { get; }
		public ICommand NavigateToSubFolderCommand { get; }
		public ICommand GoBackCommand { get; }
		public ICommand GoForwardCommand { get; }
		public ICommand GoUpCommand { get; }

		private void NavigateToPath(string path)
		{
			if (IsSearchMode) ExitSearchMode();
			if (ArchiveHelper.IsArchiveVirtualPath(path, out var archiveFile, out var relative))
			{
				NavigateToArchivePath(archiveFile, relative);
				return;
			}
			var target = FindNodeByPathFast(path);
			if (target != null)
			{
				// 相同引用时 [ObservableProperty] 会跳过通知：仅做轻量重绘（已展示时会被守卫跳过），
				// 不做磁盘重载——刷新按钮走 RefreshCurrentFolderAsync
				if (ReferenceEquals(target, SelectedFolder))
					_ = UpdateCurrentFolderContentAsync(target, version: null);
				else
					SelectedFolder = target;
			}
			else
				NavigateToNewPath(path);
		}

		/// <summary>
		/// 为固定栏等独立节点寻找最优导航目标：
		/// 当前文件夹 → 直接子项 → 同目录（父目录子项，固定栏同目录切换最常见）→ 祖先链。
		/// 全部为 O(子项数)/O(深度)，不做全树递归；未命中时回退到传入的节点自身
		/// （其可能已在之前的访问中加载过）。
		/// </summary>
		public FileSystemNodeViewModel? FindBestNodeForNavigation(FileSystemNodeViewModel fallback)
		{
			if (fallback == null || !fallback.IsDirectory) return fallback;
			var path = fallback.FullPath;
			var current = SelectedFolder;
			if (current != null && string.Equals(current.FullPath, path, StringComparison.OrdinalIgnoreCase))
				return current;

			// 当前文件夹的直接子项
			if (current?.IsLoaded == true)
			{
				foreach (var child in current.Children)
				{
					if (!child.IsPlaceholder && string.Equals(child.FullPath, path, StringComparison.OrdinalIgnoreCase))
						return child;
				}
			}

			// 同目录切换：当前文件夹的父目录中的同级节点（固定栏在同目录内切换两个文件夹）
			var parent = current?.Parent;
			if (parent?.IsLoaded == true)
			{
				foreach (var child in parent.Children)
				{
					if (!child.IsPlaceholder && string.Equals(child.FullPath, path, StringComparison.OrdinalIgnoreCase))
						return child;
				}
			}

			// 祖先链（向上/后退）
			for (var ancestor = parent; ancestor != null; ancestor = ancestor.Parent)
			{
				if (string.Equals(ancestor.FullPath, path, StringComparison.OrdinalIgnoreCase))
					return ancestor;
			}

			return fallback;
		}

		/// <summary>
		/// 查找导航目标节点。先走 O(1)/O(子项数)/O(深度) 的快速路径（当前文件夹、直接子项、祖先链），
		/// 避免面包屑/后退/前进/地址栏等每次导航都对整棵已加载目录树做递归搜索造成小卡顿；
		/// 快速路径未命中才回退到全树递归查找（用于树中其它分支的节点）。
		/// </summary>
		private FileSystemNodeViewModel? FindNodeByPathFast(string fullPath)
		{
			var current = SelectedFolder;
			if (current != null && string.Equals(current.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
				return current;

			// 最常见场景：导航到当前文件夹的直接子项（面包屑下一级/后退/地址栏）
			if (current != null && current.IsLoaded)
			{
				foreach (var child in current.Children)
				{
					if (!child.IsPlaceholder && string.Equals(child.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
						return child;
				}
			}

			// 祖先链（向上按钮/后退/面包屑上级）：沿 Parent 指针逐级向上，O(深度)
			for (var ancestor = current?.Parent; ancestor != null; ancestor = ancestor.Parent)
			{
				if (string.Equals(ancestor.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
					return ancestor;
			}

			return FindNodeByPath(fullPath);
		}

		private void NavigateToArchivePath(string archiveFile, string relative)
		{
			var browser = App.PluginManager?.GetArchiveBrowsers()
				.FirstOrDefault(b => b.CanBrowse(archiveFile));
			if (browser == null)
			{
				OpenWithDefaultProgram(archiveFile);
				return;
			}
			if (SelectedFolder?.IsStandalone == true && CurrentTab != null)
				CurrentTab.FolderToRelease = SelectedFolder;
			var node = FileSystemNodeViewModel.CreateArchiveDirectory(archiveFile, relative, AppConfigs!, _uiDispatcherQueue);
			node.IsStandalone = true;
			if (CurrentTab != null) CurrentTab.PreviousPath = null;
			SelectedFolder = node;
		}

		private void NavigateToNewPath(string path)
		{
			if (!Directory.Exists(path)) return;
			if (SelectedFolder?.IsStandalone == true && CurrentTab != null)
				CurrentTab.FolderToRelease = SelectedFolder;
			// lazyLoad: true —— 目录枚举统一由 UpdateCurrentFolderContentAsync → LoadChildrenAsync 完成一次，
			// 避免构造函数里 StartAsyncCount 再全量枚举一遍（面包屑/地址栏/后退进入新路径更跟手）
			var node = new FileSystemNodeViewModel(path, true, false, _appConfigs, _uiDispatcherQueue, true);
			node.IsStandalone = true;
			if (CurrentTab != null) CurrentTab.PreviousPath = null;
			SelectedFolder = node;
		}

		private void GoBack()
		{
			var tab = CurrentTab;
			if (tab == null || tab.BackStack.Count == 0) return;
			tab.IsNavigatingFromHistory = true;
			tab.ForwardStack.Add(tab.PreviousPath ?? _selectedFolder?.FullPath ?? "");
			if (tab.ForwardStack.Count > MaxBackDepth) tab.ForwardStack.RemoveAt(0);
			var path = tab.BackStack[^1]; tab.BackStack.RemoveAt(tab.BackStack.Count - 1);
			tab.PreviousPath = null;
			NavigateToPath(path);
			CanGoBack = tab.BackStack.Count > 0;
			CanGoForward = tab.ForwardStack.Count > 0;
			tab.IsNavigatingFromHistory = false;
		}

		private void GoForward()
		{
			var tab = CurrentTab;
			if (tab == null || tab.ForwardStack.Count == 0) return;
			tab.IsNavigatingFromHistory = true;
			tab.BackStack.Add(tab.PreviousPath ?? _selectedFolder?.FullPath ?? "");
			if (tab.BackStack.Count > MaxBackDepth) tab.BackStack.RemoveAt(0);
			var path = tab.ForwardStack[^1]; tab.ForwardStack.RemoveAt(tab.ForwardStack.Count - 1);
			tab.PreviousPath = null;
			NavigateToPath(path);
			CanGoBack = tab.BackStack.Count > 0;
			CanGoForward = tab.ForwardStack.Count > 0;
			tab.IsNavigatingFromHistory = false;
		}

		private void GoUp()
		{
			if (_selectedFolder == null) return;
			var parentPath = GetParentPath(_selectedFolder.FullPath);
			if (parentPath == null) return;
			NavigateToPath(parentPath);
		}

		private static string? GetParentPath(string path)
		{
			if (string.IsNullOrEmpty(path)) return null;
			if (ArchiveHelper.IsArchiveVirtualPath(path, out var archiveFile, out var relative))
			{
				if (string.IsNullOrEmpty(relative))
					return ArchiveHelper.GetParentOfArchiveRoot(archiveFile);
				var parts = relative.TrimEnd('\\').Split('\\');
				if (parts.Length <= 1)
					return archiveFile;
				return ArchiveHelper.CombineArchiveVirtualPath(archiveFile, string.Join("\\", parts.Take(parts.Length - 1)));
			}
			if (path.EndsWith(":\\") || path == "\\\\")
				return null;
			if (path.StartsWith("\\\\"))
			{
				var parts = path.TrimEnd('\\').Split('\\');
				if (parts.Length <= 2) return null;
				return string.Join("\\", parts.Take(parts.Length - 1));
			}
			var parent = Directory.GetParent(path);
			return parent?.FullName;
		}
		public FileSystemNodeViewModel? FindNodeByPath(string fullPath)
		{
			foreach (var root in RootDirectories)
			{
				var result = FindNodeRecursive(root, fullPath);
				if (result != null)
					return result;
			}
			return null;
		}

		public static FileSystemNodeViewModel? FindNodeRecursive(FileSystemNodeViewModel node, string fullPath)
		{
			if (string.Equals(node.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
				return node;

			if (node.IsDirectory && node.IsLoaded)
			{
				foreach (var child in node.Children)
				{
					var result = FindNodeRecursive(child, fullPath);
					if (result != null)
						return result;
				}
			}
			return null;
		}

		private void InitializePinnedShortcuts(Configs configs, Microsoft.UI.Dispatching.DispatcherQueue uiDispatcherQueue)
		{
			var pinnedPaths = GetQuickAccessPinnedFolders();
			foreach (var path in pinnedPaths)
			{
				if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
				{
					var node = new FileSystemNodeViewModel(path, true, false, configs, uiDispatcherQueue, true);
					PinnedShortcuts.Add(node);
				}
			}
		}

		private static List<string> GetQuickAccessPinnedFolders()
		{
			var result = new List<string>();
			try
			{
				Type shellType = Type.GetTypeFromProgID("Shell.Application", true);
				dynamic shell = Activator.CreateInstance(shellType);
				dynamic quickAccess = shell.NameSpace("shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}");
				if (quickAccess != null)
				{
					foreach (dynamic item in quickAccess.Items())
					{
						try
						{
							string? path = item.Path;
							if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
								result.Add(path);
						}
						catch { }
					}
				}
			}
			catch { }
            return result;
		}
	}
}
