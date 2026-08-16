using FastFluentFilesFolders.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace FastFluentFilesFolders.UserControls
{
    public sealed partial class FileOperationsButton : UserControl
    {
        private ObservableCollection<FileOperationItem>? _items;

        public FileOperationsButton()
        {
            InitializeComponent();
            App.ML.PropertyChanged += OnMLPropertyChanged;
            ApplyLocalizedStrings();
        }

        public void SetItems(ObservableCollection<FileOperationItem> items)
        {
            _items = items;
            OpsList.ItemsSource = items;
            items.CollectionChanged += (_, _) => UpdateCount();
            UpdateCount();
        }

        private void UpdateCount()
        {
            if (_items == null) return;
            RootBtn.Visibility = Visibility.Visible;
            var count = _items.Count;
            CountText.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            CountText.Text = count.ToString();
            EmptyText.Visibility = count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        public void RemoveItem(FileOperationItem item)
        {
            _items?.Remove(item);
        }

        public void ClearCompleted()
        {
            if (_items == null) return;
            for (var i = _items.Count - 1; i >= 0; i--)
                if (_items[i].IsCompleted) _items.RemoveAt(i);
        }

        private void CloseItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: FileOperationItem item }) RemoveItem(item);
        }

        private void ClearCompleted_Click(object sender, RoutedEventArgs e)
        {
            ClearCompleted();
        }

        private void ApplyLocalizedStrings()
        {
            HeaderTitleText.Text = App.ML.FileOperationsTitle;
            ClearCompletedBtn.Content = App.ML.ClearCompleted;
            EmptyText.Text = App.ML.NoFileOperations;
        }

        private void OnMLPropertyChanged(object? sender, PropertyChangedEventArgs e) => ApplyLocalizedStrings();
    }
}