using System.Collections.ObjectModel;
using System.Windows;
using MediaFileDLConverter.Helpers;
using MediaFileDLConverter.Models;
using MediaFileDLConverter.Services;

namespace MediaFileDLConverter.ViewModels
{
    /// <summary>
    /// ViewModel for the Download page. Manages link input, format selection,
    /// download mode, queue, and orchestrates downloads.
    /// </summary>
    public class DownloadPageViewModel : ObservableObject
    {
        private readonly DownloadService _downloadService;
        private readonly SettingsService _settingsService;

        private string _urlInput = string.Empty;
        private LinkType _detectedLinkType = LinkType.Unknown;
        private DownloadMode _downloadMode = DownloadMode.Single;
        private OutputFormat _selectedFormat = OutputFormat.MP3;
        private bool _isDownloading;
        private string _statusMessage = string.Empty;
        private bool _isStatusError;
        private bool _hasPlaylist;

        // Format availability flags
        private bool _isMp3Enabled;
        private bool _isWavEnabled;
        private bool _isMp4LowEnabled;
        private bool _isMp4HighEnabled;

        public string UrlInput
        {
            get => _urlInput;
            set
            {
                if (SetProperty(ref _urlInput, value))
                {
                    DetectLink();
                }
            }
        }

        public LinkType DetectedLinkType
        {
            get => _detectedLinkType;
            set
            {
                if (SetProperty(ref _detectedLinkType, value))
                {
                    OnPropertyChanged(nameof(LinkTypeDisplay));
                    OnPropertyChanged(nameof(IsLinkValid));
                }
            }
        }

        public string LinkTypeDisplay => DetectedLinkType switch
        {
            LinkType.YouTube => "YouTube",
            LinkType.SoundCloud => "SoundCloud",
            LinkType.MixCloud => "MixCloud",
            _ => ""
        };

        public bool IsLinkValid => DetectedLinkType != LinkType.Unknown;

        public DownloadMode DownloadMode
        {
            get => _downloadMode;
            set => SetProperty(ref _downloadMode, value);
        }

        public bool IsPlaylistMode
        {
            get => _downloadMode == DownloadMode.Playlist;
            set
            {
                DownloadMode = value ? DownloadMode.Playlist : DownloadMode.Single;
                OnPropertyChanged(nameof(IsPlaylistMode));
                OnPropertyChanged(nameof(IsSingleMode));
            }
        }

        public bool IsSingleMode
        {
            get => _downloadMode == DownloadMode.Single;
            set
            {
                DownloadMode = value ? DownloadMode.Single : DownloadMode.Playlist;
                OnPropertyChanged(nameof(IsSingleMode));
                OnPropertyChanged(nameof(IsPlaylistMode));
            }
        }

        public OutputFormat SelectedFormat
        {
            get => _selectedFormat;
            set => SetProperty(ref _selectedFormat, value);
        }

        public bool IsFormatMp3
        {
            get => _selectedFormat == OutputFormat.MP3;
            set { if (value) SelectedFormat = OutputFormat.MP3; OnPropertyChanged(); }
        }

        public bool IsFormatWav
        {
            get => _selectedFormat == OutputFormat.WAV;
            set { if (value) SelectedFormat = OutputFormat.WAV; OnPropertyChanged(); }
        }

        public bool IsFormatMp4Low
        {
            get => _selectedFormat == OutputFormat.MP4Low;
            set { if (value) SelectedFormat = OutputFormat.MP4Low; OnPropertyChanged(); }
        }

        public bool IsFormatMp4High
        {
            get => _selectedFormat == OutputFormat.MP4High;
            set { if (value) SelectedFormat = OutputFormat.MP4High; OnPropertyChanged(); }
        }

        public bool IsMp3Enabled
        {
            get => _isMp3Enabled;
            set => SetProperty(ref _isMp3Enabled, value);
        }

        public bool IsWavEnabled
        {
            get => _isWavEnabled;
            set => SetProperty(ref _isWavEnabled, value);
        }

        public bool IsMp4LowEnabled
        {
            get => _isMp4LowEnabled;
            set => SetProperty(ref _isMp4LowEnabled, value);
        }

        public bool IsMp4HighEnabled
        {
            get => _isMp4HighEnabled;
            set => SetProperty(ref _isMp4HighEnabled, value);
        }

        public bool HasPlaylist
        {
            get => _hasPlaylist;
            set => SetProperty(ref _hasPlaylist, value);
        }

        public bool IsDownloading
        {
            get => _isDownloading;
            set => SetProperty(ref _isDownloading, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool IsStatusError
        {
            get => _isStatusError;
            set => SetProperty(ref _isStatusError, value);
        }

        public ObservableCollection<DownloadItemViewModel> DownloadQueue { get; } = new();

        public RelayCommand DownloadCommand { get; }
        public RelayCommand ClearCompletedCommand { get; }
        public RelayCommand CancelAllCommand { get; }

        // Event to notify MainWindow about status updates
        public event Action<string, bool>? StatusUpdated;

        public DownloadPageViewModel(DownloadService downloadService, SettingsService settingsService)
        {
            _downloadService = downloadService;
            _settingsService = settingsService;

            DownloadCommand = new RelayCommand(
                async () => await StartDownloadAsync(),
                () => IsLinkValid && !IsDownloading && IsFormatSelected());

            ClearCompletedCommand = new RelayCommand(ClearCompleted);
            CancelAllCommand = new RelayCommand(CancelAll);

            // Check tool availability
            if (!_downloadService.IsYtDlpAvailable || !_downloadService.IsFfmpegAvailable)
            {
                RaiseStatus(_downloadService.GetToolStatus(), true);
            }
        }

        private bool IsFormatSelected()
        {
            return SelectedFormat switch
            {
                OutputFormat.MP3 => IsMp3Enabled,
                OutputFormat.WAV => IsWavEnabled,
                OutputFormat.MP4Low => IsMp4LowEnabled,
                OutputFormat.MP4High => IsMp4HighEnabled,
                _ => false
            };
        }

        /// <summary>
        /// Detects the link type from the current URL input and updates format availability.
        /// </summary>
        private void DetectLink()
        {
            var url = _urlInput?.Trim() ?? string.Empty;
            DetectedLinkType = LinkParserService.DetectLinkType(url);

            var allowedFormats = LinkParserService.GetAllowedFormats(DetectedLinkType);

            IsMp3Enabled = allowedFormats.Contains(OutputFormat.MP3);
            IsWavEnabled = allowedFormats.Contains(OutputFormat.WAV);
            IsMp4LowEnabled = allowedFormats.Contains(OutputFormat.MP4Low);
            IsMp4HighEnabled = allowedFormats.Contains(OutputFormat.MP4High);

            HasPlaylist = DetectedLinkType == LinkType.YouTube && LinkParserService.HasPlaylist(url);

            // Auto-select first available format if current is not available
            if (!IsFormatSelected() && allowedFormats.Length > 0)
            {
                SelectedFormat = allowedFormats[0];
                OnPropertyChanged(nameof(IsFormatMp3));
                OnPropertyChanged(nameof(IsFormatWav));
                OnPropertyChanged(nameof(IsFormatMp4Low));
                OnPropertyChanged(nameof(IsFormatMp4High));
            }

            if (IsLinkValid)
            {
                RaiseStatus($"Detected: {LinkTypeDisplay}" + (HasPlaylist ? " (Playlist)" : ""), false);
            }
            else if (!string.IsNullOrWhiteSpace(url))
            {
                RaiseStatus("Unsupported or invalid link", true);
            }
            else
            {
                RaiseStatus("", false);
            }
        }

        /// <summary>
        /// Starts the download process.
        /// </summary>
        private async Task StartDownloadAsync()
        {
            if (string.IsNullOrWhiteSpace(_urlInput))
                return;

            var url = _urlInput.Trim();
            IsDownloading = true;

            try
            {
                if (IsPlaylistMode && HasPlaylist)
                {
                    await DownloadPlaylistAsync(url);
                }
                else
                {
                    await DownloadSingleAsync(url);
                }
            }
            catch (Exception ex)
            {
                RaiseStatus($"Error: {ex.Message}", true);
            }
            finally
            {
                IsDownloading = false;
            }
        }

        /// <summary>
        /// Downloads a single item.
        /// </summary>
        private async Task DownloadSingleAsync(string url)
        {
            var item = new DownloadItemViewModel
            {
                Title = "Fetching info...",
                Url = url,
                Format = SelectedFormat,
                Status = DownloadStatus.Downloading,
                StatusText = "Starting..."
            };

            var cts = new CancellationTokenSource();
            item.CancellationSource = cts;

            Application.Current.Dispatcher.Invoke(() => DownloadQueue.Insert(0, item));

            RaiseStatus("Downloading...", false);

            var result = await _downloadService.DownloadAsync(
                url,
                SelectedFormat,
                DownloadMode.Single,
                (progress, message) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (progress >= 0)
                        {
                            item.Progress = progress;
                            item.StatusText = $"{progress:F1}%";
                        }
                        else
                        {
                            item.StatusText = message.Length > 80 ? message[..80] + "..." : message;
                        }

                        // Try to extract title from yt-dlp output
                        if (message.Contains("[download] Destination:"))
                        {
                            var titlePart = message.Replace("[download] Destination:", "").Trim();
                            var fileName = System.IO.Path.GetFileNameWithoutExtension(titlePart);
                            if (!string.IsNullOrEmpty(fileName))
                                item.Title = fileName;
                        }
                    });
                },
                cts.Token);

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (result.Success)
                {
                    item.Status = DownloadStatus.Completed;
                    item.StatusText = "Completed";
                    item.Progress = 100;
                    if (item.Title == "Fetching info...")
                        item.Title = "Download Complete";
                    RaiseStatus($"✓ Downloaded: {item.Title}", false);
                }
                else if (item.Status != DownloadStatus.Cancelled)
                {
                    item.Status = DownloadStatus.Failed;
                    item.StatusText = "Failed";
                    RaiseStatus($"✗ Error: {result.Message}", true);
                }
            });
        }

        /// <summary>
        /// Downloads all items in a playlist.
        /// </summary>
        private async Task DownloadPlaylistAsync(string url)
        {
            RaiseStatus("Fetching playlist info...", false);

            var playlistId = LinkParserService.ExtractPlaylistId(url);
            if (playlistId == null)
            {
                RaiseStatus("Could not extract playlist ID", true);
                return;
            }

            var playlistUrl = LinkParserService.BuildPlaylistUrl(playlistId, url);

            try
            {
                var items = await _downloadService.GetPlaylistItemsAsync(playlistUrl, CancellationToken.None);

                if (items.Count == 0)
                {
                    RaiseStatus("No items found in playlist", true);
                    return;
                }

                RaiseStatus($"Found {items.Count} items in playlist. Starting downloads...", false);

                foreach (var (title, itemUrl) in items)
                {
                    var item = new DownloadItemViewModel
                    {
                        Title = title,
                        Url = itemUrl,
                        Format = SelectedFormat,
                        Status = DownloadStatus.Queued,
                        StatusText = "Queued"
                    };

                    Application.Current.Dispatcher.Invoke(() => DownloadQueue.Add(item));
                }

                // Download items sequentially
                foreach (var item in DownloadQueue.ToList())
                {
                    if (item.Status != DownloadStatus.Queued)
                        continue;

                    var cts = new CancellationTokenSource();
                    item.CancellationSource = cts;
                    item.Status = DownloadStatus.Downloading;
                    item.StatusText = "Starting...";

                    RaiseStatus($"Downloading: {item.Title}", false);

                    var result = await _downloadService.DownloadAsync(
                        item.Url,
                        SelectedFormat,
                        DownloadMode.Single,
                        (progress, message) =>
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                if (progress >= 0)
                                {
                                    item.Progress = progress;
                                    item.StatusText = $"{progress:F1}%";
                                }
                                else
                                {
                                    item.StatusText = message.Length > 60 ? message[..60] + "..." : message;
                                }
                            });
                        },
                        cts.Token);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (result.Success)
                        {
                            item.Status = DownloadStatus.Completed;
                            item.StatusText = "Completed";
                            item.Progress = 100;
                        }
                        else if (item.Status != DownloadStatus.Cancelled)
                        {
                            item.Status = DownloadStatus.Failed;
                            item.StatusText = "Failed";
                        }
                    });
                }

                var completed = DownloadQueue.Count(i => i.Status == DownloadStatus.Completed);
                var failed = DownloadQueue.Count(i => i.Status == DownloadStatus.Failed);
                RaiseStatus($"Playlist done — {completed} completed, {failed} failed", failed > 0);
            }
            catch (Exception ex)
            {
                RaiseStatus($"Playlist error: {ex.Message}", true);
            }
        }

        private void ClearCompleted()
        {
            var toRemove = DownloadQueue
                .Where(i => i.Status == DownloadStatus.Completed ||
                            i.Status == DownloadStatus.Failed ||
                            i.Status == DownloadStatus.Cancelled)
                .ToList();

            foreach (var item in toRemove)
                DownloadQueue.Remove(item);
        }

        private void CancelAll()
        {
            foreach (var item in DownloadQueue.ToList())
            {
                if (item.Status == DownloadStatus.Downloading || item.Status == DownloadStatus.Queued)
                {
                    item.CancellationSource?.Cancel();
                    item.Status = DownloadStatus.Cancelled;
                    item.StatusText = "Cancelled";
                }
            }

            IsDownloading = false;
            RaiseStatus("All downloads cancelled", false);
        }

        private void RaiseStatus(string message, bool isError)
        {
            StatusMessage = message;
            IsStatusError = isError;
            StatusUpdated?.Invoke(message, isError);
        }
    }
}
