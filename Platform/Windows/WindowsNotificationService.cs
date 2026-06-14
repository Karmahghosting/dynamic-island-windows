using System.IO;
using Avalonia.Media.Imaging;
using Windows.Storage.Streams;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace DynamicIsland.Platform.Windows;

internal sealed class WindowsNotificationService : INotificationService
{
    private readonly HashSet<uint> _seen = new();
    private UserNotificationListener? _listener;
    private System.Threading.Timer? _timer;
    private bool _baseline;
    private int _busy;

    public event Action<NotificationInfo>? Received;

    public async Task StartAsync()
    {
        try
        {
            _listener = UserNotificationListener.Current;
            await _listener.RequestAccessAsync();
            _timer = new System.Threading.Timer(_ => Poll(), null, 0, 3000);
        }
        catch { }
    }

    private async void Poll()
    {
        if (_listener is null) return;
        if (System.Threading.Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var list = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
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
        catch { }
        finally { System.Threading.Interlocked.Exchange(ref _busy, 0); }
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

            Bitmap? icon = null;
            try
            {
                var logo = n.AppInfo?.DisplayInfo?.GetLogo(new global::Windows.Foundation.Size(48, 48));
                if (logo is not null) icon = await LoadAsync(logo);
            }
            catch { }

            return new NotificationInfo(app, title, body, icon);
        }
        catch { return null; }
    }

    private static async Task<Bitmap?> LoadAsync(IRandomAccessStreamReference r)
    {
        using var ras = await r.OpenReadAsync();
        using var net = ras.AsStreamForRead();
        using var ms = new MemoryStream();
        await net.CopyToAsync(ms);
        ms.Position = 0;
        return new Bitmap(ms);
    }
}
