using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Windows.Graphics;
using Windows.UI;
using Microsoft.UI.Xaml.Media.Imaging;

namespace TableViewRepro
{
    public sealed partial class MainWindow : Window
    {
        private readonly DispatcherQueueTimer _timer;
        private int _dataset;

        public MainWindow()
        {
            InitializeComponent();
            Title = "TableViewRepro-Swap";
            GridT.ItemContainerTransitions = new TransitionCollection();

            try { AppWindow.Resize(new SizeInt32(1200, 720)); } catch { }

            Swap();
            _timer = DispatcherQueue.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(1000);
            _timer.Tick += (_, _) => Swap();
            _timer.Start();
        }

        private void Swap()
        {
            _dataset ^= 1;
            var data = BuildDataset(_dataset);
            GridT.ItemsSource = data;
            Status.Text = "dataset=" + _dataset + " count=" + data.Count + " t=" + DateTime.Now.ToString("HH:mm:ss.fff");
        }

        private static ObservableCollection<RowItem> BuildDataset(int seed)
        {
            var rnd = new Random(seed);
            var list = new List<RowItem>();
            var now = DateTime.Now;
            var prefix = seed == 0 ? "AAAA" : "BBBB";
            for (int i = 0; i < 394; i++)
            {
                var isDir = i % 3 == 0;
                list.Add(new RowItem
                {
                    Name = prefix + "-item-" + i.ToString("D3"),
                    ModifiedText = now.AddDays(-rnd.Next(0, 30)).ToString("MM-dd HH:mm"),
                    CreatedText = now.AddDays(-rnd.Next(30, 200)).ToString("MM-dd HH:mm"),
                    SizeText = isDir ? "" : rnd.Next(1024, 999999).ToString(),
                    ShowSize = isDir ? Visibility.Collapsed : Visibility.Visible,
                    ShowCalc = isDir ? Visibility.Visible : Visibility.Collapsed,
                    Icon = null
                });
            }
            return new ObservableCollection<RowItem>(list);
        }
    }

    public class RowItem
    {
        public string Name { get; set; } = "";
        public string ModifiedText { get; set; } = "";
        public string CreatedText { get; set; } = "";
        public string SizeText { get; set; } = "";
        public Visibility ShowSize { get; set; } = Visibility.Visible;
        public Visibility ShowCalc { get; set; } = Visibility.Collapsed;
        public ImageSource? Icon { get; set; }
    }
}
