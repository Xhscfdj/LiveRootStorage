using FastFluentFilesFolders.ViewModels;
using Microsoft.UI.Xaml.Controls;
using System.IO;

namespace FastFluentFilesFolders.Views
{
	/// <summary>详细信息页：纯 XAML，数据绑定到 ViewModel。</summary>
	public sealed partial class PropertiesDetailsView : UserControl
	{
		public PropertiesDetailsViewModel Vm { get; }

		public PropertiesDetailsView(FileSystemNodeViewModel item, FileAttributes originalAttributes)
		{
			InitializeComponent();
			Vm = new PropertiesDetailsViewModel(item, originalAttributes);
			DataContext = Vm;
		}
	}
}
