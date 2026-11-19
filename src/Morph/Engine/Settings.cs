using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Morph.Engine;

/// <summary>What Morph remembers between runs, in %LOCALAPPDATA%\Morph\settings.json.</summary>
public sealed class Settings
{
    /// <summary>Quality preset per category, e.g. "Video": "Balanced".</summary>
    public Dictionary<string, Quality> Quality { get; set; } = [];
    /// <summary>Last format id per category, e.g. "Audio": "mp3".</summary>
    public Dictionary<string, string> LastFormat { get; set; } = [];
    public bool UseCustomFolder { get; set; }
    public string? CustomFolder { get; set; }
    public bool OpenFolderWhenDone { get; set; }

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Morph", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception) { }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception) { }
    }
}
