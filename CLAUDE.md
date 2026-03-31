# Captain of Industry Mod — ResearchQueue

## Project Overview

This is a C# mod for the game **Captain of Industry** (COI), developed using the official Mafi modding framework. The mod's goal is to give players a new UI element where they can reorder their research queue.

The user developing the mod and prompting is a non-programmer (technical product manager and gaming enthusiast). Code should be explained clearly and kept as simple as possible. Avoid over-engineering.

## Mod Identity

- **Author:** Jagg111
- **Mod ID:** `ResearchQueue`
- **Game:** Captain of Industry (Update 4+)
- **Framework:** Mafi (.NET 4.8)
- **Mod type:** `IMod` (upgraded from DataOnlyMod to access Initialize() and DI)

## Project Structure

```
ResearchQueue.sln          # Visual Studio solution
ResearchQueue.csproj       # Project file (build config, references, auto-deploy)
ResearchQueue.cs           # Main mod entry point
ResearchQueueWindowController.cs  # Queue panel injected into research tree (auto-registered via DI)
manifest.json                # Mod metadata (id, version, authors, dependencies, etc.)
bin/                         # Build output (gitignored)
obj/                         # Build intermediates (gitignored)
```

## Build & Deploy

### Environment Variables Required
- `COI_ROOT` — path to the Captain of Industry game install directory (e.g., Steam folder)
- `COI_MODS` — auto-set to `%APPDATA%\Captain of Industry\Mods` in the .csproj

### Build
Open `ResearchQueue.sln` in Visual Studio and build, or run:
```
dotnet build /p:LangVersion=latest
```

On build, the mod is automatically deployed to `%APPDATA%\Captain of Industry\Mods\ResearchQueue\`.

### What gets deployed
- `ResearchQueue.dll` — compiled mod
- `manifest.json` — mod metadata
- `ResearchQueue.pdb` — debug symbols (Debug builds only)

## GitHub Release Packaging

- `manifest.json` version is the source of truth for release packaging, tags, and release titles
- When `manifest.json` is version-bumped for a functional release, generate a fresh package with:
  - `.\create-github-release.ps1`
- The script creates a local, gitignored `githubrelease\` folder containing:
  - `ResearchQueue\ResearchQueue.dll`
  - `ResearchQueue\manifest.json`
  - a versioned zip ready for GitHub Releases
  - `release-notes.md` generated from git history
- Default behavior is to create a GitHub draft release through the terminal using `gh`
- Use `.\create-github-release.ps1 -PackageOnly` when you want the zip and notes without creating the draft release yet

## Modding Reference

For detailed game API docs, modding patterns, reflection examples, and UI patterns (all verified against Update 4 DLLs), see **MODDING-REFERENCE.md**.

## Mod Goal & Player Experience

**Problem being solved:** Player has queued up many research items, then gets a new goal/quest that makes something deep in the queue suddenly urgent. They need a way to move that item to the top without removing and re-adding everything.

**Target feature:** Drag-and-drop reordering of the research queue, surfaced inside the existing research tree screen (the screen shown when you click the beaker icon).

**Save compatibility (critical):**
- Mod must work on existing saves (no new game required to use it)
- If mod is removed, queue remains in whatever order it was last left in — player just loses the ability to reorder
- Do NOT store queue order in a separate mod-owned file; queue state must live entirely in the game's own save data
- Implementation: manipulate the game's internal queue directly via reflection, not a parallel data structure

## Working Style Notes

- User is not a programmer — explain what code does in plain language when making changes
- Keep the mod focused and simple — one clear purpose
- **Version bumping:** See the Versioning section below for full details. If a session involves code changes, **proactively ask the user whether a version bump should be included before the session ends**. If the user confirms a bump, also run `.\create-github-release.ps1` to create a GitHub draft release automatically so the user can go straight to GitHub and approve it.
- The `COI_ROOT` env var must be set for builds to work
- Ask clarifying questions before writing code; document answers in this file
- **GitHub Issues:** Before starting any bug fix or feature work, check `gh issue list` for a related open issue. If one exists, remind the user so commit messages can include `Fixes #N` (or `Closes #N` / `Resolves #N`) — GitHub auto-closes the issue when the commit lands on `main`

## Versioning

This project uses **Semantic Versioning** (`MAJOR.MINOR.PATCH`):

- **Patch (0.0.X)** — Bug fixes and small tweaks. **When in doubt, bump this one.** Examples: fixing items not showing up, correcting a visual glitch, small behavior fix.
- **Minor (0.X.0)** — New features a player would notice. Resets patch to 0. Examples: adding drag-and-drop, adding a new button or UI element, adding a new capability.
- **Major (X.0.0)** — Reserved for major milestones or breaking changes. Resets minor and patch to 0. Examples: mod reaching "complete" status (1.0.0), a game update forcing a major rewrite that changes how the mod works for the player.

**Rules:**
- Do NOT bump for docs-only, build script, or comment-only changes
- If unsure whether something is a minor or patch, **it's a patch**
- `manifest.json` version is always the source of truth

**End-of-session workflow:**
1. If code changes were made during the session, ask the user if a version bump is needed
2. If yes, bump `manifest.json`, commit the change, and run `.\create-github-release.ps1` to create a GitHub draft release
3. The user can then go directly to GitHub and approve the draft

## Documentation Rules (IMPORTANT)

Whenever we discover something new about how the game works, its APIs, types, method signatures, or modding patterns — **always update the relevant docs without being asked**:

- **MODDING-REFERENCE.md** — Game API discoveries, type signatures, working code patterns, gotchas, and corrections to previous assumptions. This is the technical encyclopedia.
- **CLAUDE.md** — Update if project-level info changes (mod type, structure, scope decisions, etc.)

Do not wait for the user to prompt for doc updates. If we learn it, we document it.
