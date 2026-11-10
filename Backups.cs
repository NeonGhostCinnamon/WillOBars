using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Dalamud.Game.Config;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace WillOBars;

/// <summary>One saved copy of the game's own hotbar setup for one character.</summary>
public sealed class HotbarBackup
{
    public DateTime Created { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Character { get; set; } = string.Empty;
    public ulong ContentId { get; set; }
    public bool Automatic { get; set; }

    /// <summary>The game's "Shared" box for every hotbar and cross hotbar (option name -> value).</summary>
    public Dictionary<string, uint> Sharing { get; set; } = new();

    /// <summary>Every filled slot of every saved hotbar (all classes/jobs, shared, PvP).</summary>
    public List<SlotData> Slots { get; set; } = new();

    public int ActionCount => Slots.Count;

    public sealed class SlotData
    {
        /// <summary>Saved hotbar group (one per class/job, plus shared and PvP).</summary>
        public int G { get; set; }
        /// <summary>Hotbar inside the group (0-9 normal, 10-17 cross hotbar).</summary>
        public int B { get; set; }
        /// <summary>Slot on the hotbar.</summary>
        public int S { get; set; }
        /// <summary>Command type (action, item, macro, emote...).</summary>
        public byte T { get; set; }
        /// <summary>Command id.</summary>
        public uint I { get; set; }
    }
}

/// <summary>
/// Saves and restores the game's own hotbars: every action on every hotbar of every class/job,
/// and the "Shared" setting of each bar. Backups are plain JSON files in the plugin's config folder.
/// </summary>
public sealed class Backups
{
    private const int MaxAutomatic = 15;

    /// <summary>Name of the backup made the first time Will-o-Bars runs on a character. Never pruned.</summary>
    public const string FirstBackupName = "Before Will-o-Bars";

    private static readonly UiConfigOption[] ShareOptions =
    {
        UiConfigOption.HotbarCommon01, UiConfigOption.HotbarCommon02, UiConfigOption.HotbarCommon03,
        UiConfigOption.HotbarCommon04, UiConfigOption.HotbarCommon05, UiConfigOption.HotbarCommon06,
        UiConfigOption.HotbarCommon07, UiConfigOption.HotbarCommon08, UiConfigOption.HotbarCommon09,
        UiConfigOption.HotbarCommon10,
        UiConfigOption.HotbarCrossCommon01, UiConfigOption.HotbarCrossCommon02, UiConfigOption.HotbarCrossCommon03,
        UiConfigOption.HotbarCrossCommon04, UiConfigOption.HotbarCrossCommon05, UiConfigOption.HotbarCrossCommon06,
        UiConfigOption.HotbarCrossCommon07, UiConfigOption.HotbarCrossCommon08,
    };

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private string folder;
    private readonly IGameConfig gameConfig;
    private readonly IPluginLog log;

    /// <summary>Newest first.</summary>
    public List<(string Path, HotbarBackup Backup)> List { get; private set; } = new();

    public string? LastMessage { get; set; }

    /// <summary>Where backups are kept right now.</summary>
    public string Folder => folder;

    private readonly string pluginBackups;

    /// <param name="configDirectory">The plugin's own folder: used until the player picks a save folder.</param>
    /// <param name="saveFolder">The folder the player picked (backups go in its "backups" subfolder), or null.</param>
    public Backups(string configDirectory, string? saveFolder, IGameConfig gameConfig, IPluginLog log)
    {
        pluginBackups = Path.Combine(configDirectory, "backups");
        this.gameConfig = gameConfig;
        this.log = log;
        folder = pluginBackups;
        UseFolder(saveFolder);
    }

    /// <summary>
    /// Switch to the player's save folder (or back to the plugin's own folder when null), moving any backups
    /// from the old places so nothing gets lost.
    /// </summary>
    public void UseFolder(string? saveFolder)
    {
        var target = string.IsNullOrEmpty(saveFolder) ? pluginBackups : Path.Combine(saveFolder, "backups");
        Directory.CreateDirectory(target);

        // Backups from older versions in Documents\Will-o-Bars only move once a folder is picked.
        var sources = string.IsNullOrEmpty(saveFolder)
            ? new[] { folder, pluginBackups }
            : new[] { folder, pluginBackups, Path.Combine(SettingsTransfer.OldDocumentsFolder, "backups") };
        foreach (var src in sources.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(Path.GetFullPath(src), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase) || !Directory.Exists(src))
                continue;
            foreach (var file in Directory.EnumerateFiles(src, "*.json"))
            {
                var dest = Path.Combine(target, Path.GetFileName(file));
                try
                {
                    if (!File.Exists(dest))
                        File.Move(file, dest);
                }
                catch (Exception e)
                {
                    log.Warning(e, $"Could not move backup {file}.");
                }
            }
        }

        folder = target;
        Reload();
    }

    public void Reload()
    {
        var list = new List<(string, HotbarBackup)>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            try
            {
                var b = JsonSerializer.Deserialize<HotbarBackup>(File.ReadAllText(file), Json);
                if (b != null)
                    list.Add((file, b));
            }
            catch (Exception e)
            {
                log.Warning(e, $"Could not read backup {file}.");
            }
        }
        List = list.OrderByDescending(x => x.Item2.Created).ToList();
    }

    /// <summary>Whether the hotbars are loaded and can be saved or restored right now.</summary>
    public static unsafe bool Ready
    {
        get
        {
            var m = RaptureHotbarModule.Instance();
            return m != null && m->ModuleReady && m->CharacterContentId != 0;
        }
    }

    public static unsafe ulong CurrentContentId
    {
        get
        {
            var m = RaptureHotbarModule.Instance();
            return m == null ? 0 : m->CharacterContentId;
        }
    }

    /// <summary>Copy the current hotbars into a new backup file. Must run on the game's main thread.</summary>
    public unsafe bool Save(string name, string character, bool automatic)
    {
        var module = RaptureHotbarModule.Instance();
        if (module == null || !module->ModuleReady || module->CharacterContentId == 0)
        {
            LastMessage = "Your hotbars aren't loaded yet. Log in to a character first.";
            return false;
        }

        var backup = new HotbarBackup
        {
            Created = DateTime.Now,
            Name = name,
            Character = character,
            ContentId = module->CharacterContentId,
            Automatic = automatic,
        };

        foreach (var opt in ShareOptions)
        {
            if (gameConfig.TryGet(opt, out uint v))
                backup.Sharing[opt.ToString()] = v;
        }

        var groups = module->SavedHotbars;
        for (var g = 0; g < groups.Length; g++)
        {
            var bars = groups[g].Hotbars;
            for (var b = 0; b < bars.Length; b++)
            {
                var slots = bars[b].Slots;
                for (var s = 0; s < slots.Length; s++)
                {
                    var slot = slots[s];
                    if (slot.CommandType == RaptureHotbarModule.HotbarSlotType.Empty)
                        continue;
                    backup.Slots.Add(new HotbarBackup.SlotData { G = g, B = b, S = s, T = (byte)slot.CommandType, I = slot.CommandId });
                }
            }
        }

        // Two backups in the same second (e.g. the first backup and a "Shared" change right after it)
        // must not overwrite each other.
        var baseName = $"{backup.Created:yyyyMMdd-HHmmss}{(automatic ? "-auto" : string.Empty)}";
        var file = Path.Combine(folder, baseName + ".json");
        for (var n = 2; File.Exists(file); n++)
            file = Path.Combine(folder, $"{baseName}-{n}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(backup, Json));
        log.Information($"Saved hotbar backup {file} ({backup.ActionCount} actions).");

        Reload();
        PruneAutomatic();
        LastMessage = automatic ? null : $"Saved: {backup.ActionCount} actions on all your hotbars.";
        return true;
    }

    /// <summary>Put a backup back into the game. Must run on the game's main thread.</summary>
    public unsafe bool Restore(HotbarBackup backup)
    {
        var module = RaptureHotbarModule.Instance();
        if (module == null || !module->ModuleReady || module->CharacterContentId == 0)
        {
            LastMessage = "Your hotbars aren't loaded yet. Log in to a character first.";
            return false;
        }
        if (backup.ContentId != module->CharacterContentId)
        {
            LastMessage = $"This backup belongs to {backup.Character}. Log in to that character to restore it.";
            return false;
        }

        // 1. The "Shared" boxes.
        for (var i = 0; i < ShareOptions.Length; i++)
        {
            if (!backup.Sharing.TryGetValue(ShareOptions[i].ToString(), out var v))
                continue;
            gameConfig.Set(ShareOptions[i], v);

            var mask = module->HotbarShareStateBitmask;
            var bit = (byte)(1 << (i % 8));
            if (v != 0) mask[i / 8] |= bit;
            else mask[i / 8] &= (byte)~bit;
        }

        // 2. Every saved hotbar: clear it, then put the saved actions back.
        var groups = module->SavedHotbars;
        for (var g = 0; g < groups.Length; g++)
        {
            var bars = groups[g].Hotbars;
            for (var b = 0; b < bars.Length; b++)
            {
                var slots = bars[b].Slots;
                for (var s = 0; s < slots.Length; s++)
                {
                    slots[s].CommandType = RaptureHotbarModule.HotbarSlotType.Empty;
                    slots[s].CommandId = 0;
                }
            }
        }

        foreach (var slot in backup.Slots)
        {
            if (slot.G >= groups.Length) continue;
            var bars = groups[slot.G].Hotbars;
            if (slot.B >= bars.Length) continue;
            var slots = bars[slot.B].Slots;
            if (slot.S >= slots.Length) continue;
            slots[slot.S].CommandType = (RaptureHotbarModule.HotbarSlotType)slot.T;
            slots[slot.S].CommandId = slot.I;
        }

        // 3. Ask the game to write HOTBAR.DAT, and show the restored actions right away.
        module->HasChanges = true;
        module->IsSavePending = true;

        var job = (uint)(module->ActiveHotbarClassJobId & 0x7F);
        for (uint bar = 0; bar < 18; bar++)
            module->LoadSavedHotbar(module->IsHotbarShared(bar) ? 0u : job, bar);

        log.Information($"Restored hotbar backup from {backup.Created} ({backup.ActionCount} actions).");
        LastMessage = $"Restored your hotbars from {backup.Created:d MMM yyyy, HH:mm}.";
        return true;
    }

    public void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e)
        {
            log.Warning(e, $"Could not delete backup {path}.");
        }
        Reload();
    }

    /// <summary>Whether this character already has an automatic backup (made before Will-o-Bars changed anything).</summary>
    public bool HasAutomaticFor(ulong contentId) => List.Any(x => x.Backup.ContentId == contentId);

    private void PruneAutomatic()
    {
        // Keep each character's very first backup (the hotbars from before Will-o-Bars) however many come after it.
        foreach (var old in List.Where(x => x.Backup.Automatic && x.Backup.Name != FirstBackupName).Skip(MaxAutomatic).ToList())
            Delete(old.Path);
    }
}
