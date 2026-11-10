<div align="center">

<img src="images/icon.png" alt="Will-o-Bars" width="128">

# Will-o-Bars

**Show or hide hotbars depending on your class or job.**

<a href="#install">Install</a> · <a href="https://ko-fi.com/willocinnamon">Support me on Ko-fi</a> · <a href="https://github.com/NeonGhostCinnamon/WillOPlugins">All my plugins</a>

</div>

Want a hotbar full of Fisher actions that only shows up when you're fishing? **Will-o-Bars** shows and hides your hotbars automatically every time you change class or job. More hotbar magic is in the works!

## How it works

- **At the top** you see your current class/job, and which hotbars Will-o-Bars changes on it: an eye means shown, a crossed-out eye hidden, a pin "only on this job".
- **1 · Pick classes and jobs** (optional): pick some to check whether each hotbar is shown or hidden on them. Click an icon, Shift+click for a range, click and drag for many, or click a role name for the whole group.
- **2 · Hotbars**: all ten hotbars, 1 to 5 on the left and 6 to 10 on the right. Hotbars Will-o-Bars controls get a glowing border and a little flame. For the others you can switch between **shown** and **hidden** the vanilla way, right there.

Unfold a hotbar for its settings:

- **Mode**: *Game setting* (left to the game), *Show* (always visible), *Show only on* or *Hide on*. For the last two you tick the classes and jobs right in the card.
- **Own actions per class/job**: every class and job keeps its own actions on that hotbar. Put Fisher actions on hotbar 4 while you're Fisher and they stay on Fisher; other jobs get their own bar, and the Fisher actions come back when you switch to Fisher again.
- The eye button pauses a hotbar's rule. Pausing it, or setting it back to *Game setting*, hands the hotbar back to the game.

## Import/Export

In the **Settings** tab, first **choose a save folder** (nothing is picked until you do). Then you can **Save to file** there, **Copy as text** to paste somewhere safe or send to a friend, and **Paste text** or **Import** a saved file to bring your hotbar rules back, for example after resetting the plugin. Pick a folder outside the plugin, so resetting Will-o-Bars doesn't delete your saves.

## Backups

The **Backups** tab saves every action on every hotbar and cross hotbar, for all your classes and jobs, plus which hotbars are shared, so you can always go back to your own setup. Will-o-Bars also saves one automatically the first time you log in with it, and right before it changes whether a hotbar is shared. Restoring a backup pauses all rules, so nothing changes your hotbars again until you turn a rule back on. Once you've picked a save folder, backups are kept in its `backups` folder, so resetting the plugin doesn't delete them.


<a name="install"></a>
## Install

1. In game, type `/xlsettings` and open the **Experimental** tab.
2. Under **Custom Plugin Repositories**, paste this URL from [Will-o-Plugins](https://github.com/NeonGhostCinnamon/WillOPlugins) (it has all of my plugins), click **+**, tick **Enabled** and click **Save and Close**:

   ```
   https://raw.githubusercontent.com/NeonGhostCinnamon/WillOPlugins/HEAD/repo.json
   ```

3. Type `/xlplugins`, search for **Will-o-Bars** and install it.
4. Open the settings with **`/willobars`**. `/willobars apply` re-applies your rules right away.

## AI disclosure

This plugin was written with the help of AI.

<div align="center"><sub><a href="https://ko-fi.com/willocinnamon">Support me on Ko-fi</a></sub></div>
