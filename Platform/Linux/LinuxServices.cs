using System.Diagnostics;
using System.IO;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace DynamicIsland.Platform.Linux;

internal static class Shell
{
    public static string Run(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return "";
            string outp = p.StandardOutput.ReadToEnd();
            p.WaitForExit(2000);
            return outp.Trim();
        }
        catch { return ""; }
    }
}

internal sealed class LinuxMediaService : IMediaService
{
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1) };
    private string _signature = "";

    public event Action? Changed;
    public MediaInfo? Current { get; private set; }

    public Task StartAsync()
    {
        _poll.Tick += (_, _) => Refresh();
        _poll.Start();
        Refresh();
        return Task.CompletedTask;
    }

    private void Refresh()
    {
        string status = Shell.Run("playerctl", "status");
        if (status.Length == 0 || status.StartsWith("No players"))
        {
            if (Current is not null) { Current = null; _signature = ""; Changed?.Invoke(); }
            return;
        }

        string meta = Shell.Run("playerctl", "metadata --format \"{{title}}@@@{{artist}}@@@{{mpris:length}}@@@{{position}}\"");
        var parts = meta.Split("@@@");
        string title = parts.Length > 0 ? parts[0] : "";
        string artist = parts.Length > 1 ? parts[1] : "";

        double progress = 0;
        if (parts.Length > 3 && long.TryParse(parts[2], out var len) && len > 0 && double.TryParse(parts[3], out var pos))
            progress = Math.Clamp(pos / len, 0, 1);

        bool playing = status.Equals("Playing", StringComparison.OrdinalIgnoreCase);
        string sig = $"{title}|{artist}|{playing}";
        Current = new MediaInfo(title, artist, playing, progress, null, null);

        if (sig != _signature) { _signature = sig; Changed?.Invoke(); }
        else Changed?.Invoke();
    }

    public void PlayPause() => Shell.Run("playerctl", "play-pause");
    public void Next() => Shell.Run("playerctl", "next");
    public void Prev() => Shell.Run("playerctl", "previous");
}

internal sealed class LinuxBatteryService : IBatteryService
{
    public BatteryInfo Get()
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories("/sys/class/power_supply"))
            {
                var typePath = Path.Combine(dir, "type");
                if (!File.Exists(typePath)) continue;
                if (File.ReadAllText(typePath).Trim() != "Battery") continue;

                var capPath = Path.Combine(dir, "capacity");
                var statusPath = Path.Combine(dir, "status");
                if (!File.Exists(capPath)) continue;

                int percent = int.TryParse(File.ReadAllText(capPath).Trim(), out var c) ? c : 100;
                bool charging = File.Exists(statusPath) &&
                                File.ReadAllText(statusPath).Trim().Equals("Charging", StringComparison.OrdinalIgnoreCase);
                return new BatteryInfo(percent, charging, charging, true);
            }
        }
        catch { }
        return new BatteryInfo(100, true, true, false);
    }
}

internal sealed class LinuxBluetoothService : IBluetoothService
{
    public Task<List<BtDevice>> GetAsync()
    {
        var list = new List<BtDevice>();
        try
        {
            string dump = Shell.Run("upower", "-d");
            string? name = null;
            foreach (var raw in dump.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("native-path:") && line.Contains("bluetooth")) name ??= "Bluetooth";
                if (line.StartsWith("model:")) name = line["model:".Length..].Trim();
                if (line.StartsWith("percentage:") && name is not null)
                {
                    var pct = line["percentage:".Length..].Trim().TrimEnd('%');
                    if (int.TryParse(pct.Split('.')[0], out var p)) { list.Add(new BtDevice(name, p)); name = null; }
                }
            }
        }
        catch { }
        return Task.FromResult(list);
    }
}

internal sealed class LinuxAutoStartService : IAutoStartService
{
    private static string DesktopPath
    {
        get
        {
            string config = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(config, "autostart", "dynamic-island.desktop");
        }
    }

    public bool IsEnabled() => File.Exists(DesktopPath);

    public void Set(bool on)
    {
        try
        {
            if (on)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DesktopPath)!);
                string exe = Environment.ProcessPath ?? "dynamic-island";
                File.WriteAllText(DesktopPath,
                    "[Desktop Entry]\nType=Application\nName=Dynamic Island\n" +
                    $"Exec={exe}\nX-GNOME-Autostart-enabled=true\n");
            }
            else if (File.Exists(DesktopPath))
            {
                File.Delete(DesktopPath);
            }
        }
        catch { }
    }
}

internal sealed class LinuxFileIconService : IFileIconService
{
    public Bitmap? ForFile(string path) => null;
}

internal sealed class LinuxNotificationService : INotificationService
{
    public event Action<NotificationInfo>? Received;

    public Task StartAsync()
    {
        _ = Received;
        return Task.CompletedTask;
    }
}

internal sealed class LinuxSystemStatsService : ISystemStatsService
{
    private long _prevIdle, _prevTotal;
    private double _lastCpu;

    public SystemStats Get()
    {
        try
        {
            var fields = File.ReadLines("/proc/stat").First().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            long idle = 0, total = 0;
            for (int i = 1; i < fields.Length; i++)
            {
                if (long.TryParse(fields[i], out var v)) { total += v; if (i == 4 || i == 5) idle += v; }
            }
            if (_prevTotal != 0)
            {
                long dt = total - _prevTotal, di = idle - _prevIdle;
                if (dt > 0) _lastCpu = Math.Clamp((dt - di) * 100.0 / dt, 0, 100);
            }
            _prevIdle = idle; _prevTotal = total;
        }
        catch { }

        int ramPercent = 0;
        string ramText = "";
        try
        {
            long memTotal = 0, memAvail = 0;
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:")) memTotal = ParseKb(line);
                else if (line.StartsWith("MemAvailable:")) { memAvail = ParseKb(line); break; }
            }
            if (memTotal > 0)
            {
                long used = memTotal - memAvail;
                ramPercent = (int)(used * 100 / memTotal);
                ramText = $"{used / 1048576.0:0.0} / {memTotal / 1048576.0:0.0} Go";
            }
        }
        catch { }

        var uptime = TimeSpan.Zero;
        try
        {
            var up = File.ReadAllText("/proc/uptime").Split(' ')[0];
            if (double.TryParse(up, System.Globalization.CultureInfo.InvariantCulture, out var secs))
                uptime = TimeSpan.FromSeconds(secs);
        }
        catch { }

        return new SystemStats(_lastCpu, ramPercent, ramText, uptime);
    }

    private static long ParseKb(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && long.TryParse(parts[1], out var v) ? v : 0;
    }
}
