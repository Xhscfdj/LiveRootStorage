using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using System.Collections.Specialized;
using FastFluentFilesFolders.Models;

namespace FastFluentFilesFolders.Services
{
	public class FileOperator : IFileOperator
	{
		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct SHFILEOPSTRUCT
		{
			public IntPtr hwnd;
			public uint wFunc;
			[MarshalAs(UnmanagedType.LPWStr)]
			public string pFrom;
			[MarshalAs(UnmanagedType.LPWStr)]
			public string pTo;
			public ushort fFlags;
			public bool fAnyOperationsAborted;
			public IntPtr hNameMappings;
			[MarshalAs(UnmanagedType.LPWStr)]
			public string lpszProgressTitle;
		}

		[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
		private static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);

		private const uint FO_DELETE = 0x0003;
		private const ushort FOF_ALLOWUNDO = 0x0040;
		private const ushort FOF_NOCONFIRMATION = 0x0010;
		private const ushort FOF_SILENT = 0x0004;
		public async Task CopyToAsync(string sourcePath, string destinationPath, bool overwrite = false, Action<FileOperationProgress>? progress = null)
		{
			if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(destinationPath))
				throw new ArgumentException("路径不能为空");

			await Task.Run(() =>
			{
				var state = new FileOperationProgress();
				if (File.Exists(sourcePath))
				{
					// 文件复制
					File.Copy(sourcePath, destinationPath, overwrite);
					state.CompletedFiles = 1;
					state.CompletedBytes = SafeGetFileLength(sourcePath);
					progress?.Invoke(state);
				}
				else if (Directory.Exists(sourcePath))
				{
					// 文件夹复制（递归）
					CopyDirectoryRecursive(sourcePath, destinationPath, overwrite, state, progress);
				}
				else
				{
					throw new FileNotFoundException($"源路径不存在: {sourcePath}");
				}
			});
		}

		/// <summary>
		/// 统计一组路径（文件或文件夹，递归）包含的文件总数与总字节数。
		/// </summary>
		public async Task<(int FileCount, long TotalBytes)> GetTransferStatsAsync(IEnumerable<string> paths)
		{
			if (paths == null) return (0, 0);
			return await Task.Run(() =>
			{
				int count = 0;
				long bytes = 0;
				foreach (var path in paths)
				{
					if (File.Exists(path))
					{
						count++;
						bytes += SafeGetFileLength(path);
					}
					else if (Directory.Exists(path))
					{
						EnumerateStats(path, ref count, ref bytes);
					}
				}
				return (count, bytes);
			});
		}

		private static void EnumerateStats(string dir, ref int count, ref long bytes)
		{
			try
			{
				foreach (string file in Directory.EnumerateFiles(dir))
				{
					count++;
					bytes += SafeGetFileLength(file);
				}
				foreach (string subDir in Directory.EnumerateDirectories(dir))
				{
					EnumerateStats(subDir, ref count, ref bytes);
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[GetTransferStats] {dir}: {ex.Message}");
			}
		}

		private static long SafeGetFileLength(string path)
		{
			try { return new FileInfo(path).Length; }
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[FileOperator] 获取文件大小失败 {path}: {ex.Message}");
				return 0;
			}
		}

		private void CopyDirectoryRecursive(string sourceDir, string destDir, bool overwrite, FileOperationProgress state, Action<FileOperationProgress>? progress)
		{
			Directory.CreateDirectory(destDir);

			// 复制所有文件
			foreach (string file in Directory.GetFiles(sourceDir))
			{
				string fileName = Path.GetFileName(file);
				string destFile = Path.Combine(destDir, fileName);
				File.Copy(file, destFile, overwrite);
				state.CompletedFiles++;
				state.CompletedBytes += SafeGetFileLength(file);
				progress?.Invoke(state);
			}

			// 递归复制子目录
			foreach (string subDir in Directory.GetDirectories(sourceDir))
			{
				string dirName = Path.GetFileName(subDir);
				string destSubDir = Path.Combine(destDir, dirName);
				CopyDirectoryRecursive(subDir, destSubDir, overwrite, state, progress);
			}
		}

		public async Task MoveAsync(string sourcePath, string destinationPath)
		{
			if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(destinationPath))
				throw new ArgumentException("路径不能为空");

			if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
				return;

			await Task.Run(() =>
			{
				if (File.Exists(sourcePath))
				{
					File.Move(sourcePath, destinationPath);
				}
				else if (Directory.Exists(sourcePath))
				{
					Directory.Move(sourcePath, destinationPath);
				}
				else
				{
					throw new FileNotFoundException($"源路径不存在: {sourcePath}");
				}
			});
		}

		public async Task DeleteAsync(string path)
		{
			if (string.IsNullOrEmpty(path))
				throw new ArgumentException("路径不能为空");

			await Task.Run(() =>
			{
				if (File.Exists(path))
				{
					File.Delete(path);
				}
				else if (Directory.Exists(path))
				{
					Directory.Delete(path, true); // 递归删除
				}
				else
				{
					// 如果路径不存在，静默返回（或可选择抛出异常）
				}
			});
		}

		public async Task DeleteToRecycleBinAsync(string path)
		{
			if (string.IsNullOrEmpty(path))
				throw new ArgumentException("路径不能为空");

			await Task.Run(() =>
			{
				var fileOp = new SHFILEOPSTRUCT
				{
					wFunc = FO_DELETE,
					pFrom = path + "\0\0",
					fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION
				};
				SHFileOperation(ref fileOp);
			});
		}

		public async Task RenameAsync(string oldPath, string newName)
		{
			if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newName))
				throw new ArgumentException("路径或新名称不能为空");

			string directory = Path.GetDirectoryName(oldPath);
			string newPath = Path.Combine(directory, newName);

			// 重命名其实就是 Move 到同一目录下的新名称
			await MoveAsync(oldPath, newPath);
		}

		// ========== 剪贴板操作 ==========

		public async Task CopyToClipBoard(IEnumerable<string> filePaths, bool cut = false)
		{
			if (filePaths == null || !filePaths.Any())
				return;

			// 注意：由于需要将路径转换为 StorageItem，这里使用异步转同步（仅用于UI线程安全）
			// 建议在实际调用时确保此方法在 UI 线程执行，或者改为异步方法。
			var storageItems = new List<IStorageItem>();
			foreach (var path in filePaths)
			{
				try
				{
					if (File.Exists(path))
					{
						var file = StorageFile.GetFileFromPathAsync(path).AsTask().GetAwaiter().GetResult();
						storageItems.Add(file);
					}
					else if (Directory.Exists(path))
					{
						var folder = StorageFolder.GetFolderFromPathAsync(path).AsTask().GetAwaiter().GetResult();
						storageItems.Add(folder);
					}
				}
				catch (Exception ex)
				{
					// 处理无法访问的路径（记录日志或忽略）
					System.Diagnostics.Debug.WriteLine($"无法添加 {path} 到剪贴板: {ex.Message}");
				}
			}

			if (!storageItems.Any())
				return;

			var dataPackage = new DataPackage();
			dataPackage.SetStorageItems(storageItems);
			// 关键：设置操作类型，支持剪切标记
			dataPackage.RequestedOperation = cut ? DataPackageOperation.Move : DataPackageOperation.Copy;

			Clipboard.SetContent(dataPackage);
		}

		public async Task<(IEnumerable<string> FilePaths, bool IsCut)> PasteClipboardFiles()
		{
			var dataPackageView = Clipboard.GetContent();
			if (dataPackageView.Contains(StandardDataFormats.StorageItems))
			{
				try
				{
					var storageItems = await dataPackageView.GetStorageItemsAsync();
					var paths = storageItems.Select(item => item.Path).ToList();
					var operation = dataPackageView.RequestedOperation;
					bool isCut = (operation == DataPackageOperation.Move);
					return (paths, isCut);
				}
				catch (Exception ex)
				{
					// 剪贴板内容无法读取
					System.Diagnostics.Debug.WriteLine($"读取剪贴板失败: {ex.Message}");
					return (null, false);
				}
			}

			return (null, false);
		}
	}
}
