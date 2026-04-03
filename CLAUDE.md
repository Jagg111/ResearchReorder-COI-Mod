# Captain of Industry Mod — ResearchQueue

## Project Overview

This is a C# mod for the game **Captain of Industry** (COI), developed using the official Mafi modding framework. The mod's goal is to give players a new UI element where they can reorder their research queue.

The maintainer developing the mod and prompting is a not a programmer by trade. Code should be explained clearly and kept as simple as possible.

## Mod Identity

- **Author:** Jagg111
- **Mod ID:** `ResearchQueue`
- **GitHub repo:** `Jagg111/COI-ResearchQueue`
- **Game:** Captain of Industry (Update 4+)
- **Framework:** Mafi (.NET 4.8)
- **Mod type:** `IMod`

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

### Build
Open `ResearchQueue.sln` in Visual Studio and build, or run from `C:\Code\CaptainOfIndustry`:
```
dotnet build ResearchQueue.sln
```
Note: always specify `ResearchQueue.sln` explicitly and run from the project root. Do not pass `/p:LangVersion=latest` — it breaks argument parsing with `dotnet build`.

On build, the mod is automatically deployed to `%APPDATA%\Captain of Industry\Mods\ResearchQueue\`.

### What gets deployed
- `ResearchQueue.dll` — compiled mod
- `manifest.json` — mod metadata
- `ResearchQueue.pdb` — debug symbols (Debug builds only)

## GitHub Release Packaging

The skill `/ship-it` will create a new GitHub release. The skill handles everything end-to-end including mod version bumps and release notes -- see `.claude/skills/ship-it/SKILL.md` for details.

- `manifest.json` -- version is the source of truth for release tags and titles
- `create-github-release.ps1` -- the underlying packaging script called by `/ship-it`; can also be run standalone

## Health Checks & Game Version Compatibility

The skill `/game-version-check` can be run after any Captain of Industry game update to check whether the mod will still work. The skill handles the full workflow end-to-end -- see `.claude/skills/game-version-check/SKILL.md` for details.

- `check-reflection-targets.ps1` — the underlying diagnostic script; checks all internal game references the mod depends on against the actual game DLLs. Can also be run standalone.
- `inspect_dll.ps1` — deeper inspection tool used when something breaks to see what changed in the game

## Modding Reference

For detailed game API docs, modding patterns, reflection examples, and UI patterns (all verified against Update 4 DLLs), see **MODDING-REFERENCE.md**.

## Mod Goal & Player Experience

The mod adds a drag-and-drop research queue panel inside the existing research tree screen, letting players reorder queued research items and start/remove them.

Queue state lives in the game's own save data, manipulated directly via reflection — no separate mod-owned files. The mod works on existing saves and can be removed safely; the queue stays in whatever order it was last left in.

## Working Style Notes

- User is not a programmer — explain what code does in plain language when making changes
- **Version bumping:** See the Versioning section below for full details. If a session involves code changes, **proactively ask the user whether a version bump should be included before the session ends**. If the user confirms a bump, use `/ship-it` to run the full release workflow.
- The `COI_ROOT` env var must be set for builds to work
- Before writing any code, ask clarifying questions to gather enough context to attempt the task in one pass. Don't start writing until the intent is clear and there is little room for ambiguity or interpretation.
- **GitHub Issues:** Before starting any bug fix or feature work, check `gh issue list` for a related open issue. If one exists, remind the user so commit messages can include `Fixes #N` (or `Closes #N` / `Resolves #N`) — GitHub auto-closes the issue when the commit lands on `main`
- **Commit messages:** Single line describing what changed. No body text. For sessions related to github issues append `Fixes #N` for bug issues or `Closes #N` for enhancement issues.
- **Reflection safety:** All reflection access (`GetField`, `GetProperty`, `GetMethod`) must go through the `ReflectionProbe` helper in `ResearchQueueWindowController.cs`. This keeps the runtime health check and `check-reflection-targets.ps1` automatically in sync. After a game update, run `/game-version-check` to diagnose breakage.

## Versioning

This project uses **Semantic Versioning** (`MAJOR.MINOR.PATCH`):

- **Patch (0.0.X)** — Bug fixes and small tweaks. **When in doubt, bump this one.** Examples: fixing items not showing up, correcting a visual glitch, small behavior fix.
- **Minor (0.X.0)** — New features a player would notice. Resets patch to 0. Examples: adding drag-and-drop, adding a new button or UI element, adding a new capability.
- **Major (X.0.0)** — Reserved for major milestones or breaking changes. Resets minor and patch to 0. Examples: mod reaching "complete" status (1.0.0), a game update forcing a major rewrite that changes how the mod works for the player.

**Rules:**
- Do NOT bump for docs-only, build script, or comment-only changes
- If unsure then remind the user about semenatic versioning and ask what their preference is
- `manifest.json` version is always the source of truth

**End-of-session workflow:**
1. If code changes were made during the session, ask the user if a version bump is needed
2. If yes, run `/ship-it` — it handles version bump, What's New drafting, commit message suggestion, packaging, and GitHub draft release creation end-to-end
3. The user can then go directly to GitHub and publish the draft

## Documentation Rules (IMPORTANT)

Whenever we discover something new about how the game works, its APIs, types, method signatures, or modding patterns — **always update the relevant docs without being asked**:

- **MODDING-REFERENCE.md** — Game API discoveries, type signatures, working code patterns, gotchas, and corrections to previous assumptions. This is the technical encyclopedia.
- **CLAUDE.md** — Update if project-level info changes (mod type, structure, scope decisions, etc.)

Do not wait for the user to prompt for doc updates. If we learn it, we document it.
