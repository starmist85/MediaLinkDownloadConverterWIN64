using System.Windows.Media;
using MediaFileDLConverter.Helpers;
using MediaFileDLConverter.Models;

namespace MediaFileDLConverter.ViewModels
{
    /// <summary>
    /// ViewModel for a single download queue item.
    /// Tracks title, progress, status, and provides a cancel command.
    /// </summary>
    public class DownloadItemViewModel : ObservableObject
    {
        private string _title = string.Empty;
        private string _url = string.Empty;
        private double _progress;
        private string _statusText = "Queued";
        private DownloadStatus _status = DownloadStatus.Queued;
        private OutputFormat _format;
        private CancellationTokenSource? _cancellationTokenSource;

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string Url
        {
            get => _url;
            set => SetProperty(ref _url, value);
        }

        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public DownloadStatus Status
        {
            get => _status;
            set
            {
                if (SetProperty(ref _status, value))
                {
                    OnPropertyChanged(nameof(StatusColor));
                    OnPropertyChanged(nameof(CanCancel));
                    OnPropertyChanged(nameof(IsActive));
                }
            }
        }

        public OutputFormat Format
        {
            get => _format;
            set => SetProperty(ref _format, value);
        }

        public string FormatLabel => Format switch
        {
            OutputFormat.MP3 => "MP3",
            OutputFormat.WAV => "WAV",
            OutputFormat.MP4Low => "MP4 (720p)",
            OutputFormat.MP4High => "MP4 (Best)",
            _ => "Unknown"
        };

        public Brush StatusColor => Status switch
        {
            DownloadStatus.Queued => new SolidColorBrush(Color.FromRgb(160, 160, 160)),
            DownloadStatus.Downloading => new SolidColorBrush(Color.FromRgb(66, 165, 245)),
            DownloadStatus.Converting => new SolidColorBrush(Color.FromRgb(255, 183, 77)),
            DownloadStatus.Completed => new SolidColorBrush(Color.FromRgb(102, 187, 106)),
            DownloadStatus.Failed => new SolidColorBrush(Color.FromRgb(239, 83, 80)),
            DownloadStatus.Cancelled => new SolidColorBrush(Color.FromRgb(189, 189, 189)),
            _ => new SolidColorBrush(Color.FromRgb(160, 160, 160))
        };

        public bool CanCancel => Status == DownloadStatus.Queued ||
                                 Status == DownloadStatus.Downloading ||
                                 Status == DownloadStatus.Converting;

        public bool IsActive => Status == DownloadStatus.Downloading ||
                                Status == DownloadStatus.Converting;

        public CancellationTokenSource? CancellationSource
        {
            get => _cancellationTokenSource;
            set => _cancellationTokenSource = value;
        }

        public RelayCommand CancelCommand { get; }

        public DownloadItemViewModel()
        {
            CancelCommand = new RelayCommand(Cancel, () => CanCancel);
        }

        private void Cancel()
        {
            _cancellationTokenSource?.Cancel();
            Status = DownloadStatus.Cancelled;
            StatusText = "Cancelled";
            Progress = 0;
        }
    }
}
