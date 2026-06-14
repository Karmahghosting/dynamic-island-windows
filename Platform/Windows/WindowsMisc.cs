using System.Runtime.InteropServices;
using Windows.Devices.Enumeration;

namespace DynamicIsland.Platform.Windows;

internal sealed class WindowsBatteryService : IBatteryService
{
    public BatteryInfo Get()
    {
        if (!GetSystemPowerStatus(out var s) || s.BatteryLifePercent == 255)
            return new BatteryInfo(100, s.ACLineStatus == 1, true, false);
        return new BatteryInfo(s.BatteryLifePercent, s.ACLineStatus == 1, s.ACLineStatus == 1, true);
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
}

internal sealed class WindowsBluetoothService : IBluetoothService
{
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private const string ConnectedSelector =
        "System.Devices.Aep.IsConnected:=System.StructuredQueryType.Boolean#True";

    public async Task<List<BtDevice>> GetAsync()
    {
        var result = new List<BtDevice>();
        try
        {
            var props = new[] { BatteryKey, "System.ItemNameDisplay" };
            var devices = await DeviceInformation.FindAllAsync(
                ConnectedSelector, props, DeviceInformationKind.AssociationEndpoint);

            foreach (var d in devices)
            {
                if (!d.Properties.TryGetValue(BatteryKey, out var value) || value is null) continue;
                int percent = value switch { byte b => b, int i => i, uint u => (int)u, _ => -1 };
                if (percent < 0 || percent > 100) continue;

                string name = d.Name;
                if (d.Properties.TryGetValue("System.ItemNameDisplay", out var n) && n is string s && s.Length > 0)
                    name = s;
                result.Add(new BtDevice(name, percent));
            }
        }
        catch { }
        return result;
    }
}

internal sealed class WindowsAutoStartService : IAutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "DynamicIsland";

    public bool IsEnabled()
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(Name) is not null;
    }

    public void Set(bool on)
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
        if (k is null) return;
        if (on) k.SetValue(Name, $"\"{Environment.ProcessPath}\"");
        else k.DeleteValue(Name, false);
    }
}
