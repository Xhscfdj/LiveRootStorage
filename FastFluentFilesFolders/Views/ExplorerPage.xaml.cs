using FastFluentFilesFolders.Models;
using FastFluentFilesFolders.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace FastFluentFilesFolders.Views
{
    public sealed partial class ExplorerPage : Page
    {
        private MainWindowViewModel VM => App.SharedViewModel;

        public ExplorerPage()
        {
            InitializeComponent();
            DataContext = App.SharedViewModel;

            // Ctrl+T 新建标签页
            var newTabAccel = new KeyboardAccelerator { Key = VirtualKey.T, Modifiers = VirtualKeyModifiers.Control };
            newTabAccel.Invoked += (_, args) =>
            {
                args.Handled = true;
                VM.NewTabCommand.Execute(null);
                SyncTabSelection();
            };
            RootGrid.KeyboardAccelerators.Add(newTabAccel);

            // Ctrl+W 关闭当前标签页
            var closeTabAccel = new KeyboardAccelerator { Key = VirtualKey.W, Modifiers = VirtualKeyModifiers.Control };
            closeTabAccel.Invoked += (_, args) =>
            {
                args.Handled = true;
                VM.CloseCurrentTab();
                SyncTabSelection();
            };
            RootGrid.KeyboardAccelerators.Add(closeTabAccel);

            this.Loaded += (_, _) => SyncTabSelection();
        }

        /// <summary>让 TabView 的选中项与 VM 保持一致</summary>
        private void SyncTabSelection()
        {
            if (VM.SelectedTab != null && !ReferenceEquals(TabsCtrl.SelectedItem, VM.SelectedTab))
                TabsCtrl.SelectedItem = VM.SelectedTab;
        }

        private void OnTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TabsCtrl.SelectedItem is ExplorerTab tab)
                VM.SwitchToTab(tab);
        }

        private void OnTabsCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
        {
            if (args.Item is ExplorerTab tab)
            {
                VM.CloseTab(tab);
                SyncTabSelection();
            }
        }

        private void OnTabsAddTabClick(TabView sender, object args)
        {
            VM.NewTabCommand.Execute(null);
            SyncTabSelection();
        }
    }
}