using Dalamud.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace WillOBars;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 3;

    /// <summary>One rule per hotbar. Starts empty: the player adds the rules they want.</summary>
    public List<HotbarRule> Rules { get; set; } = new();

    /// <summary>
    /// Keep exactly one rule per hotbar: drop duplicates (keeping the first) and rules for hotbars outside 1-10.
    /// Returns true when something was removed.
    /// </summary>
    public bool Normalize()
    {
        var changed = false;

        // Version 1 always stored "Own actions per class/job" as ticked or unticked, so an untouched box
        // (false) made Will-o-Bars switch the game's "Shared" setting on. From version 2 an untouched box is
        // stored as null: the game's own setting is left alone until the player ticks or unticks it.
        if (Version < 2)
        {
            foreach (var r in Rules)
            {
                if (r.SeparatePerJob == false)
                    r.SeparatePerJob = null;
            }
            Version = 2;
            changed = true;
        }

        // Version 3: rules became cases ("when ... show/hide", "otherwise ..."). Older rules are converted.
        if (Version < 3)
        {
            foreach (var r in Rules)
                r.Upgrade();
            Version = 3;
            changed = true;
        }
        foreach (var r in Rules)
        {
            if (r.Cases == null)
            {
                r.Upgrade();
                changed = true;
            }
        }

        var seen = new HashSet<int>();
        var before = Rules.Count;
        Rules.RemoveAll(r => r.Hotbar is < 1 or > 10 || !seen.Add(r.Hotbar));
        return changed || Rules.Count != before;
    }

    /// <summary>The "Pick classes and jobs" card is folded away.</summary>
    public bool PickFolded { get; set; } = true;

    /// <summary>The "Hotbars" card is folded away.</summary>
    public bool HotbarsFolded { get; set; } = true;

    /// <summary>The window has been given its starting width once.</summary>
    public bool StartSizeDone { get; set; }

    /// <summary>Folder for exported settings and hotbar backups, picked by the player. Empty until they pick one.</summary>
    public string SaveFolder { get; set; } = string.Empty;

    /// <summary>The rule editor stays stuck to the side of the main window (and moves with it).</summary>
    public bool RulesStuck { get; set; } = true;

    /// <summary>Hotbar cards that are unfolded in the window.</summary>
    public HashSet<int> OpenBars { get; set; } = new();

    /// <summary>Dark (default), Light or Dalamud, like the other Will-o plugins.</summary>
    public ThemeMode ThemeChoice { get; set; } = ThemeMode.Dark;
}

[Serializable]
public class HotbarRule
{
    /// <summary>Hotbar number as the game shows it, 1 to 10.</summary>
    public int Hotbar { get; set; } = 4;

    public RuleMode Mode { get; set; } = RuleMode.ShowOnly;

    /// <summary>ClassJob row ids this rule applies to.</summary>
    public HashSet<uint> Jobs { get; set; } = new();

    /// <summary>A paused rule leaves its hotbar alone (and shows it again).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Give this hotbar its own actions on every class/job (the game's "Shared" box unticked).
    /// Fisher actions on hotbar 4 then stay on Fisher, and come back when you switch to Fisher again.
    /// Null: the player never changed it here, so the game's own "Shared" setting is left as it is.
    /// </summary>
    public bool? SeparatePerJob { get; set; }


    /// <summary>
    /// The cases, checked from the top: the first one that matches decides. Null in settings from before version 3
    /// (then <see cref="Mode"/> and <see cref="Jobs"/> are converted by <see cref="Upgrade"/>).
    /// </summary>
    public List<RuleCase>? Cases { get; set; }

    /// <summary>Shown (or hidden) when no case matches.</summary>
    public bool OtherwiseShow { get; set; } = true;

    /// <summary>Turn the old Mode + Jobs into cases.</summary>
    public void Upgrade()
    {
        Cases = new List<RuleCase>();
        switch (Mode)
        {
            case RuleMode.ShowOnly:
                Cases.Add(new RuleCase { Show = true, Conditions = { new RuleCondition { Kind = ConditionKind.Job, Ids = new HashSet<uint>(Jobs) } } });
                OtherwiseShow = false;
                break;
            case RuleMode.HideOn:
                Cases.Add(new RuleCase { Show = false, Conditions = { new RuleCondition { Kind = ConditionKind.Job, Ids = new HashSet<uint>(Jobs) } } });
                OtherwiseShow = true;
                break;
            default:
                OtherwiseShow = true;
                break;
        }
    }

    public HotbarRule Copy() => new()
    {
        Hotbar = Hotbar,
        Mode = Mode,
        Jobs = new HashSet<uint>(Jobs),
        Enabled = Enabled,
        SeparatePerJob = SeparatePerJob,
        Cases = Cases?.Select(c => c.Copy()).ToList(),
        OtherwiseShow = OtherwiseShow,
    };

    /// <summary>Old (before version 3): whether the hotbar should be visible on this job.</summary>
    public bool VisibleOn(uint jobId) => Mode switch
    {
        RuleMode.ShowOnly => Jobs.Contains(jobId),
        RuleMode.HideOn => !Jobs.Contains(jobId),
        _ => true,
    };
}

public enum RuleMode
{
    /// <summary>Visible on the selected jobs, hidden on every other one.</summary>
    ShowOnly,

    /// <summary>Hidden on the selected jobs, visible on every other one.</summary>
    HideOn,

    /// <summary>Always visible (useful together with separate actions per class/job).</summary>
    Show,
}

/// <summary>The window's look: the website's dark or light colours, or Dalamud's own style.</summary>
public enum ThemeMode
{
    Dark,
    Light,
    Dalamud,
}
