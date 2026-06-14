using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DynamicIsland;

public partial class MainWindow : Window
{
    private const string GlyphPlay = "";
    private const string GlyphPause = "";

    private const double CompactWBase = 200, CompactWPlaying = 280, CompactH = 34;
    private const double ExpandedW = 380, ExpandedH = 250;
    private const double BannerW = 350, BannerH = 68;

    private GlobalSystemMediaTransportControlsSessionManager? _smtc;
    private GlobalSystemMediaTransportControlsSession? _session;

    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _countdown = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _notifTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _bannerTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly NotificationListener _notif = new();

    private readonly Settings _settings = Settings.Load();
    private bool _firstRun;
    private bool _expanded;
    private bool _bannerActive;
    private bool _isPlaying;
    private string _currentTitle = "";
    private int _tickCount;

    private TimeSpan _timerRemaining;
    private bool _timerRunning;

    private readonly List<string> _shelf = new();

    private double CompactW =>
        (_timerRunning || (_isPlaying && _currentTitle.Length > 0)) ? CompactWPlaying : CompactWBase;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        PositionTop();
        UpdateAutoStartLabel();
        StartCompactBars();
        CompactBars.Visibility = Visibility.Collapsed;
        SwitchTab("media");
        UpdateTimerDisplay();

        _clockTimer.Tick += (_, _) => UpdateClockAndBattery();
        _clockTimer.Start();
        UpdateClockAndBattery();

        _countdown.Tick += Countdown_Tick;
        _countdown.Start();

        _bannerTimer.Tick += (_, _) => HideBanner();

        await InitMediaAsync();
        await InitNotificationsAsync();
        _ = UpdateBluetoothAsync();

        if (!_settings.FirstRunDone) ShowFirstRun();
    }

    private void ShowFirstRun()
    {
        _firstRun = true;
        SetCorner(32);
        CompactView.Opacity = 0;
        FirstRunPanel.Visibility = Visibility.Visible;
        FirstRunPanel.Opacity = 0;
        Fade(FirstRunPanel, 1, 220);
        AnimateSize(ExpandedW, ExpandedH);
    }

    private void FirstRunEnableStart_Click(object sender, RoutedEventArgs e)
    {
        SetAutoStart(true);
        _settings.AutoStart = true;
        UpdateAutoStartLabel();
        FirstRunStartBtn.Content = "Démarrage activé ✓";
    }

    private void FirstRunDone_Click(object sender, RoutedEventArgs e)
    {
        _firstRun = false;
        _settings.FirstRunDone = true;
        _settings.Save();
        var fade = Fade(FirstRunPanel, 0, 160);
        fade.Completed += (_, _) => FirstRunPanel.Visibility = Visibility.Collapsed;
        if (Island.IsMouseOver) Expand();
        else { SetCorner(22); AnimateSize(CompactW, CompactH); Fade(CompactView, 1, 160); }
    }

    private void PositionTop()
    {
        var work = SystemParameters.WorkArea;
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2.0;
        Top = work.Top;
    }

    private void Island_MouseEnter(object sender, MouseEventArgs e) => Expand();
    private void Island_MouseLeave(object sender, MouseEventArgs e) => Collapse();

    private void Island_Click(object sender, MouseButtonEventArgs e)
    {
        if (_expanded) Collapse(); else Expand();
    }

    private void Expand()
    {
        _bannerTimer.Stop();
        if (_bannerActive)
        {
            _bannerActive = false;
            NotifBanner.Visibility = Visibility.Collapsed;
            NotifBanner.Opacity = 0;
        }
        if (_expanded) return;
        _expanded = true;
        SetCorner(32);
        ExpandedView.Visibility = Visibility.Visible;
        AnimateSize(ExpandedW, ExpandedH);
        Fade(ExpandedView, 1, 160);
        Fade(CompactView, 0, 120);
    }

    private void Collapse()
    {
        if (_firstRun) return;
        if (!_expanded && !_bannerActive) return;
        _expanded = false;
        _bannerActive = false;
        _bannerTimer.Stop();
        SettingsPanel.Visibility = Visibility.Collapsed;
        NotifBanner.Visibility = Visibility.Collapsed;
        SetCorner(22);
        AnimateSize(CompactW, CompactH);
        Fade(CompactView, 1, 160);
        var fe = Fade(ExpandedView, 0, 120);
        fe.Completed += (_, _) => { if (!_expanded) ExpandedView.Visibility = Visibility.Collapsed; };
    }

    private void SetCorner(double bottom)
    {
        var c = new CornerRadius(0, 0, bottom, bottom);
        Island.CornerRadius = c;
        Shell.CornerRadius = c;
    }

    private void AnimateSize(double w, double h)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(260);
        Island.BeginAnimation(WidthProperty, new DoubleAnimation(w, dur) { EasingFunction = ease });
        Island.BeginAnimation(HeightProperty, new DoubleAnimation(h, dur) { EasingFunction = ease });
    }

    private DoubleAnimation Fade(UIElement el, double to, double ms)
    {
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms));
        el.BeginAnimation(OpacityProperty, anim);
        return anim;
    }

    private void StartCompactBars()
    {
        int i = 0;
        foreach (var child in CompactBars.Children)
        {
            if (child is System.Windows.Shapes.Rectangle r)
            {
                var a = new DoubleAnimation
                {
                    From = 4,
                    To = 14,
                    Duration = TimeSpan.FromMilliseconds(420 + i * 90),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase()
                };
                r.BeginAnimation(HeightProperty, a);
                i++;
            }
        }
    }

    private void UpdateCompactText()
    {
        if (_timerRunning && _timerRemaining.TotalSeconds > 0)
            CompactClock.Text = "⏱ " + FormatTime(_timerRemaining);
        else if (_isPlaying && _currentTitle.Length > 0)
            CompactClock.Text = _currentTitle;
        else
            CompactClock.Text = DateTime.Now.ToString("HH:mm");

        if (!_expanded && !_bannerActive)
            Island.BeginAnimation(WidthProperty,
                new DoubleAnimation(CompactW, TimeSpan.FromMilliseconds(220))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void TabMedia_Click(object sender, RoutedEventArgs e) => SwitchTab("media");
    private void TabTimer_Click(object sender, RoutedEventArgs e) => SwitchTab("timer");
    private void TabFiles_Click(object sender, RoutedEventArgs e) => SwitchTab("files");

    private void SwitchTab(string tab)
    {
        MediaPanel.Visibility = tab == "media" ? Visibility.Visible : Visibility.Collapsed;
        TimerPanel.Visibility = tab == "timer" ? Visibility.Visible : Visibility.Collapsed;
        FilesPanel.Visibility = tab == "files" ? Visibility.Visible : Visibility.Collapsed;

        var active = (Brush)FindResource("TextBrush");
        var idle = (Brush)FindResource("SubTextBrush");
        TabMediaBtn.Foreground = tab == "media" ? active : idle;
        TabTimerBtn.Foreground = tab == "timer" ? active : idle;
        TabFilesBtn.Foreground = tab == "files" ? active : idle;
    }

    private async Task InitMediaAsync()
    {
        try
        {
            _smtc = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _smtc.CurrentSessionChanged += (_, _) => Dispatcher.Invoke(HookCurrentSession);
            HookCurrentSession();
        }
        catch { }
    }

    private void HookCurrentSession()
    {
        _session = _smtc?.GetCurrentSession();
        if (_session is null)
        {
            _isPlaying = false;
            _currentTitle = "";
            TrackTitle.Text = "Aucune lecture";
            TrackArtist.Text = "—";
            AlbumArt.Source = null;
            CompactArt.Source = null;
            CompactBars.Visibility = Visibility.Collapsed;
            UpdateCompactText();
            return;
        }

        CompactArt.Source = AppIcon.Resolve(_session.SourceAppUserModelId);

        _session.MediaPropertiesChanged += (_, _) => Dispatcher.Invoke(async () => await RefreshMediaAsync());
        _session.PlaybackInfoChanged += (_, _) => Dispatcher.Invoke(RefreshPlayback);
        _session.TimelinePropertiesChanged += (_, _) => Dispatcher.Invoke(RefreshTimeline);

        _ = RefreshMediaAsync();
        RefreshPlayback();
        RefreshTimeline();
    }

    private async Task RefreshMediaAsync()
    {
        if (_session is null) return;
        try
        {
            var props = await _session.TryGetMediaPropertiesAsync();
            _currentTitle = string.IsNullOrWhiteSpace(props.Title) ? "" : props.Title;
            TrackTitle.Text = _currentTitle.Length == 0 ? "Lecture en cours" : _currentTitle;
            TrackArtist.Text = string.IsNullOrWhiteSpace(props.Artist) ? "—" : props.Artist;
            AlbumArt.Source = await LoadThumbAsync(props.Thumbnail);
            UpdateCompactText();
        }
        catch { }
    }

    private void RefreshPlayback()
    {
        if (_session is null) return;
        var info = _session.GetPlaybackInfo();
        _isPlaying = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        PlayBtn.Content = _isPlaying ? GlyphPause : GlyphPlay;
        CompactBars.Visibility = _isPlaying ? Visibility.Visible : Visibility.Collapsed;
        UpdateCompactText();
    }

    private void RefreshTimeline()
    {
        if (_session is null) return;
        var t = _session.GetTimelineProperties();
        double total = (t.EndTime - t.StartTime).TotalSeconds;
        double pos = (t.Position - t.StartTime).TotalSeconds;
        double ratio = total > 0 ? Math.Clamp(pos / total, 0, 1) : 0;
        ProgressFill.Width = (ExpandedW - 36) * ratio;
    }

    private static async Task<BitmapImage?> LoadThumbAsync(IRandomAccessStreamReference? thumbRef)
    {
        if (thumbRef is null) return null;
        try
        {
            using var ras = await thumbRef.OpenReadAsync();
            using var net = ras.AsStreamForRead();
            using var ms = new MemoryStream();
            await net.CopyToAsync(ms);
            ms.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        try { await _session.TryTogglePlayPauseAsync(); } catch { }
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        try { await _session.TrySkipNextAsync(); } catch { }
    }

    private async void Prev_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        try { await _session.TrySkipPreviousAsync(); } catch { }
    }

    private void TimerPlus1_Click(object sender, RoutedEventArgs e) => AddTimer(1);
    private void TimerPlus5_Click(object sender, RoutedEventArgs e) => AddTimer(5);
    private void TimerPlus10_Click(object sender, RoutedEventArgs e) => AddTimer(10);

    private void AddTimer(int minutes)
    {
        _timerRemaining += TimeSpan.FromMinutes(minutes);
        UpdateTimerDisplay();
    }

    private void TimerStartPause_Click(object sender, RoutedEventArgs e)
    {
        if (_timerRemaining.TotalSeconds <= 0) return;
        _timerRunning = !_timerRunning;
        TimerStartBtn.Content = _timerRunning ? "Pause" : "Démarrer";
        UpdateCompactText();
    }

    private void TimerReset_Click(object sender, RoutedEventArgs e)
    {
        _timerRunning = false;
        _timerRemaining = TimeSpan.Zero;
        TimerStartBtn.Content = "Démarrer";
        UpdateTimerDisplay();
        UpdateCompactText();
    }

    private void Countdown_Tick(object? sender, EventArgs e)
    {
        if (!_timerRunning) return;
        _timerRemaining -= TimeSpan.FromSeconds(1);
        if (_timerRemaining.TotalSeconds <= 0)
        {
            _timerRemaining = TimeSpan.Zero;
            _timerRunning = false;
            TimerStartBtn.Content = "Démarrer";
            ShowBanner(null, "Minuteur", "Terminé", "Le minuteur est écoulé.");
        }
        UpdateTimerDisplay();
        UpdateCompactText();
    }

    private void UpdateTimerDisplay() => TimerDisplay.Text = FormatTime(_timerRemaining);

    private static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";

    private void Island_DragEnter(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        Expand();
        SwitchTab("files");
    }

    private void Island_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Island_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var f in files)
                if (!_shelf.Contains(f)) _shelf.Add(f);
            RebuildShelf();
            Expand();
            SwitchTab("files");
        }
    }

    private void RebuildShelf()
    {
        ShelfPanel.Children.Clear();
        ShelfHint.Visibility = _shelf.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var path in _shelf)
            ShelfPanel.Children.Add(CreateChip(path));
    }

    private Border CreateChip(string path)
    {
        var chip = new Border
        {
            Background = (Brush)FindResource("IslandBrush"),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 6, 6),
            Padding = new Thickness(8, 4, 4, 4),
            Cursor = Cursors.Hand,
            Tag = path
        };
        chip.Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));

        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new Image { Width = 18, Height = 18, Margin = new Thickness(0, 0, 6, 0), Source = AppIcon.ForFile(path) });
        sp.Children.Add(new TextBlock
        {
            Text = Path.GetFileName(path),
            Foreground = (Brush)FindResource("TextBrush"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 130,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var remove = new Button
        {
            Content = "",
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 10,
            Foreground = (Brush)FindResource("SubTextBrush"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(6, 0, 0, 0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        remove.Click += (_, _) => { _shelf.Remove(path); RebuildShelf(); };
        sp.Children.Add(remove);

        chip.Child = sp;

        Point start = default;
        bool pressed = false;
        chip.PreviewMouseLeftButtonDown += (_, ev) => { start = ev.GetPosition(null); pressed = true; };
        chip.PreviewMouseMove += (_, ev) =>
        {
            if (!pressed || ev.LeftButton != MouseButtonState.Pressed) return;
            var d = ev.GetPosition(null) - start;
            if (Math.Abs(d.X) > 6 || Math.Abs(d.Y) > 6)
            {
                pressed = false;
                try
                {
                    var data = new DataObject(DataFormats.FileDrop, new[] { path });
                    DragDrop.DoDragDrop(chip, data, DragDropEffects.Copy);
                }
                catch { }
            }
        };
        chip.MouseLeftButtonUp += (_, _) => { if (pressed) { pressed = false; OpenFile(path); } };
        return chip;
    }

    private static void OpenFile(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
    }

    private async Task InitNotificationsAsync()
    {
        _notif.Received += info => ShowBanner(info.Icon, info.App, info.Title, info.Body);
        await _notif.StartAsync();
        _notifTimer.Tick += async (_, _) => await _notif.PollAsync();
        _notifTimer.Start();
    }

    private void ShowBanner(ImageSource? icon, string app, string title, string body)
    {
        if (Island.IsMouseOver || _expanded) return;

        BannerIcon.Source = icon;
        BannerApp.Text = app;
        BannerTitle.Text = title;
        BannerBody.Text = body;
        BannerBody.Visibility = string.IsNullOrWhiteSpace(body) ? Visibility.Collapsed : Visibility.Visible;

        _bannerActive = true;
        SetCorner(28);
        CompactView.Opacity = 0;
        NotifBanner.Visibility = Visibility.Visible;
        Fade(NotifBanner, 1, 160);
        AnimateSize(BannerW, BannerH);

        _bannerTimer.Stop();
        _bannerTimer.Start();
    }

    private void HideBanner()
    {
        _bannerTimer.Stop();
        if (!_bannerActive) return;
        _bannerActive = false;
        var fade = Fade(NotifBanner, 0, 140);
        fade.Completed += (_, _) => NotifBanner.Visibility = Visibility.Collapsed;
        if (Island.IsMouseOver) Expand();
        else
        {
            SetCorner(22);
            AnimateSize(CompactW, CompactH);
            Fade(CompactView, 1, 160);
        }
    }

    private async Task UpdateBluetoothAsync()
    {
        var devices = await BluetoothBattery.GetAsync();
        if (devices.Count == 0)
        {
            BtPanel.Visibility = Visibility.Collapsed;
            return;
        }
        var d = devices[0];
        BtPanel.Visibility = Visibility.Visible;
        BtText.Text = $"{d.Percent}%";
        BtPanel.ToolTip = d.Name;
    }

    private void UpdateClockAndBattery()
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString("HH:mm");
        DateText.Text = now.ToString("dddd d MMMM");
        UpdateCompactText();
        RefreshTimeline();

        if (GetSystemPowerStatus(out var s))
        {
            if (s.BatteryLifePercent == 255)
            {
                BatteryText.Text = "Secteur";
                BatteryIcon.Text = "\U0001F50C";
            }
            else
            {
                BatteryText.Text = $"{s.BatteryLifePercent}%";
                bool charging = s.ACLineStatus == 1;
                BatteryIcon.Text = charging ? "⚡" : (s.BatteryLifePercent <= 20 ? "\U0001FAAB" : "\U0001F50B");
            }
        }

        if (_tickCount++ % 30 == 0) _ = UpdateBluetoothAsync();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        UpdateAutoStartLabel();
        SettingsPanel.Visibility = Visibility.Visible;
        Fade(SettingsPanel, 1, 140);
        Fade(ExpandedView, 0, 100);
    }

    private void CloseSettings_Click(object sender, RoutedEventArgs e)
    {
        var fade = Fade(SettingsPanel, 0, 120);
        fade.Completed += (_, _) => SettingsPanel.Visibility = Visibility.Collapsed;
        Fade(ExpandedView, 1, 140);
    }

    private void Recenter_Click(object sender, RoutedEventArgs e) => PositionTop();
    private void Quit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private void AutoStart_Click(object sender, RoutedEventArgs e)
    {
        bool on = !IsAutoStart();
        SetAutoStart(on);
        _settings.AutoStart = on;
        _settings.Save();
        UpdateAutoStartLabel();
    }

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRegName = "DynamicIsland";

    private static bool IsAutoStart()
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return k?.GetValue(AppRegName) is not null;
    }

    private static void SetAutoStart(bool on)
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (k is null) return;
        if (on) k.SetValue(AppRegName, $"\"{Environment.ProcessPath}\"");
        else k.DeleteValue(AppRegName, false);
    }

    private void UpdateAutoStartLabel() =>
        AutoStartBtn.Content = IsAutoStart()
            ? "Démarrage avec Windows : activé"
            : "Démarrage avec Windows : désactivé";
}
