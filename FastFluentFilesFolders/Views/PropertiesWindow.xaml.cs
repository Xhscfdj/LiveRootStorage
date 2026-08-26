using FastFluentFilesFolders.ViewModels;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace FastFluentFilesFolders.Views
{
	/// <summary>
	/// 属性窗口外壳：标题栏 + 选项卡 + 底部按钮。
	/// 各选项卡内容均为独立的 XAML UserControl（Properties*View）。
	/// </summary>
	public sealed partial class PropertiesWindow : Window
	{
		private readonly FileSystemNodeViewModel _item;
		private readonly FileAttributes _originalAttributes;

		private PropertiesGeneralView? _generalView;
		private PropertiesSignatureView? _signatureView;
		private PropertiesCompatibilityView? _compatibilityView;
		private PropertiesSecurityView? _securityView;
		private PropertiesDetailsView? _detailsView;
		private PropertiesPreviousVersionsView? _previousVersionsView;

		public PropertiesWindow(FileSystemNodeViewModel item)
		{
			_item = item;
			_originalAttributes = ReadAttributes(item.FullPath);
			InitializeComponent();

			ExtendsContentIntoTitleBar = true;
			SetTitleBar(AppTitleBar);

			try
			{
				SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
			}
			catch
			{
				// 某些环境可能不支持 Mica，保持默认背景即可。
			}

			ApplyLocalization();
			CompatibilityTab.Visibility = IsExecutable(item) ? Visibility.Visible : Visibility.Collapsed;
			ShowGeneral();
		}

		private static string L(string key) => App.ML.Get(key);

		private void ApplyLocalization()
		{
			GeneralTab.Text = L("PropertiesGeneralTab");
			SignatureTab.Text = L("PropertiesSignatureTab");
			SecurityTab.Text = L("PropertiesSecurityTab");
			DetailsTab.Text = L("PropertiesDetailsTab");
			PreviousVersionsTab.Text = L("PropertiesPreviousVersionsTab");
			CompatibilityTab.Text = L("PropertiesCompatibilityTab");
			OkButton.Content = L("CmdOk");
			CancelButton.Content = L("CmdCancel");
			ApplyButton.Content = L("CmdApply");
		}

		private void OnTabsSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
		{
			if (sender.SelectedItem == GeneralTab) ShowGeneral();
			else if (sender.SelectedItem == SignatureTab) ShowSignature();
			else if (sender.SelectedItem == SecurityTab) ShowSecurity();
			else if (sender.SelectedItem == DetailsTab) ShowDetails();
			else if (sender.SelectedItem == PreviousVersionsTab) ShowPreviousVersions();
			else if (sender.SelectedItem == CompatibilityTab) ShowCompatibility();
		}

		private void SetContent(UIElement content)
		{
			ContentHost.Children.Clear();
			ContentHost.Children.Add(content);
		}

		private void ShowGeneral()
		{
			_generalView ??= new PropertiesGeneralView(_item, _originalAttributes);
			SetContent(_generalView);
		}

		private void ShowSignature()
		{
			_signatureView ??= new PropertiesSignatureView(_item);
			SetContent(_signatureView);
		}

		private void ShowSecurity()
		{
			_securityView ??= new PropertiesSecurityView(_item);
			SetContent(_securityView);
		}

		private void ShowDetails()
		{
			_detailsView ??= new PropertiesDetailsView(_item, _originalAttributes);
			SetContent(_detailsView);
		}

		private void ShowPreviousVersions()
		{
			if (_previousVersionsView == null)
				_previousVersionsView = new PropertiesPreviousVersionsView(_item);
			else
				_previousVersionsView.Refresh();
			SetContent(_previousVersionsView);
		}

		private void ShowCompatibility()
		{
			_compatibilityView ??= new PropertiesCompatibilityView(_item);
			SetContent(_compatibilityView);
		}

		private async void OnOkClick(object sender, RoutedEventArgs e)
		{
			if (await TryApplyAll())
				Close();
		}

		private async void OnApplyClick(object sender, RoutedEventArgs e)
		{
			await TryApplyAll();
		}

		private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

		private async Task<bool> TryApplyAll()
		{
			try
			{
				if (_generalView != null && !_generalView.Apply())
					return false;
				if (_compatibilityView != null && !_compatibilityView.Apply())
					return false;
				if (_securityView != null && !await _securityView.Apply())
					return false;
				return true;
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[Properties] Apply failed for '{_item.FullPath}': {ex.Message}");
				ShowError(ex.Message);
				return false;
			}
		}

		private void ShowError(string message)
		{
			try
			{
				var dialog = new ContentDialog
				{
					Title = L("CmdError"),
					Content = new TextBlock
					{
						Text = message,
						TextWrapping = TextWrapping.Wrap
					},
					CloseButtonText = L("CmdOk"),
					XamlRoot = Content?.XamlRoot
				};
				_ = dialog.ShowAsync();
			}
			catch
			{
				// 若窗口已关闭，忽略。
			}
		}

		private static bool IsExecutable(FileSystemNodeViewModel item)
		{
			return !item.IsDirectory &&
			       string.Equals(item.Extension, ".exe", StringComparison.OrdinalIgnoreCase);
		}

		private static FileAttributes ReadAttributes(string path)
		{
			try
			{
				return File.GetAttributes(path);
			}
			catch
			{
				return FileAttributes.Normal;
			}
		}
	}
}