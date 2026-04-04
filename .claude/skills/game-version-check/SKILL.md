---
name: game-version-check
description: Run after a Captain of Industry game update. Compares game version, runs reflection diagnostics, guides manual in-game testing, analyzes logs, and prompts for version bump + release if needed.
disable-model-invocation: true
---

Walk through each step in order. Stop and report clearly if anything fails.

## What this does and why

The ResearchQueue mod depends on internal game code that isn't part of any official modding API. When the game updates, those internal references can change -- renamed, moved, or removed entirely. This skill runs a full compatibility check: it verifies the game version, checks all internal references offline, walks you through manual in-game testing, then analyzes the game log to confirm everything lines up.

## Key files

| File | What it does |
|------|-------------|
| `check-reflection-targets.ps1` | The diagnostic script. Reads the mod's source code to find every internal game reference, then checks each one against the actual game files. No separate list to maintain -- it always matches what's in the code. |
| `inspect_dll.ps1` | A deeper inspection tool. When something breaks, this shows you what a game type looks like now so you can spot what changed (renamed, moved, etc.). |
| `ResearchQueueWindowController.cs` | The mod's main code file. Contains all the `ReflectionProbe.*` calls that define what internal game code the mod depends on. |

---

## Step 0 -- Determine game version and compare

Read the game's log to find the current version and check it against what the mod was last verified with.

1. Find the newest log file in the game's log folder. Run:
   ```
   powershell.exe -ExecutionPolicy Bypass -Command "Get-ChildItem '$env:APPDATA\Captain of Industry\Logs' -File | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | ForEach-Object { Write-Output $_.FullName; Get-Content $_.FullName -TotalCount 10 }"
   ```

2. In the output, look for a line matching this pattern:
   ```
   Build: <number>, v<version> (Update <N>)
   ```
   For example: `Build: 559, v0.8.2c (Update 4)`. Extract the version string (e.g., `0.8.2c`).

3. If no log file exists or no version line is found, ask the user: "I couldn't detect the game version from the logs. What version of Captain of Industry are you running? (You can find this on the game's main menu.)"

4. Read `manifest.json` and extract the `max_verified_game_version` value.

5. Show the user a clear comparison:
   - **Current game version:** (what you found)
   - **Max verified version in manifest:** (what manifest.json says)
   - **Match?** Yes / No

6. Remember whether these versions match or differ -- you'll need this in Step 5.

Continue to Step 1.

---

## Step 1 -- Run reflection checks

Run the offline diagnostic to see if the mod's internal game references still resolve.

Run the diagnostic script from the project root:

```
powershell -ExecutionPolicy Bypass -File check-reflection-targets.ps1
```

Always show the user the full output table. The results break down into three categories:

- **PASS** -- The mod can find this internal game reference. This feature will work.
- **FAIL** -- The game changed and the mod can't find this reference anymore. This feature is broken and needs a code fix.
- **SKIP** -- This reference uses a dynamic type that can only be checked by actually running the game. The mod's built-in health check (visible in the game log at startup) will verify these automatically.

### If everything passes

Tell the user the offline checks look good, and continue to Step 2 for manual testing.

### If something fails

Explain to the user in plain language what broke and what it means for the mod. For each failed target:

1. Run `inspect_dll.ps1` on the affected type to see what it looks like now:
   ```
   powershell -File inspect_dll.ps1 <TypeName> <DllName>
   ```

2. Compare the output against the member name that failed. Explain what likely happened:
   - **Renamed:** The game developers renamed it. Fix: update the name string in the `ReflectionProbe` call in the code.
   - **Moved:** It's now on a different class. Fix: update the type reference in the `ReflectionProbe` call.
   - **Removed:** The game no longer has this at all. The mod feature tied to it will need a new approach, or it stays disabled. The mod's graceful degradation system will automatically disable just that feature without crashing.

3. After making fixes, rebuild and re-run the diagnostic:
   ```
   dotnet build ResearchQueue.sln
   powershell -ExecutionPolicy Bypass -File check-reflection-targets.ps1
   ```

4. Repeat until all static checks pass. Only then continue to Step 2.

---

## Step 2 -- Manual in-game testing

Time to test the mod in the actual game. Present this checklist to the user and wait for their feedback:

> Launch the game, load a save, and work through this checklist. 
>
> 1. **Startup** -- Open the research tree. The queue panel should appear on the right side.
> 2. **Panel visibility toggle** -- Click on a research node (panel should hide). Click away to deselect (panel should reappear).
> 3. **Current research display** -- Start researching something. The queue panel should show its name and a progress bar.
> 4. **Lab status indicator** -- If you have an active research lab, the progress bar should be green. If you pause or don't have a lab, it should turn orange.
> 5. **Queue population** -- Add 3 or more items to the research queue using the normal research tree buttons. They should all appear in the queue panel.
> 6. **Promote button** -- On a queued item, click the promote button. That item should jump to active research.
> 7. **Remove button in queue (mod)** -- On a queued item, click the X/remove button. It should disappear from the queue.
> 8. **Remove button on current (mod)** -- Click the red X next to the current research in the queue panel. Research should cancel but the rest of the queue should stay intact.
> 9. **Cancel button on current (native)** -- Select the active research node in the tree and use the game's built-in cancel button in the detail panel. The queue should still be preserved (not wiped).
> 10. **Drag-and-drop reorder** -- Drag a queue item by its handle to a new position. The order should update.
> 11. **Prerequisite constraint** -- Try dragging an item above one of its unresearched prerequisites. It should snap back to its original position and play an error sound.
> 12. **Out-of-order warning** -- If any item ends up above its prerequisite, it should have an orange/red tint and show a "Move below: [name]" message.
> 13. **Empty queue** -- Remove all items from the queue. The panel should show an "Empty" label.
> 14. **Scrollbar** -- Add enough items so the list overflows. A scrollbar should appear. Remove items until it fits -- scrollbar should hide.
>
> Let me know how it goes! You can say something like "all clear" or list any items that had issues.

Wait for the user to respond before continuing.

---

## Step 3 -- Analyze game log

Check the game log for any warnings or errors the mod reported during the test session.

Immediately after receiving the user's manual test feedback, pull the latest log file and analyze it. Do NOT prompt the user again -- just do this automatically.

1. Find the newest log file (the user just played, so there should be a fresh one):
   ```
   powershell.exe -ExecutionPolicy Bypass -Command "Get-ChildItem '$env:APPDATA\Captain of Industry\Logs' -File | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | ForEach-Object { Write-Output $_.FullName }"
   ```

2. Read the log file and extract all lines containing `ResearchQueue:`.

3. Parse the health check block (look for lines between `=== Health Check ===` and `================================`):
   - If you see "All N reflection targets resolved -- fully operational" that's a full pass.
   - If you see "N/total reflection targets missing -- some features disabled" that's a partial pass -- note which features are disabled.
   - If you see "CRITICAL reflection targets missing" that's a critical failure.

4. Look for any WARNING (`W` prefix) or ERROR (`E` prefix) lines that contain `ResearchQueue:`. List them.

5. Cross-reference the log findings against the user's manual test feedback:
   - If the user reported a failure, check the log for related errors or warnings that explain it.
   - If the user reported everything passed but the log shows warnings, flag the discrepancy.
   - If both the user and the log agree everything is clean, say so.

6. Present a clear summary:
   - **Health check:** PASS / PARTIAL / FAIL (with details)
   - **Warnings found:** (list, or "None")
   - **Errors found:** (list, or "None")
   - **Manual vs. log alignment:** Do the log findings match the user's test results?

Continue to Step 4.

---

## Step 4 -- Resolution loop

If there are any failures or discrepancies from Steps 1, 2, or 3:
- Investigate each issue with the user
- Make code fixes as needed, rebuild (`dotnet build ResearchQueue.sln`), and re-run the reflection check
- For issues found during manual testing, ask the user to re-test just the specific items that failed
- Pull the log again after re-testing to confirm
- Continue until all issues are resolved

If everything passed cleanly across all checks, skip this step and declare: "All clear -- the mod is fully compatible with this game version."

Continue to Step 5.

---

## Step 5 -- Version bump (conditional)

**Only run this step if the game version from Step 0 is different from the `max_verified_game_version` in manifest.json.** If they already match, skip this step entirely and say something fun and lighthearted.

If the versions differ and all checks passed:

1. Tell the user: "The game version has changed and all compatibility checks passed. Let's update the mod to reflect the new verified version."

2. List the three places that need updating:
   - `manifest.json`: change `max_verified_game_version` from the old version to the new one
   - `ResearchQueueWindowController.cs` (line 56): change the `MAX_VERIFIED_VERSION` string from the old version to the new one
   - `README.md` (compatibility table, line 60): change the `(X.Y.Zx verified)` text to show the new version

3. Ask the user if they'd like to proceed with these updates.

4. If yes, make all three edits.

5. After the edits are done, tell the user: "Version references updated. You can now run `/ship-it` to publish a new release with the updated game version."

---

## Edge cases

The mod won't crash if targets are broken. It has a built-in safety system with two layers:

1. **Health check log** -- On startup, the mod writes a report to the game log showing exactly what resolved and what's missing. Look for the `=== Health Check ===` block.

2. **Graceful degradation** -- If some features can't work, the mod disables just those features instead of crashing. For example, if queue reading works but queue manipulation doesn't, the panel shows in read-only mode (you can see your queue but not reorder it). Critical failures disable the mod entirely with a clear message.

## Notes

- Some targets are marked SKIP because they depend on types that only exist at runtime inside the game. The offline diagnostic can't check these -- the mod's built-in health check handles them instead.
- If you're sharing this with someone else debugging the mod: they need the `COI_ROOT` environment variable set to their game install path for the script to find the game files.
