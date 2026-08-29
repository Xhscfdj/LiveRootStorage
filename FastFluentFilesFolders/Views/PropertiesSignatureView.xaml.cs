using FastFluentFilesFolders.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace FastFluentFilesFolders.Views
{
	/// <summary>数字签名页：纯 XAML 绑定 ViewModel；仅详细信息对话框在视图代码隐藏中。</summary>
	public sealed partial class PropertiesSignatureView : UserControl
	{
		public PropertiesSignatureViewModel Vm { get; }

		public PropertiesSignatureView(FileSystemNodeViewModel item)
		{
			InitializeComponent();
			Vm = new PropertiesSignatureViewModel(item);
			DataContext = Vm;
			SignerColumn.Header = Vm.SignerColumnHeader;
			DigestColumn.Header = Vm.DigestColumnHeader;
			TimestampColumn.Header = Vm.TimestampColumnHeader;
			CtlSignerColumn.Header = Vm.SignerColumnHeader;
			CtlDigestColumn.Header = Vm.DigestColumnHeader;
			CtlTimestampColumn.Header = Vm.TimestampColumnHeader;
		}

		private void OnEmbeddedDetailsClick(object sender, RoutedEventArgs e)
		{
			try
			{
				var dialog = new ContentDialog
				{
					Title = App.ML.Get("PropertiesSignatureDetails"),
					Content = new ScrollViewer
					{
						Content = new ItemsControl { ItemsSource = Vm.EmbeddedDetails },
						MaxHeight = 420
					},
					CloseButtonText = App.ML.Get("CmdClose"),
					XamlRoot = XamlRoot
				};
				_ = dialog.ShowAsync();
			}
			catch { }
		}
	}
}
