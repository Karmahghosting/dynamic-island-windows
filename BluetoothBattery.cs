using Windows.Devices.Enumeration;

namespace DynamicIsland;

internal sealed record BtDevice(string Name, int Percent);

internal static class BluetoothBattery
{
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private const string ConnectedSelector =
        "System.Devices.Aep.IsConnected:=System.StructuredQueryType.Boolean#True";

    public static async Task<List<BtDevice>> GetAsync()
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

                int percent = value switch
                {
                    byte b => b,
                    int i => i,
                    uint u => (int)u,
                    _ => -1
                };
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
