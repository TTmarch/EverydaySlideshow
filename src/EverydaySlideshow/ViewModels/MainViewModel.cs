using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EverydaySlideshow.Core;
using EverydaySlideshow.Infrastructure;
using EverydaySlideshow.Services;
using Microsoft.VisualBasic.FileIO;

namespace EverydaySlideshow.ViewModels;

public sealed record FolderRegistrationResult(string Path, string Name, bool IncludeSubfolders, bool IsPrivate);

public sealed record MonitorOption(string DeviceName, string DisplayName);

public sealed record SpeedOption(string Label, int Seconds);

public sealed record FitModeOption(string Label, FitMode Mode);

public sealed record VerticalBiasOption(string Label, VerticalPhotoBias Bias);

public sealed class MenuActionItem(string header, ICommand command, object? parameter = null) : ObservableObject
{
    private bool _isChecked;

    public string Header { get; } = header;
    public ICommand Command { get; } = command;
    public object? Parameter { get; } = parameter;

    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }
}

public sealed class MainViewModel : ObservableObject
{
    private const string PlaybackSettingsKey = "playback-settings";
    private const string DisplaySettingsKey = "display-settings";
    private const string FilterSettingsKey = "filter-settings";
    private const string ResumeStateKey = "resume-state";
    private const string CustomMoodProfilesKey = "custom-mood-profiles";

    private readonly SqliteAppDatabase _database;
    private readonly MediaScanner _scanner = new();
    private readonly SmartShuffleEngine _shuffleEngine = new();
    private readonly ThumbnailCacheService _thumbnailCache = new();
    private readonly GitHubUpdateService _updateService = new();
    private readonly DispatcherTimer _slideTimer = new();
    private readonly DispatcherTimer _overlayTimer = new();
    private readonly DispatcherTimer _idleTimer = new();
    private readonly Stack<MediaItem> _backStack = new();
    private List<MediaItem> _playlist = [];
    private PlaybackSession? _sequentialSession;
    private ShuffleState? _shuffleState;
    private CancellationTokenSource? _imageLoadCts;
    private Task? _initializeTask;
    private string _queueKey = "default";
    private MediaItem? _currentItem;
    private ImageSource? _currentImage;
    private bool _isPlayerVisible;
    private bool _isBusy;
    private bool _isPlaying;
    private bool _isOverlayVisible = true;
    private bool _isCurrentVideo;
    private DisplayModeKind? _displayModeBeforePlaybackFullscreen;
    private bool _playbackFullscreenApplied;
    private string _statusMessage = LocalizedText.Translate(AppLanguage.English, "StatusNoFolders");
    private string _scanStatus = "";
    private string _activeTitle = LocalizedText.Translate(AppLanguage.English, "PlayAll");
    private string _customSecondsText = "15";
    private int _totalItems;
    private int _currentIndex;

    public MainViewModel()
        : this(new SqliteAppDatabase(AppPaths.DatabasePath))
    {
    }

    public MainViewModel(SqliteAppDatabase database)
    {
        _database = database;
        AppPaths.EnsureCreated();

        AddFolderCommand = new AsyncRelayCommand(AddFolderAsync);
        DeleteFolderCommand = new AsyncRelayCommand(parameter => DeleteFolderAsync(parameter as FolderProfile));
        ScanAllCommand = new AsyncRelayCommand(ScanAllAsync);
        PlayAllCommand = new AsyncRelayCommand(() => StartPlaybackAsync(null, T("PlayAll")));
        PlayFavoritesCommand = new AsyncRelayCommand(() => StartPlaybackAsync(null, T("FavoritesOnly"), options => options.FavoritesOnly = true));
        PlayWatchLaterCommand = new AsyncRelayCommand(() => StartPlaybackAsync(null, T("WatchLater"), options =>
        {
            options.WatchLaterOnly = true;
            options.FamilySafeMode = false;
            options.IncludeVideos = true;
        }));
        PlayRecentlyUnseenCommand = new AsyncRelayCommand(() => StartPlaybackAsync(null, T("RecentlyUnseen"), options => options.RecentlyUnseenOnly = true));
        PlayAnniversaryCommand = new AsyncRelayCommand(() => StartPlaybackAsync(null, T("AnniversaryAroundToday"), options => options.AnniversaryAroundTodayOnly = true));
        PlayFolderCommand = new AsyncRelayCommand(parameter => StartFolderPlaybackAsync(parameter as FolderProfile));
        CreatePlaylistCommand = new AsyncRelayCommand(CreatePlaylistAsync);
        PlayPlaylistCommand = new AsyncRelayCommand(parameter => StartPlaylistPlaybackAsync(parameter as FolderPlaylist));
        DeletePlaylistCommand = new AsyncRelayCommand(parameter => DeletePlaylistAsync(parameter as FolderPlaylist));
        ApplyMoodCommand = new AsyncRelayCommand(parameter => ApplyMoodAsync(parameter?.ToString()));
        CreateCustomMoodCommand = new AsyncRelayCommand(CreateCustomMoodAsync);
        PlayCustomMoodCommand = new AsyncRelayCommand(parameter => ApplyCustomMoodAsync(parameter as CustomMoodProfile));
        DeleteCustomMoodCommand = new AsyncRelayCommand(parameter => DeleteCustomMoodAsync(parameter as CustomMoodProfile));
        TogglePauseCommand = new RelayCommand(TogglePause);
        NextCommand = new AsyncRelayCommand(() => MoveNextAsync());
        PreviousCommand = new AsyncRelayCommand(MovePreviousAsync);
        FirstCommand = new AsyncRelayCommand(MoveFirstAsync);
        BackHomeCommand = new RelayCommand(BackHome);
        ToggleFavoriteCommand = new AsyncRelayCommand(ToggleFavoriteAsync);
        HideCurrentCommand = new AsyncRelayCommand(HideCurrentAsync);
        DeleteCandidateCommand = new AsyncRelayCommand(MarkDeleteCandidateAsync);
        WatchLaterCommand = new AsyncRelayCommand(ToggleWatchLaterAsync);
        PurgeDeleteCandidatesCommand = new AsyncRelayCommand(PurgeDeleteCandidatesAsync);
        ResetHiddenCommand = new AsyncRelayCommand(ResetHiddenAsync);
        ResetDeleteCandidatesCommand = new AsyncRelayCommand(ResetDeleteCandidatesAsync);
        SetSpeedCommand = new RelayCommand(parameter => SetSpeed(parameter));
        SetFitModeCommand = new RelayCommand(parameter => SetFitMode(parameter));
        SetMonitorCommand = new RelayCommand(parameter => SetMonitor(parameter));
        ToggleFullscreenCommand = new RelayCommand(ToggleFullscreen);
        ToggleBorderlessCommand = new RelayCommand(ToggleBorderless);
        SetWindowModeCommand = new RelayCommand(SetWindowMode);
        SetLanguageCommand = new RelayCommand(SetLanguage);
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);

        RebuildLocalizedOptions();

        Folders.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Folders));
            OnPropertyChanged(nameof(HasFolders));
            RebuildFolderMenuItems();
        };
        Playlists.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Playlists));
            OnPropertyChanged(nameof(HasPlaylists));
            RebuildPlaylistMenuItems();
        };
        CustomMoodProfiles.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CustomMoodProfiles));
            OnPropertyChanged(nameof(HasCustomMoodProfiles));
            RebuildCustomMoodMenuItems();
        };

        _slideTimer.Tick += async (_, _) => await OnSlideTimerTickAsync();
        _overlayTimer.Interval = TimeSpan.FromSeconds(3);
        _overlayTimer.Tick += (_, _) =>
        {
            _overlayTimer.Stop();
            if (IsPlayerVisible)
            {
                IsOverlayVisible = false;
            }
        };

        _idleTimer.Interval = TimeSpan.FromSeconds(15);
        _idleTimer.Tick += async (_, _) => await OnIdleTimerTickAsync();
        _idleTimer.Start();
    }

    public event Func<Task<FolderRegistrationResult?>>? RequestFolderRegistration;
    public event Func<IReadOnlyList<FolderProfile>, Task<FolderPlaylist?>>? RequestPlaylistCreation;
    public event Func<CustomMoodProfile, Task<CustomMoodProfile?>>? RequestCustomMoodCreation;
    public event Func<string, string, Task<bool>>? RequestConfirmation;
    public event Action<string>? NotifyUser;
    public event Action<string>? RequestOpenUrl;
    public event Action? DisplaySettingsChanged;
    public event Action? LanguageChanged;
    public event Action? PlaybackVisualStateChanged;

    public LocalizedText Text { get; } = new();
    public ObservableCollection<FolderProfile> Folders { get; } = [];
    public ObservableCollection<FolderPlaylist> Playlists { get; } = [];
    public ObservableCollection<CustomMoodProfile> CustomMoodProfiles { get; } = [];
    public ObservableCollection<MonitorOption> Monitors { get; } = [];
    public ObservableCollection<MenuActionItem> FolderMenuItems { get; } = [];
    public ObservableCollection<MenuActionItem> PlaylistMenuItems { get; } = [];
    public ObservableCollection<MenuActionItem> CustomMoodMenuItems { get; } = [];
    public ObservableCollection<MenuActionItem> MonitorMenuItems { get; } = [];
    public ObservableCollection<SpeedOption> SpeedOptions { get; } = [];
    public ObservableCollection<FitModeOption> FitModeOptions { get; } = [];
    public ObservableCollection<VerticalBiasOption> VerticalBiasOptions { get; } = [];
    public PlaybackSettings PlaybackSettings { get; private set; } = new();
    public DisplaySettings DisplaySettings { get; private set; } = new();
    public SlideshowFilterOptions FilterOptions { get; private set; } = new();

    public AsyncRelayCommand AddFolderCommand { get; }
    public AsyncRelayCommand DeleteFolderCommand { get; }
    public AsyncRelayCommand ScanAllCommand { get; }
    public AsyncRelayCommand PlayAllCommand { get; }
    public AsyncRelayCommand PlayFavoritesCommand { get; }
    public AsyncRelayCommand PlayWatchLaterCommand { get; }
    public AsyncRelayCommand PlayRecentlyUnseenCommand { get; }
    public AsyncRelayCommand PlayAnniversaryCommand { get; }
    public AsyncRelayCommand PlayFolderCommand { get; }
    public AsyncRelayCommand CreatePlaylistCommand { get; }
    public AsyncRelayCommand PlayPlaylistCommand { get; }
    public AsyncRelayCommand DeletePlaylistCommand { get; }
    public AsyncRelayCommand ApplyMoodCommand { get; }
    public AsyncRelayCommand CreateCustomMoodCommand { get; }
    public AsyncRelayCommand PlayCustomMoodCommand { get; }
    public AsyncRelayCommand DeleteCustomMoodCommand { get; }
    public RelayCommand TogglePauseCommand { get; }
    public AsyncRelayCommand NextCommand { get; }
    public AsyncRelayCommand PreviousCommand { get; }
    public AsyncRelayCommand FirstCommand { get; }
    public RelayCommand BackHomeCommand { get; }
    public AsyncRelayCommand ToggleFavoriteCommand { get; }
    public AsyncRelayCommand HideCurrentCommand { get; }
    public AsyncRelayCommand DeleteCandidateCommand { get; }
    public AsyncRelayCommand WatchLaterCommand { get; }
    public AsyncRelayCommand PurgeDeleteCandidatesCommand { get; }
    public AsyncRelayCommand ResetHiddenCommand { get; }
    public AsyncRelayCommand ResetDeleteCandidatesCommand { get; }
    public RelayCommand SetSpeedCommand { get; }
    public RelayCommand SetFitModeCommand { get; }
    public RelayCommand SetMonitorCommand { get; }
    public RelayCommand ToggleFullscreenCommand { get; }
    public RelayCommand ToggleBorderlessCommand { get; }
    public RelayCommand SetWindowModeCommand { get; }
    public RelayCommand SetLanguageCommand { get; }
    public AsyncRelayCommand CheckForUpdatesCommand { get; }

    public MediaItem? CurrentItem
    {
        get => _currentItem;
        private set
        {
            if (SetProperty(ref _currentItem, value))
            {
                OnPropertyChanged(nameof(CurrentFileName));
                OnPropertyChanged(nameof(CurrentMetaText));
                OnPropertyChanged(nameof(CurrentIsFavorite));
                OnPropertyChanged(nameof(CurrentIsHidden));
                OnPropertyChanged(nameof(CurrentIsWatchLater));
                OnPropertyChanged(nameof(CurrentIsDeleteCandidate));
                OnPropertyChanged(nameof(HideButtonText));
                OnPropertyChanged(nameof(WatchLaterButtonText));
                OnPropertyChanged(nameof(DeleteCandidateButtonText));
                OnPropertyChanged(nameof(HasCurrentItem));
            }
        }
    }

    public ImageSource? CurrentImage
    {
        get => _currentImage;
        private set => SetProperty(ref _currentImage, value);
    }

    public bool IsPlayerVisible
    {
        get => _isPlayerVisible;
        private set
        {
            if (SetProperty(ref _isPlayerVisible, value))
            {
                OnPropertyChanged(nameof(IsHomeVisible));
            }
        }
    }

    public bool IsHomeVisible => !IsPlayerVisible;

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (SetProperty(ref _isPlaying, value))
            {
                OnPropertyChanged(nameof(PlayPauseText));
                PlaybackVisualStateChanged?.Invoke();
                ScheduleSlideTimer();
            }
        }
    }

    public bool IsOverlayVisible
    {
        get => _isOverlayVisible;
        private set => SetProperty(ref _isOverlayVisible, value);
    }

    public bool IsCurrentVideo
    {
        get => _isCurrentVideo;
        private set => SetProperty(ref _isCurrentVideo, value);
    }

    public bool HasFolders => Folders.Count > 0;
    public bool HasPlaylists => Playlists.Count > 0;
    public bool HasCustomMoodProfiles => CustomMoodProfiles.Count > 0;
    public bool HasMonitors => Monitors.Count > 0;
    public bool HasCurrentItem => CurrentItem is not null;
    public string PlayPauseText => IsPlaying ? T("Pause") : T("Resume");
    public string CurrentFileName => CurrentItem?.FileName ?? "";
    public bool CurrentIsFavorite => CurrentItem?.IsFavorite == true;
    public bool CurrentIsHidden => CurrentItem?.IsHidden == true;
    public bool CurrentIsWatchLater => CurrentItem?.IsWatchLater == true;
    public bool CurrentIsDeleteCandidate => CurrentItem?.IsDeletionCandidate == true;
    public string HideButtonText => CurrentIsHidden ? T("Unhide") : T("Hide");
    public string WatchLaterButtonText => CurrentIsWatchLater ? T("UnwatchLater") : T("WatchLater");
    public string DeleteCandidateButtonText => CurrentIsDeleteCandidate ? T("RemoveDeleteCandidate") : T("DeleteCandidate");
    public string CurrentSlideSecondsText => F("CurrentSecondsFormat", SlideSeconds);
    public AppLanguage CurrentLanguage
    {
        get => DisplaySettings.Language;
        set => ApplyLanguage(value, save: true);
    }

    public bool IsEnglishLanguage => CurrentLanguage == AppLanguage.English;

    public bool IsJapaneseLanguage => CurrentLanguage == AppLanguage.Japanese;

    public bool IsWindowMode => DisplaySettings.DisplayMode == DisplayModeKind.Window;

    public bool IsFullscreenMode => DisplaySettings.DisplayMode == DisplayModeKind.Fullscreen;

    public bool IsBorderlessMode => DisplaySettings.DisplayMode == DisplayModeKind.Borderless;

    public bool IsFitModeFit => CurrentFitMode == FitMode.Fit;

    public bool IsFitModeFill => CurrentFitMode == FitMode.Fill;

    public bool IsFitModeOriginal => CurrentFitMode == FitMode.Original;

    public bool IsFitModeBlurBackground => CurrentFitMode == FitMode.BlurBackground;

    public bool IsDefaultMoodSelected => PlaybackSettings.Mood == MoodMode.Normal;

    public bool IsWorkMoodSelected => PlaybackSettings.Mood == MoodMode.Work;

    public bool IsBedtimeMoodSelected => PlaybackSettings.Mood == MoodMode.Bedtime;

    public bool IsFamilySafeMoodSelected => PlaybackSettings.Mood == MoodMode.FamilySafe;

    public string CurrentMetaText
    {
        get
        {
            if (CurrentItem is null)
            {
                return "";
            }

            var date = CurrentItem.CapturedUtc ?? CurrentItem.ModifiedUtc;
            var kind = CurrentItem.IsVideo ? T("MediaVideo") : T("MediaPhoto");
            return $"{kind}  {date.ToLocalTime():yyyy/MM/dd}  {CurrentItem.FolderName}";
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string ScanStatus
    {
        get => _scanStatus;
        private set => SetProperty(ref _scanStatus, value);
    }

    public string ActiveTitle
    {
        get => _activeTitle;
        private set => SetProperty(ref _activeTitle, value);
    }

    public int TotalItems
    {
        get => _totalItems;
        private set => SetProperty(ref _totalItems, value);
    }

    public int CurrentIndex
    {
        get => _currentIndex;
        private set => SetProperty(ref _currentIndex, value);
    }

    public int SlideSeconds
    {
        get => PlaybackSettings.SlideSeconds;
        set
        {
            value = PlaybackSpeedCatalog.Normalize(value);
            if (PlaybackSettings.SlideSeconds == value)
            {
                return;
            }

            PlaybackSettings.SlideSeconds = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentSlideSecondsText));
            ScheduleSlideTimer();
            _ = SaveSettingsAsync();
        }
    }

    public string CustomSecondsText
    {
        get => _customSecondsText;
        set => SetProperty(ref _customSecondsText, value);
    }

    public bool Loop
    {
        get => PlaybackSettings.Loop;
        set
        {
            if (PlaybackSettings.Loop == value)
            {
                return;
            }

            PlaybackSettings.Loop = value;
            if (_sequentialSession is not null)
            {
                _sequentialSession.Loop = value;
            }

            OnPropertyChanged();
            _ = SaveSettingsAsync();
        }
    }

    public bool PauseEachPhoto
    {
        get => PlaybackSettings.PauseEachPhoto;
        set
        {
            if (PlaybackSettings.PauseEachPhoto == value)
            {
                return;
            }

            PlaybackSettings.PauseEachPhoto = value;
            OnPropertyChanged();
            ScheduleSlideTimer();
            _ = SaveSettingsAsync();
        }
    }

    public bool UseSmartShuffle
    {
        get => PlaybackSettings.Order == PlaybackOrder.SmartShuffle;
        set
        {
            PlaybackSettings.Order = value ? PlaybackOrder.SmartShuffle : PlaybackOrder.Sequential;
            OnPropertyChanged();
            _ = SaveSettingsAsync();
        }
    }

    public bool IncludeVideos
    {
        get => FilterOptions.IncludeVideos;
        set
        {
            if (FilterOptions.IncludeVideos == value)
            {
                return;
            }

            FilterOptions.IncludeVideos = value;
            OnPropertyChanged();
            _ = SaveSettingsAsync();
        }
    }

    public bool FamilySafeMode
    {
        get => FilterOptions.FamilySafeMode;
        set
        {
            if (FilterOptions.FamilySafeMode == value)
            {
                return;
            }

            FilterOptions.FamilySafeMode = value;
            OnPropertyChanged();
            _ = SaveSettingsAsync();
        }
    }

    public VerticalPhotoBias CurrentVerticalBias
    {
        get => FilterOptions.VerticalBias;
        set
        {
            if (FilterOptions.VerticalBias == value)
            {
                return;
            }

            FilterOptions.VerticalBias = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedVerticalBiasOption));
            _ = SaveSettingsAsync();
        }
    }

    public VerticalBiasOption? SelectedVerticalBiasOption
    {
        get => VerticalBiasOptions.FirstOrDefault(option => option.Bias == CurrentVerticalBias);
        set
        {
            if (value is not null)
            {
                CurrentVerticalBias = value.Bias;
            }
        }
    }

    public FitMode CurrentFitMode
    {
        get => DisplaySettings.FitMode;
        set
        {
            if (DisplaySettings.FitMode == value)
            {
                return;
            }

            DisplaySettings.FitMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedFitModeOption));
            RaiseFitModePropertiesChanged();
            _ = SaveSettingsAsync();
        }
    }

    public FitModeOption? SelectedFitModeOption
    {
        get => FitModeOptions.FirstOrDefault(option => option.Mode == CurrentFitMode);
        set
        {
            if (value is not null)
            {
                CurrentFitMode = value.Mode;
            }
        }
    }

    public double WindowOpacity
    {
        get => DisplaySettings.Opacity;
        set
        {
            value = Math.Clamp(value, 0.35, 1.0);
            if (Math.Abs(DisplaySettings.Opacity - value) < 0.001)
            {
                return;
            }

            DisplaySettings.Opacity = value;
            OnPropertyChanged();
            DisplaySettingsChanged?.Invoke();
            _ = SaveSettingsAsync();
        }
    }

    public bool TopmostMode
    {
        get => DisplaySettings.Topmost;
        set
        {
            if (DisplaySettings.Topmost == value)
            {
                return;
            }

            DisplaySettings.Topmost = value;
            OnPropertyChanged();
            DisplaySettingsChanged?.Invoke();
            _ = SaveSettingsAsync();
        }
    }

    public bool MuteVideo
    {
        get => DisplaySettings.MuteVideo;
        set
        {
            if (DisplaySettings.MuteVideo == value)
            {
                return;
            }

            DisplaySettings.MuteVideo = value;
            OnPropertyChanged();
            PlaybackVisualStateChanged?.Invoke();
            _ = SaveSettingsAsync();
        }
    }

    public bool StartSlideshowFullscreen
    {
        get => DisplaySettings.StartSlideshowFullscreen;
        set
        {
            if (DisplaySettings.StartSlideshowFullscreen == value)
            {
                return;
            }

            DisplaySettings.StartSlideshowFullscreen = value;
            OnPropertyChanged();
            _ = SaveSettingsAsync();
        }
    }

    public bool DarkMode
    {
        get => DisplaySettings.DarkMode;
        set
        {
            if (DisplaySettings.DarkMode == value)
            {
                return;
            }

            DisplaySettings.DarkMode = value;
            OnPropertyChanged();
            DisplaySettingsChanged?.Invoke();
            _ = SaveSettingsAsync();
        }
    }

    public bool AutoStartWithWindows
    {
        get => DisplaySettings.AutoStartWithWindows;
        set
        {
            if (DisplaySettings.AutoStartWithWindows == value)
            {
                return;
            }

            DisplaySettings.AutoStartWithWindows = value;
            OnPropertyChanged();
            StartupService.SetEnabled(value);
            _ = SaveSettingsAsync();
        }
    }

    public bool IdleAutoPlay
    {
        get => DisplaySettings.IdleAutoPlay;
        set
        {
            if (DisplaySettings.IdleAutoPlay == value)
            {
                return;
            }

            DisplaySettings.IdleAutoPlay = value;
            OnPropertyChanged();
            _ = SaveSettingsAsync();
        }
    }

    public int IdleAutoPlayMinutes
    {
        get => DisplaySettings.IdleAutoPlayMinutes;
        set
        {
            value = Math.Clamp(value, 1, 180);
            if (DisplaySettings.IdleAutoPlayMinutes == value)
            {
                return;
            }

            DisplaySettings.IdleAutoPlayMinutes = value;
            OnPropertyChanged();
            _ = SaveSettingsAsync();
        }
    }

    public string? SelectedMonitorDeviceName
    {
        get => DisplaySettings.MonitorDeviceName;
        set
        {
            if (DisplaySettings.MonitorDeviceName == value)
            {
                return;
            }

            DisplaySettings.MonitorDeviceName = value;
            OnPropertyChanged();
            DisplaySettingsChanged?.Invoke();
            _ = SaveSettingsAsync();
        }
    }

    public async Task InitializeAsync()
    {
        if (_initializeTask is not null)
        {
            await _initializeTask;
            return;
        }

        _initializeTask = InitializeCoreAsync();
        await _initializeTask;
    }

    public async Task RefreshLibraryListsAsync()
    {
        await _database.InitializeAsync();
        await ReloadFoldersAsync();
        await ReloadPlaylistsAsync();
        UpdateLibraryStatusMessage();
    }

    private async Task InitializeCoreAsync()
    {
        await _database.InitializeAsync();
        PlaybackSettings = await _database.LoadSettingAsync(PlaybackSettingsKey, new PlaybackSettings());
        DisplaySettings = await _database.LoadSettingAsync(DisplaySettingsKey, new DisplaySettings());
        FilterOptions = await _database.LoadSettingAsync(FilterSettingsKey, new SlideshowFilterOptions());
        ApplyLanguage(DisplaySettings.Language, save: false);
        CustomMoodProfiles.Clear();
        foreach (var profile in await _database.LoadSettingAsync(CustomMoodProfilesKey, new List<CustomMoodProfile>()))
        {
            CustomMoodProfiles.Add(profile);
        }

        DisplaySettings.AutoStartWithWindows = StartupService.IsEnabled();
        CustomSecondsText = PlaybackSettings.CustomSlideSeconds.ToString();

        await ReloadFoldersAsync();
        await ReloadPlaylistsAsync();
        RaiseSettingsPropertiesChanged();
        OnPropertyChanged(nameof(HasCustomMoodProfiles));
        UpdateLibraryStatusMessage();
        UpdateMoodSelectionStates();
        DisplaySettingsChanged?.Invoke();
    }

    public void SetMonitors(IEnumerable<MonitorOption> monitors)
    {
        Monitors.Clear();
        foreach (var monitor in monitors)
        {
            Monitors.Add(monitor);
        }

        RebuildMonitorMenuItems();
        OnPropertyChanged(nameof(HasMonitors));

        if (DisplaySettings.MonitorDeviceName is null && Monitors.Count > 0)
        {
            DisplaySettings.MonitorDeviceName = Monitors[0].DeviceName;
            OnPropertyChanged(nameof(SelectedMonitorDeviceName));
        }
    }

    public void ShowOverlayTemporarily()
    {
        IsOverlayVisible = true;
        _overlayTimer.Stop();
        if (IsPlayerVisible)
        {
            _overlayTimer.Start();
        }
    }

    public async Task OnVideoEndedAsync()
    {
        if (IsPlayerVisible && CurrentItem?.IsVideo == true)
        {
            await MoveNextAsync();
        }
    }

    public void ResumeAfterWake()
    {
        if (DisplaySettings.ResumeAfterWake && IsPlayerVisible)
        {
            IsPlaying = true;
            StatusMessage = T("StatusWakeResume");
        }
    }

    public void SaveWindowPlacement(double left, double top, double width, double height, bool maximized)
    {
        if (width < 480 || height < 320)
        {
            return;
        }

        DisplaySettings.WindowLeft = left;
        DisplaySettings.WindowTop = top;
        DisplaySettings.WindowWidth = width;
        DisplaySettings.WindowHeight = height;
        DisplaySettings.WindowMaximized = maximized;
        _database.SaveSettingAsync(DisplaySettingsKey, DisplaySettings).GetAwaiter().GetResult();
    }

    private string T(string key) => LocalizedText.Translate(DisplaySettings.Language, key);

    private string F(string key, params object?[] args) => LocalizedText.Format(DisplaySettings.Language, key, args);

    private void RebuildLocalizedOptions()
    {
        SpeedOptions.Clear();
        foreach (var seconds in new[] { 1, 2, 3, 5, 10, 30, 60, 300 })
        {
            var label = seconds < 60 ? F("SecondsOption", seconds) : F("MinutesOption", seconds / 60);
            SpeedOptions.Add(new SpeedOption(label, seconds));
        }

        FitModeOptions.Clear();
        FitModeOptions.Add(new FitModeOption(T("Fit"), FitMode.Fit));
        FitModeOptions.Add(new FitModeOption(T("Fill"), FitMode.Fill));
        FitModeOptions.Add(new FitModeOption(T("Original"), FitMode.Original));
        FitModeOptions.Add(new FitModeOption(T("BlurBackground"), FitMode.BlurBackground));

        VerticalBiasOptions.Clear();
        VerticalBiasOptions.Add(new VerticalBiasOption(T("NoPreference"), VerticalPhotoBias.Normal));
        VerticalBiasOptions.Add(new VerticalBiasOption(T("MorePortraits"), VerticalPhotoBias.Prefer));
        VerticalBiasOptions.Add(new VerticalBiasOption(T("FewerPortraits"), VerticalPhotoBias.Avoid));
    }

    private void SetLanguage(object? parameter)
    {
        if (parameter is AppLanguage language)
        {
            CurrentLanguage = language;
            return;
        }

        if (parameter is string value && Enum.TryParse<AppLanguage>(value, out var parsed))
        {
            CurrentLanguage = parsed;
        }
    }

    private void ApplyLanguage(AppLanguage language, bool save)
    {
        var fitMode = CurrentFitMode;
        var verticalBias = CurrentVerticalBias;
        var changed = DisplaySettings.Language != language || Text.Language != language;
        DisplaySettings.Language = language;
        Text.Use(language);
        RebuildLocalizedOptions();
        DisplaySettings.FitMode = fitMode;
        FilterOptions.VerticalBias = verticalBias;
        RaiseLanguagePropertiesChanged();
        if (!IsPlayerVisible)
        {
            UpdateLibraryStatusMessage();
        }

        if (changed)
        {
            LanguageChanged?.Invoke();
        }

        if (save)
        {
            _ = SaveSettingsAsync();
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        StatusMessage = T("StatusCheckingUpdates");
        var result = await _updateService.CheckLatestAsync(
            AppReleaseInfo.RepositoryOwner,
            AppReleaseInfo.RepositoryName,
            AppReleaseInfo.CurrentVersion);

        StatusMessage = result.Status switch
        {
            UpdateCheckStatus.UpdateAvailable => F("StatusUpdateAvailable", result.LatestTag ?? result.LatestVersion?.ToString() ?? ""),
            UpdateCheckStatus.UpToDate => F("StatusUpToDate", result.LatestTag ?? result.LatestVersion?.ToString() ?? AppReleaseInfo.CurrentVersion.ToString(3)),
            UpdateCheckStatus.NotConfigured => T("StatusUpdateNotConfigured"),
            UpdateCheckStatus.NoReleaseFound => T("StatusNoReleaseFound"),
            UpdateCheckStatus.InvalidResponse => T("StatusUpdateInvalidResponse"),
            _ => T("StatusUpdateNetworkError")
        };

        NotifyUser?.Invoke(StatusMessage);
        if (result.HasUpdate && !string.IsNullOrWhiteSpace(result.ReleaseUrl))
        {
            RequestOpenUrl?.Invoke(result.ReleaseUrl);
        }
    }

    private async Task AddFolderAsync()
    {
        if (RequestFolderRegistration is null)
        {
            return;
        }

        var registration = await RequestFolderRegistration.Invoke();
        if (registration is null)
        {
            return;
        }

        var existing = Folders.FirstOrDefault(folder =>
            string.Equals(folder.Path.TrimEnd('\\'), registration.Path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));

        var folderProfile = existing ?? new FolderProfile();
        folderProfile.Path = registration.Path;
        folderProfile.Name = string.IsNullOrWhiteSpace(registration.Name)
            ? new DirectoryInfo(registration.Path).Name
            : registration.Name.Trim();
        folderProfile.IncludeSubfolders = registration.IncludeSubfolders;
        folderProfile.IsPrivate = registration.IsPrivate;
        folderProfile.IsEnabled = true;

        await _database.UpsertFolderAsync(folderProfile);
        await ReloadFoldersAsync();
        await ScanFolderAsync(folderProfile);
        StatusMessage = F("StatusFolderAdded", folderProfile.Name);
    }

    private async Task DeleteFolderAsync(FolderProfile? folder)
    {
        if (folder is null)
        {
            return;
        }

        var confirmed = RequestConfirmation is not null
            && await RequestConfirmation.Invoke(
                T("ConfirmDeleteFolderTitle"),
                F("ConfirmDeleteFolderMessage", folder.Name));
        if (!confirmed)
        {
            return;
        }

        await _database.DeleteFolderAsync(folder.Id);
        await ReloadFoldersAsync();
        await ReloadPlaylistsAsync();
        StatusMessage = F("StatusFolderRemoved", folder.Name);
    }

    private async Task ReloadFoldersAsync()
    {
        Folders.Clear();
        var folders = await _database.GetFoldersAsync();
        foreach (var folder in folders)
        {
            Folders.Add(folder);
        }

        OnPropertyChanged(nameof(Folders));
        OnPropertyChanged(nameof(HasFolders));
    }

    private async Task ReloadPlaylistsAsync()
    {
        Playlists.Clear();
        var playlists = await _database.GetPlaylistsAsync();
        foreach (var playlist in playlists)
        {
            Playlists.Add(playlist);
        }

        OnPropertyChanged(nameof(Playlists));
        OnPropertyChanged(nameof(HasPlaylists));
    }

    private void RebuildFolderMenuItems()
    {
        FolderMenuItems.Clear();
        foreach (var folder in Folders)
        {
            FolderMenuItems.Add(new MenuActionItem(folder.Name, PlayFolderCommand, folder));
        }
    }

    private void RebuildPlaylistMenuItems()
    {
        PlaylistMenuItems.Clear();
        foreach (var playlist in Playlists)
        {
            PlaylistMenuItems.Add(new MenuActionItem(playlist.Name, PlayPlaylistCommand, playlist));
        }
    }

    private void RebuildCustomMoodMenuItems()
    {
        CustomMoodMenuItems.Clear();
        foreach (var profile in CustomMoodProfiles)
        {
            CustomMoodMenuItems.Add(new MenuActionItem(profile.Name, PlayCustomMoodCommand, profile)
            {
                IsChecked = IsSelectedCustomMood(profile)
            });
        }
    }

    private bool IsSelectedCustomMood(CustomMoodProfile profile)
        => PlaybackSettings.Mood == MoodMode.Custom
           && string.Equals(PlaybackSettings.CustomMoodProfileId, profile.Id, StringComparison.OrdinalIgnoreCase);

    private void UpdateMoodSelectionStates()
    {
        RaiseMoodPropertiesChanged();
        foreach (var item in CustomMoodMenuItems)
        {
            item.IsChecked = item.Parameter is CustomMoodProfile profile && IsSelectedCustomMood(profile);
        }
    }

    private void RebuildMonitorMenuItems()
    {
        MonitorMenuItems.Clear();
        foreach (var monitor in Monitors)
        {
            MonitorMenuItems.Add(new MenuActionItem(monitor.DisplayName, SetMonitorCommand, monitor.DeviceName));
        }
    }

    private void UpdateLibraryStatusMessage()
    {
        if (Folders.Count == 0)
        {
            StatusMessage = T("StatusNoFolders");
            return;
        }

        StatusMessage = Playlists.Count == 0
            ? F("StatusFoldersLoaded", Folders.Count)
            : F("StatusFoldersAndPlaylistsLoaded", Folders.Count, Playlists.Count);
    }

    private async Task ScanAllAsync()
    {
        if (Folders.Count == 0)
        {
            StatusMessage = T("StatusAddFolderFirst");
            return;
        }

        foreach (var folder in Folders.Where(folder => folder.IsEnabled))
        {
            await ScanFolderAsync(folder);
        }
    }

    private async Task ScanFolderAsync(FolderProfile folder)
    {
        try
        {
            IsBusy = true;
            ScanStatus = F("ScanLoading", folder.Name);
            var progress = new Progress<ScanProgress>(scan =>
            {
                ScanStatus = F("ScanCount", folder.Name, scan.FoundCount);
            });
            var items = await _scanner.ScanFolderAsync(folder, progress);
            await _database.UpsertMediaItemsAsync(items);
            ScanStatus = F("ScanSaved", folder.Name, items.Count);

            var imagePaths = items.Where(item => item.IsImage).Select(item => item.Path).ToList();
            _ = Task.Run(async () =>
            {
                await _thumbnailCache.WarmAsync(imagePaths);
                foreach (var path in imagePaths.Take(80))
                {
                    var metadata = await BitmapMediaLoader.LoadImageAsync(path, decodePixelWidth: 48);
                    if (metadata.Width.HasValue || metadata.CapturedUtc.HasValue)
                    {
                        await _database.UpdateMediaMetadataAsync(path, metadata.Width, metadata.Height, metadata.CapturedUtc);
                    }
                }
            });
        }
        catch (OperationCanceledException)
        {
            ScanStatus = T("ScanCancelled");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task StartFolderPlaybackAsync(FolderProfile? folder)
        => folder is null
            ? Task.CompletedTask
            : StartPlaybackAsync([folder.Id], folder.Name);

    private async Task CreatePlaylistAsync()
    {
        if (Folders.Count == 0)
        {
            StatusMessage = T("StatusPlaylistNeedsFolder");
            return;
        }

        if (RequestPlaylistCreation is null)
        {
            return;
        }

        var playlist = await RequestPlaylistCreation.Invoke(Folders.ToList());
        if (playlist is null || playlist.FolderIds.Count == 0)
        {
            return;
        }

        playlist.Name = string.IsNullOrWhiteSpace(playlist.Name) ? T("DefaultPlaylistName") : playlist.Name.Trim();
        playlist.UpdatedUtc = DateTimeOffset.UtcNow;
        await _database.UpsertPlaylistAsync(playlist);
        await ReloadPlaylistsAsync();
        StatusMessage = F("StatusPlaylistCreated", playlist.Name);
    }

    private Task StartPlaylistPlaybackAsync(FolderPlaylist? playlist)
    {
        return playlist is null || playlist.FolderIds.Count == 0
            ? Task.CompletedTask
            : StartPlaybackAsync(playlist.FolderIds, playlist.Name);
    }

    private async Task DeletePlaylistAsync(FolderPlaylist? playlist)
    {
        if (playlist is null)
        {
            return;
        }

        var confirmed = RequestConfirmation is not null
            && await RequestConfirmation.Invoke(T("ConfirmDeletePlaylistTitle"), F("ConfirmDeletePlaylistMessage", playlist.Name));
        if (!confirmed)
        {
            return;
        }

        await _database.DeletePlaylistAsync(playlist.Id);
        await ReloadPlaylistsAsync();
        StatusMessage = F("StatusPlaylistDeleted", playlist.Name);
    }

    private async Task StartPlaybackAsync(IEnumerable<string>? folderIds, string title, Action<SlideshowFilterOptions>? configure = null)
    {
        if (Folders.Count == 0)
        {
            StatusMessage = T("StatusStartAddFolderFirst");
            return;
        }

        IsBusy = true;
        try
        {
            var selectedFolderIds = folderIds?.ToList();
            var items = await _database.GetMediaItemsAsync(selectedFolderIds);
            if (items.Count == 0)
            {
                var foldersToScan = selectedFolderIds is null
                    ? Folders.Where(folder => folder.IsEnabled).ToList()
                    : Folders.Where(folder => selectedFolderIds.Contains(folder.Id)).ToList();
                foreach (var folder in foldersToScan)
                {
                    await ScanFolderAsync(folder);
                }

                items = await _database.GetMediaItemsAsync(selectedFolderIds);
            }

            var options = CopyFilterOptions();
            configure?.Invoke(options);
            options.Now = DateTimeOffset.Now;
            _playlist = SlideshowFilterService.Apply(items, options)
                .Where(item => File.Exists(item.Path))
                .ToList();

            if (_playlist.Count == 0)
            {
                IsPlayerVisible = false;
                StatusMessage = T("StatusNoPlayableMedia");
                return;
            }

            ActiveTitle = title;
            TotalItems = _playlist.Count;
            CurrentIndex = 0;
            _backStack.Clear();
            _sequentialSession = new PlaybackSession(_playlist, PlaybackSettings.Loop);
            _queueKey = BuildQueueKey(selectedFolderIds, title, options);
            _shuffleState = await _database.LoadShuffleStateAsync(_queueKey);
            IsPlayerVisible = true;
            ApplyPlaybackStartDisplayMode();
            IsOverlayVisible = true;
            IsPlaying = true;

            var resume = await _database.LoadSettingAsync(ResumeStateKey, new PlaybackResumeState());
            MediaItem? startItem = null;
            if (PlaybackSettings.ResumeLastPosition && !string.IsNullOrWhiteSpace(resume.LastMediaPath))
            {
                startItem = _playlist.FirstOrDefault(item =>
                    string.Equals(item.Path, resume.LastMediaPath, StringComparison.OrdinalIgnoreCase));
            }

            if (startItem is not null)
            {
                _sequentialSession.ResumeFromPath(startItem.Path);
                await SetCurrentItemAsync(startItem);
            }
            else
            {
                await MoveNextAsync(pushHistory: false);
            }

            if (selectedFolderIds is not null)
            {
                foreach (var folderId in selectedFolderIds)
                {
                    await _database.TouchFolderPlayedAsync(folderId);
                }
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyMoodAsync(string? moodName)
    {
        var mood = moodName switch
        {
            "Default" => MoodMode.Normal,
            "Work" => MoodMode.Work,
            "Bedtime" => MoodMode.Bedtime,
            "FamilySafe" => MoodMode.FamilySafe,
            _ => MoodMode.Normal
        };

        PlaybackSettings.Mood = mood;
        PlaybackSettings.CustomMoodProfileId = null;
        switch (mood)
        {
            case MoodMode.Work:
                SlideSeconds = 30;
                WindowOpacity = 0.86;
                MuteVideo = true;
                IncludeVideos = true;
                FamilySafeMode = false;
                StatusMessage = T("MoodWorkSelected");
                break;
            case MoodMode.Bedtime:
                SlideSeconds = 60;
                WindowOpacity = 0.72;
                MuteVideo = true;
                IncludeVideos = true;
                FamilySafeMode = false;
                StatusMessage = T("MoodBedtimeSelected");
                break;
            case MoodMode.FamilySafe:
                SlideSeconds = 5;
                WindowOpacity = 1.0;
                MuteVideo = true;
                IncludeVideos = true;
                FamilySafeMode = true;
                CurrentVerticalBias = VerticalPhotoBias.Normal;
                StatusMessage = T("MoodSafeSelected");
                break;
            default:
                SlideSeconds = 5;
                WindowOpacity = 1.0;
                MuteVideo = false;
                IncludeVideos = true;
                FamilySafeMode = false;
                CurrentVerticalBias = VerticalPhotoBias.Normal;
                StatusMessage = T("MoodDefaultSelected");
                break;
        }

        UpdateMoodSelectionStates();
        await SaveSettingsAsync();
    }

    private async Task CreateCustomMoodAsync()
    {
        if (RequestCustomMoodCreation is null)
        {
            return;
        }

        var draft = new CustomMoodProfile
        {
            Name = F("CustomMoodDraftNameFormat", CustomMoodProfiles.Count + 1),
            SlideSeconds = SlideSeconds,
            Opacity = WindowOpacity,
            MuteVideo = MuteVideo,
            IncludeVideos = IncludeVideos,
            FamilySafeMode = FamilySafeMode,
            VerticalBias = CurrentVerticalBias
        };

        var profile = await RequestCustomMoodCreation.Invoke(draft);
        if (profile is null)
        {
            return;
        }

        profile.Name = string.IsNullOrWhiteSpace(profile.Name) ? T("CustomMoodFallbackName") : profile.Name.Trim();
        profile.SlideSeconds = PlaybackSpeedCatalog.Normalize(profile.SlideSeconds);
        profile.Opacity = Math.Clamp(profile.Opacity, 0.35, 1.0);
        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            profile.Id = Guid.NewGuid().ToString("N");
        }

        CustomMoodProfiles.Add(profile);
        await SaveCustomMoodProfilesAsync();
        OnPropertyChanged(nameof(HasCustomMoodProfiles));
        UpdateMoodSelectionStates();
        StatusMessage = F("StatusCustomMoodCreated", profile.Name);
    }

    private async Task ApplyCustomMoodAsync(CustomMoodProfile? profile)
    {
        if (profile is null)
        {
            return;
        }

        PlaybackSettings.Mood = MoodMode.Custom;
        PlaybackSettings.CustomMoodProfileId = profile.Id;
        SlideSeconds = profile.SlideSeconds;
        WindowOpacity = profile.Opacity;
        MuteVideo = profile.MuteVideo;
        IncludeVideos = profile.IncludeVideos;
        FamilySafeMode = profile.FamilySafeMode;
        CurrentVerticalBias = profile.VerticalBias;
        StatusMessage = F("StatusCustomMoodSelected", profile.Name);
        UpdateMoodSelectionStates();
        await SaveSettingsAsync();
    }

    private async Task DeleteCustomMoodAsync(CustomMoodProfile? profile)
    {
        if (profile is null)
        {
            return;
        }

        CustomMoodProfiles.Remove(profile);
        if (IsSelectedCustomMood(profile))
        {
            PlaybackSettings.Mood = MoodMode.Normal;
            PlaybackSettings.CustomMoodProfileId = null;
            UpdateMoodSelectionStates();
            await SaveSettingsAsync();
        }

        await SaveCustomMoodProfilesAsync();
        OnPropertyChanged(nameof(HasCustomMoodProfiles));
        UpdateMoodSelectionStates();
        StatusMessage = F("StatusCustomMoodDeleted", profile.Name);
    }

    private async Task MoveNextAsync(bool pushHistory = true)
    {
        if (_playlist.Count == 0)
        {
            return;
        }

        var previous = CurrentItem;
        MediaItem? next = null;

        if (PlaybackSettings.Order == PlaybackOrder.Sequential)
        {
            _sequentialSession ??= new PlaybackSession(_playlist, PlaybackSettings.Loop);
            next = CurrentItem is null ? _sequentialSession.Current : _sequentialSession.MoveNext();
            if (next is null)
            {
                IsPlaying = false;
                StatusMessage = T("StatusReachedEnd");
                return;
            }
        }
        else
        {
            var pick = _shuffleEngine.PickNext(
                _playlist,
                _shuffleState,
                new SmartShuffleOptions
                {
                    Now = DateTimeOffset.UtcNow,
                    VerticalBias = FilterOptions.VerticalBias
                },
                _queueKey);
            _shuffleState = pick.State;
            next = pick.Item;
            await _database.SaveShuffleStateAsync(_shuffleState);
        }

        if (next is null)
        {
            return;
        }

        if (pushHistory && previous is not null)
        {
            _backStack.Push(previous);
        }

        await SetCurrentItemAsync(next);
    }

    private async Task MovePreviousAsync()
    {
        if (_backStack.Count > 0)
        {
            await SetCurrentItemAsync(_backStack.Pop());
            return;
        }

        if (PlaybackSettings.Order == PlaybackOrder.Sequential)
        {
            _sequentialSession ??= new PlaybackSession(_playlist, PlaybackSettings.Loop);
            await SetCurrentItemAsync(_sequentialSession.MovePrevious() ?? _sequentialSession.Current);
        }
    }

    private async Task MoveFirstAsync()
    {
        if (_playlist.Count == 0)
        {
            return;
        }

        _backStack.Clear();
        if (PlaybackSettings.Order == PlaybackOrder.Sequential)
        {
            _sequentialSession ??= new PlaybackSession(_playlist, PlaybackSettings.Loop);
            await SetCurrentItemAsync(_sequentialSession.MoveFirst());
            return;
        }

        _shuffleState = new ShuffleState { QueueKey = _queueKey };
        await MoveNextAsync(pushHistory: false);
    }

    private async Task SetCurrentItemAsync(MediaItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (!File.Exists(item.Path))
        {
            await MoveNextAsync(pushHistory: false);
            return;
        }

        _imageLoadCts?.Cancel();
        CurrentItem = item;
        CurrentIndex = Math.Max(1, _playlist.FindIndex(candidate =>
            string.Equals(candidate.Path, item.Path, StringComparison.OrdinalIgnoreCase)) + 1);
        IsCurrentVideo = item.IsVideo;
        if (item.IsVideo)
        {
            ClearDisplayedImages();
        }
        await _database.RecordPlaybackAsync(item.Path, item.FolderId, completed: false);
        await _database.SaveSettingAsync(ResumeStateKey, new PlaybackResumeState
        {
            LastFolderId = item.FolderId,
            LastMediaPath = item.Path,
            UpdatedUtc = DateTimeOffset.UtcNow
        });

        if (item.IsImage)
        {
            _imageLoadCts = new CancellationTokenSource();
            var token = _imageLoadCts.Token;
            var result = await BitmapMediaLoader.LoadImageAsync(item.Path, cancellationToken: token);
            if (token.IsCancellationRequested || CurrentItem?.Path != item.Path)
            {
                return;
            }

            if (result.Source is null)
            {
                StatusMessage = F("StatusSkippedUnreadable", item.FileName);
                await MoveNextAsync(pushHistory: false);
                return;
            }

            CurrentImage = result.Source;
            item.Width = result.Width;
            item.Height = result.Height;
            item.CapturedUtc ??= result.CapturedUtc;
            await _database.UpdateMediaMetadataAsync(item.Path, result.Width, result.Height, result.CapturedUtc);
        }

        ScheduleSlideTimer();
        PlaybackVisualStateChanged?.Invoke();
    }

    private async Task OnSlideTimerTickAsync()
    {
        _slideTimer.Stop();
        if (IsPlaying && CurrentItem?.IsImage == true && !PlaybackSettings.PauseEachPhoto)
        {
            await MoveNextAsync();
        }
    }

    private async Task OnIdleTimerTickAsync()
    {
        if (!DisplaySettings.IdleAutoPlay || IsPlayerVisible || IsBusy || Folders.Count == 0)
        {
            return;
        }

        if (IdleService.GetIdleTime() >= TimeSpan.FromMinutes(DisplaySettings.IdleAutoPlayMinutes))
        {
            await StartPlaybackAsync(null, T("IdleAutoPlayTitle"));
        }
    }

    private void ScheduleSlideTimer()
    {
        _slideTimer.Stop();
        if (!IsPlaying || CurrentItem?.IsImage != true || PlaybackSettings.PauseEachPhoto)
        {
            return;
        }

        _slideTimer.Interval = TimeSpan.FromSeconds(PlaybackSettings.SlideSeconds);
        _slideTimer.Start();
    }

    private void TogglePause()
    {
        IsPlaying = !IsPlaying;
    }

    private async Task ToggleFavoriteAsync()
    {
        if (CurrentItem is null)
        {
            return;
        }

        CurrentItem.IsFavorite = !CurrentItem.IsFavorite;
        await _database.SetMediaFlagAsync(CurrentItem.Path, nameof(MediaItem.IsFavorite), CurrentItem.IsFavorite);
        OnPropertyChanged(nameof(CurrentItem));
        OnPropertyChanged(nameof(CurrentIsFavorite));
    }

    private async Task HideCurrentAsync()
    {
        if (CurrentItem is null)
        {
            return;
        }

        CurrentItem.IsHidden = !CurrentItem.IsHidden;
        await _database.SetMediaFlagAsync(CurrentItem.Path, nameof(MediaItem.IsHidden), CurrentItem.IsHidden);
        OnPropertyChanged(nameof(CurrentIsHidden));
        OnPropertyChanged(nameof(HideButtonText));
        StatusMessage = CurrentItem.IsHidden ? T("StatusHidden") : T("StatusUnhidden");
    }

    private async Task MarkDeleteCandidateAsync()
    {
        if (CurrentItem is null)
        {
            return;
        }

        CurrentItem.IsDeletionCandidate = !CurrentItem.IsDeletionCandidate;
        await _database.SetMediaFlagAsync(CurrentItem.Path, nameof(MediaItem.IsDeletionCandidate), CurrentItem.IsDeletionCandidate);
        OnPropertyChanged(nameof(CurrentIsDeleteCandidate));
        OnPropertyChanged(nameof(DeleteCandidateButtonText));
        StatusMessage = CurrentItem.IsDeletionCandidate ? T("StatusDeleteCandidateAdded") : T("StatusDeleteCandidateRemoved");
    }

    private async Task ToggleWatchLaterAsync()
    {
        if (CurrentItem is null)
        {
            return;
        }

        CurrentItem.IsWatchLater = !CurrentItem.IsWatchLater;
        await _database.SetMediaFlagAsync(CurrentItem.Path, nameof(MediaItem.IsWatchLater), CurrentItem.IsWatchLater);
        OnPropertyChanged(nameof(CurrentIsWatchLater));
        OnPropertyChanged(nameof(WatchLaterButtonText));
        StatusMessage = CurrentItem.IsWatchLater ? T("StatusWatchLaterAdded") : T("StatusWatchLaterRemoved");
    }

    private async Task PurgeDeleteCandidatesAsync()
    {
        var candidates = (await _database.GetMediaItemsAsync())
            .Where(item => item.IsDeletionCandidate && File.Exists(item.Path))
            .ToList();

        if (candidates.Count == 0)
        {
            StatusMessage = T("StatusNoDeleteCandidates");
            return;
        }

        var confirmed = RequestConfirmation is not null
            && await RequestConfirmation.Invoke(T("ConfirmPurgeDeleteTitle"), F("ConfirmPurgeDeleteMessage", candidates.Count));
        if (!confirmed)
        {
            return;
        }

        var deleted = 0;
        foreach (var item in candidates)
        {
            try
            {
                FileSystem.DeleteFile(item.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                deleted++;
            }
            catch
            {
                // Leave the tag in place so the user can retry after checking the file.
            }
        }

        StatusMessage = F("StatusMovedToRecycleBin", deleted);
        NotifyUser?.Invoke(StatusMessage);
    }

    private async Task ResetHiddenAsync()
    {
        var confirmed = RequestConfirmation is not null
            && await RequestConfirmation.Invoke(T("ConfirmResetHiddenTitle"), T("ConfirmResetHiddenMessage"));
        if (!confirmed)
        {
            return;
        }

        var changed = await _database.ClearMediaFlagAsync(nameof(MediaItem.IsHidden));
        foreach (var item in _playlist)
        {
            item.IsHidden = false;
        }

        if (CurrentItem is not null)
        {
            CurrentItem.IsHidden = false;
            OnPropertyChanged(nameof(CurrentIsHidden));
            OnPropertyChanged(nameof(HideButtonText));
        }

        StatusMessage = F("StatusResetHidden", changed);
        NotifyUser?.Invoke(StatusMessage);
    }

    private async Task ResetDeleteCandidatesAsync()
    {
        var confirmed = RequestConfirmation is not null
            && await RequestConfirmation.Invoke(T("ConfirmResetDeleteTitle"), T("ConfirmResetDeleteMessage"));
        if (!confirmed)
        {
            return;
        }

        var changed = await _database.ClearMediaFlagAsync(nameof(MediaItem.IsDeletionCandidate));
        foreach (var item in _playlist)
        {
            item.IsDeletionCandidate = false;
        }

        if (CurrentItem is not null)
        {
            CurrentItem.IsDeletionCandidate = false;
            OnPropertyChanged(nameof(CurrentIsDeleteCandidate));
            OnPropertyChanged(nameof(DeleteCandidateButtonText));
        }

        StatusMessage = F("StatusResetDelete", changed);
        NotifyUser?.Invoke(StatusMessage);
    }

    private void SetSpeed(object? parameter)
    {
        if (parameter is SpeedOption option)
        {
            SlideSeconds = option.Seconds;
            return;
        }

        if (int.TryParse(CustomSecondsText, out var custom))
        {
            PlaybackSettings.CustomSlideSeconds = PlaybackSpeedCatalog.Normalize(custom);
            SlideSeconds = PlaybackSettings.CustomSlideSeconds;
            CustomSecondsText = PlaybackSettings.CustomSlideSeconds.ToString();
        }
    }

    private void SetFitMode(object? parameter)
    {
        if (parameter is FitMode mode)
        {
            CurrentFitMode = mode;
            return;
        }

        if (parameter is string value && Enum.TryParse<FitMode>(value, out var parsed))
        {
            CurrentFitMode = parsed;
        }
    }

    private void SetMonitor(object? parameter)
    {
        if (parameter is string deviceName)
        {
            SelectedMonitorDeviceName = deviceName;
        }
    }

    private void ApplyPlaybackStartDisplayMode()
    {
        if (!DisplaySettings.StartSlideshowFullscreen
            || DisplaySettings.DisplayMode == DisplayModeKind.Fullscreen)
        {
            return;
        }

        _displayModeBeforePlaybackFullscreen = DisplaySettings.DisplayMode;
        _playbackFullscreenApplied = true;
        DisplaySettings.DisplayMode = DisplayModeKind.Fullscreen;
        RaiseDisplayModePropertiesChanged();
        DisplaySettingsChanged?.Invoke();
    }

    private void RestorePlaybackStartDisplayMode()
    {
        if (!_playbackFullscreenApplied)
        {
            return;
        }

        DisplaySettings.DisplayMode = _displayModeBeforePlaybackFullscreen ?? DisplayModeKind.Window;
        _displayModeBeforePlaybackFullscreen = null;
        _playbackFullscreenApplied = false;
        RaiseDisplayModePropertiesChanged();
        DisplaySettingsChanged?.Invoke();
        _ = SaveSettingsAsync();
    }

    private void ToggleFullscreen()
    {
        DisplaySettings.DisplayMode = DisplaySettings.DisplayMode == DisplayModeKind.Fullscreen
            ? DisplayModeKind.Window
            : DisplayModeKind.Fullscreen;
        RaiseDisplayModePropertiesChanged();
        DisplaySettingsChanged?.Invoke();
        _ = SaveSettingsAsync();
    }

    private void ToggleBorderless()
    {
        DisplaySettings.DisplayMode = DisplaySettings.DisplayMode == DisplayModeKind.Borderless
            ? DisplayModeKind.Window
            : DisplayModeKind.Borderless;
        RaiseDisplayModePropertiesChanged();
        DisplaySettingsChanged?.Invoke();
        _ = SaveSettingsAsync();
    }

    private void SetWindowMode()
    {
        DisplaySettings.DisplayMode = DisplayModeKind.Window;
        RaiseDisplayModePropertiesChanged();
        DisplaySettingsChanged?.Invoke();
        _ = SaveSettingsAsync();
    }

    private void BackHome()
    {
        IsPlaying = false;
        IsPlayerVisible = false;
        ClearDisplayedImages();
        _slideTimer.Stop();
        _imageLoadCts?.Cancel();
        RestorePlaybackStartDisplayMode();
    }

    private void ClearDisplayedImages()
    {
        CurrentImage = null;
    }

    private SlideshowFilterOptions CopyFilterOptions()
    {
        return new SlideshowFilterOptions
        {
            FavoritesOnly = FilterOptions.FavoritesOnly,
            WatchLaterOnly = FilterOptions.WatchLaterOnly,
            RecentlyUnseenOnly = FilterOptions.RecentlyUnseenOnly,
            AnniversaryAroundTodayOnly = FilterOptions.AnniversaryAroundTodayOnly,
            IncludeVideos = FilterOptions.IncludeVideos,
            FamilySafeMode = FilterOptions.FamilySafeMode,
            VerticalBias = FilterOptions.VerticalBias,
            RecentlyUnseenDays = FilterOptions.RecentlyUnseenDays,
            AnniversaryWindowDays = FilterOptions.AnniversaryWindowDays,
            Now = DateTimeOffset.Now
        };
    }

    private static string BuildQueueKey(IEnumerable<string>? folderIds, string title, SlideshowFilterOptions options)
    {
        var folderKey = folderIds is null ? "all" : string.Join("+", folderIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase));
        return $"{folderKey}|{title}|fav:{options.FavoritesOnly}|watch:{options.WatchLaterOnly}|safe:{options.FamilySafeMode}|unseen:{options.RecentlyUnseenOnly}|anniv:{options.AnniversaryAroundTodayOnly}|video:{options.IncludeVideos}";
    }

    private async Task SaveSettingsAsync()
    {
        await _database.SaveSettingAsync(PlaybackSettingsKey, PlaybackSettings);
        await _database.SaveSettingAsync(DisplaySettingsKey, DisplaySettings);
        await _database.SaveSettingAsync(FilterSettingsKey, FilterOptions);
    }

    private Task SaveCustomMoodProfilesAsync()
        => _database.SaveSettingAsync(CustomMoodProfilesKey, CustomMoodProfiles.ToList());

    private void RaiseDisplayModePropertiesChanged()
    {
        OnPropertyChanged(nameof(DisplaySettings));
        OnPropertyChanged(nameof(IsWindowMode));
        OnPropertyChanged(nameof(IsFullscreenMode));
        OnPropertyChanged(nameof(IsBorderlessMode));
    }

    private void RaiseFitModePropertiesChanged()
    {
        OnPropertyChanged(nameof(IsFitModeFit));
        OnPropertyChanged(nameof(IsFitModeFill));
        OnPropertyChanged(nameof(IsFitModeOriginal));
        OnPropertyChanged(nameof(IsFitModeBlurBackground));
    }

    private void RaiseMoodPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsDefaultMoodSelected));
        OnPropertyChanged(nameof(IsWorkMoodSelected));
        OnPropertyChanged(nameof(IsBedtimeMoodSelected));
        OnPropertyChanged(nameof(IsFamilySafeMoodSelected));
    }

    private void RaiseLanguagePropertiesChanged()
    {
        OnPropertyChanged(nameof(CurrentLanguage));
        OnPropertyChanged(nameof(IsEnglishLanguage));
        OnPropertyChanged(nameof(IsJapaneseLanguage));
        OnPropertyChanged(nameof(PlayPauseText));
        OnPropertyChanged(nameof(HideButtonText));
        OnPropertyChanged(nameof(WatchLaterButtonText));
        OnPropertyChanged(nameof(DeleteCandidateButtonText));
        OnPropertyChanged(nameof(CurrentMetaText));
        OnPropertyChanged(nameof(CurrentSlideSecondsText));
        OnPropertyChanged(nameof(CurrentFitMode));
        OnPropertyChanged(nameof(SelectedFitModeOption));
        OnPropertyChanged(nameof(CurrentVerticalBias));
        OnPropertyChanged(nameof(SelectedVerticalBiasOption));
        OnPropertyChanged(nameof(SpeedOptions));
        OnPropertyChanged(nameof(FitModeOptions));
        OnPropertyChanged(nameof(VerticalBiasOptions));
        RaiseFitModePropertiesChanged();
    }

    private void RaiseSettingsPropertiesChanged()
    {
        OnPropertyChanged(nameof(SlideSeconds));
        OnPropertyChanged(nameof(Loop));
        OnPropertyChanged(nameof(PauseEachPhoto));
        OnPropertyChanged(nameof(UseSmartShuffle));
        OnPropertyChanged(nameof(IncludeVideos));
        OnPropertyChanged(nameof(FamilySafeMode));
        OnPropertyChanged(nameof(CurrentVerticalBias));
        OnPropertyChanged(nameof(SelectedVerticalBiasOption));
        OnPropertyChanged(nameof(CurrentFitMode));
        OnPropertyChanged(nameof(SelectedFitModeOption));
        OnPropertyChanged(nameof(WindowOpacity));
        OnPropertyChanged(nameof(TopmostMode));
        OnPropertyChanged(nameof(MuteVideo));
        OnPropertyChanged(nameof(StartSlideshowFullscreen));
        OnPropertyChanged(nameof(DarkMode));
        OnPropertyChanged(nameof(AutoStartWithWindows));
        OnPropertyChanged(nameof(IdleAutoPlay));
        OnPropertyChanged(nameof(IdleAutoPlayMinutes));
        OnPropertyChanged(nameof(SelectedMonitorDeviceName));
        OnPropertyChanged(nameof(CustomSecondsText));
        RaiseLanguagePropertiesChanged();
        OnPropertyChanged(nameof(Folders));
        OnPropertyChanged(nameof(Playlists));
        OnPropertyChanged(nameof(HasMonitors));
        OnPropertyChanged(nameof(HasFolders));
        OnPropertyChanged(nameof(HasPlaylists));
        OnPropertyChanged(nameof(HasCustomMoodProfiles));
        RaiseDisplayModePropertiesChanged();
        RaiseFitModePropertiesChanged();
        RaiseMoodPropertiesChanged();
    }
}
