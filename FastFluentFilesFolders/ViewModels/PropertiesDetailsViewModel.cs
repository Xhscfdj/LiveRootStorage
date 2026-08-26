using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace FastFluentFilesFolders.ViewModels
{
	/// <summary>详细信息页 ViewModel：属性/值 表格数据。</summary>
	public partial class PropertiesDetailsViewModel : ObservableObject
	{
		public string HeaderText => App.ML.Get("PropertiesDetailsHeader");
		public string PropertyHeader => App.ML.Get("PropertiesDetailProperty");
		public string ValueHeader => App.ML.Get("PropertiesDetailValue");
		public IReadOnlyList<DetailRow> Rows { get; }

		public PropertiesDetailsViewModel(FileSystemNodeViewModel item, FileAttributes attributes)
		{
			Rows = BuildRows(item, attributes);
		}

		private static List<DetailRow> BuildRows(FileSystemNodeViewModel item, FileAttributes attributes)
		{
			var rows = new List<DetailRow>
			{
				new DetailRow(App.ML.Get("PropertiesDetailName"), item.Name),
				new DetailRow(App.ML.Get("PropertiesDetailFolderPath"), GetParentPath(item)),
				new DetailRow(App.ML.Get("PropertiesDetailSize"), item.VisualSize),
				new DetailRow(App.ML.Get("PropertiesDetailCreated"), item.FirstCreatedTimeString),
				new DetailRow(App.ML.Get("PropertiesDetailModified"), item.LastModifiedTimeString),
				new DetailRow(App.ML.Get("PropertiesDetailAccessed"), ReadAccessTime(item.FullPath)),
				new DetailRow(App.ML.Get("PropertiesDetailAttributes"), FormatAttributes(attributes))
			};

			if (!item.IsDirectory)
			{
				try
				{
					var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(item.FullPath);
					if (!string.IsNullOrWhiteSpace(info.FileDescription))
						rows.Add(new DetailRow(App.ML.Get("PropertiesDetailDescription"), info.FileDescription));
					if (!string.IsNullOrWhiteSpace(info.FileVersion))
						rows.Add(new DetailRow(App.ML.Get("PropertiesDetailFileVersion"), info.FileVersion));
					if (!string.IsNullOrWhiteSpace(info.ProductName))
						rows.Add(new DetailRow(App.ML.Get("PropertiesDetailProductName"), info.ProductName));
					if (!string.IsNullOrWhiteSpace(info.ProductVersion))
						rows.Add(new DetailRow(App.ML.Get("PropertiesDetailProductVersion"), info.ProductVersion));
					if (!string.IsNullOrWhiteSpace(info.CompanyName))
						rows.Add(new DetailRow(App.ML.Get("PropertiesDetailCompany"), info.CompanyName));
					if (!string.IsNullOrWhiteSpace(info.OriginalFilename))
						rows.Add(new DetailRow(App.ML.Get("PropertiesDetailOriginalFilename"), info.OriginalFilename));
					if (!string.IsNullOrWhiteSpace(info.LegalCopyright))
						rows.Add(new DetailRow(App.ML.Get("PropertiesDetailCopyright"), info.LegalCopyright));
				}
				catch
				{
					// 非 PE 文件没有版本资源时忽略。
				}
			}

			return rows;
		}

		private static string GetParentPath(FileSystemNodeViewModel item)
		{
			try
			{
				var path = item.FullPath.TrimEnd('\\');
				var parent = Path.GetDirectoryName(path);
				return string.IsNullOrWhiteSpace(parent) ? path : parent;
			}
			catch
			{
				return item.FullPath;
			}
		}

		private static string ReadAccessTime(string path)
		{
			try { return File.GetLastAccessTime(path).ToString("yyyy-MM-dd HH:mm:ss"); }
			catch { return "-"; }
		}

		private static string FormatAttributes(FileAttributes attributes)
		{
			var parts = new List<string>();
			if (attributes.HasFlag(FileAttributes.ReadOnly)) parts.Add("R");
			if (attributes.HasFlag(FileAttributes.Hidden)) parts.Add("H");
			if (attributes.HasFlag(FileAttributes.System)) parts.Add("S");
			if (attributes.HasFlag(FileAttributes.Archive)) parts.Add("A");
			if (attributes.HasFlag(FileAttributes.Directory)) parts.Add("D");
			if (attributes.HasFlag(FileAttributes.Compressed)) parts.Add("C");
			if (attributes.HasFlag(FileAttributes.Encrypted)) parts.Add("E");
			if (attributes.HasFlag(FileAttributes.NotContentIndexed)) parts.Add("I");
			return parts.Count > 0 ? string.Join(" ", parts) : "-";
		}
	}

	/// <summary>详细信息表格的一行（属性名 | 属性值）。</summary>
	public sealed class DetailRow
	{
		public string Name { get; }
		public string Value { get; }

		public DetailRow(string name, string value)
		{
			Name = name;
			Value = value;
		}
	}
}
