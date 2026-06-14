using Avalonia.Media.Imaging;

namespace DynamicIsland;

public sealed record MediaInfo(
    string Title,
    string Artist,
    bool IsPlaying,
    double Progress,
    Bitmap? Artwork,
    Bitmap? AppIcon);

public sealed record NotificationInfo(string App, string Title, string Body, Bitmap? Icon);

public sealed record BatteryInfo(int Percent, bool Charging, bool OnAc, bool Present);

public sealed record BtDevice(string Name, int Percent);

public sealed record SystemStats(double CpuPercent, int RamPercent, string RamText, TimeSpan Uptime);

public interface IMediaService
{
    event Action? Changed;
    MediaInfo? Current { get; }
    Task StartAsync();
    void PlayPause();
    void Next();
    void Prev();
}

public interface INotificationService
{
    event Action<NotificationInfo>? Received;
    Task StartAsync();
}

public interface IBatteryService
{
    BatteryInfo Get();
}

public interface IBluetoothService
{
    Task<List<BtDevice>> GetAsync();
}

public interface IAutoStartService
{
    bool IsEnabled();
    void Set(bool on);
}

public interface IFileIconService
{
    Bitmap? ForFile(string path);
}

public interface ISystemStatsService
{
    SystemStats Get();
}
