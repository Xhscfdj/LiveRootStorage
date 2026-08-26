using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Tasks;

namespace FastFluentFilesFolders.ViewModels
{
	/// <summary>安全页 ViewModel：主体列表 + 允许/拒绝权限矩阵（可编辑），ACL 读写逻辑。</summary>
	public partial class PropertiesSecurityViewModel : ObservableObject
	{
		private readonly FileSystemNodeViewModel _item;
		private readonly List<FileSystemAccessRule> _allRules = new();
		private string _owner = string.Empty;
		private string _group = string.Empty;
		private bool _edited;
		private bool _syncing;

		public ObservableCollection<SecurityPrincipalInfo> Principals { get; } = new();

		// 标签文本（供 XAML 绑定）
		public string UsersGroupsLabel => App.ML.Get("PropertiesSecurityUsersGroups");
		public string NoEntriesLabel => App.ML.Get("PropertiesSecurityNoEntries");
		public string AdvancedLabel => App.ML.Get("PropertiesSecurityAdvanced");
		public string AllowHeaderLabel => App.ML.Get("PropertiesSecurityAllow");
		public string DenyHeaderLabel => App.ML.Get("PropertiesSecurityDeny");
		public string FullControlLabel => App.ML.Get("PropertiesSecurityFullControl");
		public string ModifyLabel => App.ML.Get("PropertiesSecurityModify");
		public string ReadExecuteLabel => App.ML.Get("PropertiesSecurityReadExecute");
		public string ListDirectoryLabel => App.ML.Get("PropertiesSecurityListDirectory");
		public string ReadLabel => App.ML.Get("PropertiesSecurityRead");
		public string WriteLabel => App.ML.Get("PropertiesSecurityWrite");
		public string SpecialLabel => App.ML.Get("PropertiesSecuritySpecial");
		public string PermissionHintText => App.ML.Get("PropertiesSecurityReadOnlyHint");

		[ObservableProperty] private SecurityPrincipalInfo? selectedPrincipal;
		[ObservableProperty] private string objectCaption = string.Empty;
		[ObservableProperty] private string ownerCaption = string.Empty;
		[ObservableProperty] private bool hasOwner;
		[ObservableProperty] private bool hasPrincipals;
		[ObservableProperty] private bool hasNoPrincipals;
		[ObservableProperty] private bool hasError;
		[ObservableProperty] private string errorMessage = string.Empty;
		[ObservableProperty] private bool advancedEnabled;
		[ObservableProperty] private bool isDirectory;
		[ObservableProperty] private string permissionsTitle = string.Empty;
		[ObservableProperty] private bool showListDirectoryRow;
		[ObservableProperty] private bool showSpecialRow;

		// 允许/拒绝矩阵（TwoWay 绑定）
		[ObservableProperty] private bool allowFullControl;
		[ObservableProperty] private bool denyFullControl;
		[ObservableProperty] private bool allowModify;
		[ObservableProperty] private bool denyModify;
		[ObservableProperty] private bool allowReadExecute;
		[ObservableProperty] private bool denyReadExecute;
		[ObservableProperty] private bool allowListDirectory;
		[ObservableProperty] private bool denyListDirectory;
		[ObservableProperty] private bool allowRead;
		[ObservableProperty] private bool denyRead;
		[ObservableProperty] private bool allowWrite;
		[ObservableProperty] private bool denyWrite;
		[ObservableProperty] private bool allowSpecial;
		[ObservableProperty] private bool denySpecial;

		// 可编辑状态（继承项置灰）
		[ObservableProperty] private bool allowFullControlEnabled;
		[ObservableProperty] private bool denyFullControlEnabled;
		[ObservableProperty] private bool allowModifyEnabled;
		[ObservableProperty] private bool denyModifyEnabled;
		[ObservableProperty] private bool allowReadExecuteEnabled;
		[ObservableProperty] private bool denyReadExecuteEnabled;
		[ObservableProperty] private bool allowListDirectoryEnabled;
		[ObservableProperty] private bool denyListDirectoryEnabled;
		[ObservableProperty] private bool allowReadEnabled;
		[ObservableProperty] private bool denyReadEnabled;
		[ObservableProperty] private bool allowWriteEnabled;
		[ObservableProperty] private bool denyWriteEnabled;

		public string? LastError { get; private set; }

		public PropertiesSecurityViewModel(FileSystemNodeViewModel item)
		{
			_item = item;
			ReloadState();
		}

		partial void OnSelectedPrincipalChanged(SecurityPrincipalInfo? value) => SyncMatrix();

		partial void OnAllowFullControlChanged(bool value) { if (!_syncing) { Toggle(FileSystemRights.FullControl, true, value); SyncMatrix(); } }
		partial void OnDenyFullControlChanged(bool value) { if (!_syncing) { Toggle(FileSystemRights.FullControl, false, value); SyncMatrix(); } }
		partial void OnAllowModifyChanged(bool value) { if (!_syncing) { Toggle(FileSystemRights.Modify, true, value); SyncMatrix(); } }
		partial void OnDenyModifyChanged(bool value) { if (!_syncing) { Toggle(FileSystemRights.Modify, false, value); SyncMatrix(); } }
		partial void OnAllowReadExecuteChanged(bool value) { if (!_syncing) { Toggle(FileSystemRights.ReadAndExecute, true, value); SyncMatrix(); } }
		partial void OnDenyReadExecuteChanged(bool value) { if (!_syncing) { Toggle(FileSystemRights.ReadAndExecute, false, value); SyncMatrix(); } }
		partial void OnAllowListDirectoryChanged(bool value) { if (!_syncing) { Toggle(FileSystemRights.ListDirectory, true, value); SyncMatrix(); } }
		partial void OnDenyListDirectoryChanged(bool value) { if (!_syncing) { Toggle(FileSystemRights.ListDirectory, false, value); SyncMatrix(); } }
		partial void OnAllowReadChanged(bool value) { if (!_syncing) { Toggle(FileSystemRights.Read, true, value); SyncMatrix(); } }
		partial void OnDenyReadChanged(bool value) { if (!_syncing) { Toggle(FileSystemRights.Read, false, value); SyncMatrix(); } }
		partial void OnAllowWriteChanged(bool value) { if (!_syncing) { Toggle(FileSystemRights.Write, true, value); SyncMatrix(); } }
		partial void OnDenyWriteChanged(bool value) { if (!_syncing) { Toggle(FileSystemRights.Write, false, value); SyncMatrix(); } }

		// ------------------------------------------------------------ 加载

		public void ReloadState()
		{
			try
			{
				_allRules.Clear();
				Principals.Clear();
				_edited = false;
				LastError = null;

				var owner = string.Empty;
				var group = string.Empty;
				var sections = AccessControlSections.Access | AccessControlSections.Owner | AccessControlSections.Group;
				FileSystemSecurity security = _item.IsDirectory
					? new DirectorySecurity(_item.FullPath, sections)
					: new FileSecurity(_item.FullPath, sections);

				try { owner = TryGetAccountName(security.GetOwner(typeof(NTAccount))); } catch { }
				try { group = TryGetAccountName(security.GetGroup(typeof(NTAccount))); } catch { }
				_owner = owner;
				_group = group;

				foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(NTAccount)))
				{
					_allRules.Add(rule);
					var identity = rule.IdentityReference.Value ?? string.Empty;
					var info = Principals.FirstOrDefault(p =>
						string.Equals(p.Identity, identity, StringComparison.OrdinalIgnoreCase));
					if (info == null)
					{
						info = CreatePrincipalInfo(identity);
						Principals.Add(info);
					}

					if (rule.AccessControlType == AccessControlType.Allow)
					{
						if (rule.IsInherited) info.AllowedInherited |= rule.FileSystemRights;
						else info.Allowed |= rule.FileSystemRights;
					}
					else
					{
						if (rule.IsInherited) info.DeniedInherited |= rule.FileSystemRights;
						else info.Denied |= rule.FileSystemRights;
					}

					if (!rule.IsInherited && !info.HasExplicitRules)
					{
						info.InheritanceFlags = rule.InheritanceFlags;
						info.PropagationFlags = rule.PropagationFlags;
						info.HasExplicitRules = true;
					}
				}

				foreach (var p in Principals)
				{
					p.OriginalAllowed = p.Allowed;
					p.OriginalDenied = p.Denied;
				}

				ObjectCaption = $"{App.ML.Get("PropertiesSecurityObject")} {_item.FullPath}";
				OwnerCaption = $"{App.ML.Get("PropertiesSecurityOwner")} {owner}";
				HasOwner = owner.Length > 0;
				HasPrincipals = Principals.Count > 0;
				HasNoPrincipals = Principals.Count == 0;
				HasError = false;
				AdvancedEnabled = _allRules.Count > 0 || HasPrincipals;
				SelectedPrincipal = HasPrincipals ? Principals[0] : null;
				SyncMatrix();
			}
			catch (UnauthorizedAccessException ex)
			{
				Debug.WriteLine($"[Properties] Security load denied: {ex.Message}");
				ErrorMessage = App.ML.Get("PropertiesSecurityAccessDeniedHint");
				HasError = true;
				AdvancedEnabled = false;
				HasPrincipals = false;
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[Properties] Security load failed: {ex.Message}");
				ErrorMessage = $"{App.ML.Get("PropertiesSecurityError")} ({ex.Message})";
				HasError = true;
				AdvancedEnabled = false;
				HasPrincipals = false;
			}
		}

		private void SyncMatrix()
		{
			_syncing = true;
			try
			{
				var p = SelectedPrincipal;
				IsDirectory = _item.IsDirectory;
				ShowListDirectoryRow = _item.IsDirectory;
				ShowSpecialRow = p != null && HasSpecialRights(p);
				PermissionsTitle = p == null
					? App.ML.Get("PropertiesSecurityUsersGroups")
					: string.Format(App.ML.Get("PropertiesSecurityPermissionsFor"), p.DisplayName);

				SetChecks(p);
			}
			finally
			{
				_syncing = false;
			}
		}

		private void SetChecks(SecurityPrincipalInfo? p)
		{
			bool has = p != null;
			AllowFullControl = has && (p!.Allowed | p.AllowedInherited).HasFlag(FileSystemRights.FullControl);
			DenyFullControl = has && (p.Denied | p.DeniedInherited).HasFlag(FileSystemRights.FullControl);
			AllowModify = has && (p.Allowed | p.AllowedInherited).HasFlag(FileSystemRights.Modify);
			DenyModify = has && (p.Denied | p.DeniedInherited).HasFlag(FileSystemRights.Modify);
			AllowReadExecute = has && (p.Allowed | p.AllowedInherited).HasFlag(FileSystemRights.ReadAndExecute);
			DenyReadExecute = has && (p.Denied | p.DeniedInherited).HasFlag(FileSystemRights.ReadAndExecute);
			AllowListDirectory = has && (p.Allowed | p.AllowedInherited).HasFlag(FileSystemRights.ListDirectory);
			DenyListDirectory = has && (p.Denied | p.DeniedInherited).HasFlag(FileSystemRights.ListDirectory);
			AllowRead = has && (p.Allowed | p.AllowedInherited).HasFlag(FileSystemRights.Read);
			DenyRead = has && (p.Denied | p.DeniedInherited).HasFlag(FileSystemRights.Read);
			AllowWrite = has && (p.Allowed | p.AllowedInherited).HasFlag(FileSystemRights.Write);
			DenyWrite = has && (p.Denied | p.DeniedInherited).HasFlag(FileSystemRights.Write);

			AllowSpecial = has && SpecialRights(p!.Allowed | p.AllowedInherited) != 0;
			DenySpecial = has && SpecialRights(p.Denied | p.DeniedInherited) != 0;

			AllowFullControlEnabled = has && !p.AllowedInherited.HasFlag(FileSystemRights.FullControl);
			DenyFullControlEnabled = has && !p.DeniedInherited.HasFlag(FileSystemRights.FullControl);
			AllowModifyEnabled = has && !p.AllowedInherited.HasFlag(FileSystemRights.Modify);
			DenyModifyEnabled = has && !p.DeniedInherited.HasFlag(FileSystemRights.Modify);
			AllowReadExecuteEnabled = has && !p.AllowedInherited.HasFlag(FileSystemRights.ReadAndExecute);
			DenyReadExecuteEnabled = has && !p.DeniedInherited.HasFlag(FileSystemRights.ReadAndExecute);
			AllowListDirectoryEnabled = has && !p.AllowedInherited.HasFlag(FileSystemRights.ListDirectory);
			DenyListDirectoryEnabled = has && !p.DeniedInherited.HasFlag(FileSystemRights.ListDirectory);
			AllowReadEnabled = has && !p.AllowedInherited.HasFlag(FileSystemRights.Read);
			DenyReadEnabled = has && !p.DeniedInherited.HasFlag(FileSystemRights.Read);
			AllowWriteEnabled = has && !p.AllowedInherited.HasFlag(FileSystemRights.Write);
			DenyWriteEnabled = has && !p.DeniedInherited.HasFlag(FileSystemRights.Write);
		}

		private void Toggle(FileSystemRights right, bool allow, bool value)
		{
			if (SelectedPrincipal == null) return;
			if (allow)
			{
				if (value) { SelectedPrincipal.Allowed |= right; SelectedPrincipal.Denied &= ~right; }
				else SelectedPrincipal.Allowed &= ~right;
			}
			else
			{
				if (value) { SelectedPrincipal.Denied |= right; SelectedPrincipal.Allowed &= ~right; }
				else SelectedPrincipal.Denied &= ~right;
			}
			SelectedPrincipal.Changed = true;
			_edited = true;
		}

		private static FileSystemRights SpecialRights(FileSystemRights rights)
		{
			const FileSystemRights explained =
				FileSystemRights.FullControl | FileSystemRights.Modify | FileSystemRights.ReadAndExecute |
				FileSystemRights.Read | FileSystemRights.Write | FileSystemRights.ListDirectory | FileSystemRights.Synchronize;
			return rights & ~explained;
		}

		private static bool HasSpecialRights(SecurityPrincipalInfo p)
		{
			return SpecialRights(p.Allowed) != 0 ||
			       SpecialRights(p.Denied) != 0 ||
			       SpecialRights(p.AllowedInherited) != 0 ||
			       SpecialRights(p.DeniedInherited) != 0;
		}

		// ------------------------------------------------------------ 应用

		/// <summary>是否需要在写入前弹出警告（受保护系统资源且已编辑）。</summary>
		public bool NeedsWarningBeforeApply => _edited && IsProtectedSystemResource();

		/// <summary>执行 ACL 写入（仅 DACL / Access 段）。成功返回 true。</summary>
		public bool ApplyCore()
		{
			LastError = null;
			if (!_edited || Principals.Count == 0)
				return true;

			try
			{
				if (_item.IsDirectory)
				{
					var dirInfo = new DirectoryInfo(_item.FullPath);
					var security = dirInfo.GetAccessControl(AccessControlSections.Access);
					ApplyPrincipalRules(security);
					dirInfo.SetAccessControl(security);
				}
				else
				{
					var fileInfo = new FileInfo(_item.FullPath);
					var security = fileInfo.GetAccessControl(AccessControlSections.Access);
					ApplyPrincipalRules(security);
					fileInfo.SetAccessControl(security);
				}

				_edited = false;
				ReloadState();
				return true;
			}
			catch (UnauthorizedAccessException ex)
			{
				Debug.WriteLine($"[Properties] Apply security changes denied: {ex.Message}");
				LastError = App.ML.Get("PropertiesSecurityAccessDeniedHint");
				return false;
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[Properties] Apply security changes failed: {ex.Message}");
				LastError = ex.Message;
				return false;
			}
		}

		private void ApplyPrincipalRules(FileSystemSecurity security)
		{
			foreach (var p in Principals)
			{
				if (!p.Changed) continue;

				var explicitRules = security.GetAccessRules(true, false, typeof(NTAccount))
					.Cast<FileSystemAccessRule>()
					.Where(r => string.Equals(r.IdentityReference.Value, p.Identity, StringComparison.OrdinalIgnoreCase))
					.ToList();
				foreach (var rule in explicitRules)
					security.RemoveAccessRuleSpecific(rule);

				var inheritance = p.HasExplicitRules
					? p.InheritanceFlags
					: _item.IsDirectory ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit : InheritanceFlags.None;
				var propagation = p.HasExplicitRules ? p.PropagationFlags : PropagationFlags.None;

				if (p.Allowed != 0)
					security.AddAccessRule(new FileSystemAccessRule(p.Identity, p.Allowed, inheritance, propagation, AccessControlType.Allow));
				if (p.Denied != 0)
					security.AddAccessRule(new FileSystemAccessRule(p.Identity, p.Denied, inheritance, propagation, AccessControlType.Deny));
			}
		}

		/// <summary>判断目标是否为受保护的系统资源（系统目录或属主为 TrustedInstaller/SYSTEM）。</summary>
		public bool IsProtectedSystemResource()
		{
			try
			{
				var path = _item.FullPath.ToUpperInvariant();
				if (path.StartsWith("C:\\WINDOWS\\") ||
				    path.StartsWith("C:\\PROGRAM FILES\\") ||
				    path.StartsWith("C:\\PROGRAM FILES (X86)\\") ||
				    path.StartsWith("C:\\PROGRAMDATA\\"))
					return true;
			}
			catch { }
			return false;
		}

		// ------------------------------------------------------------ 高级对话框数据

		public IReadOnlyList<string> BuildAdvancedLines()
		{
			var lines = new List<string>
			{
				$"{App.ML.Get("PropertiesSecurityObject")} {_item.FullPath}"
			};
			if (!string.IsNullOrEmpty(_owner))
				lines.Add($"{App.ML.Get("PropertiesSecurityOwner")} {_owner}");
			if (!string.IsNullOrEmpty(_group))
				lines.Add($"{App.ML.Get("PropertiesSecurityGroups")} {_group}");
			lines.Add(string.Empty);

			foreach (var rule in _allRules)
			{
				var type = rule.AccessControlType == AccessControlType.Allow
					? App.ML.Get("PropertiesSecurityAllow") : App.ML.Get("PropertiesSecurityDeny");
				var identity = rule.IdentityReference.Value ?? "-";
				var inherited = rule.IsInherited
					? App.ML.Get("PropertiesSecurityAdvancedYes") : App.ML.Get("PropertiesSecurityAdvancedNo");
				lines.Add($"{type}  {identity}");
				lines.Add($"   {App.ML.Get("PropertiesSecurityAdvancedAccess")}: {FormatRights(rule.FileSystemRights)}");
				lines.Add($"   {App.ML.Get("PropertiesSecurityAdvancedInherited")}: {inherited}");
				lines.Add($"   {App.ML.Get("PropertiesSecurityAdvancedInheritance")}: {FormatInheritance(rule)}");
				lines.Add(string.Empty);
			}
			return lines;
		}

		private string FormatRights(FileSystemRights rights)
		{
			var parts = new List<string>();
			if (rights.HasFlag(FileSystemRights.FullControl)) parts.Add(App.ML.Get("PropertiesSecurityFullControl"));
			else
			{
				if (rights.HasFlag(FileSystemRights.Modify)) parts.Add(App.ML.Get("PropertiesSecurityModify"));
				if (rights.HasFlag(FileSystemRights.ReadAndExecute)) parts.Add(App.ML.Get("PropertiesSecurityReadExecute"));
				if (rights.HasFlag(FileSystemRights.Read)) parts.Add(App.ML.Get("PropertiesSecurityRead"));
				if (rights.HasFlag(FileSystemRights.Write)) parts.Add(App.ML.Get("PropertiesSecurityWrite"));
				if (_item.IsDirectory && rights.HasFlag(FileSystemRights.ListDirectory)) parts.Add(App.ML.Get("PropertiesSecurityListDirectory"));
				if (SpecialRights(rights) != 0) parts.Add(App.ML.Get("PropertiesSecuritySpecial"));
			}
			return parts.Count > 0 ? string.Join(", ", parts) : rights.ToString();
		}

		private static string FormatInheritance(FileSystemAccessRule rule)
		{
			if (rule.InheritanceFlags == InheritanceFlags.None && rule.PropagationFlags == PropagationFlags.None)
				return App.ML.Get("PropertiesSecurityInheritOnlyThis");
			var parts = new List<string>();
			if (rule.InheritanceFlags.HasFlag(InheritanceFlags.ContainerInherit)) parts.Add(App.ML.Get("PropertiesSecurityInheritSubfolders"));
			if (rule.InheritanceFlags.HasFlag(InheritanceFlags.ObjectInherit)) parts.Add(App.ML.Get("PropertiesSecurityInheritFiles"));
			if (rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly)) parts.Add(App.ML.Get("PropertiesSecurityInheritInheritOnly"));
			if (rule.PropagationFlags.HasFlag(PropagationFlags.NoPropagateInherit)) parts.Add(App.ML.Get("PropertiesSecurityInheritNoPropagate"));
			return parts.Count > 0 ? string.Join(", ", parts) : "-";
		}

		private static string TryGetAccountName(IdentityReference? reference)
		{
			try { return reference?.Value ?? string.Empty; }
			catch { return string.Empty; }
		}

		private static SecurityPrincipalInfo CreatePrincipalInfo(string value)
		{
			var info = new SecurityPrincipalInfo { Identity = value };
			var name = value;
			var domain = string.Empty;
			var idx = value.IndexOf('\\');
			if (idx >= 0)
			{
				name = value[(idx + 1)..];
				domain = value[..idx];
			}
			info.DisplayName = string.IsNullOrEmpty(name) ? value : name;
			info.Domain = string.IsNullOrEmpty(domain) ? null : domain;
			var upper = value.ToUpperInvariant();
			info.IsGroup = upper.Contains("BUILTIN\\") ||
			               upper.EndsWith("\\ADMINISTRATORS") ||
			               upper.EndsWith("\\USERS") ||
			               upper.EndsWith("\\GUESTS") ||
			               upper.EndsWith("\\PERFORMANCE LOG USERS");
			return info;
		}
	}

	/// <summary>安全页中的一个主体（组或用户）及其权限掩码。</summary>
	public sealed class SecurityPrincipalInfo
	{
		public string Identity { get; init; } = string.Empty;
		public string DisplayName { get; set; } = string.Empty;
		public string? Domain { get; set; }
		public bool IsGroup { get; set; }
		public FileSystemRights Allowed { get; set; }
		public FileSystemRights Denied { get; set; }
		public FileSystemRights AllowedInherited { get; set; }
		public FileSystemRights DeniedInherited { get; set; }
		public FileSystemRights OriginalAllowed { get; set; }
		public FileSystemRights OriginalDenied { get; set; }
		public InheritanceFlags InheritanceFlags { get; set; }
		public PropagationFlags PropagationFlags { get; set; }
		public bool HasExplicitRules { get; set; }
		public bool Changed { get; set; }

		public string Glyph => IsGroup ? "\uE902" : "\uE77B";
		public bool HasDomain => !string.IsNullOrEmpty(Domain);
		public string FullIdentity => HasDomain ? $"({Identity})" : string.Empty;
	}
}