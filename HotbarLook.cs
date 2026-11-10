using Dalamud.Plugin.Services;

namespace WillOBars;

/// <summary>Reads what the game itself is doing with a hotbar.</summary>
public sealed class HotbarLook
{
    private readonly IGameGui gameGui;

    public HotbarLook(IGameGui gameGui) => this.gameGui = gameGui;

    public static string AddonName(int bar) => bar == 1 ? "_ActionBar" : $"_ActionBar{bar - 1:00}";

    /// <summary>Whether the game is showing this hotbar right now (null when it can't be read).</summary>
    public bool? IsShownInGame(int bar)
    {
        var addon = gameGui.GetAddonByName(AddonName(bar));
        return addon.IsNull ? null : addon.IsVisible;
    }
}
