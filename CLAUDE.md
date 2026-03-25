# Captain of Industry Mod — ResearchReorder

## Project Overview

This is a C# mod for the game **Captain of Industry** (COI), developed using the official Mafi modding framework. The mod's goal is to give players a new UI element where they can reorder their research queue.

The user developing the mod and prompting is a non-programmer (technical product manager and gaming enthusiast). Code should be explained clearly and kept as simple as possible. Avoid over-engineering.

## Mod Identity

- **Author:** Jagg111
- **Mod ID:** `ResearchReorder`
- **Game:** Captain of Industry (Update 4+)
- **Framework:** Mafi (.NET 4.8)
- **Mod type:** `IMod` (upgraded from DataOnlyMod to access Initialize() and DI)

## Project Structure

```
ResearchReorder.sln          # Visual Studio solution
ResearchReorder.csproj       # Project file (build config, references, auto-deploy)
ResearchReorder.cs           # Main mod entry point
manifest.json                # Mod metadata (id, version, authors, dependencies, etc.)
bin/                         # Build output (gitignored)
obj/                         # Build intermediates (gitignored)
```

## Build & Deploy

### Environment Variables Required
- `COI_ROOT` — path to the Captain of Industry game install directory (e.g., Steam folder)
- `COI_MODS` — auto-set to `%APPDATA%\Captain of Industry\Mods` in the .csproj

### Build
Open `ResearchReorder.sln` in Visual Studio and build, or run:
```
dotnet build /p:LangVersion=latest
```

On build, the mod is automatically deployed to `%APPDATA%\Captain of Industry\Mods\ResearchReorder\`.

### What gets deployed
- `ResearchReorder.dll` — compiled mod
- `manifest.json` — mod metadata
- `ResearchReorder.pdb` — debug symbols (Debug builds only)

## Modding Reference

For detailed game API docs, modding patterns, reflection examples, and UI patterns (all verified against Update 4 DLLs), see **MODDING-REFERENCE.md**.

## Implementation Progress

For the phased implementation plan, task tracking, and technical discoveries, see **IMPLEMENTATION-PLAN.md**. Current status: **Phases 1-3a complete, Phase 3b up next** (show queue items in window).

## Mod Goal & Player Experience

**Problem being solved:** Player has queued up many research items, then gets a new goal/quest that makes something deep in the queue suddenly urgent. They need a way to move that item to the top without removing and re-adding everything.

**Target feature:** Drag-and-drop reordering of the research queue, surfaced inside the existing research tree screen (the screen shown when you click the beaker icon).

**Scope decisions:**
- Queue reordering only (not tree layout changes)
- Single-player only (COI has no multiplayer)
- Ideally any item can be moved anywhere; the currently-in-progress item may need to stay locked in place (TBD once we dig into the API)
- Drag-and-drop preferred; arrow buttons as fallback if drag-and-drop is too complex
- Phased approach: core reorder logic first → move-up/move-down buttons → upgrade to drag-and-drop

**Save compatibility (critical):**
- Mod must work on existing saves (no new game required to use it)
- If mod is removed, queue remains in whatever order it was last left in — player just loses the ability to reorder
- Do NOT store queue order in a separate mod-owned file; queue state must live entirely in the game's own save data
- Implementation: manipulate the game's internal queue directly via reflection, not a parallel data structure

**UI context from screenshot:**
- Top-left hover tooltip over the beaker icon shows: "Current research: X" and "Research queue: item1, item2..."
- Right panel shows selected tech details with "In queue (N)" and "Remove from queue" button
- The queue panel/tooltip is where reordering UI should live

## Working Style Notes

- User is not a programmer — explain what code does in plain language when making changes
- Keep the mod focused and simple — one clear purpose
- Always update `manifest.json` version when making functional changes
- The `COI_ROOT` env var must be set for builds to work
- Ask clarifying questions before writing code; document answers in this file

## Documentation Rules (IMPORTANT)

Whenever we discover something new about how the game works, its APIs, types, method signatures, or modding patterns — **always update the relevant docs without being asked**:

- **MODDING-REFERENCE.md** — Game API discoveries, type signatures, working code patterns, gotchas, and corrections to previous assumptions. This is the technical encyclopedia.
- **IMPLEMENTATION-PLAN.md** — Task checklist updates (mark items done, add new tasks), phase status changes, new open questions, and any change to the implementation strategy.
- **CLAUDE.md** — Update if project-level info changes (mod type, structure, scope decisions, etc.)

Do not wait for the user to prompt for doc updates. If we learn it, we document it.
