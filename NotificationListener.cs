using System.IO;
using System.Windows.Media.Imaging;
using Windows.Storage.Streams;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace DynamicIsland;

internal sealed record NotificationInfo(string App, string Title, string Body, BitmapImage? Icon);

internal sealed class NotificationListener
{
    private readonly HashSet<uint> _seen = new();
    private UserNotificationListener? _listener;
    private bool _baseline;

    public event Action<NotificationInfo>? Received;

    public async Task<bool> StartAsync()
    {
        try
        {
            _listener = UserNotificationListener.Current;
            var status = await _listener.RequestAccessAsync();
            return status == UserNotificationListenerAccessStatus.Allowed;
        }
        catch
        {
            _listener = null;
            return false;
        }
    }

    public async Task PollAsync()
    {
        if (_listener is null) return;

        IReadOnlyList<UserNotification> list;
        try { list = await _listener.GetNotificationsAsync(NotificationKinds.Toast); }
        catch { return; }

        var current = new HashSet<uint>();
        var fresh = new List<UserNotification>();
        foreach (var n in list)
        {
            current.Add(n.Id);
            if (!_seen.Contains(n.Id)) fresh.Add(n);
        }

        _seen.Clear();
        foreach (var id in current) _seen.Add(id);

        if (!_baseline) { _baseline = true; return; }

        foreach (var n in fresh)
        {
            var info = await ToInfoAsync(n);
            if (info is not null) Received?.Invoke(info);
        }
    }

    private static async Task<NotificationInfo?> ToInfoAsync(UserNotification n)
    {
        try
        {
            string app = n.AppInfo?.DisplayInfo?.DisplayName ?? "Notification";
            string title = "", body = "";

            var binding = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
            if (binding is not null)
            {
                var texts = binding.GetTextElements();
                if (texts.Count > 0) title = texts[0].Text;
                if (texts.Count > 1) body = string.Join(" ", texts.Skip(1).Select(t => t.Text));
            }

            BitmapImage? icon = null;
            try
            {
                var logo = n.AppInfo?.DisplayInfo?.GetLogo(new Windows.Foundation.Size(48, 48));
                if (logo is not null) icon = await LoadAsync(logo);
            }
            catch { }

            return new NotificationInfo(app, title, body, icon);
        }
        catch { return null; }
    }

    private static async Task<BitmapImage?> LoadAsync(IRandomAccessStreamReference r)
    {
        using var ras = await r.OpenReadAsync();
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
}
