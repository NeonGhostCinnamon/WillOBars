<div align="center">

<img src="images/readme/header.svg" alt="Will-o-Bars: hotbars that show up when you need them" width="100%">

<br><br>

<a href="#how-to-install"><img src="images/readme/btn-install.svg" alt="Install in Dalamud" height="46"></a>&nbsp;
<a href="https://github.com/NeonGhostCinnamon/WillOPlugins"><img src="images/readme/btn-plugins.svg" alt="All my plugins" height="46"></a>

</div>

## Usage

I've created these plugins for my own use, but figured I might as well release them on GitHub. They're not as fancy as "proper" plugins, but I hope you can make good use of them too!

## Description

Will-o-Bars shows or hides your hotbars depending on your job and what you're doing, like a fishing hotbar only on Fisher, or a hotbar that hides in combat.

<details>
<summary><b>More details and settings</b></summary>
<br>

- **A rule per hotbar**, in plain words: "Show hotbar 4 when *all/any* of these…". Add more cases ("otherwise, when…") for anything trickier; the first one that matches decides.
- **Conditions**: job or role · in combat · weapon drawn · mounted · crafting · gathering or fishing · in a city or sanctuary · in a duty (dungeon, trial, raid, PvP…) · zone · weather · in a party. Each can be "is" or "is not".
- **Templates**, including "Use current conditions": build a rule from where you are right now and untick what shouldn't matter.
- **Own actions per class/job**: switch the game's "Shared" setting per hotbar. Will-o-Bars saves a backup of your hotbars first.
- **Backups**: every action on every hotbar of every class/job. One is made automatically the first time, so you can always go back.
- **Settings**: export or import your rules as text or a file · pick a save folder · dark, light or Dalamud theme.

</details>

## How to install

1. In game, type `/xlsettings` and open the **Experimental** tab.
2. Under **Custom Plugin Repositories**, paste this URL, click **+**, tick **Enabled** and click **Save and Close**:

   ```
   https://raw.githubusercontent.com/NeonGhostCinnamon/WillOPlugins/HEAD/repo.json
   ```

3. Type `/xlplugins`, search for **Will-o-Bars** and install it.
4. Open it any time with `/willobars`.

## AI Disclosure

This plugin was written and tested with the help of AI.

<br>

<div align="center">
<img src="images/readme/footer.svg" alt="Will-o-Cinnamon" width="100%">
<br>
<sub><a href="https://ko-fi.com/willocinnamon">Support me on Ko-fi</a></sub>
</div>
