using System.IO;
using System.Text.Json;

namespace DataCollector.Launcher.Wpf;

public sealed class LauncherSettings
{
    public string ServerBaseUrl { get; set; } = "http://localhost:5180";

    public string AgentNodeName { get; set; } = "W01-Agent";

    public static LauncherSettings Load(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new LauncherSettings();
            }

            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<LauncherSettings>(json) ?? new LauncherSettings();
        }
        catch
        {
            return new LauncherSettings();
        }
    }

    public void Save(string filePath)
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
        File.WriteAllText(filePath, json);
    }
}
