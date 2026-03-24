# Captain of Industry Mod — ResearchReorder

## Project Overview

This is a C# mod for the game **Captain of Industry** (COI), developed using the official Mafi modding framework. The mod's goal is to reorder research items in the tech tree.

The user is a non-programmer (technical product manager and gaming enthusiast). Code should be explained clearly and kept as simple as possible. Avoid over-engineering.

## Mod Identity

- **Author:** Jagg111
- **Mod ID:** `ResearchReorder`
- **Game:** Captain of Industry (Update 4+)
- **Framework:** Mafi (.NET 4.8)
- **Mod type:** `DataOnlyMod` (no UI/Unity patches, data/prototype changes only)

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

## manifest.json Fields (Update 4 Format)

As of Update 4, the manifest supports additional fields displayed in the mod selection UI:

| Field | Purpose |
|---|---|
| `id` | Unique mod identifier (must match folder/DLL name) |
| `version` | Semantic version string |
| `primary_dlls` | Array of DLL filenames to load |
| `non_locking_dll_load` | If true, DLL is loaded without file lock |
| `authors` | Array of author name strings |
| `links` | Array of URLs (GitHub, mod page, etc.) |
| `min_game_version` | Minimum COI version required |
| `last_verified_game_version` | Last COI version this was tested against |
| `dependencies` | Required mods (format: `"modId v0.0.0+"`) |
| `optional_dependencies` | Optional mods (same format) |
| `description` | Short description shown in mod selection UI |

## Modding API

- Official repo: https://github.com/MaFi-Games/Captain-of-industry-modding
- Wiki (WIP): https://wiki.coigame.com/Modding
- Game assemblies referenced: `Mafi.dll`, `Mafi.Core.dll`, `Mafi.Base.dll`, `Mafi.Unity.dll`

### Key Concepts
- **`DataOnlyMod`** — base class for mods that only modify data/prototypes (no Unity patches)
- **`RegisterPrototypes(ProtoRegistrator)`** — override this to add/modify game data
- **`ModManifest`** — passed to constructor, contains mod metadata at runtime

## Update 4 Mod System Notes

Update 4 introduced a new mod selection UI with:
- Green checkmark = enabled, Red X = invalid/error, Unchecked = disabled
- Dependency validation (missing dependencies shown as "Missing" badge)
- Mod detail panel showing all manifest fields
- Invalid mod counter at bottom of mod list

## Working Style Notes

- User is not a programmer — explain what code does in plain language when making changes
- Keep the mod focused and simple — one clear purpose
- Always update `manifest.json` version when making functional changes
- The `COI_ROOT` env var must be set for builds to work
