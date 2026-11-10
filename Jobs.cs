using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace WillOBars;

public enum JobGroup
{
    Tanks,
    Healers,
    Melee,
    Ranged,
    Casters,
    Crafters,
    Gatherers,
}

public sealed record JobInfo(uint Id, string Name, string Abbreviation, JobGroup Group, bool IsBaseClass, uint IconId);

/// <summary>Every class and job from the game data, grouped by role, in the game's own order.</summary>
public static class Jobs
{
    public const uint FisherId = 18;

    private const uint DiscipleOfTheHandCategory = 33;
    private const uint DiscipleOfTheLandCategory = 32;

    public static IReadOnlyList<JobInfo> All { get; private set; } = new List<JobInfo>();

    public static void Load(IDataManager data)
    {
        var sheet = data.GetExcelSheet<ClassJob>();
        var list = new List<(JobInfo Info, int Priority)>();

        foreach (var row in sheet)
        {
            // Row 0 is "Adventurer"; real classes/jobs all have an experience slot.
            if (row.RowId == 0 || row.ExpArrayIndex < 0)
                continue;

            var name = row.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var group = row.ClassJobCategory.RowId switch
            {
                DiscipleOfTheHandCategory => JobGroup.Crafters,
                DiscipleOfTheLandCategory => JobGroup.Gatherers,
                _ => row.Role switch
                {
                    1 => JobGroup.Tanks,
                    4 => JobGroup.Healers,
                    2 => JobGroup.Melee,
                    // Role 3 is every ranged job; intelligence users are the casters.
                    _ => row.PrimaryStat == 4 ? JobGroup.Casters : JobGroup.Ranged,
                },
            };

            // Base classes (Gladiator, Conjurer...) have no job index; crafters/gatherers are their own thing.
            var isBaseClass = row.JobIndex == 0 && group is not (JobGroup.Crafters or JobGroup.Gatherers) && !row.IsLimitedJob;

            var info = new JobInfo(
                row.RowId,
                CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name),
                row.Abbreviation.ExtractText(),
                group,
                isBaseClass,
                62100 + row.RowId);

            list.Add((info, row.UIPriority));
        }

        All = list
            .OrderBy(x => x.Info.Group)
            .ThenBy(x => x.Info.IsBaseClass)
            .ThenBy(x => x.Priority)
            .ThenBy(x => x.Info.Id)
            .Select(x => x.Info)
            .ToList();
    }

    public static JobInfo? Get(uint id) => All.FirstOrDefault(j => j.Id == id);

    public static string GroupName(JobGroup g) => g switch
    {
        JobGroup.Tanks => "Tanks",
        JobGroup.Healers => "Healers",
        JobGroup.Melee => "Melee",
        JobGroup.Ranged => "Ranged",
        JobGroup.Casters => "Casters",
        JobGroup.Crafters => "Crafters",
        _ => "Gatherers",
    };
}
