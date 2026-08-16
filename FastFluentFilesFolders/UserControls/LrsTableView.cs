using FastFluentFilesFolders.Helpers;
using FastFluentFilesFolders.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using WinUI.TableView;
using SD = WinUI.TableView.SortDirection;
using VirtualKey = Windows.System.VirtualKey;

namespace FastFluentFilesFolders.UserControls
{
    public class LrsTableView : TableView
    {
        private GroupedFileList? _groupedSource;

        public LrsTableView()
        {
            AllowLiveShaping = false;
        }

        public void UpdateSource(ObservableCollection<FileSystemNodeViewModel> items, bool grouped)
        {
            if (_groupedSource == null)
            {
                var source = new GroupedFileList();
                _groupedSource = source;
                source.FlatListChanged += OnFlatListChanged;
                ItemsSource = source;
            }
            _groupedSource.SetItems(items, grouped);
        }

        public void SortBy(string sortPath, bool ascending)
        {
            if (_groupedSource != null && _groupedSource.Count > 0)
            {
                _groupedSource.SortWithinGroups(sortPath, ascending);
                var s = ItemsSource;
                ItemsSource = null;
                ItemsSource = s;
            }

            foreach (var col in Columns)
            {
                if (col.SortMemberPath == sortPath)
                {
                    col.SortDirection = ascending ? SD.Ascending : SD.Descending;
                }
                else
                {
                    col.SortDirection = null;
                }
            }
        }

        private void OnFlatListChanged()
        {
            var source = ItemsSource;
            ItemsSource = null;
            ItemsSource = source;
        }

        protected override void OnKeyDown(KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                var isAltDown = ((int)InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu) & 1) != 0;
                var isCtrlDown = ((int)InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & 1) != 0;
                var isShiftDown = ((int)InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & 1) != 0;

                // Alt+Enter → properties; plain Enter → open item (handled in MiddleFilesView)
                // Only let base handle Enter for cell navigation when Ctrl/Shift is held
                if (!isCtrlDown && !isShiftDown)
                {
                    return;
                }
            }

            base.OnKeyDown(e);
        }

        protected override void OnSorting(TableViewSortingEventArgs args)
        {
            if (_groupedSource == null || _groupedSource.Count == 0)
            {
                base.OnSorting(args);
                return;
            }

            var column = args.Column;
            var sortPath = column.SortMemberPath;
            if (string.IsNullOrEmpty(sortPath))
            {
                base.OnSorting(args);
                return;
            }

            SD? direction = column.SortDirection switch
            {
                null => SD.Ascending,
                SD.Ascending => SD.Descending,
                SD.Descending => null,
                _ => null
            };

            if (direction is not null)
            {
                _groupedSource.SortWithinGroups(sortPath, direction == SD.Ascending);
                column.SortDirection = direction;
            }
            else
            {
                _groupedSource.ResetSort();
                column.SortDirection = null;
            }

            var s = ItemsSource;
            ItemsSource = null;
            ItemsSource = s;
            args.Handled = true;
        }
    }
}
