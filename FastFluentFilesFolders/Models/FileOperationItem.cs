using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FastFluentFilesFolders.Models
{
    public class FileOperationItem : INotifyPropertyChanged
    {
        private string _text = "";
        private double _progress;
        private int _fileCount;
        private string _process = "";
        private string _remainTime = "";
        private string _sizeText = "";

        public string Text { get => _text; set { if (_text != value) { _text = value; Notify(); } } }
        public double Progress { get => _progress; set { if (_progress != value) { _progress = value; Notify(); } } }
        public int FileCount { get => _fileCount; set { if (_fileCount != value) { _fileCount = value; Notify(); Notify(nameof(ProgressDisplay)); } } }
        public string Process { get => _process; set { if (_process != value) { _process = value; Notify(); Notify(nameof(ProgressDisplay)); } } }
        public string RemainTime { get => _remainTime; set { if (_remainTime != value) { _remainTime = value; Notify(); } } }
        public string SizeText { get => _sizeText; set { if (_sizeText != value) { _sizeText = value; Notify(); Notify(nameof(ProgressDisplay)); } } }

        public string ProgressDisplay => string.Format(
            App.ML?.FileOpProgressFmt ?? "{0} items, progress {1}, size {2}",
            FileCount, Process, SizeText);

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string name = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
