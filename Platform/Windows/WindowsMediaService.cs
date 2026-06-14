using System.IO;
using Avalonia.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DynamicIsland.Platform.Windows;

internal sealed class WindowsMediaService : IMediaService
{
    private GlobalSystemMediaTransportControlsSessionManager? _mgr;
    private GlobalSystemMediaTransportControlsSession? _session;

    public event Action? Changed;
    public MediaInfo? Current { get; private set; }

    public async Task StartAsync()
    {
        try
        {
            _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _mgr.CurrentSessionChanged += (_, _) => Hook();
            Hook();
        }
        catch { }
    }

    private void Hook()
    {
        _session = _mgr?.GetCurrentSession();
        if (_session is null) { Current = null; Changed?.Invoke(); return; }

        _session.MediaPropertiesChanged += (_, _) => _ = Refresh();
        _session.PlaybackInfoChanged += (_, _) => _ = Refresh();
        _session.TimelinePropertiesChanged += (_, _) => _ = Refresh();
        _ = Refresh();
    }

    private async Task Refresh()
    {
        var s = _session;
        if (s is null) return;
        try
        {
            var props = await s.TryGetMediaPropertiesAsync();
            var info = s.GetPlaybackInfo();
            var tl = s.GetTimelineProperties();

            double total = (tl.EndTime - tl.StartTime).TotalSeconds;
            double pos = (tl.Position - tl.StartTime).TotalSeconds;
            double progress = total > 0 ? pos / total : 0;

            Bitmap? art = await LoadAsync(props.Thumbnail);
            Bitmap? icon = WindowsIcon.ForApp(s.SourceAppUserModelId);

            Current = new MediaInfo(
                props.Title ?? "",
                props.Artist ?? "",
                info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                progress,
                art,
                icon);
            Changed?.Invoke();
        }
        catch { }
    }

    public void PlayPause() { try { _ = _session?.TryTogglePlayPauseAsync(); } catch { } }
    public void Next() { try { _ = _session?.TrySkipNextAsync(); } catch { } }
    public void Prev() { try { _ = _session?.TrySkipPreviousAsync(); } catch { } }

    private static async Task<Bitmap?> LoadAsync(IRandomAccessStreamReference? r)
    {
        if (r is null) return null;
        try
        {
            using var ras = await r.OpenReadAsync();
            using var net = ras.AsStreamForRead();
            using var ms = new MemoryStream();
            await net.CopyToAsync(ms);
            ms.Position = 0;
            return new Bitmap(ms);
        }
        catch { return null; }
    }
}
