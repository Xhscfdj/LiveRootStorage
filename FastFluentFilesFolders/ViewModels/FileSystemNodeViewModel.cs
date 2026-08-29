using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI;
using FastFluentFilesFolders.Services;
using FastFluentFilesFolders.Extensions;
using FastFluentFilesFolders.Extensions.Interfaces;
using FastFluentFilesFolders.Helpers;
using FastFluentFilesFolders.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FastFluentFilesFolders.ViewModels
{
	public partial class FileSystemNodeViewModel : ViewModelBase
	{
		// 构造函数（统一入口）
		public FileSystemNodeViewModel(
			string fullPath,
			bool isDirectory,
			bool isPlaceholder,
			Configs configs,
			DispatcherQueue uiDispatcherQueue,
			bool lazyLoad)
			: base()
		{
			IsPlaceholder = isPlaceholder;
			_isLazyLoad = lazyLoad;
			_uiDispatcherQueue = uiDispatcherQueue;
			if (configs != null)
			{
				_configs = configs;
			}
			FullPath = fullPath;
			IsDirectory = isDirectory;
			// 设置名称和扩展名
			if (isDirectory && !IsPlaceholder)
			{
				// 对于驱动器根目录，名称为 "C:\" 形式
				Children.Add(new PlaceholderNodeViewModel());
				Name = (fullPath.Length == 3 && fullPath.EndsWith(":\\")) ? fullPath : Path.GetFileName(fullPath.TrimEnd('\\'));
				Extension = string.Empty;
				IsSpecialFolder = ShellIconHelper.IsSpecialFolder(fullPath);
				if (_configs != null && _configs.IsTimeGroupedFolder(fullPath))
				{
					WillSplitToDifferentSorts = true;
				}
			}
			else
			{
				Name = Path.GetFileName(fullPath);
				Extension = Path.GetExtension(fullPath);
			}

			if (!_isLazyLoad)
			{
				_ = LoadBasicInfoAsync();
				if (configs != null)
				{
					_ = LoadIconAsync(fullPath, isDirectory);
				}

				if (isDirectory)
				{
					_ = StartAsyncCount();
				}
			}

			if (!isDirectory)
			{
				_isLoaded = true;
			}
		}
		private readonly Configs _configs;
		private readonly DispatcherQueue _uiDispatcherQueue;
		private bool _isLoaded;
		private bool _isCounting;
		private bool _isInited;
		private bool _isLazyLoad;
		private bool _hasBasicInfo;
		public bool IsStandalone { get; set; }
		// 父节点引用（目录树节点加载子项时回填），用于 O(深度) 的祖先快速查找
		public FileSystemNodeViewModel? Parent { get; set; }

		// 限制“缓存未命中”时的图标解码并发，防止阻塞式 Shell 调用耗尽线程池而卡顿。
		// 并发上限在启动时由 Configs.IconParallelLoadingCount 注入（默认 30）；
		// 尚未注入前先用处理器数的一半作为保守默认值。
		private static System.Threading.SemaphoreSlim _iconLoadGate =
			new(Math.Max(2, Environment.ProcessorCount / 2), Math.Max(2, Environment.ProcessorCount / 2));

		/// <summary>
		/// 在启动早期注入图标解码并发上限。之后新发起的 LoadIconAsync 调用会使用新上限。
		/// concurrency &lt;= 0 表示“自动”：使用安全上限 16——SHGetFileInfo/GDI+ 解码
		/// 并发过高会偶发失败导致图标缺失（尤其固定栏这类只实体化一次的行）；
		/// &gt;0 时按配置限流（上限 64，防止配置误填过大）。
		/// </summary>
		public static void ConfigureIconLoadConcurrency(int concurrency)
		{
			if (concurrency <= 0)
				_iconLoadGate = new System.Threading.SemaphoreSlim(16, 16);
			else
			{
				var cap = Math.Min(concurrency, 64);
				_iconLoadGate = new System.Threading.SemaphoreSlim(cap, cap);
			}
		}

		// 图标加载失败自动重试（有界）：并发高峰下 SHGetFileInfo/GDI+ 会偶发失败，
		// 对固定栏这类“只实体化一次、不会因滚动重读 Icon”的行，必须主动重试
		// 才能让图标出现（成功后经属性通知刷新已实体化的行）。
		private const int MaxIconRetries = 3;
		private const int IconRetryBaseDelayMs = 500;
		private int _iconRetryCount;

		// 图标加载请求批量合并：行实体化瞬间会有几十个 Icon getter 触发，
		// 全部收进同一批、一个调度周期统一发起加载，避免与赋值排空交错造成逐批弹出。
		private static readonly object _iconLoadRequestLock = new();
		private static readonly List<FileSystemNodeViewModel> _iconLoadRequests = new();
		private static bool _iconLoadRequestScheduled;

		private static void RequestIconLoad(FileSystemNodeViewModel node)
		{
			var queue = node._uiDispatcherQueue;
			if (queue == null) return;

			lock (_iconLoadRequestLock)
			{
				_iconLoadRequests.Add(node);
				if (_iconLoadRequestScheduled) return;
				_iconLoadRequestScheduled = true;
			}
			queue.TryEnqueue(ProcessIconLoadRequests);
		}

		private static void ProcessIconLoadRequests()
		{
			FileSystemNodeViewModel[] batch;
			lock (_iconLoadRequestLock)
			{
				if (_iconLoadRequests.Count == 0)
				{
					_iconLoadRequestScheduled = false;
					return;
				}
				batch = _iconLoadRequests.ToArray();
				_iconLoadRequests.Clear();
				_iconLoadRequestScheduled = false;
			}

			foreach (var node in batch)
				_ = node.LoadIconAsync(node.FullPath, node.IsDirectory);
		}

		// 批量图标赋值：图标在后台完成的时间不同，若每个完成都立即在 UI 线程单独赋值，
		// 图标会跨多个帧逐行出现（“从顶部一行行替换”）。改为先收集到队列，
		// 再在每个 UI 调度周期统一应用一次，视觉上快速成批铺满。
		private static readonly object _iconAssignLock = new();
		private static readonly List<(FileSystemNodeViewModel Node, ImageSource Icon)> _iconAssignPending = new();
		private static bool _iconAssignScheduled;

		private static void QueueIconAssign(FileSystemNodeViewModel node, ImageSource icon)
		{
			var queue = node._uiDispatcherQueue;
			if (queue == null) return;

			lock (_iconAssignLock)
			{
				_iconAssignPending.Add((node, icon));
				if (_iconAssignScheduled) return;
				_iconAssignScheduled = true;
			}
			queue.TryEnqueue(FlushIconAssigns);
		}

		private static void FlushIconAssigns()
		{
			(FileSystemNodeViewModel Node, ImageSource Icon)[] batch;
			lock (_iconAssignLock)
			{
				if (_iconAssignPending.Count == 0)
				{
					_iconAssignScheduled = false;
					return;
				}
				batch = _iconAssignPending.ToArray();
				_iconAssignPending.Clear();
				_iconAssignScheduled = false;
			}

			foreach (var (node, icon) in batch)
			{
				try
				{
					node.Icon = icon;
				}
				catch (Exception ex)
				{
					// 单个节点赋值异常不应拖垮整批图标
					Debug.WriteLine($"[IconAssign] Failed for {node.FullPath}: {ex.Message}");
				}
			}
		}

		// 基础属性
		[ObservableProperty] private TagViewModel _tag = new();
		[ObservableProperty] private bool _isPlaceholder = false;
		[ObservableProperty] private bool _isSpecialFolder = false;
		[ObservableProperty] private bool _willSplitToDifferentSorts = false;
		[ObservableProperty] private string _name = string.Empty;
		[ObservableProperty] private string _fullPath = string.Empty;
		[ObservableProperty] private bool _isDirectory = true;
		[ObservableProperty] private string _extension = string.Empty;
		// 图标按需加载：仅当虚拟化列表/树将该行实体化并读取 Icon 时才触发加载，
		// 避免一次性为整个文件夹的所有项加载图标导致卡顿
		// 诊断开关（临时）：false = 不加载图标，用于确认“滚动替换”是否由图标异步填充引起。
#if !RELEASE
		internal static bool IconLoadingEnabled = true;
#endif
		private ImageSource? _icon;
		private bool _iconRequested;
		public ImageSource? Icon
		{
			get
			{
				if (!_iconRequested && !IsPlaceholder && App.SharedIconProvider != null && _uiDispatcherQueue != null)
				{
					_iconRequested = true;
					// 诊断开关：临时禁用异步图标加载，确认“滚动/进入时的替换感”是否来自图标逐行填充。
					// 测试完请改回 true。
#if !RELEASE
					if (IconLoadingEnabled)
						RequestIconLoad(this);
#endif
#if RELEASE
					RequestIconLoad(this);
#endif
				}
				return _icon;
			}
			set => SetProperty(ref _icon, value);
		}
		[ObservableProperty] private long _exactSize = 0;
		[ObservableProperty] private string _visualSize = "0B";
		[ObservableProperty] private DateTime _lastModifiedTime = DateTime.MinValue;
		[ObservableProperty] private DateTime _firstCreatedTime = DateTime.MinValue;
		[ObservableProperty] private string _lastModifiedTimeString = string.Empty;
		[ObservableProperty] private string _firstCreatedTimeString = string.Empty;
		[ObservableProperty] private bool _isSelected = false;
		[ObservableProperty] private bool _isRenaming = false;
		[ObservableProperty] private bool _isCutPending = false;
		[ObservableProperty] private bool _isSizeCalculated = false;
		[ObservableProperty] private bool _isHidden = false;
		[ObservableProperty] private bool _isSystem = false;

		// 使用进程（懒加载，类似 Icon）
		private string _processesUsingThisFile = string.Empty;
		private bool _processInfoRequested;
		public string ProcessesUsingThisFile
		{
			get
			{
				if (!_processInfoRequested && !IsPlaceholder && !IsDirectory && _uiDispatcherQueue != null)
				{
					_processInfoRequested = true;
					_uiDispatcherQueue.TryEnqueue(() => _ = LoadProcessInfoAsync());
				}
				return _processesUsingThisFile;
			}
			set => SetProperty(ref _processesUsingThisFile, value);
		}

		// 压缩包预览相关：当节点位于压缩包内部时为 true
		[ObservableProperty] private bool _isArchiveEntry = false;
		// 物理压缩包文件的完整路径（如 C:\a\test.zip）
		public string ArchiveFilePath { get; private set; } = string.Empty;
		// 在压缩包内部的相对路径（"" 表示压缩包根目录）
		public string ArchiveRelativePath { get; private set; } = string.Empty;
		private string _sortByTime = string.Empty;
		public string SortByTime
		{
			get => _sortByTime;
			set
			{
				_sortByTime = value;
				OnPropertyChanged();
			}
		}
		public bool IsGroupExpanded { get; set; } = true;

		public Microsoft.UI.Xaml.Visibility IsRenamingVisibility =>
			IsRenaming ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
		public Microsoft.UI.Xaml.Visibility IsNotRenamingVisibility =>
			IsRenaming ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

		// 多语言（供 DataTemplate 内按钮等直接绑定使用）
		public MultiLanguageStringsViewModel? ML => App.ML;

		// 文件夹显示“计算大小”按钮；文件或已计算完成的文件夹显示大小文本
		public Microsoft.UI.Xaml.Visibility CalculateSizeButtonVisibility =>
			(!IsPlaceholder && IsDirectory && !IsSizeCalculated)
				? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
		public Microsoft.UI.Xaml.Visibility SizeTextVisibility =>
			(!IsPlaceholder && (!IsDirectory || IsSizeCalculated))
				? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

		partial void OnIsSizeCalculatedChanged(bool value)
		{
			OnPropertyChanged(nameof(CalculateSizeButtonVisibility));
			OnPropertyChanged(nameof(SizeTextVisibility));
		}

		public double CutOpacity => IsCutPending ? 0.4 : 1.0;

		// 隐藏/系统文件以半透明显示（类似 Windows 资源管理器的淡化效果）
		public double RowOpacity => (IsHidden || IsSystem) ? HiddenOrSystemOpacity : CutOpacity;
		public const double HiddenOrSystemOpacity = 0.5;

		partial void OnIsRenamingChanged(bool value)
		{
			OnPropertyChanged(nameof(IsRenamingVisibility));
			OnPropertyChanged(nameof(IsNotRenamingVisibility));
		}

		partial void OnIsCutPendingChanged(bool value)
		{
			OnPropertyChanged(nameof(CutOpacity));
			OnPropertyChanged(nameof(RowOpacity));
		}

		partial void OnIsHiddenChanged(bool value) => OnPropertyChanged(nameof(RowOpacity));
		partial void OnIsSystemChanged(bool value) => OnPropertyChanged(nameof(RowOpacity));

		// 树形结构相关（文件夹特有，文件则为空）
		private ObservableCollection<FileSystemNodeViewModel>? _children;
		public ObservableCollection<FileSystemNodeViewModel> Children
		{
			get => _children ??= [];
			set => SetProperty(ref _children, value);
		}
		[ObservableProperty] private string _childrenCountText = string.Empty;
		private int? _cachedChildrenCount;

		public bool IsLoaded => _isLoaded;

		/// <summary>
		/// 释放子项缓存（内存优化）。同时把加载状态重置为未加载，
		/// 保证之后再次导航回该文件夹时会重新枚举磁盘，
		/// 否则会出现“返回后文件夹为空、刷新才恢复”的问题。
		/// </summary>
		public void ReleaseChildren()
		{
			Children.Clear();
			_isLoaded = false;
			_cachedChildrenCount = null;
			ChildrenCountText = string.Empty;
		}

		// 创建压缩包根节点
		public static FileSystemNodeViewModel CreateArchiveRoot(
			string archiveFilePath,
			Configs configs,
			DispatcherQueue uiDispatcherQueue)
		{
			var node = new FileSystemNodeViewModel(
				ArchiveHelper.GetArchiveRootVirtualPath(archiveFilePath),
				true, false, configs, uiDispatcherQueue, true);
			node.IsArchiveEntry = true;
			node.ArchiveFilePath = archiveFilePath;
			node.ArchiveRelativePath = string.Empty;
			node.Name = Path.GetFileName(archiveFilePath);
			node.Extension = Path.GetExtension(archiveFilePath);
			node.Children.Clear();
			node.Children.Add(new PlaceholderNodeViewModel());
			_ = node.LoadIconAsync(archiveFilePath, false);
			return node;
		}

		// 创建压缩包内部指定相对路径的目录节点（用于地址栏/历史导航）
		public static FileSystemNodeViewModel CreateArchiveDirectory(
			string archiveFilePath,
			string relativePath,
			Configs configs,
			DispatcherQueue uiDispatcherQueue)
		{
			if (string.IsNullOrEmpty(relativePath))
				return CreateArchiveRoot(archiveFilePath, configs, uiDispatcherQueue);

			var virtualPath = ArchiveHelper.CombineArchiveVirtualPath(archiveFilePath, relativePath);
			var node = new FileSystemNodeViewModel(virtualPath, true, false, configs, uiDispatcherQueue, true);
			node.IsArchiveEntry = true;
			node.ArchiveFilePath = archiveFilePath;
			node.ArchiveRelativePath = relativePath.TrimEnd('\\');
			node.Name = node.ArchiveRelativePath.Split('\\').Last();
			node.Extension = string.Empty;
			node.Children.Clear();
			node.Children.Add(new PlaceholderNodeViewModel());
			return node;
		}

		// 根据压缩包内部条目创建节点
		private FileSystemNodeViewModel CreateArchiveChild(
			Extensions.Interfaces.ArchiveEntry entry)
		{
			var virtualPath = ArchiveHelper.CombineArchiveVirtualPath(ArchiveFilePath, entry.RelativePath);
			var node = new FileSystemNodeViewModel(
				entry.IsDirectory ? virtualPath : virtualPath.TrimEnd('\\'),
				entry.IsDirectory, false, _configs, _uiDispatcherQueue, true);
			node.Parent = this;
			node.IsArchiveEntry = true;
			node.ArchiveFilePath = ArchiveFilePath;
			node.ArchiveRelativePath = entry.RelativePath;
			node.Name = entry.Name;
			node.Extension = entry.IsDirectory ? string.Empty : Path.GetExtension(entry.Name);
			node.ApplyMetadata(entry.IsDirectory, entry.Size, entry.LastModified, entry.LastModified);
			if (entry.IsDirectory)
			{
				node.Children.Clear();
				node.Children.Add(new PlaceholderNodeViewModel());
			}
			return node;
		}

		// 同步加载基本文件/文件夹信息（确保排序时属性已就绪）
		private async Task LoadBasicInfoAsync()
		{
			if (IsPlaceholder || _hasBasicInfo) { return; }
			_hasBasicInfo = true;
			try
			{
				if (IsDirectory)
				{
					if (Directory.Exists(FullPath))
					{
						var dirInfo = await Task.Run(() => new DirectoryInfo(FullPath));
						LastModifiedTime = dirInfo.LastWriteTimeUtc;
						FirstCreatedTime = dirInfo.CreationTimeUtc;
						ApplyFileAttributes(dirInfo.Attributes);
					}
					ExactSize = 0;
				}
				else if (File.Exists(FullPath))
				{
					var fileInfo = await Task.Run(() => new FileInfo(FullPath));
					LastModifiedTime = fileInfo.LastWriteTimeUtc;
					FirstCreatedTime = fileInfo.CreationTimeUtc;
					ExactSize = fileInfo.Length;
					ApplyFileAttributes(fileInfo.Attributes);
				}

				await _uiDispatcherQueue.EnqueueAsync(() =>
				{
					// 更新字符串显示
					LastModifiedTimeString = LastModifiedTime.ToString("yyyy-MM-dd HH:mm:ss");
					FirstCreatedTimeString = FirstCreatedTime.ToString("yyyy-MM-dd HH:mm:ss");
					VisualSize = FormatFileSize(ExactSize);
				});

				Debug.WriteLine($"[BasicInfo] {FullPath} loaded: Size={ExactSize}, Modified={LastModifiedTimeString}");
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[LoadBasicInfo] Error loading info for {FullPath}: {ex.Message}");
				// 保持默认值，不影响排序
			}
		}
		public async Task InitAsync(string fullPath, bool isDirectory)
		{
			if (_isInited) return;
			_isInited = true;

			await Task.Run(async () =>
			{
				await LoadBasicInfoAsync();
				_ = LoadIconAsync(fullPath, isDirectory);
			});
			
			if (IsDirectory)
			{
				await StartAsyncCount();
			}
		}	

		// 直接应用枚举时一次性获取到的元数据，避免每个子项再单独发起一次文件系统访问
		public void ApplyMetadata(bool isDirectory, long size, DateTime lastWriteUtc, DateTime creationUtc,
			bool isHidden = false, bool isSystem = false)
		{
			if (IsPlaceholder) return;
			_hasBasicInfo = true;
			LastModifiedTime = lastWriteUtc;
			FirstCreatedTime = creationUtc;
			ExactSize = isDirectory ? 0 : size;
			LastModifiedTimeString = LastModifiedTime.ToString("yyyy-MM-dd HH:mm:ss");
			FirstCreatedTimeString = FirstCreatedTime.ToString("yyyy-MM-dd HH:mm:ss");
			VisualSize = FormatFileSize(ExactSize);
			IsHidden = isHidden;
			IsSystem = isSystem;
		}

		// 根据文件属性设置隐藏/系统标记（用于半透明显示）
		public void ApplyFileAttributes(FileAttributes attributes)
		{
			IsHidden = (attributes & FileAttributes.Hidden) != 0;
			IsSystem = (attributes & FileAttributes.System) != 0;
		}

		// 同步读取磁盘元数据（用于粘贴/新建等刚创建的项，确保加入分组视图前 LastModifiedTime 已就绪）
		public void LoadMetadataSync()
		{
			if (IsPlaceholder) return;
			try
			{
				if (IsDirectory)
				{
					var dirInfo = new DirectoryInfo(FullPath);
					ApplyMetadata(true, 0, dirInfo.LastWriteTimeUtc, dirInfo.CreationTimeUtc,
						(dirInfo.Attributes & FileAttributes.Hidden) != 0,
						(dirInfo.Attributes & FileAttributes.System) != 0);
				}
				else if (File.Exists(FullPath))
				{
					var fileInfo = new FileInfo(FullPath);
					ApplyMetadata(false, fileInfo.Length, fileInfo.LastWriteTimeUtc, fileInfo.CreationTimeUtc,
						(fileInfo.Attributes & FileAttributes.Hidden) != 0,
						(fileInfo.Attributes & FileAttributes.System) != 0);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[LoadMetadataSync] Error for {FullPath}: {ex.Message}");
			}
		}

		public async Task RefreshAsync()
		{
			_hasBasicInfo = false;
			if (IsPlaceholder) return;
			await LoadBasicInfoAsync();
			_ = LoadIconAsync(FullPath, IsDirectory);
		}

		/// <summary>
		/// 有界自动重试失败的图标加载：退避递增（500ms/1s/1.5s）。
		/// 若期间图标已由其它请求设置（如共享缓存键的其它文件夹成功），则跳过。
		/// </summary>
		private async Task ScheduleIconRetryAsync(string fullPath, bool isDirectory)
		{
			if (_iconRetryCount >= MaxIconRetries) return;
			int attempt = ++_iconRetryCount;
			try
			{
				await Task.Delay(IconRetryBaseDelayMs * attempt);
			}
			catch
			{
				return;
			}

			if (_icon != null || _iconRequested) return; // 已有图标或其它加载进行中，无需重试
			await LoadIconAsync(fullPath, isDirectory);
		}

		/// <summary>
		/// 懒加载：异步查询正在使用此文件的进程名称。
		/// 仅对非目录、非占位符文件生效。
		/// </summary>
		private async Task LoadProcessInfoAsync()
		{
			if (IsPlaceholder || IsDirectory) return;
			try
			{
				var myPath = FullPath;
				var result = await Task.Run(() => Services.ProcessHelper.GetProcessesUsingFile(myPath));
				await _uiDispatcherQueue.EnqueueAsync(() =>
				{
					ProcessesUsingThisFile = result;
				});
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[LoadProcessInfo] Error for {FullPath}: {ex.Message}");
			}
		}

		public async Task LoadIconAsync(string fullPath, bool isDirectory)
		{
			try
			{
				var provider = App.SharedIconProvider;
				if (provider == null) return;
				_iconRequested = true;

				// 快速路径：命中缓存直接赋值，省去线程切换与重复解码；
				// 非 UI 线程时也走批量合并，避免导航瞬间大量缓存命中逐行入队
				if (provider.TryGetCachedIcon(fullPath, isDirectory, out var cached) && cached != null)
				{
					_iconRetryCount = 0;
					if (_uiDispatcherQueue.HasThreadAccess)
						Icon = cached;
					else
						QueueIconAssign(this, cached);
					return;
				}

				ImageSource? icon = null;
				// 慢速路径（缓存未命中）：SHGetFileInfo/快捷方式解析等是阻塞式 Shell 调用，
				// 大量并发会耗尽线程池、拖慢导航与其它后台任务，导致界面卡顿。
				// 用共享信号量限制真正的解码并发数。
				await _iconLoadGate.WaitAsync();
				try
				{
					await Task.Run( async () =>
					{
						var task = provider.GetIconAsync(fullPath, isDirectory, _uiDispatcherQueue, 24);
						if (task != null) icon = await task;
					});
				}
				finally
				{
					_iconLoadGate.Release();
				}
				if (icon != null)
				{
					// 批量赋值：同一调度周期内完成的图标一次性应用到界面，
					// 避免图标解码完成时间不同导致逐行渐进渲染
					_iconRetryCount = 0;
					QueueIconAssign(this, icon);
				}
				else
				{
					// 解码失败（如 SHGetFileInfo 偶发失败）：重置请求标记，
					// 并主动调度有界重试，让已实体化、不会重读 Icon 的行也能补上图标
					_iconRequested = false;
					_ = ScheduleIconRetryAsync(fullPath, isDirectory);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[LoadIconAsync] Failed for {fullPath}: {ex.Message}");
				// 异常同样重置请求标记并调度重试
				_iconRequested = false;
				_ = ScheduleIconRetryAsync(fullPath, isDirectory);
			}
		}

		// 静态工具方法
		public static string FormatFileSize(long bytes)
		{
			string[] sizes = { "B", "KB", "MB", "GB", "TB" };
			double len = bytes;
			int order = 0;
			while (len >= 1024 && order < sizes.Length - 1)
			{
				order++;
				len /= 1024;
			}
			return $"{len:0.##} {sizes[order]}";
		}

		// 递归计算文件夹总大小（以 Byte 为单位）
		public long ViewDirSize(string fullPath)
		{
			long total = 0;
			try
			{
				foreach (var file in SafeGetFiles(fullPath))
				{
					try { total += new FileInfo(file).Length; }
					catch (Exception ex) { Debug.WriteLine($"[ViewDirSize] file error {file}: {ex.Message}"); }
				}
				foreach (var subDir in SafeGetDirs(fullPath))
				{
					total += ViewDirSize(subDir);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[ViewDirSize] {fullPath}: {ex.Message}");
			}
			return total;
		}

		// 点击“计算大小”按钮：后台计算文件夹大小并格式化显示到表格
		[RelayCommand]
		private async Task CalculateSizeAsync()
		{
			if (IsPlaceholder || !IsDirectory) return;
			var myPath = FullPath;
			long size = await Task.Run(() => ViewDirSize(myPath));
			await _uiDispatcherQueue.EnqueueAsync(() =>
			{
				ExactSize = size;
				VisualSize = FormatFileSize(size);
				IsSizeCalculated = true;
			});
		}

		// 安全枚举方法（已有）
		public static List<string> SafeGetFiles(string Path)
		{
			var accessible = new List<string>();
			try
			{
				var allFiles = Directory.GetFiles(Path);
				foreach (string file in allFiles)
				{
					if (IsFileAccessible(file))
						accessible.Add(file);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[SafeGetFiles] {Path}: {ex.Message}");
			}
			return accessible;
		}

		public static List<string> SafeGetDirs(string Path)
		{
			var accessible = new List<string>();
			try
			{
				var allSubDirs = Directory.GetDirectories(Path);
				foreach (string subDir in allSubDirs)
				{
					if (IsDirectoryAccessible(subDir))
						accessible.Add(subDir);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[SafeGetDirs] {Path}: {ex.Message}");
			}
			return accessible;
		}

		public static bool IsDirectoryAccessible(string Path)
		{
			try
			{
				return Directory.Exists(Path);
			}
			catch
			{
				return false;
			}
		}

		// 一次目录枚举即拿到名称/属性/时间/大小，避免对每个子项再单独调用 FileInfo/DirectoryInfo
		public readonly record struct FileSystemEntryInfo(
			string FullPath,
			bool IsDirectory,
			long Size,
			DateTime LastWriteTimeUtc,
			DateTime CreationTimeUtc,
			bool IsHidden,
			bool IsSystem);

		public static List<FileSystemEntryInfo> SafeEnumerateEntries(string path)
		{
			var dirs = new List<FileSystemEntryInfo>();
			var files = new List<FileSystemEntryInfo>();
			try
			{
				var dirInfo = new DirectoryInfo(path);
				foreach (var entry in dirInfo.EnumerateFileSystemInfos())
				{
					try
					{
						bool isDir = (entry.Attributes & FileAttributes.Directory) != 0;
						long size = isDir ? 0 : ((FileInfo)entry).Length;
						var info = new FileSystemEntryInfo(
							entry.FullName,
							isDir,
							size,
							entry.LastWriteTimeUtc,
							entry.CreationTimeUtc,
							(entry.Attributes & FileAttributes.Hidden) != 0,
							(entry.Attributes & FileAttributes.System) != 0);
						if (isDir)
							dirs.Add(info);
						else
							files.Add(info);
					}
					catch (Exception ex)
					{
						Debug.WriteLine($"[SafeEnumerateEntries] entry error {entry.FullName}: {ex.Message}");
					}
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[SafeEnumerateEntries] {path}: {ex.Message}");
			}

			// 自己实现排序：目录/文件分别按配置的默认排序方式排好，目录在前、文件在后。
			// 让 Children 从一开始就是稳定有序的，从源头避免“加载后再整理顺序”造成的替换感。
			var mode = GetDefaultOrderMode();
			SortEntries(dirs, mode, isDirectory: true);
			SortEntries(files, mode, isDirectory: false);

			var result = new List<FileSystemEntryInfo>(dirs.Count + files.Count);
			result.AddRange(dirs);
			result.AddRange(files);
			return result;
		}

		private static SortMode GetDefaultOrderMode()
		{
			var str = App.SharedViewModel?.AppConfigs?.DefaultOrderMode;
			return Enum.TryParse<SortMode>(str, out var mode) ? mode : SortMode.ModifiedDesc;
		}

		private static void SortEntries(List<FileSystemEntryInfo> entries, SortMode mode, bool isDirectory)
		{
			if (entries.Count <= 1) return;
			switch (mode)
			{
				case SortMode.NameAsc:
					entries.Sort((a, b) => string.Compare(Path.GetFileName(a.FullPath), Path.GetFileName(b.FullPath), StringComparison.CurrentCultureIgnoreCase));
					break;
				case SortMode.NameDesc:
					entries.Sort((a, b) => string.Compare(Path.GetFileName(b.FullPath), Path.GetFileName(a.FullPath), StringComparison.CurrentCultureIgnoreCase));
					break;
				case SortMode.ModifiedAsc:
					entries.Sort((a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
					break;
				case SortMode.ModifiedDesc:
					entries.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
					break;
				case SortMode.CreatedAsc:
					entries.Sort((a, b) => a.CreationTimeUtc.CompareTo(b.CreationTimeUtc));
					break;
				case SortMode.CreatedDesc:
					entries.Sort((a, b) => b.CreationTimeUtc.CompareTo(a.CreationTimeUtc));
					break;
				case SortMode.SizeAsc:
					if (isDirectory)
						entries.Sort((a, b) => string.Compare(Path.GetFileName(a.FullPath), Path.GetFileName(b.FullPath), StringComparison.CurrentCultureIgnoreCase));
					else
						entries.Sort((a, b) => a.Size.CompareTo(b.Size));
					break;
				case SortMode.SizeDesc:
					if (isDirectory)
						entries.Sort((a, b) => string.Compare(Path.GetFileName(a.FullPath), Path.GetFileName(b.FullPath), StringComparison.CurrentCultureIgnoreCase));
					else
						entries.Sort((a, b) => b.Size.CompareTo(a.Size));
					break;
				default:
					// SortMode.None：保持磁盘枚举顺序，不排序
					break;
			}
		}

		public static bool IsFileAccessible(string Path)
		{
			try
			{
				return File.Exists(Path);
			}
			catch
			{
				return false;
			}
		}

		// 异步加载子项（仅文件夹有效）
		public async Task LoadChildrenAsync()
		{
			if (_isLoaded || !IsDirectory) return;
			_isLoaded = true;
			await ReloadChildrenAsync();
		}

		public async Task ReloadChildrenAsync()
		{
			if (!IsDirectory) return;

			if (IsArchiveEntry)
			{
				await ReloadArchiveChildrenAsync();
				return;
			}

			var myPath = FullPath;
			var timingId = LoadTiming.Begin($"{myPath} (ReloadChildren)");
			var sw = System.Diagnostics.Stopwatch.StartNew();

			var entries = await Task.Run(() => SafeEnumerateEntries(myPath));
			LoadTiming.Mark(timingId, "enumerate+sort", sw.ElapsedMilliseconds);

			var (dirNodes, fileNodes) = await BuildChildNodesAsync(entries);
			LoadTiming.Mark(timingId, "build-nodes(background)", sw.ElapsedMilliseconds);

			var allNodes = new List<FileSystemNodeViewModel>(dirNodes.Count + fileNodes.Count);
			allNodes.AddRange(dirNodes);
			allNodes.AddRange(fileNodes);

			await _uiDispatcherQueue.EnqueueAsync(() =>
			{
				Children.Clear();
				foreach (var item in allNodes)
					AddChildWithSort(item);

				var actualCount = Children.Count(c => !c.IsPlaceholder);
				ChildrenCountText = actualCount > 0 ? $"[{actualCount}]" : "[?]";
			});
			LoadTiming.Mark(timingId, "fill-children(ui)", sw.ElapsedMilliseconds);
			LoadTiming.End(timingId, sw.ElapsedMilliseconds);
		}

		/// <summary>
		/// 后台构建子节点（目录在前、文件在后，保持枚举原始顺序；不做排序）。
		/// 节点构造与元数据赋值均在后台线程执行，避免大文件夹在 UI 线程批量构造卡顿。
		/// </summary>
		private async Task<(List<FileSystemNodeViewModel> DirNodes, List<FileSystemNodeViewModel> FileNodes)> BuildChildNodesAsync(List<FileSystemEntryInfo> entries)
		{
			return await Task.Run(() =>
			{
				var dirNodes = new List<FileSystemNodeViewModel>();
				var fileNodes = new List<FileSystemNodeViewModel>();
				foreach (var entry in entries)
				{
					var node = new FileSystemNodeViewModel(entry.FullPath, entry.IsDirectory, false, _configs, _uiDispatcherQueue, true);
					node.Parent = this;
					node.ApplyMetadata(entry.IsDirectory, entry.Size, entry.LastWriteTimeUtc, entry.CreationTimeUtc, entry.IsHidden, entry.IsSystem);
					if (entry.IsDirectory)
						dirNodes.Add(node);
					else
						fileNodes.Add(node);
				}
				return (dirNodes, fileNodes);
			});
		}

		private void AddChildWithSort(FileSystemNodeViewModel item)
		{
			if (WillSplitToDifferentSorts)
				item.SortByTime = Helpers.GroupedFileList.GetTimeGroup(item.LastModifiedTime);
			Children.Add(item);
		}

		private async Task ReloadArchiveChildrenAsync()
		{
			var browser = App.PluginManager?.GetArchiveBrowsers()
				.FirstOrDefault(b => b.CanBrowse(ArchiveFilePath));

			var allNodes = new List<FileSystemNodeViewModel>();
			if (browser != null)
			{
				try
				{
					var entries = await browser.ListEntriesAsync(ArchiveFilePath, ArchiveRelativePath);
					var dirNodes = new List<FileSystemNodeViewModel>();
					var fileNodes = new List<FileSystemNodeViewModel>();
					foreach (var entry in entries)
					{
						var node = CreateArchiveChild(entry);
						if (entry.IsDirectory)
							dirNodes.Add(node);
						else
							fileNodes.Add(node);
					}
					allNodes.AddRange(dirNodes);
					allNodes.AddRange(fileNodes);
				}
				catch (Exception ex)
				{
					Debug.WriteLine($"[ReloadArchiveChildren] {ArchiveFilePath}: {ex.Message}");
				}
			}

			await _uiDispatcherQueue.EnqueueAsync(() =>
			{
				Children.Clear();
				foreach (var item in allNodes)
				{
					if (WillSplitToDifferentSorts)
						item.SortByTime = Helpers.GroupedFileList.GetTimeGroup(item.LastModifiedTime);
					Children.Add(item);
				}

				var actualCount = Children.Count(c => !c.IsPlaceholder);
				ChildrenCountText = actualCount > 0 ? $"[{actualCount}]" : "[?]";
			});
		}


		// 启动异步统计子项数量（用于显示括号）
		private async Task StartAsyncCount()
		{
			if (_cachedChildrenCount.HasValue || _isCounting || !IsDirectory) return;
			_isCounting = true;

			await Task.Run(async () =>
			{
				try
				{
					int count = SafeGetDirs(FullPath).Count + SafeGetFiles(FullPath).Count;
					_cachedChildrenCount = count;
					_uiDispatcherQueue.TryEnqueue(() =>
					{
						if (Children.Count == 1 && Children[0].IsPlaceholder)
						{
							ChildrenCountText = count > 0 ? $" [{count}]" : string.Empty;
						}
						else
						{
							var actualCount = Children.Count(c => !(c.IsPlaceholder));
							ChildrenCountText = actualCount > 0 ? $" [{actualCount}]" : string.Empty;
						}
					});
				}
				catch { }
				finally { _isCounting = false; }
			});
		}

		// 获取子项数量（用于界面显示）
		public string GetChildrenCount()
		{
			if (!IsDirectory) return string.Empty;
			if (ChildrenCountText != string.Empty) return ChildrenCountText;

			int count = Children.Count(c => !(c.IsPlaceholder));
			if (count == 0 && Children.Count == 1 && Children[0].IsPlaceholder && !string.IsNullOrEmpty(FullPath))
			{
				try
				{
					count = SafeGetDirs(FullPath).Count + SafeGetFiles(FullPath).Count;
				}
				catch
				{
					count = 0;
				}
			}
			return count > 0 ? $" [{count}]" : string.Empty;
		}

		// 节点类型名称（用于调试）
		public string NodeTypeName => this.GetType().Name;

		// 扩展：展开/折叠（若需要）
		private bool _isExpanded;
		public bool IsExpanded
		{
			get => _isExpanded;
			set
			{
				if (SetProperty(ref _isExpanded, value) && value && IsDirectory)
				{
					_ = LoadChildrenAsync();
				}
			}
		}
		//private bool IsRoot()
		//{
		//	if (FullPath == null) return true;
		//	if (FullPath[FullPath.Length - 1] == '\\')
		//	{
		//		return true;
		//	}
		//	return false;
		//}
	}
}
