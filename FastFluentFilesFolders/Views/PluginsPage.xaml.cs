using FastFluentFilesFolders.Extensions;
using FastFluentFilesFolders.Extensions.Interfaces;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Diagnostics;
using System.Linq;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FastFluentFilesFolders.Views
{
    public sealed partial class PluginsPage : Page
    {
        public PluginsPage()
        {
            InitializeComponent();
            DataContext = App.ML;
            RefreshInstalledPlugins();
            LoadPluginSettings();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            RefreshAll();
        }

        public void RefreshAll()
        {
            RefreshInstalledPlugins();
            LoadPluginSettings();
        }

        private void RefreshInstalledPlugins()
        {
            InstalledList.Items.Clear();
            var manifest = PluginImporter.LoadManifest();
            foreach (var pkg in manifest.Plugins)
            {
                InstalledList.Items.Add($"{pkg.Name}  v{pkg.Version}  ({pkg.Id})");
            }

            InstalledSection.Visibility = manifest.Plugins.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void LoadPluginSettings()
        {
            PluginSettingsStack.Children.Clear();
            var plugins = App.PluginManager?.GetSettingsPlugins();
            if (plugins == null) return;

            bool hasItems = false;
            foreach (var plugin in plugins)
            {
                var cards = plugin.CreateSettingsCards();
                foreach (var card in cards)
                {
                    PluginSettingsStack.Children.Add(card);
                    hasItems = true;
                }
            }

            PluginSettingsSection.Visibility = hasItems
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private async void OnImportClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                var hwnd = WindowNative.GetWindowHandle(App.MainWindow!);
                InitializeWithWindow.Initialize(picker, hwnd);

                picker.SuggestedStartLocation = PickerLocationId.Downloads;
                picker.ViewMode = PickerViewMode.List;
                picker.FileTypeFilter.Add(".zip");
                picker.FileTypeFilter.Add(".lrsplug");

                var file = await picker.PickSingleFileAsync();
                if (file == null) return;

                ImportBtn.IsEnabled = false;
                ImportBtn.Content = App.ML.Get("PluginImport.Importing");

                var (success, message, package) = await App.PluginManager.ImportAndLoadPluginAsync(file.Path);

                if (success)
                {
                    RefreshAll();
                    await ShowDialogAsync(App.ML.Get("PluginImport.ImportSuccessTitle"),
                        $"{package?.Name ?? ""} v{package?.Version ?? ""}");
                }
                else
                {
                    await ShowDialogAsync(App.ML.Get("PluginImport.ImportFailedTitle"), App.ML.Get(message));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PluginsPage] Import error: {ex.Message}");
                await ShowDialogAsync(App.ML.Get("PluginImport.ImportFailedTitle"), ex.Message);
            }
            finally
            {
                ImportBtn.IsEnabled = true;
                ImportBtn.Content = App.ML.Get("PluginImportBtn");
            }
        }

        private async void OnUninstallClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string displayText) return;

            var manifest = PluginImporter.LoadManifest();
            var idx = InstalledList.Items.IndexOf(displayText);
            if (idx < 0 || idx >= manifest.Plugins.Count) return;

            var pluginId = manifest.Plugins[idx].Id;

            var dialog = new ContentDialog
            {
                Title = App.ML.Get("PluginImport.UninstallConfirmTitle"),
                Content = string.Format(App.ML.Get("PluginImport.UninstallConfirmFmt"), displayText),
                PrimaryButtonText = App.ML.Get("PluginImport.UninstallBtn"),
                CloseButtonText = App.ML.Get("PluginImport.CancelBtn"),
                XamlRoot = App.MainWindow!.Content.XamlRoot,
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            var (success, _) = await App.PluginManager.UninstallPluginAsync(pluginId);
            if (success) RefreshAll();
        }

        private async System.Threading.Tasks.Task ShowDialogAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = App.ML.CmdOk,
                XamlRoot = App.MainWindow!.Content.XamlRoot,
                DefaultButton = ContentDialogButton.Close
            };
            await dialog.ShowAsync();
        }
    }
}
