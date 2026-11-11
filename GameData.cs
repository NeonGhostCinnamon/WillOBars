using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace WillOBars;

/// <summary>Names from the game data, for the rule editor (zones, weather, duty types).</summary>
public static class GameData
{
    /// <summary>One pickable entry; several game ids can share one name (they're picked together).</summary>
    public sealed record Entry(string Name, uint[] Ids);

    public static IReadOnlyList<Entry> Zones { get; private set; } = new List<Entry>();
    public static IReadOnlyList<Entry> Weathers { get; private set; } = new List<Entry>();
    public static IReadOnlyList<Entry> DutyTypes { get; private set; } = new List<Entry>();

    private static readonly Dictionary<uint, string> PlaceNames = new();
    private static readonly Dictionary<uint, string> WeatherNames = new();
    private static readonly Dictionary<uint, string> DutyTypeNames = new();

    // Duty types worth picking (ContentType rows): dungeons, guildhests, trials, raids, PvP,
    // deep dungeons, ultimates, Eureka, Bozja, variant/criterion.
    private static readonly uint[] DutyTypeIds = { 2, 3, 4, 5, 6, 21, 26, 28, 29, 30 };

    public static void Load(IDataManager data)
    {
        // Zones: every territory with a name, one entry per place name.
        var zones = new Dictionary<uint, string>();
        foreach (var t in data.GetExcelSheet<TerritoryType>())
        {
            var id = t.PlaceName.RowId;
            if (id == 0 || zones.ContainsKey(id))
                continue;
            var name = t.PlaceName.ValueNullable?.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name))
                continue;
            zones[id] = name;
        }
        foreach (var (id, name) in zones)
            PlaceNames[id] = name;
        Zones = zones.GroupBy(z => z.Value).Select(g => new Entry(g.Key, g.Select(x => x.Key).ToArray()))
            .OrderBy(e => e.Name).ToList();

        // Weather: one entry per name.
        foreach (var w in data.GetExcelSheet<Weather>())
        {
            var name = w.Name.ExtractText();
            if (!string.IsNullOrWhiteSpace(name))
                WeatherNames[w.RowId] = name;
        }
        Weathers = WeatherNames.GroupBy(w => w.Value).Select(g => new Entry(g.Key, g.Select(x => x.Key).ToArray()))
            .OrderBy(e => e.Name).ToList();

        // Duty types.
        var types = data.GetExcelSheet<ContentType>();
        var list = new List<Entry>();
        foreach (var id in DutyTypeIds)
        {
            var row = types.GetRowOrDefault(id);
            var name = row?.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name))
                continue;
            DutyTypeNames[id] = name;
            list.Add(new Entry(name, new[] { id }));
        }
        DutyTypes = list;
    }

    public static string PlaceNameText(uint id) => PlaceNames.TryGetValue(id, out var n) ? n : $"zone {id}";
    public static string WeatherName(uint id) => WeatherNames.TryGetValue(id, out var n) ? n : $"weather {id}";
    public static string DutyTypeName(uint id) => DutyTypeNames.TryGetValue(id, out var n) ? n : $"duty type {id}";

    /// <summary>The entries of a list that are (partly) picked, for chips.</summary>
    public static IEnumerable<Entry> Picked(IReadOnlyList<Entry> list, HashSet<uint> ids)
        => list.Where(e => e.Ids.Any(ids.Contains));
}
