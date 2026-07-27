using FastFluentFilesFolders.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;

namespace FastFluentFilesFolders.UserControls
{
    public sealed partial class FileOperationsButton : UserControl
    {
        public FileOperationsButton()
        {
            InitializeComponent();
        }

        public void SetItems(ObservableCollection<FileOperationItem> items)
        {
            OpsList.ItemsSource = items;
            items.CollectionChanged += (_, _) =>
            {
                var count = items.Count;
                RootBtn.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
                CountText.Text = count.ToString();
            };
        }
    }
}
