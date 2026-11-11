using System;
using System.Linq;
using Dalamud.Interface.Textures;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;

namespace WillOBars.Windows;

/// <summary>
/// Will-o-Bars's window, in the same style as Will-o-Patchwisp and Will-o-Muter: no title bar (drag the header),
/// round header buttons with a pink outline, light/dark theme, glass cards and a compact minimized bar.
/// </summary>
public sealed class MainWindow : Window
{
    private const string GitHubUrl = "https://github.com/NeonGhostCinnamon/WillOBars";

    private readonly Plugin plugin;
    private Configuration Config => plugin.Configuration;

    private static float S(float v) => v * ImGuiHelpers.GlobalScale;

    private static readonly WindowSizeConstraints NormalConstraints = new()
    {
        MinimumSize = new Vector2(320, 120),
        MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
    };

    // dragging / minimizing (same approach as Will-o-Patchwisp)
    private bool dragging;
    private Vector2 dragOffset;
    private bool minimized;
    private Vector2 restoreSize;
    private Vector2? sizeOnce;
    private bool resetSizeCondition;
    private bool pushedMiniStyle;
    private Vector2 miniSize = new(260, 34);

    /// <summary>Width the window gets the first time: wide enough for the hotbars in two columns.</summary>
    private const float StartWidth = 540;

    public MainWindow(Plugin plugin)
        : base("Will-o-Bars##WillOBarsMain", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove)
    {
        this.plugin = plugin;
        Size = new Vector2(StartWidth, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = NormalConstraints;
        RespectCloseHotkey = false;
    }

    // ---------------------------------------------------------------- window frame

    public override void PreDraw()
    {
        Theme.Mode = Config.ThemeChoice;

        // First time the plugin opens (also for people who had an older version): start at the right width.
        if (!Config.StartSizeDone && !minimized)
        {
            sizeOnce = new Vector2(StartWidth, 600);
            Config.StartSizeDone = true;
            plugin.SaveOnly();
        }

        if (dragging && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
            dragging = false;
        if (dragging)
        {
            Position = ImGui.GetIO().MousePos - dragOffset;
            PositionCondition = ImGuiCond.Always;
        }
        else
        {
            Position = null;
        }

        if (resetSizeCondition)
        {
            SizeCondition = ImGuiCond.FirstUseEver;
            resetSizeCondition = false;
        }
        if (sizeOnce is { } once)
        {
            Size = once;
            SizeCondition = ImGuiCond.Always;
            sizeOnce = null;
            resetSizeCondition = !minimized;
        }

        // Not minimized: the window is always exactly as tall as its content (up to 90% of the screen),
        // so folding/unfolding a card shrinks/grows it. While you hold the mouse to resize the edge,
        // it's left alone so the width can be changed.
        if (!minimized && sizeOnce == null && fitHeight > 0
            && !(ImGui.IsMouseDown(ImGuiMouseButton.Left) && !dragging && ImGui.IsMouseDragging(ImGuiMouseButton.Left)))
        {
            var maxH = ImGui.GetMainViewport().WorkSize.Y * 0.9f;
            var fits = fitHeight + S(2) <= maxH;
            // When everything fits there's no scrollbar at all. Otherwise a scrollbar popping in and out
            // makes the content narrower/wider (icons wrap), which changes the height, which toggles the
            // scrollbar again: the window kept jumping.
            if (fits) Flags |= ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
            else Flags &= ~(ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            Size = new Vector2(fitWidth, MathF.Ceiling(fits ? fitHeight + S(2) : maxH)) / ImGuiHelpers.GlobalScale;
            SizeCondition = ImGuiCond.Always;
        }

        Theme.Push();

        if (minimized)
        {
            var padding = new Vector2(S(10), S(6));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, padding);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, (ImGui.GetFrameHeight() + padding.Y * 2) / 2f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, new Vector2(1, 1));
            pushedMiniStyle = true;
        }
    }

    public override void PostDraw()
    {
        if (pushedMiniStyle)
        {
            ImGui.PopStyleVar(3);
            pushedMiniStyle = false;
        }
        Theme.Pop();
    }

    private void SetMinimized(bool value)
    {
        if (minimized == value)
            return;

        var scale = ImGuiHelpers.GlobalScale;
        if (value)
        {
            restoreSize = ImGui.GetWindowSize();
            minimized = true;
            Flags |= ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
            SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(1, 1), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
            sizeOnce = miniSize / scale;
        }
        else
        {
            minimized = false;
            Flags &= ~(ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            SizeConstraints = NormalConstraints;
            sizeOnce = restoreSize / scale;
        }
    }

    public override void Draw()
    {
        if (minimized)
        {
            DrawMiniBar();
            Size = miniSize / ImGuiHelpers.GlobalScale;
            SizeCondition = ImGuiCond.Always;
            return;
        }

        DrawHeader();
        Theme.AccentBar();
        ImGuiHelpers.ScaledDummy(4f);

        DrawStatusCard();
        DrawTabs();
        DrawFooter();

        FitHeightToContent();
        fileDialog.Draw();
    }

    // Height of everything in the window last frame, and the width the window had.
    private float fitHeight;
    private float fitWidth;

    /// <summary>Remember how tall the content is; PreDraw sizes the window to it on the next frame.</summary>
    private void FitHeightToContent()
    {
        var style = ImGui.GetStyle();
        fitHeight = ImGui.GetCursorPosY() - style.ItemSpacing.Y + style.WindowPadding.Y;
        fitWidth = ImGui.GetWindowSize().X;
    }

    // ---------------------------------------------------------------- header

    private void DrawHeader()
    {
        var d = S(28);
        var gap = S(6);
        var buttonsW = d * 3 + gap * 2;

        var top = ImGui.GetCursorPos();
        var topScreen = ImGui.GetCursorScreenPos();
        Theme.Heading(plugin.TitleFont, buttonsW + S(8), ("Will-", false), ("o", true), ("-Bars", false));
        var headingH = ImGui.GetItemRectSize().Y;
        var after = ImGui.GetCursorPos();

        var y = top.Y + Math.Max(0, (headingH - d) / 2f);
        var right = ImGui.GetWindowContentRegionMax().X;

        ImGui.SetCursorPos(new Vector2(right - d * 2 - gap, y));
        if (Theme.IconCircle("##min", FontAwesomeIcon.Minus, Theme.Text, d))
            SetMinimized(true);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Minimize");

        ImGui.SetCursorPos(new Vector2(right - buttonsW, y));
        var mode = Config.ThemeChoice;
        if (Theme.ThemeButton("##theme", ref mode, d))
        {
            Config.ThemeChoice = mode;
            plugin.SaveOnly();
        }

        ImGui.SetCursorPos(new Vector2(right - d, y));
        if (Theme.IconCircle("##close", FontAwesomeIcon.Times, Theme.Text, d))
            IsOpen = false;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Close");

        // header = title bar: drag to move, double-click to minimize
        var winPos = ImGui.GetWindowPos();
        var headerMax = new Vector2(winPos.X + right - buttonsW - gap, topScreen.Y + Math.Max(headingH, d));
        var overHeader = ImGui.IsWindowHovered() && !ImGui.IsAnyItemHovered() && ImGui.IsMouseHoveringRect(winPos, headerMax);
        if (overHeader)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                SetMinimized(true);
                dragging = false;
            }
            else if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                dragging = true;
                dragOffset = ImGui.GetIO().MousePos - winPos;
            }
        }

        ImGui.SetCursorPos(new Vector2(after.X, Math.Max(after.Y, top.Y + d)));
        ImGui.Dummy(Vector2.Zero);
    }

    // ---------------------------------------------------------------- minimized bar

    private void DrawMiniBar()
    {
        var style = ImGui.GetStyle();
        var d = ImGui.GetFrameHeight();
        var start = ImGui.GetCursorScreenPos();
        var winPos = ImGui.GetWindowPos();

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.Text, "Will-");
        ImGui.SameLine(0, 0);
        Theme.DanglingText("o", Theme.BrandPink);
        ImGui.SameLine(0, 0);
        ImGui.TextColored(Theme.Text, "-Bars");

        // status pill
        var (label, on) = StatusText();
        ImGui.SameLine(0, S(10));
        var pillY = start.Y + (d - (ImGui.GetTextLineHeight() + S(2))) / 2f;
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, pillY));
        Theme.Pill(label, on ? Theme.Pink : new Vector4(0.55f, 0.55f, 0.62f, 1f));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Your current class/job");

        ImGui.SameLine(0, S(10));
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, start.Y));
        if (Theme.IconCircle("##expand", FontAwesomeIcon.ChevronDown, Theme.Text, d))
            SetMinimized(false);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Expand");
        ImGui.SameLine(0, S(4));
        if (Theme.IconCircle("##close", FontAwesomeIcon.Times, Theme.Text, d))
            IsOpen = false;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Close");

        var end = ImGui.GetItemRectMax();
        miniSize = new Vector2(end.X - winPos.X, end.Y - winPos.Y) + style.WindowPadding;

        if (ImGui.IsWindowHovered() && !ImGui.IsAnyItemHovered())
        {
            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                SetMinimized(false);
                dragging = false;
            }
            else if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                dragging = true;
                dragOffset = ImGui.GetIO().MousePos - winPos;
            }
        }
    }

    private (string Label, bool On) StatusText()
    {
        var job = Jobs.Get(plugin.CurrentJob);
        return (job?.Abbreviation ?? "On", true);
    }

    // ---------------------------------------------------------------- status card

    private float jobCardH;
    private float barsCardH;

    private void DrawStatusCard()
    {
        var job = Jobs.Get(plugin.CurrentJob);
        if (job == null)
        {
            Theme.Card("status", () =>
            {
                ImGui.TextColored(Theme.Muted, "Current class / job");
                Theme.Wrapped("Log in to a character to see your hotbars change.", Theme.TextSoft);
            });
            return;
        }

        var iconSize = S(44);
        var avail = ImGui.GetContentRegionAvail().X;
        var gap = S(10);

        // Left card: just wide enough for the icon and name.
        float nameW;
        using (plugin.TitleFont.Available ? plugin.TitleFont.Push() : null)
            nameW = ImGui.CalcTextSize(job.Name).X;
        var leftW = Math.Min(avail * 0.5f, Math.Max(S(170), iconSize + S(10) + nameW + S(24) + S(8)));
        var rightW = avail - leftW - gap;
        var sideBySide = rightW >= S(200);
        if (!sideBySide)
            leftW = avail;

        var start = ImGui.GetCursorScreenPos();
        // Side by side, both cards get the height of the taller one (measured last frame).
        var sameH = sideBySide ? Math.Max(jobCardH, barsCardH) : 0f;

        Theme.Card("job", () =>
        {
            ImGui.TextColored(Theme.Muted, "Current class / job");
            DrawJobIcon(job.IconId, iconSize, 1f);
            ImGui.SameLine(0, S(10));
            var y = ImGui.GetCursorPosY();
            using (plugin.TitleFont.Available ? plugin.TitleFont.Push() : null)
            {
                ImGui.SetCursorPosY(y + Math.Max(0, (iconSize - ImGui.GetTextLineHeight()) / 2f));
                ImGui.TextColored(Theme.Text, job.Name);
            }
        }, leftW, minHeight: sameH);
        jobCardH = Theme.LastCardHeight;
        var leftBottom = ImGui.GetCursorScreenPos().Y;

        if (sideBySide)
            ImGui.SetCursorScreenPos(new Vector2(start.X + leftW + gap, start.Y));

        // Right card: the hotbar badges (display only) and what the icons mean.
        Theme.Card("bars", () =>
        {
            Theme.BrandText("Hotbars on this job changed by Will-o-Bars:", Theme.Muted, Theme.CardInnerRight);

            // Only hotbars whose rule is switched on (a paused rule doesn't do anything).
            var bars = Config.Rules.Where(r => r.Enabled).Select(r => r.Hotbar).Distinct().OrderBy(b => b).ToList();
            if (bars.Count == 0)
            {
                Theme.Wrapped("No hotbars controlled right now.", Theme.TextSoft);
            }
            else
            {
                var w = S(48);
                var bgap = S(6);
                var first = true;
                foreach (var bar in bars)
                {
                    if (!first)
                    {
                        if (ImGui.GetItemRectMax().X + bgap + w <= Theme.CardInnerRight)
                            ImGui.SameLine(0, bgap);
                    }
                    first = false;

                    var state = JobState(bar, job.Id);
                    var paused = Config.Rules.First(r => r.Hotbar == bar) is { Enabled: false };
                    BarBadge(bar, state, w, paused);
                    if (ImGui.IsItemHovered())
                    {
                        var now = paused ? "Rule paused: left visible." : state switch
                        {
                            BarJobState.Hidden => $"Hidden on {job.Name}.",
                            BarJobState.Only => $"Shown only on {job.Name}" + (OnlyCount(bar) > 1 ? $" (and {OnlyCount(bar) - 1} more)." : "."),
                            _ => $"Shown on {job.Name}.",
                        };
                        ImGui.SetTooltip($"Hotbar {bar}\n{now}");
                    }
                }
            }

            ImGuiHelpers.ScaledDummy(2f);
            DrawLegend();
        }, sideBySide ? rightW : 0, minHeight: sameH);
        barsCardH = Theme.LastCardHeight;

        if (sideBySide)
        {
            var bottom = Math.Max(leftBottom, ImGui.GetCursorScreenPos().Y);
            ImGui.SetCursorScreenPos(new Vector2(start.X, bottom));
            ImGui.Dummy(Vector2.Zero);
        }
    }

    private enum BarJobState
    {
        Shown,
        Hidden,
        Only,
    }

    private BarJobState JobState(int bar, uint job)
    {
        var rule = Config.Rules.FirstOrDefault(r => r.Hotbar == bar);
        var hiddenInGame = job == plugin.CurrentJob && plugin.Look.IsShownInGame(bar) == false;
        if (rule is not { Enabled: true })
            return hiddenInGame ? BarJobState.Hidden : BarJobState.Shown;
        if (hiddenInGame)
            return BarJobState.Hidden;
        if (rule.Mode == RuleMode.ShowOnly && rule.Jobs.Contains(job))
            return BarJobState.Only;
        return rule.VisibleOn(job) ? BarJobState.Shown : BarJobState.Hidden;
    }

    private int OnlyCount(int bar) => Config.Rules.FirstOrDefault(r => r.Hotbar == bar)?.Jobs.Count ?? 0;

    private void CycleJobState(int bar, uint job, BarJobState state)
    {
        var rule = Config.Rules.FirstOrDefault(r => r.Hotbar == bar);
        if (rule == null)
        {
            rule = new HotbarRule { Hotbar = bar, Mode = RuleMode.Show };
            Config.Rules.Add(rule);
        }
        rule.Enabled = true;

        switch (state)
        {
            case BarJobState.Shown:
                // Hide on this job.
                if (rule.Mode == RuleMode.ShowOnly)
                {
                    rule.Jobs.Remove(job);
                }
                else
                {
                    if (rule.Mode == RuleMode.Show)
                        rule.Jobs.Clear();
                    rule.Mode = RuleMode.HideOn;
                    rule.Jobs.Add(job);
                }
                break;

            case BarJobState.Hidden:
                // Show only on this job.
                if (rule.Mode != RuleMode.ShowOnly)
                    rule.Jobs.Clear();
                rule.Mode = RuleMode.ShowOnly;
                rule.Jobs.Add(job);
                break;

            default:
                // Back to shown everywhere.
                rule.Mode = RuleMode.Show;
                rule.Jobs.Clear();
                break;
        }
        plugin.Save();
    }

    /// <summary>Hotbar badge (display only): number plus an eye (shown), crossed eye (hidden) or pin (only on this job).</summary>
    private static void BarBadge(int bar, BarJobState state, float width, bool paused)
    {
        var h = ImGui.GetFrameHeight();
        var pos = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(width, h));
        var dl = ImGui.GetWindowDrawList();
        var round = Theme.UseDalamud ? Theme.R(0) : h / 2f;

        var on = state != BarJobState.Hidden && !paused;
        var fill = on ? Theme.Pink : Theme.Glass;
        dl.AddRectFilled(pos, pos + new Vector2(width, h), ImGui.GetColorU32(fill), round);
        dl.AddRect(pos, pos + new Vector2(width, h), ImGui.GetColorU32(on ? Theme.Outline : Theme.Border), round, ImDrawFlags.None, S(1.2f));

        var textCol = ImGui.GetColorU32(on ? Theme.OnPink : Theme.Muted);
        var label = bar.ToString();
        var icon = paused ? FontAwesomeIcon.Pause : state switch
        {
            BarJobState.Hidden => FontAwesomeIcon.EyeSlash,
            BarJobState.Only => FontAwesomeIcon.Thumbtack,
            _ => FontAwesomeIcon.Eye,
        };

        var iconStr = icon.ToIconString();
        Vector4 ink;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            ink = Theme.InkBounds(iconStr);
        var iconW = ink.Z - ink.X;
        var labelW = ImGui.CalcTextSize(label).X;
        var gap = S(6);
        var sx = pos.X + (width - labelW - gap - iconW) / 2f;
        var cy = pos.Y + h / 2f;
        var lInk = Theme.InkBounds(label);
        dl.AddText(new Vector2(sx, cy - (lInk.Y + lInk.W) / 2f), textCol, label);
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            dl.AddText(new Vector2(sx + labelW + gap - ink.X, cy - (ink.Y + ink.W) / 2f), textCol, iconStr);
    }

    private static void DrawLegend()
    {
        void Item(FontAwesomeIcon icon, string text)
        {
            using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
                ImGui.TextColored(Theme.Muted, icon.ToIconString());
            ImGui.SameLine(0, S(5));
            ImGui.TextColored(Theme.Muted, text);
        }

        void Next(string text)
        {
            // Stay on this line only when the next item fits inside the card.
            var need = S(14) + ImGui.GetTextLineHeight() + S(5) + ImGui.CalcTextSize(text).X;
            if (ImGui.GetItemRectMax().X + need <= Theme.CardInnerRight)
                ImGui.SameLine(0, S(14));
        }

        Item(FontAwesomeIcon.Eye, "shown");
        Next("hidden on this job");
        Item(FontAwesomeIcon.EyeSlash, "hidden on this job");
        Next("only on this job");
        Item(FontAwesomeIcon.Thumbtack, "only on this job");
    }

    // ---------------------------------------------------------------- tabs

    private void DrawTabs()
    {
        using var bar = ImRaii.TabBar("##tabs");
        if (!bar.Success)
            return;

        using (var tab = ImRaii.TabItem("Hotbars"))
        {
            if (tab.Success)
            {
                ImGuiHelpers.ScaledDummy(2f);
                DrawHotbars();
            }
        }

        using (var tab = ImRaii.TabItem("Backups"))
        {
            if (tab.Success)
            {
                ImGuiHelpers.ScaledDummy(2f);
                DrawBackups();
            }
        }

        using (var tab = ImRaii.TabItem("Settings"))
        {
            if (tab.Success)
            {
                ImGuiHelpers.ScaledDummy(2f);
                DrawSettingsTransfer();
            }
        }
    }

    // ---------------------------------------------------------------- "Hotbars" tab


    private static readonly Vector4 Grey = new(0.55f, 0.55f, 0.62f, 1f);

    /// <summary>
    /// One place for everything: pick classes/jobs at the top, then every hotbar below shows what it does on
    /// them, with a switch to show/hide it there. Unfold a hotbar for its settings.
    /// </summary>
    private readonly System.Collections.Generic.HashSet<uint> selectedJobs = new();
    private uint lastJob;

    private void DrawHotbars()
    {
        // The picked class/job follows the one you're playing: when you change job, the pick resets to it.
        if (plugin.CurrentJob != 0 && plugin.CurrentJob != lastJob)
        {
            selectedJobs.Clear();
            selectedJobs.Add(plugin.CurrentJob);
            lastJob = plugin.CurrentJob;
        }

        // Step 1: which classes/jobs to check
        Theme.Card("pick", () =>
        {
            // Header row: click it (or the arrow) to fold the card away.
            var left = Theme.CardInnerLeft;
            var right = Theme.CardInnerRight;
            var rowStart = ImGui.GetCursorScreenPos();
            var headH = ImGui.GetTextLineHeight() + S(4);
            StepTitle("1", "Pick classes and jobs");

            var folded = Config.PickFolded;
            var dl = ImGui.GetWindowDrawList();
            var chevW = S(14);
            using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            {
                var chev = (folded ? FontAwesomeIcon.ChevronRight : FontAwesomeIcon.ChevronDown).ToIconString();
                var ink = Theme.InkBounds(chev);
                var hoverHead = ImGui.IsMouseHoveringRect(rowStart, new Vector2(right, rowStart.Y + headH));
                dl.AddText(new Vector2(right - chevW + (chevW - (ink.Z - ink.X)) / 2f - ink.X, rowStart.Y + headH / 2f - (ink.Y + ink.W) / 2f),
                    ImGui.GetColorU32(hoverHead ? Theme.AccentText : Theme.Muted), chev);
            }

            // Folded: show what's picked as small icons next to the title.
            if (folded)
            {
                var picked = Jobs.All.Where(j => selectedJobs.Contains(j.Id)).ToList();
                var icon = S(18);
                var x = ImGui.GetItemRectMax().X + S(10);
                var maxX = right - chevW - S(6);
                var iy = rowStart.Y + (headH - icon) / 2f;
                for (var i = 0; i < picked.Count; i++)
                {
                    if (x + icon + (i < picked.Count - 1 ? ImGui.CalcTextSize("+99").X : 0) > maxX)
                    {
                        dl.AddText(new Vector2(x, rowStart.Y + (headH - ImGui.GetTextLineHeight()) / 2f), ImGui.GetColorU32(Theme.Muted), $"+{picked.Count - i}");
                        break;
                    }
                    var tex = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(picked[i].IconId)).GetWrapOrEmpty();
                    dl.AddImage(tex.Handle, new Vector2(x, iy), new Vector2(x + icon, iy + icon));
                    x += icon + S(3);
                }
                if (picked.Count == 0)
                    dl.AddText(new Vector2(x, rowStart.Y + (headH - ImGui.GetTextLineHeight()) / 2f), ImGui.GetColorU32(Theme.Muted), "nothing picked");
            }

            var after = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(rowStart);
            if (ImGui.InvisibleButton("##foldpick", new Vector2(right - left, headH)))
            {
                Config.PickFolded = !folded;
                plugin.SaveOnly();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            }
            ImGui.SetCursorScreenPos(after);
            ImGui.Dummy(Vector2.Zero);

            if (folded)
                return;

            Theme.Wrapped("Pick classes/jobs to see on each hotbar below whether it's shown or hidden on them. Click to pick, Shift+click for a range, click and drag for many, or click a role name for the whole group.", Theme.Muted);

            if (TextLink("Current job") && plugin.CurrentJob != 0)
            {
                selectedJobs.Clear();
                selectedJobs.Add(plugin.CurrentJob);
            }
            ImGui.SameLine(0, S(14));
            if (TextLink("All"))
            {
                foreach (var j in Jobs.All)
                    selectedJobs.Add(j.Id);
            }
            ImGui.SameLine(0, S(14));
            if (TextLink("None"))
                selectedJobs.Clear();

            ImGuiHelpers.ScaledDummy(2f);
            DrawJobGrid("pick", selectedJobs.Contains, (ids, on) =>
            {
                foreach (var id in ids)
                {
                    if (on) selectedJobs.Add(id);
                    else selectedJobs.Remove(id);
                }
            });
        });

        // Step 2: the hotbars, in two columns (1-5 left, 6-10 right)
        var sel = Jobs.All.Where(j => selectedJobs.Contains(j.Id)).Select(j => j.Id).ToList();

        Theme.Card("hotbars", () =>
        {
            var left = Theme.CardInnerLeft;
            var right = Theme.CardInnerRight;
            var rowStart = ImGui.GetCursorScreenPos();
            var headH = ImGui.GetTextLineHeight() + S(4);
            var folded = Config.HotbarsFolded;
            var dl = ImGui.GetWindowDrawList();
            var chevW = S(14);

            StepTitle("2", "Hotbars");
            var titleRight = ImGui.GetItemRectMax().X;

            // Arrow on the right
            using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            {
                var chev = (folded ? FontAwesomeIcon.ChevronRight : FontAwesomeIcon.ChevronDown).ToIconString();
                var ink = Theme.InkBounds(chev);
                var hoverHead = ImGui.IsMouseHoveringRect(rowStart, new Vector2(right, rowStart.Y + headH));
                dl.AddText(new Vector2(right - chevW + (chevW - (ink.Z - ink.X)) / 2f - ink.X, rowStart.Y + headH / 2f - (ink.Y + ink.W) / 2f),
                    ImGui.GetColorU32(hoverHead ? Theme.AccentText : Theme.Muted), chev);
            }

            // Folded: the hotbars Will-o-Bars controls, as a short list next to the title.
            if (folded)
            {
                var controlled = Config.Rules.Where(r => r.Enabled).Select(r => r.Hotbar).OrderBy(b => b).ToList();
                var text = controlled.Count switch
                {
                    0 => "no hotbars controlled",
                    1 => $"controlling hotbar {controlled[0]}",
                    _ => "controlling hotbars " + string.Join(", ", controlled),
                };
                dl.PushClipRect(new Vector2(titleRight, rowStart.Y), new Vector2(right - chevW - S(6), rowStart.Y + headH), true);
                dl.AddText(new Vector2(titleRight + S(10), rowStart.Y + (headH - ImGui.GetTextLineHeight()) / 2f), ImGui.GetColorU32(Theme.Muted), text);
                dl.PopClipRect();
            }

            // "Fold all / Unfold all" just left of the arrow (only when open).
            var linkText = Enumerable.Range(1, 10).All(b => Config.OpenBars.Contains(b)) ? "Fold all" : "Unfold all";
            var linkW = folded ? 0f : ImGui.CalcTextSize(linkText).X + S(12);

            // Clicking the title row (left of the link) folds/unfolds the whole card.
            var after = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(rowStart);
            if (ImGui.InvisibleButton("##foldbars", new Vector2(Math.Max(S(40), right - left - linkW - chevW), headH)))
            {
                Config.HotbarsFolded = !folded;
                plugin.SaveOnly();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            }
            // The arrow itself is clickable too.
            ImGui.SetCursorScreenPos(new Vector2(right - chevW, rowStart.Y));
            if (ImGui.InvisibleButton("##foldbars2", new Vector2(chevW, headH)))
            {
                Config.HotbarsFolded = !folded;
                plugin.SaveOnly();
            }

            if (!folded)
            {
                ImGui.SetCursorScreenPos(new Vector2(right - chevW - linkW + S(4), rowStart.Y + S(2)));
                if (TextLink(linkText))
                {
                    var allOpen = linkText == "Fold all";
                    Config.OpenBars.Clear();
                    if (!allOpen)
                        for (var b = 1; b <= 10; b++)
                            Config.OpenBars.Add(b);
                    plugin.SaveOnly();
                }
            }

            ImGui.SetCursorScreenPos(new Vector2(left, Math.Max(after.Y, rowStart.Y + headH)));
            ImGui.Dummy(Vector2.Zero);
            if (folded)
                return;
            ImGuiHelpers.ScaledDummy(2f);

            var avail = Theme.CardInnerWidth;
            var gap = S(10);
            if (avail < S(440))
            {
                // Narrow window: one column.
                for (var bar = 1; bar <= 10; bar++)
                {
                    var b = bar;
                    Theme.Card($"bar{b}", () => DrawHotbarCard(b, sel), 0, IsControlled(b));
                }
            }
            else
            {
                var colW = (avail - gap) / 2f;
                var start = ImGui.GetCursorScreenPos();

                ImGui.BeginGroup();
                for (var bar = 1; bar <= 5; bar++)
                {
                    var b = bar;
                    Theme.Card($"bar{b}", () => DrawHotbarCard(b, sel), colW, IsControlled(b));
                }
                ImGui.EndGroup();
                var leftBottom = ImGui.GetItemRectMax().Y;

                ImGui.SetCursorScreenPos(new Vector2(start.X + colW + gap, start.Y));
                ImGui.BeginGroup();
                for (var bar = 6; bar <= 10; bar++)
                {
                    var b = bar;
                    Theme.Card($"bar{b}", () => DrawHotbarCard(b, sel), colW, IsControlled(b));
                }
                ImGui.EndGroup();
                var rightBottom = ImGui.GetItemRectMax().Y;

                ImGui.SetCursorScreenPos(new Vector2(start.X, Math.Max(leftBottom, rightBottom)));
                ImGui.Dummy(Vector2.Zero);
            }
        });
        ImGuiHelpers.ScaledDummy(2f);
    }

    private bool IsControlled(int bar) => Config.Rules.Any(r => r.Hotbar == bar && r.Enabled);

    private void StepTitle(string number, string title)
    {
        var d = ImGui.GetTextLineHeight() + S(4);
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddCircleFilled(pos + new Vector2(d / 2f), d / 2f, ImGui.GetColorU32(Theme.Pink), 24);
        var ts = ImGui.CalcTextSize(number);
        dl.AddText(pos + new Vector2((d - ts.X) / 2f, (d - ts.Y) / 2f), ImGui.GetColorU32(Theme.OnPink), number);
        ImGui.Dummy(new Vector2(d, d));
        ImGui.SameLine(0, S(8));
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, pos.Y + (d - ImGui.GetTextLineHeight()) / 2f));
        ImGui.TextColored(Theme.Text, title);
    }

    /// <summary>What the game itself is doing with this hotbar right now.</summary>
    private string GameState(int bar) => plugin.Look.IsShownInGame(bar) switch
    {
        true => "shown",
        false => "hidden",
        _ => "game setting",
    };

    /// <summary>What a hotbar's rule does, in a few words.</summary>
    private static (string Text, Vector4 Color) RuleSummary(HotbarRule? rule)
    {
        if (rule == null)
            return ("", Theme.Muted);
        if (!rule.Enabled)
            return ("paused", Theme.Muted);
        var n = rule.Jobs.Count;
        var jobs = n == 1 ? Jobs.Get(rule.Jobs.First())?.Abbreviation ?? "1 job" : $"{n} jobs";
        return rule.Mode switch
        {
            RuleMode.Show => ("always shown", Theme.AccentText),
            RuleMode.ShowOnly => (n == 0 ? "hidden everywhere" : $"only on {jobs}", Theme.AccentText),
            _ => (n == 0 ? "shown everywhere" : $"hidden on {jobs}", Theme.AccentText),
        };
    }

    private void DrawHotbarCard(int bar, System.Collections.Generic.List<uint> sel)
    {
        var rule = Config.Rules.FirstOrDefault(r => r.Hotbar == bar);
        var open = Config.OpenBars.Contains(bar);
        var d = ImGui.GetFrameHeight();
        var left = Theme.CardInnerLeft;
        var right = Theme.CardInnerRight;
        var rowStart = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();

        // Preview for the jobs picked in step 1: a small "shown"/"hidden" pill on the right.
        string? preview = null;
        var previewOn = true;
        if (rule is not { Enabled: true })
        {
            // No (active) rule: show what the game itself is doing, the same way.
            var inGame = plugin.Look.IsShownInGame(bar);
            if (inGame != null)
            {
                previewOn = inGame.Value;
                preview = inGame.Value ? "shown" : "hidden";
            }
        }
        else if (sel.Count > 0)
        {
            // For the job you're on right now, trust what the game actually shows (it may have been hidden
            // some other way, like the HUD Layout editor). Other jobs can only follow the rule.
            var inGame = plugin.Look.IsShownInGame(bar);
            var shown = sel.Count(j => j == plugin.CurrentJob && inGame != null ? inGame.Value : rule.VisibleOn(j));
            previewOn = shown > 0;
            preview = shown == sel.Count ? "shown" : shown == 0 ? "hidden" : $"{shown}/{sel.Count} shown";
        }
        // Little wisp in front of the pill when Will-o-Bars is controlling this hotbar.
        var controlled = rule is { Enabled: true };
        var wispStr = FontAwesomeIcon.Fire.ToIconString();
        var wispW = 0f;
        if (controlled && preview != null)
        {
            using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            {
                var ink = Theme.InkBounds(wispStr);
                wispW = ink.Z - ink.X + S(6);
            }
        }
        // Controlled and hidden by the rule: shown as a crossed-through "shown" pill.
        var struck = controlled && preview == "hidden";
        if (struck)
            preview = "shown";

        // Hotbars the game decides on get two clickable pills instead: "shown" and "hidden".
        var vanilla = !controlled && preview != null;
        var pillGap = S(4);
        var previewW = preview == null ? 0f
            : vanilla ? Theme.PillWidth("shown") + pillGap + Theme.PillWidth("hidden")
            : Theme.PillWidth(preview) + wispW;

        // Header row: click on it (left of the pills) to fold/unfold.
        if (ImGui.InvisibleButton("##head", new Vector2(Math.Max(S(40), right - left - previewW - S(8)), d)))
        {
            if (open) Config.OpenBars.Remove(bar);
            else Config.OpenBars.Add(bar);
            plugin.SaveOnly();
        }
        var headHovered = ImGui.IsItemHovered();
        if (headHovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (preview != null)
                ImGui.SetTooltip(rule is { Enabled: true }
                    ? $"Hotbar {bar} is {(struck ? "hidden by Will-o-Bars" : preview)} on {WhoText(sel)}."
                    : $"Hotbar {bar} is {preview} (vanilla setting).");
        }

        var ty = rowStart.Y + (d - ImGui.GetTextLineHeight()) / 2f;
        var x = rowStart.X;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            var chev = (open ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronRight).ToIconString();
            var ink = Theme.InkBounds(chev);
            var cw = S(14);
            dl.AddText(new Vector2(x + (cw - (ink.Z - ink.X)) / 2f - ink.X, rowStart.Y + d / 2f - (ink.Y + ink.W) / 2f),
                ImGui.GetColorU32(headHovered ? Theme.AccentText : Theme.Muted), chev);
            x += cw + S(8);
        }

        var title = $"Hotbar {bar}";
        dl.AddText(new Vector2(x, ty), ImGui.GetColorU32(rule != null ? Theme.Text : Theme.Muted), title);
        x += ImGui.CalcTextSize(title).X + S(10);

        // Only the game's own state for hotbars without a rule; "paused" for paused rules. Nothing else.
        var summary = rule is { Enabled: false } ? "paused" : string.Empty;
        var summaryCol = Theme.Muted;
        dl.PushClipRect(new Vector2(x, rowStart.Y), new Vector2(right - previewW - S(8), rowStart.Y + d), true);
        dl.AddText(new Vector2(x, ty), ImGui.GetColorU32(summaryCol), summary);
        dl.PopClipRect();

        if (preview != null)
        {
            var after = ImGui.GetCursorScreenPos();
            if (wispW > 0)
            {
                using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
                {
                    var ink = Theme.InkBounds(wispStr);
                    var wpos = new Vector2(right - previewW - ink.X, rowStart.Y + d / 2f - (ink.Y + ink.W) / 2f);
                    dl.AddText(wpos, ImGui.GetColorU32(Theme.BrandPink), wispStr);
                }
                if (ImGui.IsMouseHoveringRect(new Vector2(right - previewW, rowStart.Y), new Vector2(right - previewW + wispW, rowStart.Y + d)))
                    ImGui.SetTooltip("Will-o-Bars is controlling this hotbar.");
            }
            var pillY = rowStart.Y + (d - ImGui.GetTextLineHeight() - S(2)) / 2f;
            if (vanilla)
            {
                ImGui.SetCursorScreenPos(new Vector2(right - previewW, pillY));
                if (ChoicePill("##show", "shown", previewOn, Theme.Pink) && !previewOn)
                    plugin.RequestGameVisibility(bar, true);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip($"Vanilla setting: show hotbar {bar}.\nThe same as the game's own \"Display hotbar\" option; Will-o-Bars doesn't control this hotbar.");
                ImGui.SameLine(0, pillGap);
                if (ChoicePill("##hide", "hidden", !previewOn, Grey) && previewOn)
                    plugin.RequestGameVisibility(bar, false);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip($"Vanilla setting: hide hotbar {bar}.\nThe same as the game's own \"Display hotbar\" option; Will-o-Bars doesn't control this hotbar.");
            }
            else
            {
                ImGui.SetCursorScreenPos(new Vector2(right - previewW + wispW, pillY));
                if (struck)
                {
                    // Hidden by Will-o-Bars (not by the game): "shown" crossed through.
                    Theme.Pill("shown", Grey);
                    var min = ImGui.GetItemRectMin();
                    var max = ImGui.GetItemRectMax();
                    var midY = (min.Y + max.Y) / 2f;
                    dl.AddLine(new Vector2(min.X + S(5), midY), new Vector2(max.X - S(5), midY), ImGui.GetColorU32(Theme.TextOn(Grey)), S(1.6f));
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip($"Hotbar {bar} is hidden by Will-o-Bars on {WhoText(sel)}.\nChange it with the Mode below.");
                }
                else
                {
                    Theme.Pill(preview, previewOn ? Theme.Pink : Grey);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip($"Hotbar {bar} is {preview} on {WhoText(sel)}.\nWill-o-Bars controls this hotbar: change it with the Mode below.");
                }
            }
            ImGui.SetCursorScreenPos(after);
            ImGui.Dummy(Vector2.Zero);
        }

        if (!open)
            return;

        // ---- unfolded: settings for this hotbar
        ImGuiHelpers.ScaledDummy(2f);

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.TextSoft, "Mode");
        ImGui.SameLine();
        ImGui.SetCursorScreenPos(new Vector2(left + S(64), ImGui.GetCursorScreenPos().Y));
        var pauseW = rule != null ? d + S(8) : 0f;
        ImGui.SetNextItemWidth(Math.Max(S(90), Math.Min(S(150), right - ImGui.GetCursorScreenPos().X - pauseW)));
        var gameLabel = $"Game setting ({GameState(bar)})";
        using (var combo = ImRaii.Combo("##mode", rule == null ? gameLabel : ModeName(rule.Mode)))
        {
            if (combo.Success)
            {
                if (ImGui.Selectable(gameLabel, rule == null))
                {
                    if (rule != null)
                    {
                        Config.Rules.Remove(rule);
                        plugin.Save();
                    }
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Use the game's own setting for this hotbar; Will-o-Bars leaves it alone.");

                foreach (var m in new[] { RuleMode.Show, RuleMode.ShowOnly, RuleMode.HideOn })
                {
                    if (ImGui.Selectable(ModeName(m), rule?.Mode == m))
                    {
                        if (rule == null)
                        {
                            rule = new HotbarRule { Hotbar = bar };
                            Config.Rules.Add(rule);
                        }
                        rule.Mode = m;
                        plugin.Save();
                    }
                }
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Game setting: whatever the game itself does.\nShow: always visible.\nShow only on: visible only on the jobs you tick below.\nHide on: hidden on the jobs you tick below, visible everywhere else.");

        if (rule != null)
        {
            ImGui.SameLine(0, S(8));
            var paused = !rule.Enabled;
            if (Theme.IconCircle("##pause", paused ? FontAwesomeIcon.EyeSlash : FontAwesomeIcon.Eye, paused ? Theme.Muted : Theme.AccentText, d))
            {
                rule.Enabled = !rule.Enabled;
                plugin.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(paused ? "Paused (the hotbar is left visible). Click to turn it back on." : "On. Click to pause this hotbar's rule.");
        }

        // Own actions per class/job: shows the game's real "Shared" setting until the player changes it here.
        var separate = rule?.SeparatePerJob ?? plugin.IsHotbarShared(bar) == false;
        if (ImGui.Checkbox("Own actions per class/job##sep", ref separate))
        {
            if (rule == null)
            {
                // Start from what the game shows now, so ticking this box doesn't also show or hide the hotbar.
                var shownNow = plugin.Look.IsShownInGame(bar) != false;
                rule = new HotbarRule { Hotbar = bar, Mode = shownNow ? RuleMode.Show : RuleMode.ShowOnly };
                Config.Rules.Add(rule);
            }
            rule.SeparatePerJob = separate;
            plugin.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Each class/job keeps its own actions on this hotbar. Put Fisher actions on it while you're\n" +
                             "Fisher and they stay on Fisher: other classes/jobs get their own (or an empty) bar,\n" +
                             "and the Fisher actions come back when you switch to Fisher again.\n\n" +
                             "This is the game's \"Shared\" setting (Character Configuration > Hotbar Settings), unticked.\n" +
                             "A backup of your hotbars is saved automatically right before it changes.");

        // The jobs this hotbar is shown/hidden on, edited right here.
        if (rule is { Mode: RuleMode.ShowOnly or RuleMode.HideOn })
        {
            ImGuiHelpers.ScaledDummy(2f);
            ImGui.TextColored(Theme.TextSoft, rule.Mode == RuleMode.ShowOnly ? "Shown only on:" : "Hidden on:");
            var r = rule;
            DrawJobGrid($"bar{bar}", r.Jobs.Contains, (ids, on) =>
            {
                foreach (var id in ids)
                {
                    if (on) r.Jobs.Add(id);
                    else r.Jobs.Remove(id);
                }
                plugin.Save();
            }, compact: true);
        }

    }

    private static string WhoText(System.Collections.Generic.List<uint> sel)
        => sel.Count == 1 ? Jobs.Get(sel[0])?.Name ?? "the picked class/job" : $"the {sel.Count} picked classes/jobs";

    private static string ModeName(RuleMode m) => m switch
    {
        RuleMode.ShowOnly => "Show only on",
        RuleMode.HideOn => "Hide on",
        _ => "Show",
    };

    /// <summary>Change the rule for one hotbar so it is shown/hidden on these jobs, creating a rule when needed.</summary>
    private void SetVisible(int bar, System.Collections.Generic.List<uint> jobs, bool visible)
    {
        var rule = Config.Rules.FirstOrDefault(r => r.Hotbar == bar);
        if (rule == null)
        {
            if (visible)
                return; // no rule = already shown everywhere
            rule = new HotbarRule { Hotbar = bar, Mode = RuleMode.HideOn };
            Config.Rules.Add(rule);
        }

        rule.Enabled = true;
        if (rule.Mode == RuleMode.Show)
        {
            if (visible)
            {
                plugin.Save();
                return;
            }
            rule.Mode = RuleMode.HideOn;
            rule.Jobs.Clear();
        }

        // ShowOnly lists the visible jobs, HideOn lists the hidden ones.
        var add = visible == (rule.Mode == RuleMode.ShowOnly);
        foreach (var j in jobs)
        {
            if (add) rule.Jobs.Add(j);
            else rule.Jobs.Remove(j);
        }
        plugin.Save();
    }

    // ---------------------------------------------------------------- "Backups" tab

    private string backupName = string.Empty;

    // ---------------------------------------------------------------- settings export / import

    private readonly Dalamud.Interface.ImGuiFileDialog.FileDialogManager fileDialog = new();
    private string? transferMessage;
    private bool transferIsError;
    private SettingsTransfer.Data? pendingImport;
    private string pendingImportFrom = string.Empty;

    private void DrawSettingsTransfer()
    {
        Theme.Card("transfer", () =>
        {
            ImGui.TextColored(Theme.Text, "Import/Export");
            Theme.BrandText("Save your hotbar rules outside the plugin, so you can bring them back after resetting Will-o-Bars or share them with a friend.", Theme.Muted, Theme.CardInnerRight);
            ImGuiHelpers.ScaledDummy(2f);

            // Save folder: picked by the player (nothing is picked the first time).
            var folder = SettingsTransfer.Folder;
            ImGui.TextColored(Theme.TextSoft, "Save folder");
            var chooseW = S(130);
            var pathW = Theme.CardInnerWidth - chooseW - ImGui.GetStyle().ItemSpacing.X;
            ImGui.AlignTextToFramePadding();
            var shown = string.IsNullOrEmpty(folder) ? "No folder picked yet" : folder;
            var clipped = shown;
            while (clipped.Length > 4 && ImGui.CalcTextSize(clipped).X > pathW)
                clipped = "…" + clipped[Math.Min(clipped.Length - 1, 4)..];
            ImGui.TextColored(string.IsNullOrEmpty(folder) ? Theme.Amber : Theme.Muted, clipped);
            if (ImGui.IsItemHovered() && clipped != shown)
                ImGui.SetTooltip(shown);
            ImGui.SameLine();
            ImGui.SetCursorScreenPos(new Vector2(Theme.CardInnerRight - chooseW, ImGui.GetCursorScreenPos().Y));
            using (ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, S(10)))
            {
                if (Theme.Button(string.IsNullOrEmpty(folder) ? "Choose folder" : "Change folder", new Vector2(chooseW, 0)))
                {
                    fileDialog.OpenFolderDialog("Pick a folder for Will-o-Bars saves", (ok, path) =>
                    {
                        if (!ok || string.IsNullOrWhiteSpace(path))
                            return;
                        try
                        {
                            plugin.SetSaveFolder(path);
                            SetTransfer($"Saves now go to {path}.", false);
                        }
                        catch (Exception e)
                        {
                            SetTransfer($"Couldn't use that folder: {e.Message}", true);
                        }
                    }, string.IsNullOrEmpty(folder) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : folder, true);
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Exported settings and hotbar backups are saved here. Pick a folder outside the plugin, so resetting Will-o-Bars doesn't delete them.");
            ImGuiHelpers.ScaledDummy(2f);

            var gap = ImGui.GetStyle().ItemSpacing.X;
            var bw = (Theme.CardInnerWidth - gap * 2) / 3f;
            using (ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, S(10)))
            {
                bool saveClicked;
                using (ImRaii.Disabled(string.IsNullOrEmpty(folder)))
                    saveClicked = Theme.Button("Save to file", new Vector2(bw, 0));
                if (saveClicked)
                {
                    try
                    {
                        var path = SettingsTransfer.SaveFile(SettingsTransfer.From(Config));
                        SetTransfer($"Saved as {System.IO.Path.GetFileName(path)}.", false);
                    }
                    catch (Exception e)
                    {
                        SetTransfer($"Couldn't save: {e.Message}", true);
                    }
                }
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip(string.IsNullOrEmpty(folder) ? "Choose a save folder first." : $"Saves to {folder}");

                ImGui.SameLine();
                if (Theme.Button("Copy as text", new Vector2(bw, 0)))
                {
                    ImGui.SetClipboardText(SettingsTransfer.ToText(SettingsTransfer.From(Config)));
                    SetTransfer("Copied! Paste it somewhere safe, or send it to a friend.", false);
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Copies your settings as a short text to the clipboard.");

                ImGui.SameLine();
                if (Theme.Button("Paste text", new Vector2(bw, 0)))
                {
                    var data = SettingsTransfer.Parse(ImGui.GetClipboardText() ?? string.Empty, out var error);
                    if (data == null)
                        SetTransfer(error, true);
                    else
                        AskImport(data, "the clipboard");
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Imports settings text you copied earlier.");
            }

            if (transferMessage != null)
                Theme.Wrapped(transferMessage, transferIsError ? Theme.Amber : Theme.AccentText);

            // Saved files
            var files = SettingsTransfer.Files();
            if (files.Count > 0)
            {
                ImGuiHelpers.ScaledDummy(2f);
                ImGui.TextColored(Theme.TextSoft, "Saved files");
                var d = ImGui.GetFrameHeight();
                var right = Theme.CardInnerRight;
                foreach (var file in files.Take(8))
                {
                    using var id = ImRaii.PushId(file);
                    var name = System.IO.Path.GetFileNameWithoutExtension(file).Replace("settings-", string.Empty).Replace('_', ' ');
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextColored(Theme.Muted, name);
                    ImGui.SameLine();
                    ImGui.SetCursorScreenPos(new Vector2(right - S(84) - d - S(6), ImGui.GetCursorScreenPos().Y));
                    if (Chip("##load", FontAwesomeIcon.FileImport, "Import", false, S(84)))
                    {
                        try
                        {
                            var data = SettingsTransfer.Parse(System.IO.File.ReadAllText(file), out var error);
                            if (data == null) SetTransfer(error, true);
                            else AskImport(data, System.IO.Path.GetFileName(file));
                        }
                        catch (Exception e)
                        {
                            SetTransfer($"Couldn't read the file: {e.Message}", true);
                        }
                    }
                    ImGui.SameLine(0, S(6));
                    if (Theme.IconCircle("##del", FontAwesomeIcon.Trash, Theme.Text, d))
                        ImGui.OpenPopup("##confirmfiledel");
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Delete this file");

                    using (var filePopup = ImRaii.Popup("##confirmfiledel"))
                    {
                        if (filePopup.Success)
                        {
                            ImGui.TextColored(Theme.Text, "Delete this settings file?");
                            if (Theme.Button("Delete", new Vector2(S(90), 0)))
                            {
                                try { System.IO.File.Delete(file); }
                                catch (Exception e) { SetTransfer($"Couldn't delete: {e.Message}", true); }
                                ImGui.CloseCurrentPopup();
                            }
                            ImGui.SameLine();
                            if (ImGui.Button("Cancel", new Vector2(S(90), 0)))
                                ImGui.CloseCurrentPopup();
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(folder) && TextLink("Open the save folder"))
            {
                try
                {
                    System.IO.Directory.CreateDirectory(folder);
                    Util.OpenLink(folder);
                }
                catch (Exception e)
                {
                    SetTransfer($"Couldn't open the folder: {e.Message}", true);
                }
            }

            // Confirm before replacing anything.
            if (pendingImport != null)
                ImGui.OpenPopup("##confirmimport");
            using var popup = ImRaii.Popup("##confirmimport");
            if (popup.Success && pendingImport != null)
            {
                var n = pendingImport.Rules.Count;
                ImGui.TextColored(Theme.Text, $"Import settings from {pendingImportFrom}?");
                Theme.Wrapped($"Your hotbar rules are replaced by these {n} {(n == 1 ? "rule" : "rules")}" +
                              (pendingImport.Exported != default ? $" (saved {pendingImport.Exported:d MMM yyyy, HH:mm})." : "."), Theme.Muted);
                if (Theme.Button("Import", new Vector2(S(90), 0)))
                {
                    SettingsTransfer.ApplyTo(pendingImport, Config);
                    plugin.Save();
                    SetTransfer($"Imported {n} {(n == 1 ? "rule" : "rules")}.", false);
                    pendingImport = null;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel", new Vector2(S(90), 0)))
                {
                    pendingImport = null;
                    ImGui.CloseCurrentPopup();
                }
            }
            else if (!popup.Success)
            {
                pendingImport = null; // closed by clicking outside
            }
        });
    }

    private void AskImport(SettingsTransfer.Data data, string from)
    {
        pendingImport = data;
        pendingImportFrom = from;
    }

    private void SetTransfer(string message, bool error)
    {
        transferMessage = message;
        transferIsError = error;
    }

    private void DrawBackups()
    {
        var backups = plugin.Backups;

        Theme.Card("savebackup", () =>
        {
            ImGui.TextColored(Theme.Text, "Save your hotbars");
            Theme.Wrapped("Saves every action on every hotbar and cross hotbar, for all your classes and jobs, plus which hotbars are shared. Restore it any time to go back to exactly this setup.", Theme.Muted);
            ImGuiHelpers.ScaledDummy(2f);

            var w = Theme.CardInnerWidth;
            var buttonW = S(110);
            ImGui.SetNextItemWidth(Math.Max(S(80), w - buttonW - ImGui.GetStyle().ItemSpacing.X));
            ImGui.InputTextWithHint("##name", "Name (optional)", ref backupName, 60);
            ImGui.SameLine();
            using (ImRaii.Disabled(!Backups.Ready))
            using (ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, S(10)))
            {
                if (Theme.Button("Save now", new Vector2(buttonW, 0)))
                {
                    plugin.RequestBackup(string.IsNullOrWhiteSpace(backupName) ? "Saved by you" : backupName.Trim());
                    backupName = string.Empty;
                }
            }
            if (!Backups.Ready && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Log in to a character first.");

            if (!string.IsNullOrEmpty(backups.LastMessage))
                Theme.Wrapped(backups.LastMessage, Theme.AccentText);
        });

        Theme.Card("history", () =>
        {
            ImGui.TextColored(Theme.Text, "History");
            Theme.Wrapped("Will-o-Bars also saves one automatically the first time you log in with it, and right before it changes whether a hotbar is shared. Restoring pauses all your rules, so nothing changes your hotbars again until you turn a rule back on. Pick a save folder in the Settings tab to keep backups outside the plugin, so resetting it doesn't delete them.", Theme.Muted);
            ImGuiHelpers.ScaledDummy(2f);

            if (backups.List.Count == 0)
            {
                Theme.Wrapped("No backups yet.", Theme.Muted);
                return;
            }

            var current = Backups.CurrentContentId;
            var right = Theme.CardInnerRight;
            var d = ImGui.GetFrameHeight();
            (string Path, HotbarBackup Backup)? delete = null;

            foreach (var (path, b) in backups.List)
            {
                using var id = ImRaii.PushId(path);
                var rowY = ImGui.GetCursorScreenPos().Y;
                var mine = b.ContentId == current;

                ImGui.TextColored(mine ? Theme.Text : Theme.Muted, b.Name);
                if (b.Automatic)
                {
                    ImGui.SameLine(0, S(6));
                    Theme.Pill("auto", new Vector4(0.55f, 0.55f, 0.62f, 1f));
                }
                ImGui.TextColored(Theme.Muted, $"{b.Created:d MMM yyyy, HH:mm}  ·  {b.Character}  ·  {b.ActionCount} actions");

                // Restore / delete on the right
                var restoreW = S(84);
                ImGui.SetCursorScreenPos(new Vector2(right - restoreW - d - S(6), rowY + S(2)));
                using (ImRaii.Disabled(!mine || !Backups.Ready))
                {
                    if (Chip("##restore", FontAwesomeIcon.Undo, "Restore", false, restoreW))
                        ImGui.OpenPopup("##confirmrestore");
                }
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip(mine ? "Put your hotbars back to this backup" : $"Log in to {b.Character} to restore this one.");
                ImGui.SameLine(0, S(6));
                if (Theme.IconCircle("##del", FontAwesomeIcon.Trash, Theme.Text, d))
                    ImGui.OpenPopup("##confirmdel");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Delete this backup");

                using (var popup = ImRaii.Popup("##confirmrestore"))
                {
                    if (popup.Success)
                    {
                        ImGui.TextColored(Theme.Text, "Restore this backup?");
                        Theme.Wrapped($"All hotbars of {b.Character} go back to {b.Created:d MMM yyyy, HH:mm}. All rules get paused.", Theme.Muted);
                        if (Theme.Button("Restore", new Vector2(S(90), 0)))
                        {
                            plugin.RequestRestore(b);
                            ImGui.CloseCurrentPopup();
                        }
                        ImGui.SameLine();
                        if (ImGui.Button("Cancel", new Vector2(S(90), 0)))
                            ImGui.CloseCurrentPopup();
                    }
                }

                using (var popup = ImRaii.Popup("##confirmdel"))
                {
                    if (popup.Success)
                    {
                        ImGui.TextColored(Theme.Text, "Delete this backup?");
                        if (Theme.Button("Delete", new Vector2(S(90), 0)))
                        {
                            delete = (path, b);
                            ImGui.CloseCurrentPopup();
                        }
                        ImGui.SameLine();
                        if (ImGui.Button("Cancel", new Vector2(S(90), 0)))
                            ImGui.CloseCurrentPopup();
                    }
                }

                ImGui.SetCursorScreenPos(new Vector2(Theme.CardInnerLeft, Math.Max(ImGui.GetItemRectMax().Y, rowY + ImGui.GetTextLineHeightWithSpacing() * 2) + S(4)));
                ImGui.Dummy(Vector2.Zero);
            }

            if (delete is { } del)
                backups.Delete(del.Path);
        });
    }

    // ---------------------------------------------------------------- job grid (shared by both tabs)

    private sealed class GridState
    {
        public int Anchor = -1;
        public bool Painting;
        public bool PaintValue;
    }

    private readonly System.Collections.Generic.Dictionary<string, GridState> grids = new();

    /// <summary>
    /// Job icons grouped by role. Click toggles one, Shift+click sets a range, click-and-drag paints over many,
    /// clicking a role name toggles the whole group. <paramref name="set"/> gets the changed ids and their new state.
    /// </summary>
    private void DrawJobGrid(string id, Func<uint, bool> isOn, Action<System.Collections.Generic.IReadOnlyList<uint>, bool> set, bool compact = false)
    {
        if (!grids.TryGetValue(id, out var st))
            grids[id] = st = new GridState();
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            st.Painting = false;

        var icon = compact ? S(24) : S(30);
        var spacing = compact ? S(4) : S(5);
        var labelW = compact ? S(66) : S(74);
        var left = Theme.CardInnerLeft;
        var right = Theme.CardInnerRight;
        var dl = ImGui.GetWindowDrawList();
        var all = Jobs.All;

        using var pid = ImRaii.PushId(id);
        foreach (var group in all.GroupBy(j => j.Group))
        {
            var list = group.ToList();
            var groupAll = list.All(j => isOn(j.Id));
            var groupAny = list.Any(j => isOn(j.Id));

            var rowStart = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(new Vector2(left, rowStart.Y));
            if (ImGui.InvisibleButton($"##grp{group.Key}", new Vector2(labelW - spacing, icon)))
                set(list.Select(j => j.Id).ToList(), !groupAll);
            var hovered = ImGui.IsItemHovered();
            var gname = Jobs.GroupName(group.Key);
            if (hovered)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                ImGui.SetTooltip(groupAll ? $"Unpick all {gname.ToLowerInvariant()}" : $"Pick all {gname.ToLowerInvariant()}");
            }
            dl.AddText(new Vector2(left, rowStart.Y + (icon - ImGui.GetTextLineHeight()) / 2f),
                ImGui.GetColorU32(hovered ? Theme.AccentText : groupAny ? Theme.Text : Theme.Muted), gname);

            var x = left + labelW;
            var y = rowStart.Y;
            foreach (var j in list)
            {
                if (x + icon > right + 0.5f && x > left + labelW)
                {
                    x = left + labelW;
                    y += icon + spacing;
                }

                var index = IndexOf(all, j.Id);
                var pos = new Vector2(x, y);
                ImGui.SetCursorScreenPos(pos);
                ImGui.InvisibleButton($"##job{j.Id}", new Vector2(icon, icon));
                var over = ImGui.IsMouseHoveringRect(pos, pos + new Vector2(icon)) && ImGui.IsWindowHovered();

                if (ImGui.IsItemActivated())
                {
                    if (ImGui.GetIO().KeyShift && st.Anchor >= 0)
                    {
                        // Range from the last clicked icon to this one, all set like the last clicked one.
                        var value = isOn(all[st.Anchor].Id);
                        var (a, b) = st.Anchor < index ? (st.Anchor, index) : (index, st.Anchor);
                        set(all.Skip(a).Take(b - a + 1).Select(r => r.Id).ToList(), value);
                    }
                    else
                    {
                        st.PaintValue = !isOn(j.Id);
                        st.Painting = true;
                        st.Anchor = index;
                        set(new[] { j.Id }, st.PaintValue);
                    }
                }
                else if (st.Painting && over && isOn(j.Id) != st.PaintValue)
                {
                    set(new[] { j.Id }, st.PaintValue);
                }

                DrawJobIconToggle(j, isOn(j.Id), pos, icon, over);
                x += icon + spacing;
            }

            ImGui.SetCursorScreenPos(new Vector2(left, y + icon + spacing));
            ImGui.Dummy(Vector2.Zero);
        }
    }

    private static int IndexOf(System.Collections.Generic.IReadOnlyList<JobInfo> list, uint id)
    {
        for (var i = 0; i < list.Count; i++)
            if (list[i].Id == id)
                return i;
        return -1;
    }

    private static void DrawJobIconToggle(JobInfo job, bool selected, Vector2 pos, float size, bool hovered)
    {
        var dl = ImGui.GetWindowDrawList();
        var round = Theme.R(8);

        if (selected)
            dl.AddRectFilled(pos, pos + new Vector2(size), ImGui.GetColorU32(Theme.Pink with { W = Theme.UseDalamud ? 0.6f : 0.28f }), round);
        else if (hovered)
            dl.AddRectFilled(pos, pos + new Vector2(size), ImGui.GetColorU32(Theme.GlassHover), round);

        var inset = S(2);
        var tex = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(job.IconId)).GetWrapOrEmpty();
        var tint = selected ? Vector4.One : new Vector4(1f, 1f, 1f, hovered ? 0.75f : 0.38f);
        dl.AddImage(tex.Handle, pos + new Vector2(inset), pos + new Vector2(size - inset), Vector2.Zero, Vector2.One, ImGui.GetColorU32(tint));

        if (selected)
            dl.AddRect(pos, pos + new Vector2(size), ImGui.GetColorU32(Theme.UseDalamud ? Theme.Text : Theme.Pink), round, ImDrawFlags.None, S(1.8f));
        else if (hovered)
            dl.AddRect(pos, pos + new Vector2(size), ImGui.GetColorU32(Theme.Border), round, ImDrawFlags.None, S(1.2f));

        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip($"{job.Name} ({job.Abbreviation})");
        }
    }

    private static void DrawJobIcon(uint iconId, float size, float alpha)
    {
        var tex = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
        ImGui.Image(tex.Handle, new Vector2(size), Vector2.Zero, Vector2.One, new Vector4(1f, 1f, 1f, alpha));
    }

    // ---------------------------------------------------------------- small widgets

    /// <summary>Small pink text that acts like a link.</summary>
    private static bool TextLink(string label)
    {
        ImGui.TextColored(Theme.AccentText, label);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            ImGui.GetWindowDrawList().AddLine(new Vector2(min.X, max.Y), max, ImGui.GetColorU32(Theme.AccentText), S(1));
        }
        return hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
    }

    /// <summary>Small pill you can click: filled when <paramref name="active"/>, just an outline when not.</summary>
    private static bool ChoicePill(string id, string text, bool active, Vector4 fill)
    {
        var pad = new Vector2(S(7), S(1));
        var textSize = ImGui.CalcTextSize(text);
        var size = textSize + pad * 2;
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();

        if (active)
            dl.AddRectFilled(pos, pos + size, ImGui.GetColorU32(fill), Theme.R(8));
        else if (hovered)
            dl.AddRectFilled(pos, pos + size, ImGui.GetColorU32(Theme.GlassHover), Theme.R(8));
        dl.AddRect(pos, pos + size, ImGui.GetColorU32(active ? Theme.Outline : hovered ? Theme.Pink : Theme.Border), Theme.R(8));

        var ink = Theme.InkBounds(text);
        var col = active ? Theme.TextOn(fill) : hovered ? Theme.Text : Theme.Muted;
        dl.AddText(new Vector2(pos.X + (size.X - textSize.X) / 2f, pos.Y + size.Y / 2f - (ink.Y + ink.W) / 2f), ImGui.GetColorU32(col), text);

        if (hovered && !active)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        return clicked;
    }

    /// <summary>
    /// On/off switch. <paramref name="value"/> is 1 (on, pink), 0 (off) or anything between for "partly on"
    /// (the knob sits in the middle). Returns true when clicked.
    /// </summary>
    private static bool Toggle(string id, float value, float width)
    {
        var h = ImGui.GetFrameHeight();
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(width, h));
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var round = Theme.UseDalamud ? Theme.R(0) : h / 2f;

        var on = value >= 1f;
        var track = on ? Theme.Pink : value > 0f ? Theme.Pink with { W = 0.45f } : hovered ? Theme.GlassHover : Theme.Glass;
        dl.AddRectFilled(pos, pos + new Vector2(width, h), ImGui.GetColorU32(track), round);
        dl.AddRect(pos, pos + new Vector2(width, h), ImGui.GetColorU32(hovered ? (Theme.UseDalamud ? Theme.Text : Theme.ButtonHover) : on ? Theme.Outline : Theme.Border), round, ImDrawFlags.None, S(1.2f));

        var pad = S(3);
        var r = h / 2f - pad;
        var cx = pos.X + pad + r + (width - (pad + r) * 2) * Math.Clamp(value, 0f, 1f);
        var knob = on ? Theme.OnPink : Theme.Text;
        if (Theme.UseDalamud)
            dl.AddRectFilled(new Vector2(cx - r, pos.Y + pad), new Vector2(cx + r, pos.Y + h - pad), ImGui.GetColorU32(knob), Theme.R(0));
        else
            dl.AddCircleFilled(new Vector2(cx, pos.Y + h / 2f), r, ImGui.GetColorU32(knob), 24);

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        return clicked;
    }

    /// <summary>Rounded chip with an icon and a label; filled pink when <paramref name="on"/>.</summary>
    private static bool Chip(string id, FontAwesomeIcon icon, string label, bool on, float width)
    {
        var h = ImGui.GetFrameHeight();
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(width, h));
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var round = Theme.UseDalamud ? Theme.R(0) : h / 2f;

        var fill = on ? Theme.Pink : hovered ? Theme.GlassHover : Theme.Glass;
        dl.AddRectFilled(pos, pos + new Vector2(width, h), ImGui.GetColorU32(fill), round);
        dl.AddRect(pos, pos + new Vector2(width, h), ImGui.GetColorU32(on ? Theme.Outline : hovered ? Theme.Pink : Theme.Border), round, ImDrawFlags.None, S(1.2f));

        var textCol = on ? Theme.OnPink : Theme.Text;
        string iconStr = icon.ToIconString();
        Vector4 ink;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            ink = Theme.InkBounds(iconStr);
        var iconW = ink.Z - ink.X;
        var gap = S(6);
        var labelW = ImGui.CalcTextSize(label).X;
        var sx = pos.X + (width - iconW - gap - labelW) / 2f;
        var cy = pos.Y + h / 2f;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            dl.AddText(new Vector2(sx - ink.X, cy - (ink.Y + ink.W) / 2f), ImGui.GetColorU32(textCol), iconStr);
        var lInk = Theme.InkBounds(label);
        dl.AddText(new Vector2(sx + iconW + gap, cy - (lInk.Y + lInk.W) / 2f), ImGui.GetColorU32(textCol), label);

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        return clicked;
    }

    // ---------------------------------------------------------------- footer

    private void DrawFooter()
    {
        ImGuiHelpers.ScaledDummy(2f);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var avail = ImGui.GetContentRegionAvail().X;
        var linkW = S(96);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (avail - (linkW * 2 + spacing)) / 2f));

        if (Theme.LinkButton("##kofi", FontAwesomeIcon.Coffee, "Ko-fi", rainbow: true, width: linkW))
            Util.OpenLink(Theme.KofiUrl);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Support me on Ko-fi");
        ImGui.SameLine();
        if (Theme.LinkButton("##github", FontAwesomeIcon.CodeBranch, "GitHub", width: linkW))
            Util.OpenLink(GitHubUrl);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(GitHubUrl);
    }
}
