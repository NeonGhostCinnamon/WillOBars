using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace WillOBars.Windows;

/// <summary>
/// The rule editor for one hotbar, opened by "Edit" beside the main window.
/// A hotbar without a rule starts with a new one ("show when Job is [the job you're on]") that only takes effect
/// after "Turn on rule". Changes to an existing rule apply right away.
/// </summary>
public sealed class RulesWindow : Window
{
    private readonly Plugin plugin;
    private readonly MainWindow main;
    private Configuration Config => plugin.Configuration;

    private static float S(float v) => v * ImGuiHelpers.GlobalScale;

    private int bar;
    private HotbarRule? draft;
    private bool placeNextToMain;

    // dragging by the header
    private bool dragging;
    private Vector2 dragOffset;
    private float fitHeight;

    private const float Width = 470;

    /// <summary>The hotbar being edited (0 = closed).</summary>
    public int EditingBar => IsOpen ? bar : 0;

    public RulesWindow(Plugin plugin, MainWindow main)
        : base($"Will-o-Bars rules##{Plugin.Id}Rules", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize)
    {
        this.plugin = plugin;
        this.main = main;
        Size = new Vector2(Width, 300);
        SizeCondition = ImGuiCond.Always;
        RespectCloseHotkey = false;
    }

    /// <summary>Open the editor for this hotbar (or switch to it).</summary>
    public void Open(int hotbar)
    {
        if (IsOpen && bar == hotbar)
        {
            IsOpen = false;
            return;
        }
        if (!IsOpen)
            placeNextToMain = true;
        bar = hotbar;
        var rule = Config.Rules.FirstOrDefault(r => r.Hotbar == hotbar);
        draft = rule == null ? RuleLogic.NewRule(hotbar, plugin.CurrentJob) : null;
        IsOpen = true;
    }

    public override void OnClose()
    {
        draft = null;
    }

    private HotbarRule? Rule => draft ?? Config.Rules.FirstOrDefault(r => r.Hotbar == bar);

    /// <summary>A change was made: save it (nothing to save for a rule that isn't turned on yet).</summary>
    private void Changed()
    {
        if (draft == null)
            plugin.Save();
    }

    // ---------------------------------------------------------------- frame

    public override void PreDraw()
    {
        Theme.Mode = Config.ThemeChoice;

        if (dragging)
        {
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
                dragging = false;
            Position = (ImGui.GetIO().MousePos - dragOffset) / ImGuiHelpers.GlobalScale;
            PositionCondition = ImGuiCond.Always;
        }
        else if ((Config.RulesStuck || placeNextToMain) && main.IsOpen && main.LastSize.X > 0)
        {
            // Stuck to the right side of the main window (or the left side when there's no room on the right).
            var gap = S(8);
            var pos = main.LastPos + new Vector2(main.LastSize.X + gap, 0);
            var vp = ImGui.GetMainViewport();
            if (pos.X + S(Width) > vp.Pos.X + vp.Size.X)
                pos.X = main.LastPos.X - gap - S(Width);
            Position = pos / ImGuiHelpers.GlobalScale;
            PositionCondition = ImGuiCond.Always;
            placeNextToMain = false;
        }
        else
        {
            Position = null;
        }

        // As tall as the content (up to 90% of the screen).
        var maxH = ImGui.GetMainViewport().WorkSize.Y * 0.9f;
        var h = fitHeight > 0 ? Math.Min(fitHeight + S(2), maxH) : S(300);
        if (fitHeight + S(2) <= maxH) Flags |= ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        else Flags &= ~(ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        Size = new Vector2(S(Width), MathF.Ceiling(h)) / ImGuiHelpers.GlobalScale;
        SizeCondition = ImGuiCond.Always;

        Theme.Push();
        // Pink outline, so it reads as a popout of the main window.
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.Pink);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, S(1.2f));
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
        Theme.Pop();
    }

    public override void Draw()
    {
        var rule = Rule;
        if (rule == null)
        {
            IsOpen = false;
            return;
        }
        rule.Cases ??= new List<RuleCase>();

        DrawHeader();
        Theme.AccentBar(1f);
        ImGuiHelpers.ScaledDummy(2f);

        if (draft != null)
            Theme.Wrapped("A new rule starts with the job you're on. Change it or add to it, then turn it on.", Theme.Muted);
        else if (rule.Cases.Count > 1)
            Theme.Wrapped("Cases are checked from the top; the first one that matches decides.", Theme.Muted);
        if (!rule.Enabled && draft == null)
            Theme.Wrapped("This rule is paused: the hotbar is left visible.", Theme.Amber);
        ImGuiHelpers.ScaledDummy(2f);

        var deciding = RuleLogic.Evaluate(rule, plugin.State).Case;

        if (rule.Cases.Count == 0)
            rule.Cases.Add(new RuleCase { Show = !rule.OtherwiseShow });

        if (rule.Cases.Count == 1)
            DrawSingleCase(rule, deciding == 0);
        else
            DrawCases(rule, deciding);

        // + Add another case / Templates
        if (MainWindow.TextLink("+ Add another case"))
        {
            if (rule.Cases.Count == 1)
                rule.OtherwiseShow = !rule.Cases[0].Show;
            rule.Cases.Add(new RuleCase { Show = !rule.OtherwiseShow });
            Changed();
        }
        ImGui.SameLine();
        var templW = S(130);
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X - templW, ImGui.GetCursorScreenPos().Y));
        ImGui.SetNextItemWidth(templW);
        using (var combo = ImRaii.Combo("##templates", "Templates"))
        {
            if (combo.Success)
            {
                if (ImGui.Selectable("Use current conditions..."))
                    openFromNow = true;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Show this hotbar in exactly the situation you're in now.\nYou pick which parts of it count.");
                ImGui.Separator();
                foreach (var (name, make) in RuleLogic.Templates)
                {
                    if (ImGui.Selectable(name))
                    {
                        var (cases, otherwise) = make(plugin.CurrentJob);
                        rule.Cases = cases;
                        rule.OtherwiseShow = otherwise;
                        Changed();
                    }
                }
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Replace this rule with a ready-made one.");
        DrawFromNow(rule);

        // separator
        ImGuiHelpers.ScaledDummy(4f);
        var p = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(p, p + new Vector2(ImGui.GetContentRegionAvail().X, 0), ImGui.GetColorU32(Theme.Border), S(1));
        ImGuiHelpers.ScaledDummy(4f);

        DrawFooter(rule);

        var style = ImGui.GetStyle();
        fitHeight = ImGui.GetCursorPosY() - style.ItemSpacing.Y + style.WindowPadding.Y;
    }

    private void DrawHeader()
    {
        var d = S(26);
        var top = ImGui.GetCursorScreenPos();
        var winPos = ImGui.GetWindowPos();
        var right = winPos.X + ImGui.GetWindowContentRegionMax().X;

        using (plugin.TitleFont.Available ? plugin.TitleFont.Push() : null)
        {
            ImGui.SetWindowFontScale(0.72f);
            ImGui.TextColored(Theme.Text, $"Hotbar {bar} rules");
            ImGui.SetWindowFontScale(1f);
        }
        var titleH = ImGui.GetItemRectSize().Y;

        var after = ImGui.GetCursorScreenPos();
        var by = top.Y + Math.Max(0, (titleH - d) / 2f);

        // Stick to / unstick from the main window.
        ImGui.SetCursorScreenPos(new Vector2(right - d * 2 - S(6), by));
        var stuck = Config.RulesStuck;
        if (Theme.IconCircle("##stick", stuck ? FontAwesomeIcon.Link : FontAwesomeIcon.Unlink, stuck ? Theme.AccentText : Theme.Muted, d))
        {
            Config.RulesStuck = !stuck;
            plugin.SaveOnly();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(stuck
                ? "Stuck to the main window: it moves along.\nClick to unstick (or just drag this window away)."
                : "Click to stick this window to the side of the main window again.");

        ImGui.SetCursorScreenPos(new Vector2(right - d, by));
        if (Theme.IconCircle("##close", FontAwesomeIcon.Times, Theme.Text, d))
            IsOpen = false;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(draft != null ? "Close (the new rule isn't turned on)" : "Close");

        // Drag the header to move the window.
        var headerMax = new Vector2(right - d * 2 - S(12), top.Y + Math.Max(titleH, d));
        if (ImGui.IsWindowHovered() && !ImGui.IsAnyItemHovered() && ImGui.IsMouseHoveringRect(winPos, headerMax))
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                dragging = true;
                dragOffset = ImGui.GetIO().MousePos - winPos;
                // Dragging it away unsticks it.
                if (Config.RulesStuck)
                {
                    Config.RulesStuck = false;
                    plugin.SaveOnly();
                }
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(after.X, Math.Max(after.Y, top.Y + d + S(2))));
        ImGui.Dummy(Vector2.Zero);
    }

    // ---------------------------------------------------------------- one case: a single sentence

    private void DrawSingleCase(HotbarRule rule, bool matches)
    {
        var c = rule.Cases![0];
        rule.OtherwiseShow = !c.Show;

        CaseBox("single", matches, () =>
        {
            ImGui.AlignTextToFramePadding();
            if (ShowHideCombo("##action", c.Show, true, out var show))
            {
                c.Show = show;
                rule.OtherwiseShow = !show;
                Changed();
            }
            ImGui.SameLine();
            ImGui.TextColored(Theme.TextSoft, $"hotbar {bar} when");
            ImGui.SameLine();
            AllAnyCombo("##allany", c);
            ImGui.SameLine();
            ImGui.TextColored(Theme.TextSoft, "of these:");

            DrawConditions(c);

            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(Theme.Muted, c.Show ? "Hidden the rest of the time." : "Shown the rest of the time.");
            MatchNote(matches, "matches right now");
        });
    }

    // ---------------------------------------------------------------- several cases

    private void DrawCases(HotbarRule rule, int deciding)
    {
        var cases = rule.Cases!;
        int? remove = null, moveUp = null, moveDown = null;

        for (var i = 0; i < cases.Count; i++)
        {
            var c = cases[i];
            var index = i;
            CaseBox($"case{i}", deciding == i, () =>
            {
                NumberCircle(index + 1);
                ImGui.SameLine(0, S(6));
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(Theme.TextSoft, index == 0 ? "When" : "Otherwise, when");
                ImGui.SameLine();
                AllAnyCombo("##allany", c);
                ImGui.SameLine();
                ImGui.TextColored(Theme.TextSoft, "of these ->");
                ImGui.SameLine();
                if (ShowHideCombo("##action", c.Show, false, out var show))
                {
                    c.Show = show;
                    Changed();
                }

                // up / down / remove on the right
                var d = ImGui.GetFrameHeight() * 0.8f;
                var right = Theme.CardInnerRight;
                var y = ImGui.GetItemRectMin().Y + (ImGui.GetFrameHeight() - d) / 2f;
                ImGui.SetCursorScreenPos(new Vector2(right - d * 3 - S(8), y));
                if (SmallIcon("##up", FontAwesomeIcon.ArrowUp, d, index > 0))
                    moveUp = index;
                ImGui.SameLine(0, S(4));
                if (SmallIcon("##down", FontAwesomeIcon.ArrowDown, d, index < cases.Count - 1))
                    moveDown = index;
                ImGui.SameLine(0, S(4));
                if (SmallIcon("##remove", FontAwesomeIcon.Times, d, true))
                    remove = index;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Remove this case");

                DrawConditions(c);
                if (deciding == index)
                    MatchNote(true, "deciding right now");
            });
        }

        // Otherwise -> show/hide
        CaseBox("otherwise", deciding < 0, () =>
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetFrameHeight() * 0.8f + S(6));
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(Theme.TextSoft, "Otherwise ->");
            ImGui.SameLine();
            if (ShowHideCombo("##otherwise", rule.OtherwiseShow, false, out var show))
            {
                rule.OtherwiseShow = show;
                Changed();
            }
            if (deciding < 0)
                MatchNote(true, "deciding right now");
        }, dashed: true);

        if (moveUp is { } u)
        {
            (cases[u - 1], cases[u]) = (cases[u], cases[u - 1]);
            Changed();
        }
        else if (moveDown is { } dn)
        {
            (cases[dn + 1], cases[dn]) = (cases[dn], cases[dn + 1]);
            Changed();
        }
        else if (remove is { } r)
        {
            cases.RemoveAt(r);
            if (cases.Count == 1)
                cases[0].Show = !rule.OtherwiseShow;
            Changed();
        }
    }

    // ---------------------------------------------------------------- conditions

    private void DrawConditions(RuleCase c)
    {
        var indent = S(22);
        int? remove = null;
        for (var i = 0; i < c.Conditions.Count; i++)
        {
            var cond = c.Conditions[i];
            using var id = ImRaii.PushId(i);
            var rowStart = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(new Vector2(Theme.CardInnerLeft, rowStart.Y));
            ImGui.AlignTextToFramePadding();
            if (i > 0)
            {
                var join = c.MatchAll ? "AND" : "OR";
                ImGui.SetCursorScreenPos(new Vector2(Theme.CardInnerLeft + indent - ImGui.CalcTextSize(join).X - S(6), ImGui.GetCursorScreenPos().Y));
                ImGui.TextColored(Theme.AccentText, join);
                ImGui.SameLine(0, S(6));
            }
            else
            {
                ImGui.Dummy(new Vector2(indent - ImGui.GetStyle().ItemSpacing.X, 0));
                ImGui.SameLine();
            }

            // kind
            if (KindCombo("##kind", cond.Kind, out var kind))
            {
                cond.Kind = kind;
                cond.Ids.Clear();
                if (kind == ConditionKind.Job && plugin.CurrentJob != 0)
                    cond.Ids.Add(plugin.CurrentJob);
                Changed();
            }
            ImGui.SameLine();

            // is / is not
            ImGui.SetNextItemWidth(ImGui.CalcTextSize("is not").X + ImGui.GetFrameHeight() + S(10));
            var red = cond.Not;
            if (red)
            {
                ImGui.PushStyleColor(ImGuiCol.Border, Red);
                ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, S(1.4f));
            }
            var notOpen = ImGui.BeginCombo("##not", cond.Not ? "is not" : "is");
            if (red)
            {
                // Only the box itself is red; the dropdown list keeps the normal look.
                ImGui.PopStyleVar();
                ImGui.PopStyleColor();
            }
            if (notOpen)
            {
                if (ImGui.Selectable("is", !cond.Not)) { cond.Not = false; Changed(); }
                if (ImGui.Selectable("is not", cond.Not)) { cond.Not = true; Changed(); }
                ImGui.EndCombo();
            }
            if (ImGui.IsItemHovered() && red)
                ImGui.SetTooltip("\"is not\": this condition counts when it's NOT the case.");

            // remove (far right)
            var d = ImGui.GetFrameHeight() * 0.8f;
            var lineY = ImGui.GetItemRectMin().Y;

            if (RuleLogic.HasIds(cond.Kind))
                DrawChips(cond, Theme.CardInnerRight - d - S(6));

            var after = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(new Vector2(Theme.CardInnerRight - d, lineY + (ImGui.GetFrameHeight() - d) / 2f));
            if (SmallIcon("##rm", FontAwesomeIcon.Times, d, true))
                remove = i;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Remove this condition");
            ImGui.SetCursorScreenPos(after);
            ImGui.Dummy(Vector2.Zero);
        }

        if (remove is { } r)
        {
            c.Conditions.RemoveAt(r);
            Changed();
        }

        // + add condition
        ImGui.SetCursorScreenPos(new Vector2(Theme.CardInnerLeft + indent, ImGui.GetCursorScreenPos().Y));
        if (MainWindow.TextLink("+ add condition"))
            ImGui.OpenPopup("##addcond");
        using var popup = ImRaii.Popup("##addcond");
        if (popup.Success)
        {
            foreach (var (group, kinds) in RuleLogic.Menu)
            {
                ImGui.TextColored(Theme.AccentText, group.ToUpperInvariant());
                foreach (var k in kinds)
                {
                    if (ImGui.Selectable("  " + RuleLogic.KindName(k)))
                    {
                        var cond = new RuleCondition { Kind = k };
                        if (k == ConditionKind.Job && plugin.CurrentJob != 0)
                            cond.Ids.Add(plugin.CurrentJob);
                        c.Conditions.Add(cond);
                        Changed();
                    }
                }
            }
        }
    }

    /// <summary>The picked values as removable chips, then a "+" that opens the picker.</summary>
    private void DrawChips(RuleCondition cond, float maxRight)
    {
        var left = ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X;
        var lineStart = Theme.CardInnerLeft + S(22);
        var chips = new List<(string Label, uint Icon, uint[] Ids)>();
        switch (cond.Kind)
        {
            case ConditionKind.Job:
                chips.AddRange(Jobs.All.Where(j => cond.Ids.Contains(j.Id)).Select(j => (j.Abbreviation, j.IconId, new[] { j.Id })));
                break;
            case ConditionKind.Zone:
                chips.AddRange(GameData.Picked(GameData.Zones, cond.Ids).Select(e => (e.Name, 0u, e.Ids)));
                break;
            case ConditionKind.Weather:
                chips.AddRange(GameData.Picked(GameData.Weathers, cond.Ids).Select(e => (e.Name, 0u, e.Ids)));
                break;
            case ConditionKind.InDuty:
                chips.AddRange(GameData.Picked(GameData.DutyTypes, cond.Ids).Select(e => (e.Name, 0u, e.Ids)));
                break;
        }

        // Many jobs: one summary chip instead of a long row.
        if (cond.Kind == ConditionKind.Job && chips.Count > 8)
            chips = new List<(string, uint, uint[])> { ($"{chips.Count} classes/jobs", 0u, Array.Empty<uint>()) };

        var x = left;
        var y = ImGui.GetItemRectMin().Y;
        var h = ImGui.GetFrameHeight();
        uint[]? removeIds = null;
        var n = 0;
        foreach (var (label, icon, ids) in chips)
        {
            var w = ChipWidth(label, icon != 0);
            if (x + w > maxRight && x > lineStart)
            {
                x = lineStart;
                y += h + S(4);
            }
            ImGui.SetCursorScreenPos(new Vector2(x, y));
            if (ValueChip($"##chip{n++}", label, icon, ids.Length > 0))
                removeIds = ids;
            x += w + S(4);
        }

        var plusW = ImGui.CalcTextSize("+").X + S(10);
        if (x + plusW > maxRight && x > lineStart)
        {
            x = lineStart;
            y += h + S(4);
        }
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        ImGui.AlignTextToFramePadding();
        if (MainWindow.TextLink(" + "))
        {
            pickerSearch = string.Empty;
            ImGui.OpenPopup("##pick");
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(cond.Kind switch
            {
                ConditionKind.Job => "Pick classes/jobs",
                ConditionKind.Zone => "Pick zones",
                ConditionKind.Weather => "Pick weather",
                _ => "Pick duty types (none picked = any duty)",
            });

        if (removeIds != null)
        {
            foreach (var id in removeIds)
                cond.Ids.Remove(id);
            Changed();
        }

        DrawPicker(cond);
    }

    private string pickerSearch = string.Empty;

    private void DrawPicker(RuleCondition cond)
    {
        ImGui.SetNextWindowSizeConstraints(new Vector2(S(360), 0), new Vector2(S(420), S(420)));
        using var popup = ImRaii.Popup("##pick");
        if (!popup.Success)
            return;

        if (cond.Kind == ConditionKind.Job)
        {
            if (MainWindow.TextLink("Current job") && plugin.CurrentJob != 0)
            {
                cond.Ids.Clear();
                cond.Ids.Add(plugin.CurrentJob);
                Changed();
            }
            ImGui.SameLine(0, S(14));
            if (MainWindow.TextLink("All"))
            {
                foreach (var j in Jobs.All)
                    cond.Ids.Add(j.Id);
                Changed();
            }
            ImGui.SameLine(0, S(14));
            if (MainWindow.TextLink("None"))
            {
                cond.Ids.Clear();
                Changed();
            }
            Theme.Card("jobpick", () =>
            {
                main.DrawJobGrid("rulepick", cond.Ids.Contains, (ids, on) =>
                {
                    foreach (var id in ids)
                    {
                        if (on) cond.Ids.Add(id);
                        else cond.Ids.Remove(id);
                    }
                    Changed();
                }, compact: true);
            }, S(400));
            return;
        }

        var list = cond.Kind switch
        {
            ConditionKind.Zone => GameData.Zones,
            ConditionKind.Weather => GameData.Weathers,
            _ => GameData.DutyTypes,
        };

        if (list.Count > 12)
        {
            ImGui.SetNextItemWidth(-1);
            if (ImGui.IsWindowAppearing())
                ImGui.SetKeyboardFocusHere();
            ImGui.InputTextWithHint("##search", "Search...", ref pickerSearch, 100);
        }

        using var child = ImRaii.Child("##list", new Vector2(S(380), Math.Min(S(300), list.Count * ImGui.GetTextLineHeightWithSpacing() + S(4))));
        var shown = 0;
        foreach (var e in list)
        {
            if (pickerSearch.Length > 0 && !e.Name.Contains(pickerSearch, StringComparison.OrdinalIgnoreCase))
                continue;
            if (shown++ > 300)
            {
                ImGui.TextColored(Theme.Muted, "Type to narrow it down...");
                break;
            }
            var on = e.Ids.Any(cond.Ids.Contains);
            if (ImGui.Selectable($"{e.Name}##{e.Ids[0]}", on, ImGuiSelectableFlags.DontClosePopups))
            {
                foreach (var id in e.Ids)
                {
                    if (on) cond.Ids.Remove(id);
                    else cond.Ids.Add(id);
                }
                Changed();
            }
        }
    }

    // ---------------------------------------------------------------- "use current conditions"

    private bool openFromNow;

    // Parts of the current situation the player switched off (remembered while the game runs).
    private readonly HashSet<ConditionKind> fromNowSkipped = new() { ConditionKind.WeaponDrawn, ConditionKind.Weather };

    /// <summary>Everything about the current situation, as conditions.</summary>
    private List<RuleCondition> CurrentConditions()
    {
        var st = plugin.State;
        var list = new List<RuleCondition>();
        if (st.Job != 0)
            list.Add(new RuleCondition { Kind = ConditionKind.Job, Ids = { st.Job } });
        list.Add(new RuleCondition { Kind = ConditionKind.InCombat, Not = !st.InCombat });
        list.Add(new RuleCondition { Kind = ConditionKind.WeaponDrawn, Not = !st.WeaponDrawn });
        list.Add(new RuleCondition { Kind = ConditionKind.Mounted, Not = !st.Mounted });
        list.Add(new RuleCondition { Kind = ConditionKind.Crafting, Not = !st.Crafting });
        list.Add(new RuleCondition { Kind = ConditionKind.Gathering, Not = !st.Gathering });
        list.Add(new RuleCondition { Kind = ConditionKind.CityOrSanctuary, Not = !st.CityOrSanctuary });
        var duty = new RuleCondition { Kind = ConditionKind.InDuty, Not = !st.InDuty };
        if (st.InDuty && GameData.DutyTypes.Any(e => e.Ids.Contains(st.DutyType)))
            duty.Ids.Add(st.DutyType);
        list.Add(duty);
        if (st.PlaceName != 0)
        {
            var zone = new RuleCondition { Kind = ConditionKind.Zone };
            foreach (var id in GameData.Zones.FirstOrDefault(e => e.Ids.Contains(st.PlaceName))?.Ids ?? new[] { st.PlaceName })
                zone.Ids.Add(id);
            list.Add(zone);
        }
        if (st.Weather != 0)
        {
            var weather = new RuleCondition { Kind = ConditionKind.Weather };
            foreach (var id in GameData.Weathers.FirstOrDefault(e => e.Ids.Contains(st.Weather))?.Ids ?? new[] { st.Weather })
                weather.Ids.Add(id);
            list.Add(weather);
        }
        list.Add(new RuleCondition { Kind = ConditionKind.InParty, Not = !st.InParty });
        return list;
    }

    /// <summary>Popup: tick the parts of "right now" that should count, then make the rule from them.</summary>
    private void DrawFromNow(HotbarRule rule)
    {
        if (openFromNow)
        {
            ImGui.OpenPopup("##fromnow");
            openFromNow = false;
        }
        using var popup = ImRaii.Popup("##fromnow");
        if (!popup.Success)
            return;

        ImGui.TextColored(Theme.Text, $"Show hotbar {bar} when all of these are true:");
        ImGui.TextColored(Theme.Muted, "Untick what shouldn't matter. Hidden the rest of the time.");
        ImGuiHelpers.ScaledDummy(2f);

        var current = CurrentConditions();
        foreach (var c in current)
        {
            var on = !fromNowSkipped.Contains(c.Kind);
            var text = RuleLogic.Describe(c);
            text = char.ToUpperInvariant(text[0]) + text[1..];
            if (ImGui.Checkbox($"{text}##{c.Kind}", ref on))
            {
                if (on) fromNowSkipped.Remove(c.Kind);
                else fromNowSkipped.Add(c.Kind);
            }
        }

        ImGuiHelpers.ScaledDummy(4f);
        var picked = current.Where(c => !fromNowSkipped.Contains(c.Kind)).ToList();
        using (ImRaii.Disabled(picked.Count == 0))
        {
            if (PillButton("##usethese", "Use these", true))
            {
                rule.Cases = new List<RuleCase> { new() { Show = true, MatchAll = true, Conditions = picked } };
                rule.OtherwiseShow = false;
                Changed();
                ImGui.CloseCurrentPopup();
            }
        }
        ImGui.SameLine(0, S(6));
        if (PillButton("##cancelnow", "Cancel", false))
            ImGui.CloseCurrentPopup();
    }

    // ---------------------------------------------------------------- footer

    private void DrawFooter(HotbarRule rule)
    {
        // Own actions per class/job (the game's "Shared" setting).
        var separate = rule.SeparatePerJob ?? plugin.IsHotbarShared(bar) == false;
        if (ImGui.Checkbox("Own actions per class/job##sep", ref separate))
        {
            rule.SeparatePerJob = separate;
            Changed();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Each class/job keeps its own actions on this hotbar.\n" +
                             "This is the game's \"Shared\" setting (Character Configuration > Hotbar Settings), unticked.\n" +
                             "A backup of your hotbars is saved automatically right before it changes.");

        var right = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X;
        var y = ImGui.GetItemRectMin().Y;
        if (draft != null)
        {
            var w1 = PillButtonWidth("Cancel");
            var w2 = PillButtonWidth("Turn on rule");
            ImGui.SetCursorScreenPos(new Vector2(right - w2 - S(6) - w1, y));
            if (PillButton("##cancel", "Cancel", false))
                IsOpen = false;
            ImGui.SameLine(0, S(6));
            if (PillButton("##on", "Turn on rule", true))
            {
                draft.Enabled = true;
                Config.Rules.Add(draft);
                draft = null;
                plugin.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Start using this rule now.");
        }
        else
        {
            var pauseLabel = rule.Enabled ? "Pause" : "Resume";
            var w1 = PillButtonWidth(pauseLabel);
            var w2 = PillButtonWidth("Use game setting");
            ImGui.SetCursorScreenPos(new Vector2(right - w2 - S(6) - w1, y));
            if (PillButton("##pause", pauseLabel, !rule.Enabled))
            {
                rule.Enabled = !rule.Enabled;
                Changed();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(rule.Enabled ? "Stop this rule for now (the hotbar is shown again)." : "Turn this rule back on.");
            ImGui.SameLine(0, S(6));
            if (PillButton("##remove", "Use game setting", false))
                ImGui.OpenPopup("##confirmremove");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Remove this rule: Will-o-Bars leaves the hotbar to the game's own setting.");

            using var popup = ImRaii.Popup("##confirmremove");
            if (popup.Success)
            {
                ImGui.TextColored(Theme.Text, $"Remove the rule for hotbar {bar}?");
                if (Theme.Button("Remove", new Vector2(S(90), 0)))
                {
                    Config.Rules.Remove(rule);
                    plugin.Save();
                    ImGui.CloseCurrentPopup();
                    IsOpen = false;
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel", new Vector2(S(90), 0)))
                    ImGui.CloseCurrentPopup();
            }
        }
    }

    // ---------------------------------------------------------------- widgets

    /// <summary>A rounded box around a case; green outline when it's the one deciding right now.</summary>
    private static void CaseBox(string id, bool deciding, Action body, bool dashed = false)
    {
        var start = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        Theme.Card(id, body, width);
        var end = start + new Vector2(width, Theme.LastCardHeight);
        var dl = ImGui.GetWindowDrawList();
        if (deciding)
        {
            dl.AddRect(start, end, ImGui.GetColorU32(Theme.Green), Theme.R(16), ImDrawFlags.None, S(1.6f));
        }
        else if (dashed)
        {
            // A few gaps in the border, so "otherwise" reads as the fallback.
            var col = ImGui.GetColorU32(Theme.Background);
            for (var x = start.X + S(24); x < end.X - S(24); x += S(14))
            {
                dl.AddLine(new Vector2(x, start.Y), new Vector2(x + S(5), start.Y), col, S(2));
                dl.AddLine(new Vector2(x, end.Y), new Vector2(x + S(5), end.Y), col, S(2));
            }
        }
    }

    private static void MatchNote(bool on, string text)
    {
        if (!on)
            return;
        var right = Theme.CardInnerRight;
        var w = ImGui.CalcTextSize(text).X + S(14);
        ImGui.SameLine();
        ImGui.SetCursorScreenPos(new Vector2(Math.Max(ImGui.GetCursorScreenPos().X, right - w), ImGui.GetCursorScreenPos().Y));
        var p = ImGui.GetCursorScreenPos();
        var h = ImGui.GetFrameHeight();
        ImGui.GetWindowDrawList().AddCircleFilled(new Vector2(p.X + S(4), p.Y + h / 2f), S(3.5f), ImGui.GetColorU32(Theme.Green), 12);
        ImGui.SetCursorScreenPos(new Vector2(p.X + S(14), p.Y));
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.Muted, text);
    }

    private static void NumberCircle(int n)
    {
        var d = ImGui.GetFrameHeight() * 0.8f;
        var pos = ImGui.GetCursorScreenPos() + new Vector2(0, (ImGui.GetFrameHeight() - d) / 2f);
        var dl = ImGui.GetWindowDrawList();
        dl.AddCircle(pos + new Vector2(d / 2f), d / 2f - S(0.7f), ImGui.GetColorU32(Theme.Pink), 24, S(1.5f));
        var t = n.ToString();
        var ink = Theme.InkBounds(t);
        dl.AddText(pos + new Vector2(d / 2f - (ink.X + ink.Z) / 2f, d / 2f - (ink.Y + ink.W) / 2f), ImGui.GetColorU32(Theme.AccentText), t);
        ImGui.Dummy(new Vector2(d, ImGui.GetFrameHeight()));
    }

    private void AllAnyCombo(string id, RuleCase c)
    {
        ImGui.SetNextItemWidth(ImGui.CalcTextSize("any").X + ImGui.GetFrameHeight() + S(12));
        using var combo = ImRaii.Combo(id, c.MatchAll ? "all" : "any");
        if (!combo.Success)
            return;
        if (ImGui.Selectable("all (every condition)", c.MatchAll) && !c.MatchAll) { c.MatchAll = true; Changed(); }
        if (ImGui.Selectable("any (at least one)", !c.MatchAll) && c.MatchAll) { c.MatchAll = false; Changed(); }
    }

    private static readonly Vector4 Red = new(0.94f, 0.36f, 0.36f, 1f);

    /// <summary>Pink-filled Show/Hide dropdown. The open list uses the normal colours, so both options are readable.</summary>
    private static bool ShowHideCombo(string id, bool show, bool capital, out bool result)
    {
        result = show;
        var label = show ? (capital ? "Show" : "show") : (capital ? "Hide" : "hide");
        ImGui.SetNextItemWidth(ImGui.CalcTextSize("Show").X + ImGui.GetFrameHeight() + S(14));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Pink);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.ButtonHover);
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.Pink);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.ButtonHover);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.OnPink);
        var open = ImGui.BeginCombo(id, label);
        ImGui.PopStyleColor(5);
        if (!open)
            return false;
        var changed = false;
        if (ImGui.Selectable(capital ? "Show" : "show", show)) { result = true; changed = !show; }
        if (ImGui.Selectable(capital ? "Hide" : "hide", !show)) { result = false; changed = show; }
        ImGui.EndCombo();
        return changed;
    }

    private static bool KindCombo(string id, ConditionKind kind, out ConditionKind result)
    {
        result = kind;
        var label = RuleLogic.KindName(kind);
        ImGui.SetNextItemWidth(ImGui.CalcTextSize(label).X + ImGui.GetFrameHeight() + S(14));
        using var combo = ImRaii.Combo(id, label, ImGuiComboFlags.HeightLarge);
        if (!combo.Success)
            return false;
        var changed = false;
        foreach (var (group, kinds) in RuleLogic.Menu)
        {
            ImGui.TextColored(Theme.AccentText, group.ToUpperInvariant());
            foreach (var k in kinds)
            {
                if (ImGui.Selectable("  " + RuleLogic.KindName(k), k == kind) && k != kind)
                {
                    result = k;
                    changed = true;
                }
            }
        }
        return changed;
    }

    private static float ChipWidth(string label, bool icon)
        => ImGui.CalcTextSize(label).X + S(16) + (icon ? ImGui.GetTextLineHeight() + S(4) : 0) + ImGui.CalcTextSize("x").X + S(4);

    /// <summary>Chip with an optional game icon, a label and an "x"; returns true when clicked (to remove it).</summary>
    private static bool ValueChip(string id, string label, uint iconId, bool removable)
    {
        var hasIcon = iconId != 0;
        var w = ChipWidth(label, hasIcon);
        var h = ImGui.GetFrameHeight();
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(w, h));
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var round = Theme.UseDalamud ? Theme.R(0) : h / 2f;
        dl.AddRectFilled(pos, pos + new Vector2(w, h), ImGui.GetColorU32(Theme.Pink with { W = hovered ? 0.32f : 0.18f }), round);
        dl.AddRect(pos, pos + new Vector2(w, h), ImGui.GetColorU32(Theme.Pink with { W = 0.7f }), round, ImDrawFlags.None, S(1.1f));
        var x = pos.X + S(8);
        if (hasIcon)
        {
            var isz = ImGui.GetTextLineHeight();
            var tex = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
            dl.AddImage(tex.Handle, new Vector2(x, pos.Y + (h - isz) / 2f), new Vector2(x + isz, pos.Y + (h + isz) / 2f));
            x += isz + S(4);
        }
        var ty = pos.Y + (h - ImGui.GetTextLineHeight()) / 2f;
        dl.AddText(new Vector2(x, ty), ImGui.GetColorU32(Theme.AccentText), label);
        x += ImGui.CalcTextSize(label).X + S(4);
        dl.AddText(new Vector2(x, ty), ImGui.GetColorU32(hovered ? Theme.Text : Theme.Muted), "x");
        if (hovered && removable)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip($"Remove {label}");
        }
        return clicked && removable;
    }

    private static bool SmallIcon(string id, FontAwesomeIcon icon, float d, bool enabled)
    {
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(d, d)) && enabled;
        var hovered = ImGui.IsItemHovered() && enabled;
        var col = !enabled ? Theme.Muted with { W = 0.3f } : hovered ? Theme.AccentText : Theme.Muted;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            var s = icon.ToIconString();
            ImGui.SetWindowFontScale(0.85f);
            var ink = Theme.InkBounds(s);
            ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), ImGui.GetFontSize(), pos + new Vector2(d / 2f - (ink.X + ink.Z) / 2f, d / 2f - (ink.Y + ink.W) / 2f), ImGui.GetColorU32(col), s);
            ImGui.SetWindowFontScale(1f);
        }
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        return clicked;
    }

    private static float PillButtonWidth(string label) => ImGui.CalcTextSize(label).X + S(22);

    private static bool PillButton(string id, string label, bool filled)
    {
        var w = PillButtonWidth(label);
        var h = ImGui.GetFrameHeight();
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(w, h));
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var round = Theme.UseDalamud ? Theme.R(0) : h / 2f;
        var fill = filled ? (hovered ? Theme.ButtonHover : Theme.Pink) : hovered ? Theme.GlassHover : new Vector4(0, 0, 0, 0);
        dl.AddRectFilled(pos, pos + new Vector2(w, h), ImGui.GetColorU32(fill), round);
        dl.AddRect(pos, pos + new Vector2(w, h), ImGui.GetColorU32(filled ? Theme.Outline : Theme.Pink), round, ImDrawFlags.None, S(1.4f));
        var ink = Theme.InkBounds(label);
        dl.AddText(new Vector2(pos.X + (w - ImGui.CalcTextSize(label).X) / 2f, pos.Y + h / 2f - (ink.Y + ink.W) / 2f),
            ImGui.GetColorU32(filled ? Theme.OnPink : Theme.AccentText), label);
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        return clicked;
    }
}
