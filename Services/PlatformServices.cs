namespace DynamicIsland;

internal static class PlatformServices
{
#if WINDOWS
    public static IMediaService Media() => new Platform.Windows.WindowsMediaService();
    public static INotificationService Notifications() => new Platform.Windows.WindowsNotificationService();
    public static IBatteryService Battery() => new Platform.Windows.WindowsBatteryService();
    public static IBluetoothService Bluetooth() => new Platform.Windows.WindowsBluetoothService();
    public static IAutoStartService AutoStart() => new Platform.Windows.WindowsAutoStartService();
    public static IFileIconService FileIcons() => new Platform.Windows.WindowsFileIconService();
#else
    public static IMediaService Media() => new Platform.Linux.LinuxMediaService();
    public static INotificationService Notifications() => new Platform.Linux.LinuxNotificationService();
    public static IBatteryService Battery() => new Platform.Linux.LinuxBatteryService();
    public static IBluetoothService Bluetooth() => new Platform.Linux.LinuxBluetoothService();
    public static IAutoStartService AutoStart() => new Platform.Linux.LinuxAutoStartService();
    public static IFileIconService FileIcons() => new Platform.Linux.LinuxFileIconService();
#endif
}
