using FastFluentFilesFolders.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;

namespace FastFluentFilesFolders.Views
{
	/// <summary>常规页：纯 XAML，数据绑定到 ViewModel；仅高级属性对话框在视图代码隐藏中。</summary>
	public sealed partial class PropertiesGeneralView : UserControl
	{
		public PropertiesGeneralViewModel Vm { get; }

		public PropertiesGeneralView(FileSystemNodeViewModel item, FileAttributes originalAttributes)
		{
			InitializeComponent();
			Vm = new PropertiesGeneralViewModel(item, originalAttributes);
			DataContext = Vm;
		}

		/// <summary>应用重命名与属性修改；失败返回 false 并弹错误。</summary>
		public bool Apply()
		{
			if (Vm.ApplyCore())
				return true;
			ShowError(Vm.LastError ?? App.ML.Get("CmdError"));
			return false;
		}

		private void OnAdvancedClick(object sender, RoutedEventArgs e) => ShowAdvancedAttributesDialog();

		private void ShowAdvancedAttributesDialog()
		{
			try
			{
				var current = Vm.ReadCurrentAttributes();
				var archiveCheck = new CheckBox
				{
					Content = App.ML.Get("PropertiesArchive"),
					IsChecked = current.HasFlag(FileAttributes.Archive)
				};
				var notIndexedCheck = new CheckBox
				{
					Content = App.ML.Get("PropertiesNotIndexed"),
					IsChecked = current.HasFlag(FileAttributes.NotContentIndexed)
				};
				var panel = new StackPanel { Spacing = 8 };
				panel.Children.Add(archiveCheck);
				panel.Children.Add(notIndexedCheck);

				var dialog = new ContentDialog
				{
					Title = App.ML.Get("PropertiesAdvanced"),
					Content = panel,
					PrimaryButtonText = App.ML.Get("CmdOk"),
					CloseButtonText = App.ML.Get("CmdCancel"),
					DefaultButton = ContentDialogButton.Primary,
					XamlRoot = XamlRoot
				};
				dialog.PrimaryButtonClick += (_, _) =>
					Vm.ApplyAdvancedAttributes(current, archiveCheck.IsChecked == true, notIndexedCheck.IsChecked == true);
				_ = dialog.ShowAsync();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[Properties] Advanced attributes dialog failed: {ex.Message}");
			}
		}

		private void ShowError(string message)
		{
			try
			{
				var dialog = new ContentDialog
				{
					Title = App.ML.Get("CmdError"),
					Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
					CloseButtonText = App.ML.Get("CmdOk"),
					XamlRoot = XamlRoot
				};
				_ = dialog.ShowAsync();
			}
			catch { }
		}
	}
}
