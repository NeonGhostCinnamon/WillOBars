using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Game.Config;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WillOBars.Windows;

namespace WillOBars;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/willobars";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] private static ICommandManager CommandManager { get; set; } = null!;
    [PluginService] private static IFramework Framework { get; set; } = null!;
    [PluginService] private static IClientState ClientState { get; set; } = null!;
    [PluginService] private static IObjectTable ObjectTable { get; set; } = null!;
    [PluginService] private static ICondition Condition { get; set; } = null!;
    [PluginService] private static IDataManager DataManager { get; set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] private static IGameConfig GameConfig { get; set; } = null!;
    [PluginService] private static IGameGui GameGui { get; set; } = null!;
    [PluginService] private static IPluginLog Log { get; set; } = null!;

    private readonly Configuration config;
    private readonly WindowSystem windows = new("WillOBars");
    private readonly MainWindow mainWindow;

    public Configuration Configuration => config;

    /// <summary>Saved copies of the game's own hotbars.</summary>
    public Backups Backups { get; }

    /// <summary>Reads what the game is doing with each hotbar.</summary>
    public HotbarLook Look { get; }

    // Work that has to happen on the game's main thread (saving/restoring hotbars).
    private readonly Queue<Action> mainThreadWork = new();

    /// <summary>Nunito Black at heading size (the website's font), so the title is sharp.</summary>
    public IFontHandle TitleFont { get; }

    /// <summary>The class/job the rules were last applied for (0 = not logged in yet).</summary>
    public uint CurrentJob { get; private set; }

    /// <summary>Hotbars this plugin set last time, and whether it made them visible.</summary>
    public IReadOnlyDictionary<int, bool> Applied => applied;
    private readonly Dictionary<int, bool> applied = new();

    private bool dirty = true;
    private DateTime dirtySince = DateTime.UtcNow;
    private DateTime lastPoll = DateTime.MinValue;

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (config.Normalize())
            PluginInterface.SavePluginConfig(config);

        // Every start: both big cards folded, so the window opens small and tidy.
        config.PickFolded = true;
        config.HotbarsFolded = true;

        Jobs.Load(DataManager);
        SettingsTransfer.Folder = string.IsNullOrEmpty(config.SaveFolder) ? null : config.SaveFolder;
        Backups = new Backups(PluginInterface.GetPluginConfigDirectory(), SettingsTransfer.Folder, GameConfig, Log);
        Look = new HotbarLook(GameGui);

        var fontPath = Path.Combine(PluginInterface.AssemblyLocation.DirectoryName ?? string.Empty, "Nunito-Black.ttf");
        TitleFont = PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
        {
            if (File.Exists(fontPath))
                tk.AddFontFromFile(fontPath, new SafeFontConfig { SizePx = 30 });
            else
                tk.AddDalamudDefaultFont(30);
        }));

        mainWindow = new MainWindow(this);
        windows.AddWindow(mainWindow);

        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += mainWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi += mainWindow.Toggle;

        CommandManager.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Will-o-Bars settings. \"/willobars apply\" re-applies your rules right now.",
        });

        ClientState.ClassJobChanged += OnClassJobChanged;
        ClientState.Login += OnLogin;
        Framework.Update += OnUpdate;
    }

    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("apply", StringComparison.OrdinalIgnoreCase))
            MarkDirty();
        else
            mainWindow.Toggle();
    }

    private void OnClassJobChanged(uint classJobId) => MarkDirty();

    private void OnLogin() => MarkDirty();

    /// <summary>Re-apply the rules on the next safe frame (after a job change, login or settings change).</summary>
    public void MarkDirty()
    {
        if (!dirty)
            dirtySince = DateTime.UtcNow;
        dirty = true;
    }

    private void OnUpdate(IFramework framework)
    {
        while (mainThreadWork.Count > 0)
        {
            try { mainThreadWork.Dequeue()(); }
            catch (Exception e) { Log.Error(e, "Hotbar backup work failed."); }
        }

        var now = DateTime.UtcNow;
        if ((now - lastPoll).TotalMilliseconds < 250)
            return;
        lastPoll = now;

        var local = ObjectTable.LocalPlayer;
        if (!ClientState.IsLoggedIn || local == null)
        {
            CurrentJob = 0;
            return;
        }

        // First time on this character: keep a copy of the hotbars exactly as they were before Will-o-Bars.
        if (Backups.Ready && !Backups.HasAutomaticFor(Backups.CurrentContentId))
            Backups.Save(Backups.FirstBackupName, local.Name.TextValue, automatic: true);

        // Safety net in case a job change event was missed.
        var job = local.ClassJob.RowId;
        if (job != CurrentJob)
            MarkDirty();

        // Wait while loading screens are up.
        if (Condition[ConditionFlag.BetweenAreas] || Condition[ConditionFlag.BetweenAreas51])
            return;

        if (dirty && (now - dirtySince).TotalMilliseconds >= 300)
        {
            Apply(job);
            dirty = false;
            return;
        }

        Enforce(now);
    }

    // Per hotbar: when we last tried to fix it, and how many times in a row.
    private readonly Dictionary<int, (DateTime Last, int Tries)> fixes = new();

    /// <summary>
    /// Keeps controlled hotbars the way the rules say, also when something else (the HUD Layout editor,
    /// Character Configuration, another plugin) turned them on or off. First with the game's own
    /// "/hotbar display" command; if that doesn't take, by showing/hiding the hotbar directly.
    /// </summary>
    private unsafe void Enforce(DateTime now)
    {
        if (applied.Count == 0)
            return;
        if (ClientState.IsGPosing
            || Condition[ConditionFlag.OccupiedInCutSceneEvent]
            || Condition[ConditionFlag.WatchingCutscene]
            || Condition[ConditionFlag.WatchingCutscene78]
            || Condition[ConditionFlag.CreatingCharacter])
            return;

        // Don't fight the HUD Layout editor while it's open.
        var hudEditor = GameGui.GetAddonByName("HudLayout");
        if (!hudEditor.IsNull && hudEditor.IsVisible)
            return;

        // The whole HUD is hidden (e.g. Scroll Lock): leave everything alone.
        var hpBar = GameGui.GetAddonByName("_ParameterWidget");
        if (!hpBar.IsNull && !hpBar.IsVisible)
            return;

        foreach (var (bar, wanted) in applied)
        {
            var addonPtr = GameGui.GetAddonByName(HotbarLook.AddonName(bar));
            if (addonPtr.IsNull)
                continue;

            if (addonPtr.IsVisible == wanted)
            {
                fixes.Remove(bar);
                continue;
            }

            fixes.TryGetValue(bar, out var f);
            if ((now - f.Last).TotalMilliseconds < 1500)
                continue;

            if (f.Tries < 2)
            {
                Log.Information($"Hotbar {bar} should be {(wanted ? "shown" : "hidden")} but isn't; sending the command again.");
                SetHotbarVisible(bar, wanted);
            }
            else
            {
                Log.Information($"Hotbar {bar} still {(wanted ? "hidden" : "shown")}; {(wanted ? "showing" : "hiding")} it directly.");
                var addon = (FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)addonPtr.Address;
                if (wanted) addon->Show(true, 0);
                else addon->Hide(true, false, 0);
            }
            fixes[bar] = (now, f.Tries + 1);
        }
    }

    private void Apply(uint job)
    {
        CurrentJob = job;

        var wanted = new Dictionary<int, bool>();
        foreach (var rule in config.Rules.Where(r => r.Enabled && r.Hotbar is >= 1 and <= 10))
        {
            // Only touch the game's "Shared" setting when the player chose it here.
            if (wanted.TryAdd(rule.Hotbar, rule.VisibleOn(job)) && rule.SeparatePerJob is { } separate)
                EnsureSharing(rule.Hotbar, !separate, rule.VisibleOn(job));
        }

        // Bars that are no longer controlled (rule removed or paused) are shown again.
        foreach (var bar in applied.Keys.Where(b => !wanted.ContainsKey(b)).ToList())
        {
            SetHotbarVisible(bar, true);
            applied.Remove(bar);
        }

        foreach (var (bar, visible) in wanted)
        {
            SetHotbarVisible(bar, visible);
            applied[bar] = visible;
        }
    }

    /// <summary>The game's own "Shared" setting for a hotbar right now (null when the hotbars aren't loaded).</summary>
    public unsafe bool? IsHotbarShared(int hotbar)
    {
        if (hotbar is < 1 or > 10)
            return null;
        var module = RaptureHotbarModule.Instance();
        if (module == null || !module->ModuleReady)
            return null;
        return module->IsHotbarShared((uint)(hotbar - 1));
    }

    private static readonly UiConfigOption[] ShareOptions =
    {
        UiConfigOption.HotbarCommon01, UiConfigOption.HotbarCommon02, UiConfigOption.HotbarCommon03,
        UiConfigOption.HotbarCommon04, UiConfigOption.HotbarCommon05, UiConfigOption.HotbarCommon06,
        UiConfigOption.HotbarCommon07, UiConfigOption.HotbarCommon08, UiConfigOption.HotbarCommon09,
        UiConfigOption.HotbarCommon10,
    };

    /// <summary>
    /// Sets the game's "Shared" box for a hotbar. Unsharing gives every class/job its own actions on it,
    /// stored by the game itself, so they come back whenever you switch to that class/job again.
    /// When a bar is unshared on a job where it's shown and that job has nothing on it yet,
    /// the actions that were on the shared bar are copied over, so nothing you just placed is lost.
    /// </summary>
    private unsafe void EnsureSharing(int hotbar, bool shared, bool visibleHere)
    {
        var module = RaptureHotbarModule.Instance();
        if (module == null || !module->ModuleReady)
            return;

        var id = (uint)(hotbar - 1);
        if (module->IsHotbarShared(id) == shared)
            return;

        // What's on the bar right now (the shared actions, when we're about to unshare).
        var before = new List<(RaptureHotbarModule.HotbarSlotType Type, uint Id)>();
        var live = module->Hotbars[(int)id].Slots;
        for (var i = 0; i < 12; i++)
            before.Add((live[i].CommandType, live[i].CommandId));

        // Keep a copy of the hotbars from right before Will-o-Bars changes how this bar is stored.
        Backups.Save($"Before hotbar {hotbar} became {(shared ? "shared" : "separate per class/job")}",
            ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty, automatic: true);

        GameConfig.Set(ShareOptions[id], shared ? 1u : 0u);

        // Normally the game updates this itself when the option changes; make sure it did.
        var mask = module->HotbarShareStateBitmask;
        var bit = (byte)(1 << (int)(id % 8));
        if (shared) mask[(int)(id / 8)] |= bit;
        else mask[(int)(id / 8)] &= (byte)~bit;

        var job = (uint)(module->ActiveHotbarClassJobId & 0x7F);
        module->LoadSavedHotbar(shared ? 0u : job, id);

        if (!shared && visibleHere)
        {
            var now = module->Hotbars[(int)id].Slots;
            var empty = true;
            for (var i = 0; i < 12; i++)
                empty &= now[i].CommandType == RaptureHotbarModule.HotbarSlotType.Empty;

            if (empty)
            {
                for (var i = 0; i < 12; i++)
                {
                    if (before[i].Type != RaptureHotbarModule.HotbarSlotType.Empty)
                        module->SetAndSaveSlot(id, (uint)i, before[i].Type, before[i].Id, false, false);
                }
            }
        }

        Log.Debug($"Hotbar {hotbar} is now {(shared ? "shared" : "separate per class/job")}.");
    }

    /// <summary>Same as typing "/hotbar display 4 on" (or off) in the chat box.</summary>
    private static unsafe void SetHotbarVisible(int hotbar, bool visible)
    {
        var uiModule = UIModule.Instance();
        if (uiModule == null)
            return;

        var text = $"/hotbar display {hotbar} {(visible ? "on" : "off")}";
        var str = Utf8String.FromString(text);
        try
        {
            uiModule->ProcessChatBoxEntry(str, IntPtr.Zero, false);
            Log.Information($"Sent \"{text}\".");
        }
        catch (Exception e)
        {
            Log.Error(e, $"Could not run \"{text}\".");
        }
        finally
        {
            str->Dtor(true);
        }
    }

    /// <summary>Save a backup of the hotbars as they are now (runs on the next frame).</summary>
    public void RequestBackup(string name)
        => mainThreadWork.Enqueue(() => Backups.Save(name, ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty, automatic: false));

    /// <summary>
    /// Put a backup back. All rules are paused first, so Will-o-Bars doesn't change the restored hotbars again
    /// (turn them back on with the eye buttons when you're ready).
    /// </summary>
    public void RequestRestore(HotbarBackup backup)
        => mainThreadWork.Enqueue(() =>
        {
            if (backup.ContentId != Backups.CurrentContentId)
            {
                Backups.Restore(backup); // only sets the "wrong character" message
                return;
            }
            foreach (var r in config.Rules)
                r.Enabled = false;
            Save();
            Apply(CurrentJob);
            Backups.Restore(backup);
        });

    /// <summary>Show or hide a hotbar the game's own way ("/hotbar display"), for hotbars without a rule.</summary>
    public void RequestGameVisibility(int bar, bool visible)
        => mainThreadWork.Enqueue(() => SetHotbarVisible(bar, visible));

    /// <summary>Use a new folder for saved settings and hotbar backups (moves existing backups there).</summary>
    public void SetSaveFolder(string path)
    {
        config.SaveFolder = path;
        SettingsTransfer.Folder = path;
        Backups.UseFolder(path);
        SaveOnly();
    }

    /// <summary>Save and re-apply the rules.</summary>
    public void Save()
    {
        config.Normalize();
        PluginInterface.SavePluginConfig(config);
        MarkDirty();
    }

    /// <summary>Save a change that doesn't affect any hotbar (folding a card, theme...).</summary>
    public void SaveOnly() => PluginInterface.SavePluginConfig(config);

    public void Dispose()
    {
        Framework.Update -= OnUpdate;
        ClientState.ClassJobChanged -= OnClassJobChanged;
        ClientState.Login -= OnLogin;
        CommandManager.RemoveHandler(Command);

        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= mainWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi -= mainWindow.Toggle;
        windows.RemoveAllWindows();
        TitleFont.Dispose();
    }
}
