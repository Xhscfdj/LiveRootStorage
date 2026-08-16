using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FastFluentFilesFolders.Services
{
	/// <summary>
	/// 使用 Windows Restart Manager API 查询正在使用指定文件（锁定文件）的进程名称。
	/// </summary>
	public static class ProcessHelper
	{
		#region Restart Manager API P/Invoke

		private const int RmRebootReasonNone = 0;
		private const int CCH_RM_MAX_APP_NAME = 255;
		private const int CCH_RM_MAX_SVC_NAME = 63;

		private enum RM_APP_TYPE
		{
			RmUnknownApp = 0,
			RmMainWindow = 1,
			RmOtherWindow = 2,
			RmService = 3,
			RmExplorer = 4,
			RmConsole = 5,
			RmCritical = 1000
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct RM_UNIQUE_PROCESS
		{
			public uint dwProcessId;
			// FILETIME 由两个 DWORD 组成，使用两个 uint 避免 C# long 的 8 字节对齐差异
			public uint ProcessStartTimeLow;
			public uint ProcessStartTimeHigh;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct RM_PROCESS_INFO
		{
			public RM_UNIQUE_PROCESS Process;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_APP_NAME + 1)]
			public string strAppName;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_SVC_NAME + 1)]
			public string strServiceShortName;
			public RM_APP_TYPE ApplicationType;
			public uint AppStatus;
			public uint TSSessionId;
			[MarshalAs(UnmanagedType.Bool)]
			public bool bRestartable;
		}

		[DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
		private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

		[DllImport("rstrtmgr.dll")]
		private static extern int RmEndSession(uint pSessionHandle);

		[DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
		private static extern int RmRegisterResources(
			uint pSessionHandle,
			uint nFiles,
			string[] rgsFilenames,
			uint nApplications,
			RM_UNIQUE_PROCESS[]? rgApplications,
			uint nServices,
			string[]? rgsServiceNames);

		[DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
		private static extern int RmGetList(
			uint dwSessionHandle,
			out uint pnProcInfoNeeded,
			ref uint pnProcInfo,
			[In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
			out uint lpdwRebootReasons);

		#endregion

		/// <summary>
		/// 获取正在使用指定文件的所有进程名称，用逗号分隔。
		/// 如果文件未被任何进程锁定，返回空字符串。
		/// </summary>
		public static string GetProcessesUsingFile(string filePath)
		{
			if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
				return string.Empty;

			uint sessionHandle;
			int result = RmStartSession(out sessionHandle, 0, Guid.NewGuid().ToString());
			if (result != 0)
			{
				Debug.WriteLine($"[ProcessHelper] RmStartSession failed with code {result} for {filePath}");
				return string.Empty;
			}

			try
			{
				string[] resources = [filePath];
				result = RmRegisterResources(sessionHandle, (uint)resources.Length, resources, 0, null, 0, null);
				if (result != 0)
				{
					// ERROR_SEM_TIMEOUT (121) = 进程没有锁定文件，这是正常的
					// ERROR_BAD_ARGUMENTS (160) = 参数无效
					if (result == 121)
						return string.Empty;
					Debug.WriteLine($"[ProcessHelper] RmRegisterResources failed with code {result} for {filePath}");
					return string.Empty;
				}

				uint pnProcInfo = 0;
				uint pnProcInfoNeeded;
				uint lpdwRebootReasons = RmRebootReasonNone;

				result = RmGetList(sessionHandle, out pnProcInfoNeeded, ref pnProcInfo, null, out lpdwRebootReasons);
				if (result != 234) // ERROR_MORE_DATA (234) = 还有更多数据
				{
					if (result == 0 && pnProcInfo == 0)
						return string.Empty;
					Debug.WriteLine($"[ProcessHelper] RmGetList (initial) failed with code {result}");
					return string.Empty;
				}

				RM_PROCESS_INFO[] processInfo = new RM_PROCESS_INFO[pnProcInfoNeeded];
				pnProcInfo = pnProcInfoNeeded;
				result = RmGetList(sessionHandle, out pnProcInfoNeeded, ref pnProcInfo, processInfo, out lpdwRebootReasons);
				if (result != 0)
				{
					Debug.WriteLine($"[ProcessHelper] RmGetList (second) failed with code {result}");
					return string.Empty;
				}

				var processNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				for (int i = 0; i < pnProcInfo; i++)
				{
					try
					{
						string procName = processInfo[i].strAppName;
						if (!string.IsNullOrEmpty(procName))
						{
							processNames.Add(procName);
						}
						else
						{
							// Fallback: try to get process name by PID
							try
							{
								using var proc = Process.GetProcessById((int)processInfo[i].Process.dwProcessId);
								processNames.Add(proc.ProcessName);
							}
							catch { }
						}
					}
					catch (Exception ex)
					{
						Debug.WriteLine($"[ProcessHelper] Error getting process name: {ex.Message}");
					}
				}

				if (processNames.Count == 0)
					return string.Empty;

				var sb = new StringBuilder();
				foreach (var name in processNames)
				{
					if (sb.Length > 0)
						sb.Append(", ");
					sb.Append(name);
				}
				return sb.ToString();
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[ProcessHelper] Exception for {filePath}: {ex.Message}");
				return string.Empty;
			}
			finally
			{
				RmEndSession(sessionHandle);
			}
		}

		/// <summary>
		/// 检查文件是否被任何进程锁定。
		/// </summary>
		public static bool IsFileLocked(string filePath)
		{
			if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
				return false;

			try
			{
				using var fs = System.IO.File.Open(filePath, System.IO.FileMode.Open,
					System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
				return false;
			}
			catch (System.IO.IOException)
			{
				return true;
			}
			catch
			{
				return false;
			}
		}
	}
}
