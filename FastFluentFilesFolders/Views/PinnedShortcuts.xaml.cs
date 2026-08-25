using FastFluentFilesFolders.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FastFluentFilesFolders.Views
{
	public sealed partial class PinnedShortcuts : Page
	{
		private MainWindowViewModel VM => App.SharedViewModel;
		private MultiLanguageStringsViewModel ML => App.ML;

		public PinnedShortcuts()
		{
			InitializeComponent();
			DataContext = App.SharedViewModel;
		}

		private void ListView_ItemClick(object sender, ItemClickEventArgs e)
		{
			if (e.ClickedItem is FileSystemNodeViewModel node && node.IsDirectory)
			{
				// 固定栏节点是独立实例：优先复用目录树中已加载的同路径节点
				// （同目录切换时命中“父目录子项”快速路径，子项已缓存则零枚举），
				// 解析只走 O(子项数)/O(深度)，不做全树递归。
				VM.SelectedFolder = VM.FindBestNodeForNavigation(node);
			}
		}
	}
}
