using System.IO;
using System.Text.Json;

namespace DynamicIsland;

internal sealed class Settings
{
    public bool FirstRunDone { get; set; }
    public bool AutoStart { get; set; }
    public string Accent { get; set; } = "#34C759";
    public bool Clock12h { get; set; }
    public string Position { get; set; } = "center";
    public List<string> Shelf { get; set; } = new();

    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland");

    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch { }
    }
}
