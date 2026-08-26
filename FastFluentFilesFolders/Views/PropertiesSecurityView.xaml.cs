using FastFluentFilesFolders.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace FastFluentFilesFolders.Views
{
	/// <summary>安全页：纯 XAML 绑定 ViewModel；仅警告确认与高级对话框在视图代码隐藏中。</summary>
	public sealed partial class PropertiesSecurityView : UserControl
	{
		public PropertiesSecurityViewModel Vm { get; }

		public PropertiesSecurityView(FileSystemNodeViewModel item)
		{
			InitializeComponent();
			Vm = new PropertiesSecurityViewModel(item);
			DataContext = Vm;
		}

		/// <summary>应用权限修改；修改系统资源前弹警告确认框。</summary>
		public async Task<bool> Apply()
		{
			if (Vm.NeedsWarningBeforeApply)
			{
				var confirm = await ConfirmPermissionChangeAsync();
				if (!confirm)
					return false;
			}

			if (Vm.ApplyCore())
				return true;

			ShowError(Vm.LastError ?? App.ML.Get("CmdError"));
			return false;
		}

		private async Task<bool> ConfirmPermissionChangeAsync()
		{
			try
			{
				var dialog = new ContentDialog
				{
					Title = App.ML.Get("PropertiesSecurityConfirmTitle"),
					Content = new TextBlock
					{
						Text = App.ML.Get("PropertiesSecurityConfirmSystemWarning"),
						TextWrapping = TextWrapping.Wrap
					},
					PrimaryButtonText = App.ML.Get("PropertiesSecurityContinue"),
					CloseButtonText = App.ML.Get("CmdCancel"),
					DefaultButton = ContentDialogButton.Primary,
					XamlRoot = XamlRoot
				};
				return await dialog.ShowAsync() == ContentDialogResult.Primary;
			}
			catch
			{
				return false;
			}
		}

		private void OnAdvancedClick(object sender, RoutedEventArgs e) => ShowSecurityAdvancedDialog();

		private void ShowSecurityAdvancedDialog()
		{
			try
			{
				var lines = Vm.BuildAdvancedLines();
				var dialog = new ContentDialog
				{
					Title = App.ML.Get("PropertiesSecurityAdvancedTitle"),
					Content = new ScrollViewer
					{
						Content = new ItemsControl { ItemsSource = lines },
						MaxHeight = 420
					},
					CloseButtonText = App.ML.Get("CmdClose"),
					XamlRoot = XamlRoot
				};
				_ = dialog.ShowAsync();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[Properties] Security advanced dialog failed: {ex.Message}");
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
