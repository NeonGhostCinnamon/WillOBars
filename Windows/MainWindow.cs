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
        : base($"Will-o-Bars##{Plugin.Id}Main", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove)
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

    /// <summary>Where the window was last frame (the rule editor opens beside it).</summary>
    public Vector2 LastPos { get; private set; }
    public Vector2 LastSize { get; private set; }

    // Height of everything in the window last frame, and the width the window had.
    private float fitHeight;
    private float fitWidth;

    /// <summary>Remember how tall the content is; PreDraw sizes the window to it on the next frame.</summary>
    private void FitHeightToContent()
    {
        var style = ImGui.GetStyle();
        fitHeight = ImGui.GetCursorPosY() - style.ItemSpacing.Y + style.WindowPadding.Y;
        fitWidth = ImGui.GetWindowSize().X;
        LastPos = ImGui.GetWindowPos();
        LastSize = ImGui.GetWindowSize();
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
        if (Plugin.IsTestBuild)
        {
            // Test build: a small "TEST" tag after the title.
            var max = ImGui.GetItemRectMax();
            var tag = "TEST";
            var ts = ImGui.CalcTextSize(tag);
            var pos = new Vector2(max.X + S(6), max.Y - ts.Y - S(6));
            var dlh = ImGui.GetWindowDrawList();
            dlh.AddRectFilled(pos - new Vector2(S(5), S(1)), pos + ts + new Vector2(S(5), S(1)), ImGui.GetColorU32(Theme.Amber), S(6));
            dlh.AddText(pos, ImGui.GetColorU32(Theme.TextOn(Theme.Amber)), tag);
        }
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

    /// <summary>
    /// One short card: the job you're on, the hotbars Will-o-Bars controls (shown/hidden right now), and a line
    /// with where you are, the weather and whether you're in combat.
    /// </summary>
    private void DrawStatusCard()
    {
        var job = Jobs.Get(plugin.CurrentJob);
        Theme.Card("status", () =>
        {
            if (job == null)
            {
                ImGui.TextColored(Theme.Muted, "Current class / job");
                Theme.Wrapped("Log in to a character to see your hotbars change.", Theme.TextSoft);
                return;
            }

            var left = Theme.CardInnerLeft;
            var right = Theme.CardInnerRight;
            var iconSize = S(36);
            var rowTop = ImGui.GetCursorScreenPos();

            DrawJobIcon(job.IconId, iconSize, 1f);
            ImGui.SameLine(0, S(8));
            using (plugin.TitleFont.Available ? plugin.TitleFont.Push() : null)
            {
                var h = ImGui.GetTextLineHeight() * 0.8f;
                ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, rowTop.Y + (iconSize - ImGui.GetTextLineHeight()) / 2f));
                ImGui.TextColored(Theme.Text, job.Name);
            }
            var nameRight = ImGui.GetItemRectMax().X;

            // Pills for the controlled hotbars, right-aligned: "4 (eye)" shown, "6 (crossed eye)" hidden.
            var bars = Config.Rules.Where(r => r.Enabled).Select(r => r.Hotbar).Distinct().OrderBy(b => b).ToList();
            if (bars.Count > 0)
            {
                var gap = S(5);
                var widths = bars.Select(b => BarPillWidth(b)).ToList();
                var total = widths.Sum() + gap * (bars.Count - 1);
                var x = Math.Max(nameRight + S(10), right - total);
                var pillH = ImGui.GetTextLineHeight() + S(2);
                var y = rowTop.Y + (iconSize - pillH) / 2f;
                for (var i = 0; i < bars.Count; i++)
                {
                    if (x + widths[i] > right + 0.5f)
                        break;
                    ImGui.SetCursorScreenPos(new Vector2(x, y));
                    var bar = bars[i];
                    var shown = plugin.Applied.TryGetValue(bar, out var v) ? v : plugin.Look.IsShownInGame(bar) != false;
                    BarPill(bar, shown);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip($"Hotbar {bar}: {(shown ? "shown" : "hidden")}\n{WhyText(bar)}");
                    x += widths[i] + gap;
                }
            }

            ImGui.SetCursorScreenPos(new Vector2(left, rowTop.Y + iconSize + S(4)));
            var st = plugin.State;
            var where = GameData.PlaceNameText(st.PlaceName);
            var parts = new System.Collections.Generic.List<string>();
            if (st.PlaceName != 0) parts.Add(where);
            if (st.Weather != 0) parts.Add(GameData.WeatherName(st.Weather));
            parts.Add(st.InCombat ? "in combat" : "not in combat");
            Theme.Wrapped(string.Join(" · ", parts), Theme.Muted);
        });
    }

    /// <summary>Which case is deciding a hotbar right now, in words.</summary>
    private string WhyText(int bar)
    {
        var rule = Config.Rules.FirstOrDefault(r => r.Hotbar == bar);
        if (rule == null || !plugin.DecidingCase.TryGetValue(bar, out var which))
            return "Game setting.";
        var cases = rule.Cases ?? new System.Collections.Generic.List<RuleCase>();
        if (which < 0 || which >= cases.Count)
            return "No case matches, so: otherwise " + (rule.OtherwiseShow ? "show." : "hide.");
        var count = cases.Count(c => c.Conditions.Count > 0);
        return (count > 1 ? $"Case {which + 1}: " : "") + RuleLogic.Describe(cases[which]) + ".";
    }

    private static float BarPillWidth(int bar)
    {
        float iconW;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            iconW = ImGui.CalcTextSize(FontAwesomeIcon.EyeSlash.ToIconString()).X;
        return ImGui.CalcTextSize(bar.ToString()).X + S(4) + iconW + S(14);
    }

    /// <summary>Small pill: hotbar number and an eye (shown, pink) or crossed eye (hidden, grey).</summary>
    private static void BarPill(int bar, bool shown)
    {
        var width = BarPillWidth(bar);
        var h = ImGui.GetTextLineHeight() + S(2);
        var pos = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(width, h));
        var dl = ImGui.GetWindowDrawList();
        var fill = shown ? Theme.Pink : Grey;
        dl.AddRectFilled(pos, pos + new Vector2(width, h), ImGui.GetColorU32(fill), Theme.R(8));
        dl.AddRect(pos, pos + new Vector2(width, h), ImGui.GetColorU32(Theme.Outline), Theme.R(8));
        var col = ImGui.GetColorU32(Theme.TextOn(fill));
        var num = bar.ToString();
        var ink = Theme.InkBounds(num);
        dl.AddText(new Vector2(pos.X + S(7), pos.Y + h / 2f - (ink.Y + ink.W) / 2f), col, num);
        var x = pos.X + S(7) + ImGui.CalcTextSize(num).X + S(4);
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            var icon = (shown ? FontAwesomeIcon.Eye : FontAwesomeIcon.EyeSlash).ToIconString();
            var iInk = Theme.InkBounds(icon);
            dl.AddText(new Vector2(x - iInk.X, pos.Y + h / 2f - (iInk.Y + iInk.W) / 2f), col, icon);
        }
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

    internal static readonly Vector4 Grey = new(0.55f, 0.55f, 0.62f, 1f);

    /// <summary>Every hotbar as a short card: what its rule does, an Edit button and whether it's shown now.</summary>
    private void DrawHotbars()
    {
        var avail = ImGui.GetContentRegionAvail().X;
        var gap = S(8);
        if (avail < S(460))
        {
            for (var bar = 1; bar <= 10; bar++)
            {
                var b = bar;
                Theme.Card($"bar{b}", () => DrawHotbarCard(b), 0, plugin.RulesWindow.EditingBar == b);
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
                Theme.Card($"bar{b}", () => DrawHotbarCard(b), colW, plugin.RulesWindow.EditingBar == b);
            }
            ImGui.EndGroup();
            var leftBottom = ImGui.GetItemRectMax().Y;

            ImGui.SetCursorScreenPos(new Vector2(start.X + colW + gap, start.Y));
            ImGui.BeginGroup();
            for (var bar = 6; bar <= 10; bar++)
            {
                var b = bar;
                Theme.Card($"bar{b}", () => DrawHotbarCard(b), colW, plugin.RulesWindow.EditingBar == b);
            }
            ImGui.EndGroup();
            var rightBottom = ImGui.GetItemRectMax().Y;

            ImGui.SetCursorScreenPos(new Vector2(start.X, Math.Max(leftBottom, rightBottom)));
            ImGui.Dummy(Vector2.Zero);
        }
        ImGuiHelpers.ScaledDummy(2f);
    }

    private void DrawHotbarCard(int bar)
    {
        var rule = Config.Rules.FirstOrDefault(r => r.Hotbar == bar);
        var controlled = rule is { Enabled: true };
        var editing = plugin.RulesWindow.EditingBar == bar;
        var d = ImGui.GetFrameHeight();
        var left = Theme.CardInnerLeft;
        var right = Theme.CardInnerRight;
        var rowStart = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var textRight = right; // the description under the row uses the full width

        // Number in a circle (filled when Will-o-Bars controls this hotbar) and the title.
        var cd = ImGui.GetTextLineHeight() + S(4);
        var cy = rowStart.Y + d / 2f;
        var center = new Vector2(left + cd / 2f, cy);
        if (controlled)
        {
            // Will-o-Bars is using this hotbar: pink circle with the same slowly shifting pink/purple border as the glowing cards.
            var glow = Theme.Glow();
            dl.AddCircleFilled(center, cd / 2f, ImGui.GetColorU32(Theme.Pink), 24);
            dl.AddCircle(center, cd / 2f + S(1.5f), ImGui.GetColorU32(glow with { W = 0.35f }), 24, S(3f));
            dl.AddCircle(center, cd / 2f, ImGui.GetColorU32(glow), 24, S(1.8f));
        }
        else
            dl.AddCircle(center, cd / 2f - S(0.6f), ImGui.GetColorU32(Theme.Border), 24, S(1.4f));
        var num = bar.ToString();
        var nInk = Theme.InkBounds(num);
        dl.AddText(new Vector2(center.X - (nInk.X + nInk.Z) / 2f, cy - (nInk.Y + nInk.W) / 2f),
            ImGui.GetColorU32(controlled ? Theme.OnPink : Theme.Muted), num);
        var title = $"Hotbar {bar}";
        using (plugin.TitleFont.Available ? plugin.TitleFont.Push() : null)
        {
            ImGui.SetWindowFontScale(0.6f);
            dl.AddText(new Vector2(left + cd + S(8), cy - ImGui.GetTextLineHeight() / 2f), ImGui.GetColorU32(Theme.AccentText), title);
            ImGui.SetWindowFontScale(1f);
        }

        // Round button at the far right: collapses the card to just this row.
        var closed = Config.CollapsedCards.Contains(Theme.CardId);
        ImGui.SetCursorScreenPos(new Vector2(right - d, rowStart.Y));
        closed = Theme.FoldButton(Config.CollapsedCards, plugin.SaveOnly, closed);
        right -= d + S(6);

        // Right side: the state pill, and the Edit button left of it.
        var inGame = plugin.Look.IsShownInGame(bar);
        var shown = controlled && plugin.Applied.TryGetValue(bar, out var v) ? v : inGame != false;
        var stateText = inGame == null && !controlled ? "" : shown ? "shown" : "hidden";
        var stateW = stateText.Length > 0 ? Theme.PillWidth(stateText) : 0f;
        var editLabel = editing ? "Editing" : "Edit";
        var editW = EditButtonWidth(editLabel);
        var pillY = rowStart.Y + (d - ImGui.GetTextLineHeight() - S(2)) / 2f;

        if (stateText.Length > 0)
        {
            ImGui.SetCursorScreenPos(new Vector2(right - stateW, pillY));
            if (controlled)
            {
                Theme.Pill(stateText, shown ? Theme.Pink : Grey);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip($"Hotbar {bar} is {stateText} right now.\n{WhyText(bar)}");
            }
            else
            {
                // Game setting: click to switch the game's own "Display hotbar" option.
                if (ChoicePill("##vis", stateText, true, shown ? Theme.Green : Grey))
                    plugin.RequestGameVisibility(bar, !shown);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip($"Game setting: hotbar {bar} is {stateText}.\nClick to {(shown ? "hide" : "show")} it (the game's own \"Display hotbar\" option).");
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(right - stateW - (stateW > 0 ? S(6) : 0) - editW, rowStart.Y + (d - (ImGui.GetTextLineHeight() + S(4))) / 2f));
        if (EditButton("##edit", editLabel, editing))
            plugin.RulesWindow.Open(bar);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(rule == null ? $"Make a rule for hotbar {bar}." : $"Edit the rule for hotbar {bar}.");

        // Second line: what the rule does, in words (hidden while the card is collapsed).
        if (closed)
        {
            ImGui.SetCursorScreenPos(new Vector2(left, rowStart.Y + d));
            ImGui.Dummy(Vector2.Zero);
            return;
        }
        ImGui.SetCursorScreenPos(new Vector2(left + cd + S(8), rowStart.Y + d + S(2)));
        using (ImRaii.TextWrapPos(ImGui.GetCursorPosX() + (textRight - left - cd - S(8))))
        {
            if (rule == null)
            {
                Theme.Wrapped(editing ? "Game setting for now. New rule waiting in the editor." : "Game setting. Will-o-Bars leaves it alone.", Theme.Muted);
            }
            else
            {
                var summary = RuleLogic.Summary(rule);
                if (!rule.Enabled)
                    Theme.Wrapped("Paused: " + summary, Theme.Muted);
                else
                    SummaryText(summary);
            }
        }
        ImGui.Dummy(Vector2.Zero);
    }

    /// <summary>A summary with "Show"/"Hide"/"show"/"hide" in pink.</summary>
    private static void SummaryText(string summary)
    {
        var words = System.Text.RegularExpressions.Regex.Matches(summary, @"\S+\s*");
        var right = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X;
        var wrap = ImGui.GetCursorScreenPos().X;
        var startX = ImGui.GetCursorScreenPos().X;
        var maxX = Theme.CardInnerRight;
        var first = true;
        foreach (System.Text.RegularExpressions.Match m in words)
        {
            var w = m.Value;
            var bare = w.Trim().TrimEnd(',', '.');
            var pink = bare is "Show" or "Hide" or "show" or "hide";
            if (!first)
            {
                if (ImGui.GetItemRectMax().X + ImGui.CalcTextSize(w.TrimEnd()).X <= maxX)
                    ImGui.SameLine(0, 0);
                else
                    ImGui.SetCursorScreenPos(new Vector2(startX, ImGui.GetCursorScreenPos().Y));
            }
            ImGui.TextColored(pink ? Theme.AccentText : Theme.TextSoft, w);
            first = false;
        }
    }

    private static float EditButtonWidth(string label)
    {
        float iconW;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            iconW = ImGui.CalcTextSize(FontAwesomeIcon.Pen.ToIconString()).X;
        return iconW + S(5) + ImGui.CalcTextSize(label).X + S(18);
    }

    /// <summary>Small rounded button with a pen icon: pink outline, filled pink while that hotbar is being edited.</summary>
    internal static bool EditButton(string id, string label, bool on)
    {
        var width = EditButtonWidth(label);
        var h = ImGui.GetTextLineHeight() + S(4);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(width, h));
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var round = Theme.UseDalamud ? Theme.R(0) : h / 2f;
        var fill = on ? Theme.Pink : hovered ? Theme.GlassHover : new Vector4(0, 0, 0, 0);
        dl.AddRectFilled(pos, pos + new Vector2(width, h), ImGui.GetColorU32(fill), round);
        dl.AddRect(pos, pos + new Vector2(width, h), ImGui.GetColorU32(on ? Theme.Outline : Theme.Pink), round, ImDrawFlags.None, S(1.4f));
        var col = ImGui.GetColorU32(on ? Theme.OnPink : Theme.AccentText);
        var x = pos.X + S(9);
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            var icon = FontAwesomeIcon.Pen.ToIconString();
            var ink = Theme.InkBounds(icon);
            dl.AddText(new Vector2(x - ink.X, pos.Y + h / 2f - (ink.Y + ink.W) / 2f), col, icon);
            x += ImGui.CalcTextSize(icon).X + S(5);
        }
        var lInk = Theme.InkBounds(label);
        dl.AddText(new Vector2(x, pos.Y + h / 2f - (lInk.Y + lInk.W) / 2f), col, label);
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        return clicked;
    }

    // ---------------------------------------------------------------- "Backups" tab

    private string backupName = string.Empty;

    // The saved settings files, looked up every few seconds instead of reading the folder every frame.
    private System.Collections.Generic.List<string>? settingsFiles;
    private DateTime settingsFilesAt;

    private System.Collections.Generic.List<string> SettingsFiles()
    {
        var now = DateTime.UtcNow;
        if (settingsFiles == null || now - settingsFilesAt > TimeSpan.FromSeconds(5))
        {
            try { settingsFiles = SettingsTransfer.Files(); }
            catch (Exception) { settingsFiles = new System.Collections.Generic.List<string>(); }
            settingsFilesAt = now;
        }
        return settingsFiles;
    }

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
            if (!Theme.CardHeader(plugin.TitleFont, "Import/Export", Config.CollapsedCards, plugin.SaveOnly))
                return;
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
                            settingsFiles = null;
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
                        settingsFiles = null;
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
            var files = SettingsFiles();
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
                                settingsFiles = null;
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
            if (!Theme.CardHeader(plugin.TitleFont, "Save your hotbars", Config.CollapsedCards, plugin.SaveOnly))
                return;
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
            if (!Theme.CardHeader(plugin.TitleFont, "History", Config.CollapsedCards, plugin.SaveOnly, $"{backups.List.Count} saved"))
                return;
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
    internal void DrawJobGrid(string id, Func<uint, bool> isOn, Action<System.Collections.Generic.IReadOnlyList<uint>, bool> set, bool compact = false)
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

    internal static void DrawJobIcon(uint iconId, float size, float alpha)
    {
        var tex = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
        ImGui.Image(tex.Handle, new Vector2(size), Vector2.Zero, Vector2.One, new Vector4(1f, 1f, 1f, alpha));
    }

    // ---------------------------------------------------------------- small widgets

    /// <summary>Small pink text that acts like a link.</summary>
    internal static bool TextLink(string label)
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
    internal static bool ChoicePill(string id, string text, bool active, Vector4 fill)
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
    internal static bool Chip(string id, FontAwesomeIcon icon, string label, bool on, float width)
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
        var linkW = Math.Min(S(96), (avail - spacing * 2) / 3f);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (avail - (linkW * 3 + spacing * 2)) / 2f));

        if (Theme.LinkButton("##kofi", FontAwesomeIcon.Coffee, "Ko-fi", rainbow: true, width: linkW))
            Util.OpenLink(Theme.KofiUrl);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Support me on Ko-fi");
        ImGui.SameLine();
        if (Theme.LinkButton("##github", FontAwesomeIcon.CodeBranch, "GitHub", width: linkW))
            Util.OpenLink(GitHubUrl);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(GitHubUrl);
        ImGui.SameLine();
        if (Theme.DiscordButton("##discord", plugin.DiscordLogo, linkW))
            Util.OpenLink(Theme.DiscordUrl);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Join the Will-o Discord");
    }
}
