using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WillOBars;

/// <summary>
/// Export and import of Will-o-Bars' own settings (the hotbar rules and the look of the window), as a short
/// text to paste anywhere, or as a file in Documents\Will-o-Bars that survives resetting the plugin's data.
/// </summary>
public static class SettingsTransfer
{
    private const string Prefix = "WOB1:";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public sealed class Data
    {
        public int Version { get; set; } = 3;
        public DateTime Exported { get; set; }
        public List<HotbarRule> Rules { get; set; } = new();
        public ThemeMode ThemeChoice { get; set; } = ThemeMode.Dark;
    }

    /// <summary>
    /// The folder the player picked for saves (outside the plugin's own folder, so a reset doesn't delete it),
    /// or null when none is picked yet. Set by the plugin from the configuration.
    /// </summary>
    public static string? Folder { get; set; }

    /// <summary>Where older versions saved things: Documents\Will-o-Bars.</summary>
    public static string OldDocumentsFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Will-o-Bars");

    public static Data From(Configuration config) => new()
    {
        Exported = DateTime.Now,
        Rules = config.Rules.Select(r => r.Copy()).ToList(),
        ThemeChoice = config.ThemeChoice,
    };

    /// <summary>Short text for the clipboard: "WOB1:" + compressed, base64 JSON.</summary>
    public static string ToText(Data data)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data, Json));
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.SmallestSize))
            gz.Write(bytes);
        return Prefix + Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>Reads a clipboard text or a file's JSON. Returns null (with a reason) when it isn't valid.</summary>
    public static Data? Parse(string text, out string error)
    {
        error = string.Empty;
        text = text.Trim();
        try
        {
            string json;
            if (text.StartsWith(Prefix, StringComparison.Ordinal))
            {
                var bytes = Convert.FromBase64String(text[Prefix.Length..]);
                using var gz = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
                using var reader = new StreamReader(gz, Encoding.UTF8);
                json = reader.ReadToEnd();
            }
            else if (text.StartsWith('{'))
            {
                json = text;
            }
            else
            {
                error = "That isn't Will-o-Bars settings text.";
                return null;
            }

            var data = JsonSerializer.Deserialize<Data>(json, Json);
            if (data == null)
            {
                error = "The settings are empty.";
                return null;
            }

            // Keep only sensible rules: hotbars 1-10, one rule each.
            var seen = new HashSet<int>();
            data.Rules = data.Rules.Where(r => r.Hotbar is >= 1 and <= 10 && seen.Add(r.Hotbar)).ToList();

            // Settings from version 1 saved an untouched "Own actions per class/job" box as false.
            if (data.Version < 2)
            {
                foreach (var r in data.Rules)
                {
                    if (r.SeparatePerJob == false)
                        r.SeparatePerJob = null;
                }
                data.Version = 2;
            }

            // Settings from before version 3 have no cases yet.
            foreach (var r in data.Rules)
            {
                if (r.Cases == null)
                    r.Upgrade();
            }
            data.Version = 3;
            return data;
        }
        catch (Exception)
        {
            error = "The settings text is damaged or incomplete.";
            return null;
        }
    }

    public static void ApplyTo(Data data, Configuration config)
    {
        config.Rules.Clear();
        config.Rules.AddRange(data.Rules);
        config.ThemeChoice = data.ThemeChoice;
    }

    /// <summary>Saves to Documents\Will-o-Bars\settings-[date].json and returns the path.</summary>
    public static string SaveFile(Data data)
    {
        if (string.IsNullOrEmpty(Folder))
            throw new InvalidOperationException("Pick a folder first.");
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, $"settings-{data.Exported:yyyy-MM-dd_HH-mm-ss}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(data, Json));
        return path;
    }

    /// <summary>Saved settings files, newest first.</summary>
    public static List<string> Files()
    {
        if (string.IsNullOrEmpty(Folder) || !Directory.Exists(Folder))
            return new List<string>();
        return Directory.EnumerateFiles(Folder, "settings-*.json").OrderByDescending(f => f).ToList();
    }
}
