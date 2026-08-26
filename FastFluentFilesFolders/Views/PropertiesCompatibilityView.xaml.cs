using FastFluentFilesFolders.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace FastFluentFilesFolders.Views
{
	/// <summary>兼容性页：纯 XAML 绑定 ViewModel；仅疑难解答/高DPI/所有用户对话框在视图代码隐藏中。</summary>
	public sealed partial class PropertiesCompatibilityView : UserControl
	{
		public PropertiesCompatibilityViewModel Vm { get; }

		public PropertiesCompatibilityView(FileSystemNodeViewModel item)
		{
			InitializeComponent();
			Vm = new PropertiesCompatibilityViewModel(item);
			DataContext = Vm;
		}

		/// <summary>应用兼容性设置；失败弹错误并返回 false。</summary>
		public bool Apply()
		{
			if (Vm.Apply())
			{
				if (Vm.IsPackagedMode)
					ShowWarning(Vm.PackagedWarning);
				return true;
			}
			ShowError(Vm.LastError ?? App.ML.Get("CmdError"));
			return false;
		}

		private void ShowWarning(string message)
		{
			try
			{
				var dialog = new ContentDialog
				{
					Title = App.ML.Get("CmdWarning"),
					Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
					CloseButtonText = App.ML.Get("CmdOk"),
					XamlRoot = XamlRoot
				};
				_ = dialog.ShowAsync();
			}
			catch { }
		}

		private void OnTroubleClick(object sender, RoutedEventArgs e)
		{
			try
			{
				System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("msdt.exe", "-id CompatibilityDiagnostic")
				{
					UseShellExecute = false,
					CreateNoWindow = true
				});
			}
			catch (Exception ex)
			{
				ShowError(ex.Message);
			}
		}

		private void OnHighDpiClick(object sender, RoutedEventArgs e) => ShowHighDpiDialog();

		private void ShowHighDpiDialog()
		{
			try
			{
				var radioSystem = new RadioButton
				{
					Content = App.ML.Get("PropertiesHighDpiSystem"),
					GroupName = "dpi",
					IsChecked = Vm.HighDpiMode == "HIGHDPIAWARE"
				};
				var radioEnhanced = new RadioButton
				{
					Content = App.ML.Get("PropertiesHighDpiSystemEnhanced"),
					GroupName = "dpi",
					IsChecked = Vm.HighDpiMode == "PERPROCESSSYSTEMDPIFORCEON"
				};
				var radioNone = new RadioButton
				{
					Content = App.ML.Get("PropertiesHighDpiNone"),
					GroupName = "dpi",
					IsChecked = Vm.HighDpiMode == null
				};
				var panel = new StackPanel { Spacing = 8 };
				panel.Children.Add(radioSystem);
				panel.Children.Add(radioEnhanced);
				panel.Children.Add(radioNone);

				var dialog = new ContentDialog
				{
					Title = App.ML.Get("PropertiesChangeHighDpi"),
					Content = panel,
					PrimaryButtonText = App.ML.Get("CmdOk"),
					CloseButtonText = App.ML.Get("CmdCancel"),
					DefaultButton = ContentDialogButton.Primary,
					XamlRoot = XamlRoot
				};
				dialog.PrimaryButtonClick += (_, _) =>
				{
					if (radioEnhanced.IsChecked == true) Vm.SetHighDpiMode("PERPROCESSSYSTEMDPIFORCEON");
					else if (radioSystem.IsChecked == true) Vm.SetHighDpiMode("HIGHDPIAWARE");
					else Vm.SetHighDpiMode(null);
				};
				_ = dialog.ShowAsync();
			}
			catch (Exception ex)
			{
				ShowError(ex.Message);
			}
		}

		private void OnAllUsersClick(object sender, RoutedEventArgs e)
		{
			try
			{
				var dialog = new ContentDialog
				{
					Title = App.ML.Get("PropertiesChangeAllUsers"),
					Content = new TextBlock { Text = App.ML.Get("PropertiesChangeAllUsersHint"), TextWrapping = TextWrapping.Wrap },
					CloseButtonText = App.ML.Get("CmdOk"),
					XamlRoot = XamlRoot
				};
				_ = dialog.ShowAsync();
			}
			catch { }
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