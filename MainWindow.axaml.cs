using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace DynamicIsland;

public partial class MainWindow : Window
{
    private const double CompactWBase = 200, CompactWPlaying = 280, CompactH = 34;
    private const double ExpandedW = 380, ExpandedH = 250;
    private const double BannerW = 350, BannerH = 68;

    private static readonly Geometry PlayGeo = Geometry.Parse("M7,4 L19,12 L7,20 Z");
    private static readonly Geometry PauseGeo = Geometry.Parse("M7,4 H11 V20 H7 Z M13,4 H17 V20 H13 Z");

    private readonly IMediaService _media = PlatformServices.Media();
    private readonly INotificationService _notif = PlatformServices.Notifications();
    private readonly IBatteryService _battery = PlatformServices.Battery();
    private readonly IBluetoothService _bt = PlatformServices.Bluetooth();
    private readonly IAutoStartService _autoStart = PlatformServices.AutoStart();
    private readonly IFileIconService _fileIcons = PlatformServices.FileIcons();
    private readonly Settings _settings = Settings.Load();

    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _countdown = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _bannerTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    private bool _firstRun, _expanded, _bannerActive, _isPlaying;
    private string _currentTitle = "";
    private int _tick;
    private TimeSpan _timerRemaining;
    private bool _timerRunning;
    private readonly List<string> _shelf = new();

    private double CompactW => (_timerRunning || (_isPlaying && _currentTitle.Length > 0)) ? CompactWPlaying : CompactWBase;

    public MainWindow()
    {
        InitializeComponent();

        Island.PointerEntered += (_, _) => Expand();
        Island.PointerExited += (_, _) => Collapse();
        Island.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        Island.AddHandler(DragDrop.DropEvent, OnDrop);

        _clock.Tick += (_, _) => OnClockTick();
        _countdown.Tick += (_, _) => OnCountdownTick();
        _bannerTimer.Tick += (_, _) => HideBanner();

        _media.Changed += () => Dispatcher.UIThread.Post(UpdateMedia);
        _notif.Received += info => Dispatcher.UIThread.Post(() => ShowBanner(info));

        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        PositionTop();
        SwitchTab("media");
        UpdateTimerDisplay();
        UpdateAutoStartLabel();

        _clock.Start();
        _countdown.Start();
        OnClockTick();

        await _media.StartAsync();
        UpdateMedia();
        await _notif.StartAsync();
        _ = UpdateBluetoothAsync();

        if (!_settings.FirstRunDone) ShowFirstRun();
    }

    private void PositionTop()
    {
        var screen = Screens.Primary ?? (Screens.All.Count > 0 ? Screens.All[0] : null);
        if (screen is null) return;
        var wa = screen.WorkingArea;
        double scale = screen.Scaling;
        int w = (int)(Width * scale);
        Position = new PixelPoint(wa.X + (wa.Width - w) / 2, wa.Y);
    }

    private void Expand()
    {
        _bannerTimer.Stop();
        if (_bannerActive) { _bannerActive = false; NotifBanner.Opacity = 0; NotifBanner.IsVisible = false; }
        if (_expanded) return;
        _expanded = true;
        SetCorner(32);
        ExpandedView.IsVisible = true;
        Island.Width = ExpandedW;
        Island.Height = ExpandedH;
        ExpandedView.Opacity = 1;
        CompactView.Opacity = 0;
    }

    private void Collapse()
    {
        if (_firstRun) return;
        if (!_expanded && !_bannerActive) return;
        _expanded = false;
        _bannerActive = false;
        _bannerTimer.Stop();
        SettingsPanel.IsVisible = false;
        SettingsPanel.Opacity = 0;
        NotifBanner.IsVisible = false;
        NotifBanner.Opacity = 0;
        SetCorner(22);
        Island.Width = CompactW;
        Island.Height = CompactH;
        CompactView.Opacity = 1;
        ExpandedView.Opacity = 0;
        DispatcherTimer.RunOnce(() => { if (!_expanded) ExpandedView.IsVisible = false; }, TimeSpan.FromMilliseconds(180));
    }

    private void SetCorner(double bottom) => Island.CornerRadius = new CornerRadius(0, 0, bottom, bottom);

    private void UpdateCompactText()
    {
        if (_timerRunning && _timerRemaining.TotalSeconds > 0)
            CompactClock.Text = "⏱ " + FormatTime(_timerRemaining);
        else if (_isPlaying && _currentTitle.Length > 0)
            CompactClock.Text = _currentTitle;
        else
            CompactClock.Text = DateTime.Now.ToString("HH:mm");

        if (!_expanded && !_bannerActive)
            Island.Width = CompactW;
    }

    private void TabMedia_Click(object? s, RoutedEventArgs e) => SwitchTab("media");
    private void TabTimer_Click(object? s, RoutedEventArgs e) => SwitchTab("timer");
    private void TabFiles_Click(object? s, RoutedEventArgs e) => SwitchTab("files");

    private void SwitchTab(string tab)
    {
        MediaPanel.IsVisible = tab == "media";
        TimerPanel.IsVisible = tab == "timer";
        FilesPanel.IsVisible = tab == "files";

        var active = (IBrush)this.FindResource("TextBrush")!;
        var idle = (IBrush)this.FindResource("SubTextBrush")!;
        TabMediaBtn.Foreground = tab == "media" ? active : idle;
        TabTimerBtn.Foreground = tab == "timer" ? active : idle;
        TabFilesBtn.Foreground = tab == "files" ? active : idle;
    }

    private void UpdateMedia()
    {
        var m = _media.Current;
        if (m is null)
        {
            _isPlaying = false;
            _currentTitle = "";
            TrackTitle.Text = "Aucune lecture";
            TrackArtist.Text = "—";
            AlbumArt.Source = null;
            CompactArt.Source = null;
            CompactBars.IsVisible = false;
            UpdateCompactText();
            return;
        }

        _isPlaying = m.IsPlaying;
        _currentTitle = m.Title;
        TrackTitle.Text = m.Title.Length == 0 ? "Lecture en cours" : m.Title;
        TrackArtist.Text = m.Artist.Length == 0 ? "—" : m.Artist;
        AlbumArt.Source = m.Artwork;
        CompactArt.Source = m.AppIcon ?? m.Artwork;
        CompactBars.IsVisible = m.IsPlaying;
        PlayIcon.Data = m.IsPlaying ? PauseGeo : PlayGeo;
        ProgressFill.Width = (ExpandedW - 36) * Math.Clamp(m.Progress, 0, 1);
        UpdateCompactText();
    }

    private void PlayPause_Click(object? s, RoutedEventArgs e) => _media.PlayPause();
    private void Next_Click(object? s, RoutedEventArgs e) => _media.Next();
    private void Prev_Click(object? s, RoutedEventArgs e) => _media.Prev();

    private void TimerPlus1_Click(object? s, RoutedEventArgs e) => AddTimer(1);
    private void TimerPlus5_Click(object? s, RoutedEventArgs e) => AddTimer(5);
    private void TimerPlus10_Click(object? s, RoutedEventArgs e) => AddTimer(10);

    private void AddTimer(int minutes) { _timerRemaining += TimeSpan.FromMinutes(minutes); UpdateTimerDisplay(); }

    private void TimerStartPause_Click(object? s, RoutedEventArgs e)
    {
        if (_timerRemaining.TotalSeconds <= 0) return;
        _timerRunning = !_timerRunning;
        TimerStartBtn.Content = _timerRunning ? "Pause" : "Démarrer";
        UpdateCompactText();
    }

    private void TimerReset_Click(object? s, RoutedEventArgs e)
    {
        _timerRunning = false;
        _timerRemaining = TimeSpan.Zero;
        TimerStartBtn.Content = "Démarrer";
        UpdateTimerDisplay();
        UpdateCompactText();
    }

    private void OnCountdownTick()
    {
        if (!_timerRunning) return;
        _timerRemaining -= TimeSpan.FromSeconds(1);
        if (_timerRemaining.TotalSeconds <= 0)
        {
            _timerRemaining = TimeSpan.Zero;
            _timerRunning = false;
            TimerStartBtn.Content = "Démarrer";
            ShowBanner(new NotificationInfo("Minuteur", "Terminé", "Le minuteur est écoulé.", null));
        }
        UpdateTimerDisplay();
        UpdateCompactText();
    }

    private void UpdateTimerDisplay() => TimerDisplay.Text = FormatTime(_timerRemaining);

    private static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
        if (e.Data.Contains(DataFormats.Files)) { Expand(); SwitchTab("files"); }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.Data.GetFiles();
        if (files is null) return;
        foreach (var f in files)
        {
            var path = f.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path) && !_shelf.Contains(path)) _shelf.Add(path);
        }
        RebuildShelf();
        Expand();
        SwitchTab("files");
    }

    private void RebuildShelf()
    {
        ShelfPanel.Children.Clear();
        ShelfHint.IsVisible = _shelf.Count == 0;
        foreach (var path in _shelf)
            ShelfPanel.Children.Add(CreateChip(path));
    }

    private Control CreateChip(string path)
    {
        var icon = new Image { Width = 18, Height = 18, Margin = new Thickness(0, 0, 6, 0), Source = _fileIcons.ForFile(path) };
        var name = new TextBlock
        {
            Text = System.IO.Path.GetFileName(path),
            Foreground = (IBrush)this.FindResource("TextBrush")!,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 130,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var remove = new Button
        {
            Content = "✕",
            Classes = { "icon" },
            FontSize = 10,
            Padding = new Thickness(4, 0),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        remove.Click += (_, _) => { _shelf.Remove(path); RebuildShelf(); };

        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(icon);
        sp.Children.Add(name);
        sp.Children.Add(remove);

        var chip = new Border
        {
            Background = (IBrush)this.FindResource("ChipBrush")!,
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 6, 6),
            Padding = new Thickness(8, 4),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = sp
        };

        chip.PointerReleased += (_, _) => OpenFile(path);
        return chip;
    }

    private static void OpenFile(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else
                Process.Start("xdg-open", path);
        }
        catch { }
    }

    private void ShowBanner(NotificationInfo info)
    {
        if (_expanded || Island.IsPointerOver) return;
        BannerIcon.Source = info.Icon;
        BannerApp.Text = info.App;
        BannerTitle.Text = info.Title;
        BannerBody.Text = info.Body;
        BannerBody.IsVisible = !string.IsNullOrWhiteSpace(info.Body);

        _bannerActive = true;
        SetCorner(28);
        CompactView.Opacity = 0;
        NotifBanner.IsVisible = true;
        NotifBanner.Opacity = 1;
        Island.Width = BannerW;
        Island.Height = BannerH;

        _bannerTimer.Stop();
        _bannerTimer.Start();
    }

    private void HideBanner()
    {
        _bannerTimer.Stop();
        if (!_bannerActive) return;
        _bannerActive = false;
        NotifBanner.Opacity = 0;
        DispatcherTimer.RunOnce(() => NotifBanner.IsVisible = false, TimeSpan.FromMilliseconds(180));
        if (Island.IsPointerOver) { _expanded = false; Expand(); }
        else { SetCorner(22); Island.Width = CompactW; Island.Height = CompactH; CompactView.Opacity = 1; }
    }

    private async Task UpdateBluetoothAsync()
    {
        try
        {
            var devices = await _bt.GetAsync();
            if (devices.Count == 0) { BtText.IsVisible = false; return; }
            var d = devices[0];
            BtText.IsVisible = true;
            BtText.Text = $"BT {d.Percent}%";
        }
        catch { BtText.IsVisible = false; }
    }

    private void OnClockTick()
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString("HH:mm");
        DateText.Text = now.ToString("dddd d MMMM");
        UpdateCompactText();

        var b = _battery.Get();
        if (b.Present)
        {
            BatteryText.Text = $"{b.Percent}%";
            BatteryFill.Width = Math.Max(2, 14 * b.Percent / 100.0);
            BatteryFill.Background = b.Percent <= 20 && !b.Charging
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B))
                : (IBrush)this.FindResource("AccentBrush")!;
        }
        else
        {
            BatteryText.Text = "Secteur";
            BatteryFill.Width = 14;
        }

        if (_tick++ % 30 == 0) _ = UpdateBluetoothAsync();
    }

    private void Settings_Click(object? s, RoutedEventArgs e)
    {
        UpdateAutoStartLabel();
        SettingsPanel.IsVisible = true;
        SettingsPanel.Opacity = 1;
        ExpandedView.Opacity = 0;
    }

    private void CloseSettings_Click(object? s, RoutedEventArgs e)
    {
        SettingsPanel.Opacity = 0;
        DispatcherTimer.RunOnce(() => SettingsPanel.IsVisible = false, TimeSpan.FromMilliseconds(160));
        ExpandedView.Opacity = 1;
    }

    private void Recenter_Click(object? s, RoutedEventArgs e) => PositionTop();

    private void Quit_Click(object? s, RoutedEventArgs e)
    {
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d)
            d.Shutdown();
    }

    private void AutoStart_Click(object? s, RoutedEventArgs e)
    {
        bool on = !_autoStart.IsEnabled();
        _autoStart.Set(on);
        _settings.AutoStart = on;
        _settings.Save();
        UpdateAutoStartLabel();
    }

    private void UpdateAutoStartLabel() =>
        AutoStartBtn.Content = _autoStart.IsEnabled() ? "Démarrage auto : activé" : "Démarrage auto : désactivé";

    private void ShowFirstRun()
    {
        _firstRun = true;
        SetCorner(32);
        CompactView.Opacity = 0;
        FirstRunPanel.IsVisible = true;
        FirstRunPanel.Opacity = 1;
        Island.Width = ExpandedW;
        Island.Height = ExpandedH;
    }

    private void FirstRunEnableStart_Click(object? s, RoutedEventArgs e)
    {
        _autoStart.Set(true);
        _settings.AutoStart = true;
        UpdateAutoStartLabel();
        FirstRunStartBtn.Content = "Démarrage activé ✓";
    }

    private void FirstRunDone_Click(object? s, RoutedEventArgs e)
    {
        _firstRun = false;
        _settings.FirstRunDone = true;
        _settings.Save();
        FirstRunPanel.Opacity = 0;
        DispatcherTimer.RunOnce(() => FirstRunPanel.IsVisible = false, TimeSpan.FromMilliseconds(220));
        SetCorner(22);
        Island.Width = CompactW;
        Island.Height = CompactH;
        CompactView.Opacity = 1;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
