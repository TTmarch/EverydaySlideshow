using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using EverydaySlideshow.Core;
using EverydaySlideshow.Services;
using EverydaySlideshow.ViewModels;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;

namespace EverydaySlideshow;

public partial class MainWindow : Window
{
    private const int WmPowerBroadcast = 0x0218;
    private const int PbtApmResumeSuspend = 0x0007;
    private const int PbtApmResumeAutomatic = 0x0012;

    private readonly MainViewModel _viewModel = new();
    private Rect? _lastWindowedBounds;
    private bool _didRestoreWindowPlacement;
    private DisplayModeKind _appliedDisplayMode = DisplayModeKind.Window;

    public MainWindow()
    {
        // Load the persisted library before the first binding pass so the home screen
        // never paints as empty when folders already exist.
        _viewModel.InitializeAsync().GetAwaiter().GetResult();
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.RequestFolderRegistration += RequestFolderRegistrationAsync;
        _viewModel.RequestPlaylistCreation += RequestPlaylistCreationAsync;
        _viewModel.RequestCustomMoodCreation += RequestCustomMoodCreationAsync;
        _viewModel.RequestConfirmation += RequestConfirmationAsync;
        _viewModel.NotifyUser += message => System.Windows.MessageBox.Show(this, message, _viewModel.Text["AppTitle"], MessageBoxButton.OK, MessageBoxImage.Information);
        _viewModel.RequestOpenUrl += OpenExternalUrl;
        _viewModel.DisplaySettingsChanged += ApplyDisplaySettings;
        _viewModel.LanguageChanged += RefreshLanguageSensitiveOptions;
        _viewModel.PlaybackVisualStateChanged += UpdateVideoPlayer;
        _viewModel.PropertyChanged += MainViewModel_PropertyChanged;

        Loaded += MainWindow_Loaded;
        ContentRendered += MainWindow_ContentRendered;
        Activated += MainWindow_Activated;
        SourceInitialized += MainWindow_SourceInitialized;
        Closing += (_, _) => VideoPlayer.Stop();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _viewModel.SetMonitors(BuildMonitorOptions());
        await _viewModel.InitializeAsync();
        await _viewModel.RefreshLibraryListsAsync();
        _viewModel.SetMonitors(BuildMonitorOptions());
        ApplyDisplaySettings();
    }

    private async void MainWindow_ContentRendered(object? sender, EventArgs e)
    {
        await _viewModel.InitializeAsync();
        await _viewModel.RefreshLibraryListsAsync();
    }

    private async void MainWindow_Activated(object? sender, EventArgs e)
    {
        if (!_viewModel.IsPlayerVisible && _viewModel.Folders.Count == 0)
        {
            await _viewModel.InitializeAsync();
            await _viewModel.RefreshLibraryListsAsync();
        }
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmPowerBroadcast)
        {
            var code = wParam.ToInt32();
            if (code == PbtApmResumeSuspend || code == PbtApmResumeAutomatic)
            {
                Dispatcher.BeginInvoke(_viewModel.ResumeAfterWake);
            }
        }

        return IntPtr.Zero;
    }

    private Task<FolderRegistrationResult?> RequestFolderRegistrationAsync()
    {
        using var browser = new Forms.FolderBrowserDialog
        {
            Description = LocalizedText.Translate(_viewModel.DisplaySettings.Language, "FolderBrowserDescription"),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (browser.ShowDialog() != Forms.DialogResult.OK)
        {
            return Task.FromResult<FolderRegistrationResult?>(null);
        }

        var dialog = new FolderRegistrationDialog(browser.SelectedPath, _viewModel.DisplaySettings.Language)
        {
            Owner = this
        };

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.Result : null);
    }

    private Task<bool> RequestConfirmationAsync(string title, string message)
    {
        var result = System.Windows.MessageBox.Show(this, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        return Task.FromResult(result == MessageBoxResult.Yes);
    }

    private Task<FolderPlaylist?> RequestPlaylistCreationAsync(IReadOnlyList<FolderProfile> folders)
    {
        var dialog = new PlaylistDialog(folders, _viewModel.DisplaySettings.Language)
        {
            Owner = this
        };

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.Result : null);
    }

    private Task<CustomMoodProfile?> RequestCustomMoodCreationAsync(CustomMoodProfile draft)
    {
        var dialog = new CustomMoodDialog(draft, _viewModel.DisplaySettings.Language)
        {
            Owner = this
        };

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.Result : null);
    }

    private static void OpenExternalUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
    }

    private IEnumerable<MonitorOption> BuildMonitorOptions()
    {
        return Forms.Screen.AllScreens.Select((screen, index) =>
        {
            var role = screen.Primary
                ? LocalizedText.Translate(_viewModel.DisplaySettings.Language, "PrimaryMonitor")
                : LocalizedText.Translate(_viewModel.DisplaySettings.Language, "SecondaryMonitor");
            var bounds = screen.Bounds;
            return new MonitorOption(
                screen.DeviceName,
                $"{index + 1}: {role} {bounds.Width}x{bounds.Height}");
        });
    }

    private void RefreshLanguageSensitiveOptions()
    {
        _viewModel.SetMonitors(BuildMonitorOptions());
    }

    private void ApplyDisplaySettings()
    {
        var mode = GetEffectiveDisplayMode();
        ApplyTheme();
        Topmost = _viewModel.DisplaySettings.Topmost;
        Opacity = _viewModel.DisplaySettings.Opacity;
        ShowInTaskbar = true;

        var screen = Forms.Screen.AllScreens.FirstOrDefault(candidate =>
                         string.Equals(candidate.DeviceName, _viewModel.DisplaySettings.MonitorDeviceName, StringComparison.OrdinalIgnoreCase))
                     ?? Forms.Screen.PrimaryScreen
                     ?? Forms.Screen.AllScreens.First();

        if (mode != DisplayModeKind.Fullscreen && _appliedDisplayMode != DisplayModeKind.Fullscreen && WindowState == WindowState.Normal)
        {
            _lastWindowedBounds = new Rect(Left, Top, ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height);
        }

        if (mode == DisplayModeKind.Window || mode == DisplayModeKind.Borderless)
        {
            ApplyWindowFrame(mode);
            WindowState = WindowState.Normal;

            if (!_didRestoreWindowPlacement)
            {
                ApplySavedWindowPlacement();
                _didRestoreWindowPlacement = true;
            }
            else if (_appliedDisplayMode == DisplayModeKind.Fullscreen && _lastWindowedBounds is Rect bounds)
            {
                ApplyBounds(bounds);
            }

            _appliedDisplayMode = mode;
            return;
        }

        if (_appliedDisplayMode != DisplayModeKind.Fullscreen && WindowState == WindowState.Normal)
        {
            _lastWindowedBounds = new Rect(Left, Top, ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height);
        }

        WindowState = WindowState.Normal;
        ApplyWindowFrame(DisplayModeKind.Fullscreen);
        Left = screen.Bounds.Left;
        Top = screen.Bounds.Top;
        Width = screen.Bounds.Width;
        Height = screen.Bounds.Height;
        Topmost = _viewModel.DisplaySettings.Topmost;
        _appliedDisplayMode = mode;
    }

    private DisplayModeKind GetEffectiveDisplayMode()
    {
        var mode = _viewModel.DisplaySettings.DisplayMode;
        return mode == DisplayModeKind.Borderless && !_viewModel.IsPlayerVisible
            ? DisplayModeKind.Window
            : mode;
    }

    private void ApplyTheme()
    {
        if (_viewModel.DisplaySettings.DarkMode)
        {
            SetThemeBrush("AccentBrush", MediaColor.FromRgb(120, 166, 216));
            SetThemeBrush("PageBrush", MediaColor.FromRgb(13, 18, 25));
            SetThemeBrush("PanelBrush", MediaColor.FromRgb(24, 31, 42));
            SetThemeBrush("TextBrush", MediaColor.FromRgb(232, 238, 247));
            SetThemeBrush("MutedTextBrush", MediaColor.FromRgb(158, 171, 190));
            SetThemeBrush("MenuBrush", MediaColor.FromRgb(18, 24, 33));
            SetThemeBrush("SidebarBrush", MediaColor.FromRgb(20, 28, 39));
            SetThemeBrush("PanelBorderBrush", MediaColor.FromRgb(51, 64, 82));
            SetThemeBrush("ChromeBorderBrush", MediaColor.FromRgb(43, 55, 72));
            SetThemeBrush("ButtonBrush", MediaColor.FromRgb(39, 51, 67));
            SetThemeBrush("ButtonHoverBrush", MediaColor.FromRgb(54, 70, 91));
            SetThemeBrush("InputBorderBrush", MediaColor.FromRgb(67, 80, 100));
            SetThemeBrush("PopupBrush", MediaColor.FromRgb(22, 29, 39));
            SetThemeBrush("PopupHoverBrush", MediaColor.FromRgb(43, 56, 74));
            SetThemeBrush("SelectionBrush", MediaColor.FromRgb(54, 82, 114));
            ApplySystemThemeBrushes(
                popup: MediaColor.FromRgb(22, 29, 39),
                text: MediaColor.FromRgb(232, 238, 247),
                muted: MediaColor.FromRgb(158, 171, 190),
                hover: MediaColor.FromRgb(43, 56, 74),
                selection: MediaColor.FromRgb(54, 82, 114),
                border: MediaColor.FromRgb(51, 64, 82),
                control: MediaColor.FromRgb(24, 31, 42));
            return;
        }

        SetThemeBrush("AccentBrush", MediaColor.FromRgb(79, 124, 172));
        SetThemeBrush("PageBrush", MediaColor.FromRgb(245, 247, 250));
        SetThemeBrush("PanelBrush", MediaColor.FromRgb(255, 255, 255));
        SetThemeBrush("TextBrush", MediaColor.FromRgb(29, 39, 51));
        SetThemeBrush("MutedTextBrush", MediaColor.FromRgb(102, 112, 133));
        SetThemeBrush("MenuBrush", MediaColor.FromRgb(248, 250, 252));
        SetThemeBrush("SidebarBrush", MediaColor.FromRgb(238, 243, 248));
        SetThemeBrush("PanelBorderBrush", MediaColor.FromRgb(221, 229, 238));
        SetThemeBrush("ChromeBorderBrush", MediaColor.FromRgb(213, 222, 233));
        SetThemeBrush("ButtonBrush", MediaColor.FromRgb(231, 236, 243));
        SetThemeBrush("ButtonHoverBrush", MediaColor.FromRgb(216, 228, 242));
        SetThemeBrush("InputBorderBrush", MediaColor.FromRgb(203, 213, 225));
        SetThemeBrush("PopupBrush", MediaColor.FromRgb(255, 255, 255));
        SetThemeBrush("PopupHoverBrush", MediaColor.FromRgb(231, 236, 243));
        SetThemeBrush("SelectionBrush", MediaColor.FromRgb(216, 228, 242));
        ApplySystemThemeBrushes(
            popup: MediaColor.FromRgb(255, 255, 255),
            text: MediaColor.FromRgb(29, 39, 51),
            muted: MediaColor.FromRgb(102, 112, 133),
            hover: MediaColor.FromRgb(231, 236, 243),
            selection: MediaColor.FromRgb(216, 228, 242),
            border: MediaColor.FromRgb(203, 213, 225),
            control: MediaColor.FromRgb(245, 247, 250));
    }

    private static void ApplySystemThemeBrushes(
        MediaColor popup,
        MediaColor text,
        MediaColor muted,
        MediaColor hover,
        MediaColor selection,
        MediaColor border,
        MediaColor control)
    {
        SetThemeBrush(System.Windows.SystemColors.MenuBrushKey, popup);
        SetThemeBrush(System.Windows.SystemColors.MenuTextBrushKey, text);
        SetThemeBrush(System.Windows.SystemColors.WindowBrushKey, popup);
        SetThemeBrush(System.Windows.SystemColors.WindowTextBrushKey, text);
        SetThemeBrush(System.Windows.SystemColors.ControlBrushKey, control);
        SetThemeBrush(System.Windows.SystemColors.ControlTextBrushKey, text);
        SetThemeBrush(System.Windows.SystemColors.ControlLightBrushKey, border);
        SetThemeBrush(System.Windows.SystemColors.ControlDarkBrushKey, border);
        SetThemeBrush(System.Windows.SystemColors.HighlightBrushKey, selection);
        SetThemeBrush(System.Windows.SystemColors.HighlightTextBrushKey, text);
        SetThemeBrush(System.Windows.SystemColors.InactiveSelectionHighlightBrushKey, hover);
        SetThemeBrush(System.Windows.SystemColors.InactiveSelectionHighlightTextBrushKey, text);
        SetThemeBrush(System.Windows.SystemColors.GrayTextBrushKey, muted);
    }

    private static void SetThemeBrush(object key, MediaColor color)
    {
        if (System.Windows.Application.Current.Resources[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
            return;
        }

        System.Windows.Application.Current.Resources[key] = new SolidColorBrush(color);
    }

    private void ApplyWindowFrame(DisplayModeKind mode)
    {
        if (mode == DisplayModeKind.Window)
        {
            WindowChrome.SetWindowChrome(this, null);
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            return;
        }

        WindowStyle = WindowStyle.None;
        ResizeMode = mode == DisplayModeKind.Borderless ? ResizeMode.CanResize : ResizeMode.NoResize;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            CornerRadius = new CornerRadius(0),
            GlassFrameThickness = new Thickness(0),
            NonClientFrameEdges = NonClientFrameEdges.None,
            ResizeBorderThickness = mode == DisplayModeKind.Borderless ? new Thickness(6) : new Thickness(0),
            UseAeroCaptionButtons = false
        });
    }

    private void ApplySavedWindowPlacement()
    {
        var settings = _viewModel.DisplaySettings;
        var width = double.IsFinite(settings.WindowWidth) ? Math.Max(MinWidth, settings.WindowWidth) : 1240;
        var height = double.IsFinite(settings.WindowHeight) ? Math.Max(MinHeight, settings.WindowHeight) : 760;
        var left = double.IsFinite(settings.WindowLeft) ? settings.WindowLeft : Left;
        var top = double.IsFinite(settings.WindowTop) ? settings.WindowTop : Top;
        ApplyBounds(new Rect(left, top, width, height));
        if (settings.WindowMaximized && GetEffectiveDisplayMode() == DisplayModeKind.Window)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void ApplyBounds(Rect bounds)
    {
        Left = bounds.Left;
        Top = bounds.Top;
        Width = Math.Max(MinWidth, bounds.Width);
        Height = Math.Max(MinHeight, bounds.Height);
    }

    private void UpdateVideoPlayer()
    {
        if (_viewModel.CurrentItem?.IsVideo == true)
        {
            var source = new Uri(_viewModel.CurrentItem.Path, UriKind.Absolute);
            if (VideoPlayer.Source is null || VideoPlayer.Source.LocalPath != source.LocalPath)
            {
                VideoPlayer.Source = source;
                VideoPlayer.Position = TimeSpan.Zero;
            }

            VideoPlayer.Volume = _viewModel.DisplaySettings.MuteVideo ? 0 : 0.65;
            if (_viewModel.IsPlaying)
            {
                VideoPlayer.Play();
            }
            else
            {
                VideoPlayer.Pause();
            }
            return;
        }

        VideoPlayer.Stop();
        VideoPlayer.Source = null;
    }

    private async void VideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        await _viewModel.OnVideoEndedAsync();
    }

    private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _viewModel.ShowOverlayTemporarily();
    }

    private void PlayerSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_viewModel.IsPlayerVisible
            || _viewModel.DisplaySettings.DisplayMode == DisplayModeKind.Fullscreen
            || e.LeftButton != MouseButtonState.Pressed
            || e.ClickCount != 1
            || IsWindowDragBlocked(e.OriginalSource as DependencyObject))
        {
            return;
        }

        try
        {
            DragMove();
            e.Handled = true;
        }
        catch (InvalidOperationException)
        {
            // DragMove can throw if Windows has already consumed the mouse-down.
        }
    }

    private static bool IsWindowDragBlocked(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { Tag: "NoWindowDrag" })
            {
                return true;
            }

            if (source is System.Windows.Controls.Primitives.ButtonBase
                or System.Windows.Controls.Primitives.Selector
                or System.Windows.Controls.Primitives.RangeBase
                or System.Windows.Controls.Primitives.TextBoxBase
                or System.Windows.Controls.PasswordBox
                or System.Windows.Controls.Primitives.ScrollBar
                or System.Windows.Controls.Primitives.Thumb)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space:
                _viewModel.TogglePauseCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Right:
                _viewModel.NextCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Left:
                _viewModel.PreviousCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Home:
                _viewModel.FirstCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.F11:
            case Key.F:
                _viewModel.ToggleFullscreenCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.H:
                _viewModel.HideCurrentCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Delete:
                _viewModel.DeleteCandidateCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.S:
            case Key.OemPlus:
                _viewModel.ToggleFavoriteCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                if (GetEffectiveDisplayMode() != DisplayModeKind.Window)
                {
                    _viewModel.SetWindowModeCommand.Execute(null);
                }
                else if (_viewModel.IsPlayerVisible)
                {
                    _viewModel.BackHomeCommand.Execute(null);
                }
                e.Handled = true;
                break;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        SaveWindowPlacement();
        _viewModel.DisplaySettingsChanged -= ApplyDisplaySettings;
        _viewModel.RequestOpenUrl -= OpenExternalUrl;
        _viewModel.LanguageChanged -= RefreshLanguageSensitiveOptions;
        _viewModel.PlaybackVisualStateChanged -= UpdateVideoPlayer;
        _viewModel.PropertyChanged -= MainViewModel_PropertyChanged;
        base.OnClosing(e);
    }

    private void MainViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsPlayerVisible))
        {
            ApplyDisplaySettings();
        }
    }

    private void SaveWindowPlacement()
    {
        var bounds = WindowState == WindowState.Maximized
            ? RestoreBounds
            : GetEffectiveDisplayMode() == DisplayModeKind.Fullscreen && _lastWindowedBounds.HasValue
                ? _lastWindowedBounds.Value
                : new Rect(Left, Top, ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height);

        _viewModel.SaveWindowPlacement(
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            WindowState == WindowState.Maximized && GetEffectiveDisplayMode() == DisplayModeKind.Window);
    }
}
