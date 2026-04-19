using MediaFileDLConverter.Helpers;
using MediaFileDLConverter.Services;

namespace MediaFileDLConverter.ViewModels
{
    /// <summary>
    /// Main ViewModel for the application window.
    /// Manages tab selection, settings panel visibility, and status bar.
    /// </summary>
    public class MainViewModel : ObservableObject
    {
        private readonly SettingsService _settingsService;
        private readonly DownloadService _downloadService;

        private int _selectedTabIndex;
        private bool _isSettingsOpen;
        private string _downloadDirectory = string.Empty;
        private string _statusBarText = "Ready";
        private bool _isStatusError;

        public DownloadPageViewModel DownloadPageVM { get; }

        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => SetProperty(ref _selectedTabIndex, value);
        }

        public bool IsSettingsOpen
        {
            get => _isSettingsOpen;
            set => SetProperty(ref _isSettingsOpen, value);
        }

        public string DownloadDirectory
        {
            get => _downloadDirectory;
            set => SetProperty(ref _downloadDirectory, value);
        }

        public string StatusBarText
        {
            get => _statusBarText;
            set => SetProperty(ref _statusBarText, value);
        }

        public bool IsStatusError
        {
            get => _isStatusError;
            set => SetProperty(ref _isStatusError, value);
        }

        public RelayCommand ToggleSettingsCommand { get; }
        public RelayCommand SaveSettingsCommand { get; }
        public RelayCommand BrowseFolderCommand { get; }

        public MainViewModel()
        {
            _settingsService = new SettingsService();
            _downloadService = new DownloadService(_settingsService);

            DownloadDirectory = _settingsService.CurrentSettings.DownloadDirectory;

            DownloadPageVM = new DownloadPageViewModel(_downloadService, _settingsService);
            DownloadPageVM.StatusUpdated += (msg, isError) =>
            {
                StatusBarText = msg;
                IsStatusError = isError;
            };

            ToggleSettingsCommand = new RelayCommand(() => IsSettingsOpen = !IsSettingsOpen);
            SaveSettingsCommand = new RelayCommand(SaveSettings);
            BrowseFolderCommand = new RelayCommand(BrowseFolder);

            // Initial status
            if (!_downloadService.IsYtDlpAvailable || !_downloadService.IsFfmpegAvailable)
            {
                StatusBarText = _downloadService.GetToolStatus();
                IsStatusError = true;
            }
        }

        private void SaveSettings()
        {
            try
            {
                _settingsService.SetDownloadDirectory(DownloadDirectory);
                _settingsService.Save();
                StatusBarText = "✓ Settings saved successfully";
                IsStatusError = false;
                IsSettingsOpen = false;
            }
            catch (Exception ex)
            {
                StatusBarText = $"✗ Failed to save settings: {ex.Message}";
                IsStatusError = true;
            }
        }

        private void BrowseFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select Download Directory",
                InitialDirectory = string.IsNullOrEmpty(DownloadDirectory)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                    : DownloadDirectory
            };

            if (dialog.ShowDialog() == true)
            {
                DownloadDirectory = dialog.FolderName;
            }
        }
    }
}
