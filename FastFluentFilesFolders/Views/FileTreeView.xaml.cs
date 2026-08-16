using FastFluentFilesFolders.Services;
using FastFluentFilesFolders.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.System;

namespace FastFluentFilesFolders.Views
{
    public sealed partial class FileTreeView : Page
    {
        private MainWindowViewModel VM => App.SharedViewModel;

        public FileTreeView()
        {
            Configs configs = App.SharedViewModel.AppConfigs;
            try
            {
                InitializeComponent();
                this.DataContext = VM;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"InitializeComponent failed: {ex}");
                throw;
            }
        }

        //private void ToggleSettings(object sender, RoutedEventArgs e)
        //{
        //    VM.IsSettingsOpen = !VM.IsSettingsOpen;

        //    if (VM.IsSettingsOpen)
        //    {
        //        SettingsBtnIcon.Glyph = "\uE72B";
        //        SettingsBtnLabel.Text = "返回";
        //    }
        //    else
        //    {
        //        SettingsBtnIcon.Glyph = "\uE713";
        //        SettingsBtnLabel.Text = "设置";
        //    }
        //}

        private async void TreeView_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
        {
            Debug.WriteLine($"[TreeView_SelectionChanged] Entered. AddedItems count: {args.AddedItems.Count}");
            var selectedItem = args.AddedItems.FirstOrDefault() as FileSystemNodeViewModel;
            if (selectedItem is null)
            {
                Debug.WriteLine("[TreeView_SelectionChanged] No FileSystemNodeViewModel selected.");
                return;
            }
            await Task.Delay(50);
            Debug.WriteLine($"[TreeView_SelectionChanged] Selected item: {selectedItem.Name}, Type: {selectedItem.NodeTypeName}");
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (selectedItem is FileSystemNodeViewModel folder)
                {
                    Debug.WriteLine($"[TreeView_SelectionChanged] Setting SelectedFolder to {folder.FullPath}");
                    var vm = DataContext as MainWindowViewModel;
                    if (vm != null)
                    {
                        if (ReferenceEquals(folder, vm.SelectedFolder))
                            _ = vm.UpdateCurrentFolderContentAsync(folder, version: null);
                        else
                            vm.SelectedFolder = folder;
                    }
                }
                else if (!selectedItem.IsDirectory)
                {
                    Debug.WriteLine("[TreeView_SelectionChanged] Selected item is not a folder. Setting SelectedFolder to null.");
                }
            });
        }

        private void TreeView_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter)
                return;

            var isAltDown = ((int)Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu) & 1) != 0;
            var isCtrlDown = ((int)Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & 1) != 0;

            if (isAltDown || isCtrlDown)
                return;

            e.Handled = true;
            var treeView = sender as TreeView;
            var selectedItem = treeView?.SelectedItem as FileSystemNodeViewModel;
            if (selectedItem != null && !selectedItem.IsPlaceholder)
                VM.OpenItem(selectedItem);
        }
    }
}
