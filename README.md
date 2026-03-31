<div align="center">

# 🔬 Research Queue

### A mod for **Captain of Industry** that lets you manage and reorder your research queue

[![Last Updated](https://img.shields.io/github/release-date/Jagg111/ResearchReorder-COI-Mod?label=Mod%20Last%20Updated&style=flat)](https://github.com/Jagg111/ResearchReorder-COI-Mod/releases/latest)
[![Update 4 Compatible](https://img.shields.io/badge/COI-Update_4_Compatible-green?style=flat)](https://store.steampowered.com/app/1594320/Captain_of_Industry/)

**[⬇️ Download Latest Version](https://github.com/Jagg111/ResearchReorder-COI-Mod/releases/latest)**

</div>

## 🎯 Why This Mod?

You've queued up 15 research items, everything is humming along nicely... and then a new project pops up that needs something buried at #8 in your queue. Without this mod, your only option is to remove items one by one and re-add them in the right order. 😩

**Research Queue** gives you a simple drag-and-drop panel right inside the research tree so you can reorder your queue in seconds. 🎉

![Research Queue panel inside the research tree](screenshots/current.jpg)

## ✨ Features

- 🔀 **Drag-and-drop reordering** of your research queue
- 🎨 **Integrated UI** - panel appears directly inside the research tree and uses native UI elements to feel like it's a standard feature in the game
- 📊 **Shows current research** - see what's actively being researched and its progress
- 💾 **Works on existing saves** - install and go, no new game required
- 🛡️ **Safe to remove** - uninstall anytime; your queue stays in whatever order you left it

## 📦 Installation

1. **[⬇️ Download the latest release](https://github.com/Jagg111/ResearchReorder-COI-Mod/releases/latest)** (`.zip` file)
2. Extract the zip file
3. Copy the **`ResearchQueue`** folder into your mods directory:
   ```
   %APPDATA%\Captain of Industry\Mods\
   ```
4. Your folder structure should look like this:
   ```
   Captain of Industry\Mods\ResearchQueue\
       ResearchQueue.dll
       manifest.json
   ```
5. Launch the game and in the main menu enable the mod when loading your save
6. Open the research tree (hotkey `G` by default) - the queue panel appears on the right side when no research nodes are selected

<details>
<summary><strong>📁 Can't find your Mods folder?</strong></summary>

Press `Win + R`, paste this path, and hit Enter:
```
%APPDATA%\Captain of Industry\Mods
```
If the `Mods` folder doesn't exist yet, create it.

</details>

## 🔧 Compatibility

| | |
|---|---|
| 🎮 **Game version** | Update 4+ (0.8.2c verified) |
| 💾 **Save compatible** | Yes - works on existing saves |
| 🛡️ **Safe to remove** | Yes - queue stays in its current order |
| 🤝 **Other mods** | No known conflicts (yet) |

## ❓ FAQ

<details>
<summary><strong>Does this work with existing saves?</strong></summary>

✅ Yes! Just install and load your save. No new game needed.

</details>

<details>
<summary><strong>What happens if I remove the mod?</strong></summary>

✅ Nothing bad. Your research queue keeps whatever order it was in when you last played with the mod. You just lose the ability to reorder it via drag-and-drop.

</details>

<details>
<summary><strong>Does this conflict with other mods?</strong></summary>

✅ No known conflicts. The mod injects a panel into the research tree UI and manipulates the game's own internal queue - it doesn't replace or override any existing game systems.

</details>

<details>
<summary><strong>Is this mod safe? Does it modify save files?</strong></summary>

✅ The mod works with the game's existing research queue data. It doesn't create its own save data or modify save files directly - it just changes the order of items already in the queue.

</details>

## 🐛 Feedback & Bug Reports

Found a bug or have a suggestion? [Open an issue on GitHub](https://github.com/Jagg111/ResearchReorder-COI-Mod/issues) - it's the easiest way to get a hold of me to get it looked at.

## 📋 Changelog

See the [Releases page](https://github.com/Jagg111/ResearchReorder-COI-Mod/releases) for version history and download links.

## 🙏 Credits

- Built with the [Mafi modding framework](https://github.com/MaFi-Games/Captain-of-industry-modding) by MaFi Games
- Developed by [Jagg111](https://github.com/Jagg111) with [Claude Code](https://claude.com/product/claude-code)