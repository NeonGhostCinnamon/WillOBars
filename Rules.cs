using System;
using System.Collections.Generic;
using System.Linq;

namespace WillOBars;

/// <summary>Something about the game that a rule can check.</summary>
public enum ConditionKind
{
    Job,
    InCombat,
    WeaponDrawn,
    Mounted,
    Crafting,
    Gathering,
    CityOrSanctuary,
    InDuty,
    Zone,
    Weather,
    InParty,
}

/// <summary>One check, like "Job is FSH, MIN" or "In combat is not".</summary>
[Serializable]
public class RuleCondition
{
    public ConditionKind Kind { get; set; }

    /// <summary>"is not" instead of "is".</summary>
    public bool Not { get; set; }

    /// <summary>
    /// What it's compared with: class/job ids, place name ids (Zone), weather ids, duty types (ContentType ids).
    /// For "In a duty" an empty list means any duty.
    /// </summary>
    public HashSet<uint> Ids { get; set; } = new();

    public RuleCondition Copy() => new() { Kind = Kind, Not = Not, Ids = new HashSet<uint>(Ids) };
}

/// <summary>"When all/any of these conditions match: show/hide."</summary>
[Serializable]
public class RuleCase
{
    /// <summary>All conditions must match (AND), or just one of them (OR).</summary>
    public bool MatchAll { get; set; } = true;

    public List<RuleCondition> Conditions { get; set; } = new();

    /// <summary>Show the hotbar when this case matches (or hide it).</summary>
    public bool Show { get; set; } = true;

    public RuleCase Copy() => new() { MatchAll = MatchAll, Show = Show, Conditions = Conditions.Select(c => c.Copy()).ToList() };
}

/// <summary>What the game is doing right now, read once per check.</summary>
public sealed class GameState
{
    public uint Job;
    public bool InCombat;
    public bool WeaponDrawn;
    public bool Mounted;
    public bool Crafting;
    public bool Gathering;
    public bool CityOrSanctuary;
    public bool InDuty;
    public uint DutyType;
    public uint PlaceName;
    public uint Weather;
    public bool InParty;
}

/// <summary>Checking rules and describing them in plain words.</summary>
public static class RuleLogic
{
    /// <summary>Whether the hotbar should be shown, and which case decided it (-1 = "otherwise").</summary>
    public static (bool Show, int Case) Evaluate(HotbarRule rule, GameState s)
    {
        var cases = rule.Cases ?? new List<RuleCase>();
        for (var i = 0; i < cases.Count; i++)
        {
            if (Matches(cases[i], s))
                return (cases[i].Show, i);
        }
        return (rule.OtherwiseShow, -1);
    }

    public static bool Matches(RuleCase c, GameState s)
    {
        if (c.Conditions.Count == 0)
            return false;
        return c.MatchAll ? c.Conditions.All(x => Matches(x, s)) : c.Conditions.Any(x => Matches(x, s));
    }

    public static bool Matches(RuleCondition c, GameState s)
    {
        var yes = c.Kind switch
        {
            ConditionKind.Job => c.Ids.Contains(s.Job),
            ConditionKind.InCombat => s.InCombat,
            ConditionKind.WeaponDrawn => s.WeaponDrawn,
            ConditionKind.Mounted => s.Mounted,
            ConditionKind.Crafting => s.Crafting,
            ConditionKind.Gathering => s.Gathering,
            ConditionKind.CityOrSanctuary => s.CityOrSanctuary,
            ConditionKind.InDuty => s.InDuty && (c.Ids.Count == 0 || c.Ids.Contains(s.DutyType)),
            ConditionKind.Zone => c.Ids.Contains(s.PlaceName),
            ConditionKind.Weather => c.Ids.Contains(s.Weather),
            ConditionKind.InParty => s.InParty,
            _ => false,
        };
        return yes != c.Not;
    }

    /// <summary>Conditions that pick from a list (and so get chips and a "+").</summary>
    public static bool HasIds(ConditionKind k) => k is ConditionKind.Job or ConditionKind.Zone or ConditionKind.Weather or ConditionKind.InDuty;

    public static string KindName(ConditionKind k) => k switch
    {
        ConditionKind.Job => "Job",
        ConditionKind.InCombat => "In combat",
        ConditionKind.WeaponDrawn => "Weapon drawn",
        ConditionKind.Mounted => "Mounted",
        ConditionKind.Crafting => "Crafting",
        ConditionKind.Gathering => "Gathering / fishing",
        ConditionKind.CityOrSanctuary => "In a city / sanctuary",
        ConditionKind.InDuty => "In a duty",
        ConditionKind.Zone => "Zone",
        ConditionKind.Weather => "Weather",
        ConditionKind.InParty => "In a party",
        _ => k.ToString(),
    };

    /// <summary>Groups for the "add condition" menu.</summary>
    public static readonly (string Group, ConditionKind[] Kinds)[] Menu =
    {
        ("You", new[] { ConditionKind.Job }),
        ("State", new[] { ConditionKind.InCombat, ConditionKind.WeaponDrawn, ConditionKind.Mounted, ConditionKind.Crafting, ConditionKind.Gathering }),
        ("Where", new[] { ConditionKind.CityOrSanctuary, ConditionKind.InDuty, ConditionKind.Zone }),
        ("World", new[] { ConditionKind.Weather }),
        ("Group", new[] { ConditionKind.InParty }),
    };

    // ---------------------------------------------------------------- plain-words summaries

    /// <summary>Short description of a whole rule, e.g. "Show only on FSH, MIN outside cities".</summary>
    public static string Summary(HotbarRule rule)
    {
        var cases = rule.Cases ?? new List<RuleCase>();
        var otherwise = rule.OtherwiseShow;
        var used = cases.Where(c => c.Conditions.Count > 0).ToList();
        if (used.Count == 0)
            return otherwise ? "Always shown." : "Always hidden.";

        if (used.Count == 1)
        {
            var c = used[0];
            var when = Describe(c);
            if (c.Show == otherwise)
                return otherwise ? "Always shown." : "Always hidden.";
            return c.Show ? $"Show only {when}." : $"Hide {when}.";
        }

        var parts = new List<string>();
        for (var i = 0; i < used.Count; i++)
        {
            var c = used[i];
            parts.Add($"{(i == 0 ? (c.Show ? "Show" : "Hide") : (c.Show ? "otherwise show" : "otherwise hide"))} {Describe(c)}");
        }
        parts.Add(otherwise ? "otherwise show" : "otherwise hide");
        return string.Join(", ", parts) + ".";
    }

    /// <summary>"on FSH and outside cities" for one case.</summary>
    public static string Describe(RuleCase c)
        => string.Join(c.MatchAll ? " and " : " or ", c.Conditions.Select(Describe));

    public static string Describe(RuleCondition c)
    {
        var n = c.Not;
        return c.Kind switch
        {
            ConditionKind.Job => (n ? "not on " : "on ") + JobList(c.Ids),
            ConditionKind.InCombat => n ? "out of combat" : "in combat",
            ConditionKind.WeaponDrawn => n ? "with the weapon sheathed" : "with the weapon drawn",
            ConditionKind.Mounted => n ? "when not mounted" : "when mounted",
            ConditionKind.Crafting => n ? "when not crafting" : "while crafting",
            ConditionKind.Gathering => n ? "when not gathering" : "while gathering or fishing",
            ConditionKind.CityOrSanctuary => n ? "outside cities" : "in cities",
            ConditionKind.InDuty => c.Ids.Count == 0
                ? (n ? "outside duties" : "in duties")
                : (n ? "outside " : "in ") + NameList(c.Ids, GameData.DutyTypeName),
            ConditionKind.Zone => (n ? "outside " : "in ") + NameList(c.Ids, GameData.PlaceNameText),
            ConditionKind.Weather => (n ? "when it isn't " : "when it's ") + NameList(c.Ids, GameData.WeatherName, " or "),
            ConditionKind.InParty => n ? "when solo" : "in a party",
            _ => c.Kind.ToString(),
        };
    }

    private static string JobList(HashSet<uint> ids)
    {
        if (ids.Count == 0)
            return "no job";
        var parts = new List<string>();
        var left = new HashSet<uint>(ids);
        foreach (var g in Jobs.All.GroupBy(j => j.Group))
        {
            var groupIds = g.Select(j => j.Id).ToList();
            if (groupIds.Count > 1 && groupIds.All(left.Contains))
            {
                parts.Add(Jobs.GroupName(g.Key).ToLowerInvariant());
                foreach (var id in groupIds)
                    left.Remove(id);
            }
        }
        parts.AddRange(Jobs.All.Where(j => left.Contains(j.Id)).Select(j => j.Abbreviation));
        if (parts.Count > 5)
            return $"{ids.Count} classes/jobs";
        return string.Join(", ", parts);
    }

    private static string NameList(HashSet<uint> ids, Func<uint, string> name, string join = ", ")
    {
        if (ids.Count == 0)
            return "nothing";
        var names = ids.Select(name).OrderBy(x => x).ToList();
        return names.Count > 3 ? $"{string.Join(join, names.Take(3))} (+{names.Count - 3})" : string.Join(join, names);
    }

    // ---------------------------------------------------------------- starting points

    /// <summary>A new rule: show only on this job, hidden the rest of the time.</summary>
    public static HotbarRule NewRule(int bar, uint job) => new()
    {
        Hotbar = bar,
        Cases = new List<RuleCase>
        {
            new() { Show = true, Conditions = { new RuleCondition { Kind = ConditionKind.Job, Ids = job != 0 ? new HashSet<uint> { job } : new HashSet<uint>() } } },
        },
        OtherwiseShow = false,
    };

    public static readonly (string Name, Func<uint, (List<RuleCase> Cases, bool Otherwise)> Make)[] Templates =
    {
        ("Only on the job I'm on", job => (new List<RuleCase> { new() { Show = true, Conditions = { new RuleCondition { Kind = ConditionKind.Job, Ids = { job } } } } }, false)),
        ("Only on crafters and gatherers", _ => (new List<RuleCase> { new() { Show = true, Conditions = { new RuleCondition { Kind = ConditionKind.Job, Ids = Jobs.All.Where(j => j.Group is JobGroup.Crafters or JobGroup.Gatherers).Select(j => j.Id).ToHashSet() } } } }, false)),
        ("Hide on crafters and gatherers", _ => (new List<RuleCase> { new() { Show = false, Conditions = { new RuleCondition { Kind = ConditionKind.Job, Ids = Jobs.All.Where(j => j.Group is JobGroup.Crafters or JobGroup.Gatherers).Select(j => j.Id).ToHashSet() } } } }, true)),
        ("Hide in combat", _ => (new List<RuleCase> { new() { Show = false, Conditions = { new RuleCondition { Kind = ConditionKind.InCombat } } } }, true)),
        ("Only in combat", _ => (new List<RuleCase> { new() { Show = true, Conditions = { new RuleCondition { Kind = ConditionKind.InCombat } } } }, false)),
        ("Hide in cities", _ => (new List<RuleCase> { new() { Show = false, Conditions = { new RuleCondition { Kind = ConditionKind.CityOrSanctuary } } } }, true)),
        ("Only in duties", _ => (new List<RuleCase> { new() { Show = true, Conditions = { new RuleCondition { Kind = ConditionKind.InDuty } } } }, false)),
    };
}
